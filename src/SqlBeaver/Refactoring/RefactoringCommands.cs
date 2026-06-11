using System;
using System.Collections.Generic;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Analysis;
using SqlBeaver.Completion;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Integration entry points for the refactoring commands.
    /// All methods must be called on the UI thread.
    /// </summary>
    internal static class RefactoringCommands
    {
        // ---------------------------------------------------------------
        // ExpandWildcard
        // ---------------------------------------------------------------

        public static void ExpandWildcard()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Expand *: nenhum documento ativo."); return; }

                // Get full document text
                string text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                // Compute absolute caret offset
                TextSelection sel = doc.Selection;
                string textUpToCaret = doc.StartPoint.CreateEditPoint().GetText(sel.ActivePoint);
                int caretOffset = textUpToCaret.Length;

                DbMetadata metadata = GetMetadata(dte);
                if (metadata == null) { ShowStatus("Expand *: cache ainda carregando — tente novamente."); return; }

                TextReplacement replacement = WildcardExpander.TryExpand(text, caretOffset, metadata);
                if (replacement == null) { ShowStatus("Expand *: nenhum * de SELECT encontrado no cursor."); return; }

                dte.UndoContext.Open("SQL Beaver Expand *");
                try
                {
                    EditPoint epStart = doc.StartPoint.CreateEditPoint();
                    epStart.MoveToAbsoluteOffset(replacement.Start + 1); // DTE is 1-based
                    EditPoint epEnd = doc.StartPoint.CreateEditPoint();
                    epEnd.MoveToAbsoluteOffset(replacement.Start + replacement.Length + 1);
                    epStart.ReplaceText(epEnd, replacement.NewText,
                        (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus("* expandido.");
            }
            catch (Exception ex)
            {
                Log.Error("Expand wildcard", ex);
                ShowStatus("falha em Expand * — veja Output > SQL Beaver");
            }
        }

        // ---------------------------------------------------------------
        // QualifyNames / UnqualifyNames
        // ---------------------------------------------------------------

        public static void QualifyNames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ApplyNameQualification(qualify: true);
        }

        public static void UnqualifyNames()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ApplyNameQualification(qualify: false);
        }

        private static void ApplyNameQualification(bool qualify)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string label = qualify ? "Qualify" : "Unqualify";
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus(label + ": nenhum documento ativo."); return; }

                DbMetadata metadata = GetMetadata(dte);
                if (metadata == null) { ShowStatus(label + ": cache ainda carregando — tente novamente."); return; }

                bool hasSelection = !doc.Selection.IsEmpty;
                string original = hasSelection
                    ? doc.Selection.Text
                    : doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                if (string.IsNullOrWhiteSpace(original)) { ShowStatus(label + ": nada para processar."); return; }

                IReadOnlyList<TextReplacement> edits = qualify
                    ? NameQualifier.Qualify(original, metadata)
                    : NameQualifier.Unqualify(original, metadata);

                if (edits == null) { ShowStatus(label + ": erro de sintaxe no script."); return; }
                if (edits.Count == 0) { ShowStatus(qualify ? "nada a qualificar." : "nada a desqualificar."); return; }

                string result = NameQualifier.Apply(original, edits);

                dte.UndoContext.Open("SQL Beaver " + (qualify ? "Qualify Names" : "Unqualify Names"));
                try
                {
                    if (hasSelection)
                    {
                        doc.Selection.Insert(result,
                            (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    }
                    else
                    {
                        EditPoint start = doc.StartPoint.CreateEditPoint();
                        start.ReplaceText(doc.EndPoint, result,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus((qualify ? "schemas adicionados" : "schemas removidos") +
                           $" ({edits.Count} referência(s)).");
            }
            catch (Exception ex)
            {
                Log.Error(label + " names", ex);
                ShowStatus("falha em " + label + " — veja Output > SQL Beaver");
            }
        }

        // ---------------------------------------------------------------
        // RenameAliasOrVariable
        // ---------------------------------------------------------------

        public static void RenameAliasOrVariable()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Rename: nenhum documento ativo."); return; }

                string text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
                TextSelection sel = doc.Selection;
                string textUpToCaret = doc.StartPoint.CreateEditPoint().GetText(sel.ActivePoint);
                int caretOffset = textUpToCaret.Length;

                // Read word under caret (incl. leading @)
                string oldName = GetWordAtOffset(text, caretOffset);
                if (string.IsNullOrEmpty(oldName))
                {
                    ShowStatus("Rename: nenhum identificador sob o cursor.");
                    return;
                }

                bool isVariable = oldName.StartsWith("@", StringComparison.Ordinal);

                int rangeStart, rangeEnd;
                if (isVariable)
                {
                    // Variable: whole batch (between GOs or whole doc)
                    (rangeStart, rangeEnd) = TokenRenamer.StatementBounds(text, caretOffset);
                    // For @var, use the whole batch — StatementBounds gives us the batch
                    // actually let's use the whole document if no GO separators
                    rangeStart = 0;
                    rangeEnd = text.Length;

                    // Find GO boundaries around caret to scope the batch
                    (int batchStart, int batchEnd) = GetBatchBounds(text, caretOffset);
                    rangeStart = batchStart;
                    rangeEnd = batchEnd;
                }
                else
                {
                    // Alias: verify it's in scope
                    IReadOnlyList<TableRef> tables = StatementScopeAnalyzer.GetTablesInScope(text, caretOffset);
                    bool isAlias = false;
                    foreach (TableRef t in tables)
                    {
                        if (t.Alias != null && string.Equals(t.Alias, oldName, StringComparison.OrdinalIgnoreCase))
                        { isAlias = true; break; }
                    }

                    if (!isAlias)
                    {
                        ShowStatus("Rename: caret não está sobre um alias ou @variável.");
                        return;
                    }

                    // Alias: statement bounds
                    (rangeStart, rangeEnd) = TokenRenamer.StatementBounds(text, caretOffset);
                }

                // Show rename dialog
                string newName;
                using (var dlg = new RenameDialog(oldName))
                {
                    IntPtr hwnd;
                    try { hwnd = new IntPtr((int)dte.MainWindow.HWnd); }
                    catch { hwnd = IntPtr.Zero; }

                    var owner = new NativeWindowWrapper(hwnd);
                    System.Windows.Forms.DialogResult result = dlg.ShowDialog(owner);
                    if (result != System.Windows.Forms.DialogResult.OK) return;
                    newName = dlg.NewName;
                }

                if (string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    ShowStatus("Rename: nome igual ao atual.");
                    return;
                }

                IReadOnlyList<TextReplacement> edits =
                    TokenRenamer.Rename(text, rangeStart, rangeEnd, oldName, newName);

                if (edits.Count == 0)
                {
                    ShowStatus("Rename: nenhuma ocorrência encontrada.");
                    return;
                }

                dte.UndoContext.Open("SQL Beaver Rename");
                try
                {
                    // Apply edits descending (already sorted that way)
                    foreach (TextReplacement edit in edits)
                    {
                        EditPoint epStart = doc.StartPoint.CreateEditPoint();
                        epStart.MoveToAbsoluteOffset(edit.Start + 1);
                        EditPoint epEnd = doc.StartPoint.CreateEditPoint();
                        epEnd.MoveToAbsoluteOffset(edit.Start + edit.Length + 1);
                        epStart.ReplaceText(epEnd, edit.NewText,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus($"'{oldName}' renomeado para '{newName}' ({edits.Count} ocorrência(s)).");
            }
            catch (Exception ex)
            {
                Log.Error("Rename alias/variable", ex);
                ShowStatus("falha em Rename — veja Output > SQL Beaver");
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static DbMetadata GetMetadata(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ActiveConnection conn = ConnectionService.GetActiveConnection();
            if (conn == null) return null;

            return SqlBeaverCompletionSourceProvider.Cache.TryGet(
                conn.Server, conn.Database,
                new MetadataRequest
                {
                    ConnectionString = conn.ConnectionString,
                    AccessToken = conn.AccessToken,
                    ProviderConnectionType = conn.ProviderConnectionType
                });
        }

        private static string GetWordAtOffset(string text, int offset)
        {
            if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length) return null;

            // clamp
            int pos = offset < text.Length ? offset : text.Length - 1;

            // expand backwards
            int start = pos;
            while (start > 0 && IsIdentChar(text[start - 1])) start--;

            // expand forwards
            int end = pos;
            while (end < text.Length && IsIdentChar(text[end])) end++;

            if (start == end) return null;
            return text.Substring(start, end - start);
        }

        /// <summary>Returns the bounds of the GO-batch containing the caret.</summary>
        private static (int start, int end) GetBatchBounds(string text, int caret)
        {
            // Find last GO before caret
            int batchStart = 0;
            int batchEnd = text.Length;

            // Scan for GO lines
            int i = 0;
            while (i < text.Length)
            {
                // Find line start
                int lineStart = i;
                // Find line end
                int lineEnd = text.IndexOf('\n', i);
                if (lineEnd < 0) lineEnd = text.Length;

                string line = text.Substring(lineStart, lineEnd - lineStart).Trim('\r', '\n', ' ', '\t');
                if (string.Equals(line, "GO", StringComparison.OrdinalIgnoreCase))
                {
                    int goEnd = lineEnd + 1 > text.Length ? text.Length : lineEnd + 1;
                    if (lineStart <= caret) // this GO is at or before caret
                        batchStart = goEnd;
                    else if (batchEnd == text.Length) // first GO after caret
                        batchEnd = lineStart;
                }

                i = lineEnd + 1;
            }

            return (batchStart, batchEnd);
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';

        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }

        /// <summary>Thin wrapper so we can pass an HWND to ShowDialog.</summary>
        private sealed class NativeWindowWrapper : System.Windows.Forms.IWin32Window
        {
            private readonly IntPtr _handle;
            public NativeWindowWrapper(IntPtr handle) { _handle = handle; }
            public IntPtr Handle => _handle;
        }
    }
}
