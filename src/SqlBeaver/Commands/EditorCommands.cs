using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Analysis;
using SqlBeaver.Diagnostics;
using SqlBeaver.Environments;
using SqlBeaver.Navigation;
using SqlBeaver.Refactoring;
using SqlBeaver.Session;

namespace SqlBeaver.Commands
{
    /// <summary>
    /// Handlers canônicos dos comandos do editor SQL. Chamados tanto pelos botões
    /// de CommandBar (menu de contexto) quanto pelos comandos nomeados VSCT
    /// (menu Tools > SQL Beaver, toolbar e atalhos de teclado).
    /// Cada método é autocontido: assert de UI thread, try/catch, Log e status bar.
    /// </summary>
    internal static class EditorCommands
    {
        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }

        public static void FormatDocument()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Format: nenhum documento ativo."); return; }

                bool hasSelection = !doc.Selection.IsEmpty;
                string original = hasSelection
                    ? doc.Selection.Text
                    : doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                if (string.IsNullOrWhiteSpace(original)) { ShowStatus("Format: nada para formatar."); return; }

                Formatting.FormatOptions activeOptions = Formatting.FormatStyleStore.GetActiveOptions();
                if (!Formatting.SqlFormatterService.TryFormat(original, activeOptions, out string formatted, out string error, out bool containsComments))
                {
                    ShowStatus("não formatado: " + error);
                    Log.Info("Format Document abortado: " + error);
                    return;
                }

                if (containsComments)
                {
                    var owner = new System.Windows.Forms.NativeWindow();
                    owner.AssignHandle((IntPtr)(int)dte.MainWindow.HWnd);
                    System.Windows.Forms.DialogResult keep;
                    try
                    {
                        keep = System.Windows.Forms.MessageBox.Show(
                            owner,
                            "O script contém comentários e a formatação vai REMOVÊ-LOS.\r\n\r\nFormatar mesmo assim?",
                            "SQL Beaver — Format Document",
                            System.Windows.Forms.MessageBoxButtons.YesNo,
                            System.Windows.Forms.MessageBoxIcon.Warning,
                            System.Windows.Forms.MessageBoxDefaultButton.Button2);
                    }
                    finally
                    {
                        owner.ReleaseHandle();
                    }
                    if (keep != System.Windows.Forms.DialogResult.Yes)
                    {
                        ShowStatus("formatação cancelada (comentários seriam removidos).");
                        return;
                    }
                }

                dte.UndoContext.Open("SQL Beaver Format Document");
                try
                {
                    if (hasSelection)
                    {
                        doc.Selection.Insert(formatted,
                            (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    }
                    else
                    {
                        EditPoint start = doc.StartPoint.CreateEditPoint();
                        start.ReplaceText(doc.EndPoint, formatted,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus("documento formatado.");
                Log.Info("Format Document aplicado" + (hasSelection ? " (seleção)." : " (documento inteiro)."));
            }
            catch (Exception ex)
            {
                Log.Error("Format Document", ex);
                ShowStatus("falha no Format Document — veja Output > SQL Beaver");
            }
        }

        public static void GoToDefinition()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                DefinitionService.GoToDefinition();
            }
            catch (Exception ex)
            {
                Log.Error("Ir para definição", ex);
                ShowStatus("falha em Ir para definição — veja Output > SQL Beaver");
            }
        }

        public static void FindObject()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                FindObjectDialog.Show();
            }
            catch (Exception ex)
            {
                Log.Error("Localizar objeto", ex);
                ShowStatus("falha em Localizar objeto — veja Output > SQL Beaver");
            }
        }

        public static void FindReferences()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                DefinitionService.FindReferences();
            }
            catch (Exception ex)
            {
                Log.Error("Localizar referências", ex);
                ShowStatus("falha em Localizar referências — veja Output > SQL Beaver");
            }
        }

        public static void QueryHistory()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                QueryHistoryService.OpenTodayFile();
            }
            catch (Exception ex)
            {
                Log.Error("Histórico de consultas", ex);
                ShowStatus("falha em Histórico de consultas — veja Output > SQL Beaver");
            }
        }

        public static void RecoverSession()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                SessionSnapshotService.ShowRecoveryDialog();
            }
            catch (Exception ex)
            {
                Log.Error("Recuperar consultas", ex);
                ShowStatus("falha em Recuperar consultas — veja Output > SQL Beaver");
            }
        }

        public static void Environments()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                EnvironmentsDialog.Show();
            }
            catch (Exception ex)
            {
                Log.Error("Ambientes (cores)", ex);
                ShowStatus("falha ao abrir editor de ambientes — veja Output > SQL Beaver");
            }
        }

        public static void InsertColumns()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Inserir colunas: nenhum documento ativo."); return; }

                string text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
                TextSelection sel = doc.Selection;
                string textUpToCaret = doc.StartPoint.CreateEditPoint().GetText(sel.ActivePoint);
                int caretOffset = textUpToCaret.Length;

                Metadata.DbMetadata metadata = GetMetadataForCommands(dte);
                if (metadata == null) { ShowStatus("Inserir colunas: cache ainda carregando — tente novamente."); return; }

                System.Collections.Generic.IReadOnlyList<Analysis.TableRef> scope =
                    Analysis.StatementScopeAnalyzer.GetTablesInScope(text, caretOffset);
                if (scope == null || scope.Count == 0)
                { ShowStatus("Inserir colunas: nenhuma tabela no escopo do statement."); return; }

                string resultText;
                IntPtr hwnd;
                try { hwnd = new IntPtr((int)dte.MainWindow.HWnd); }
                catch { hwnd = IntPtr.Zero; }
                var owner = new System.Windows.Forms.NativeWindow();
                owner.AssignHandle(hwnd);
                try
                {
                    using (var dlg = new Completion.InsertColumnsDialog(scope, metadata))
                    {
                        var ownerWin = new NativeWindowWrapper(hwnd);
                        if (dlg.ShowDialog(ownerWin) != System.Windows.Forms.DialogResult.OK) return;
                        resultText = dlg.ResultText;
                    }
                }
                finally
                {
                    owner.ReleaseHandle();
                }

                if (string.IsNullOrEmpty(resultText)) return;

                dte.UndoContext.Open("SQL Beaver Inserir colunas");
                try
                {
                    doc.Selection.Insert(resultText,
                        (int)vsInsertFlags.vsInsertFlagsContainNewText);
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus("colunas inseridas.");
            }
            catch (Exception ex)
            {
                Log.Error("Inserir colunas", ex);
                ShowStatus("falha em Inserir colunas — veja Output > SQL Beaver");
            }
        }

        public static void RunCurrentStatement()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("nenhum documento ativo."); return; }

                // Read full document text (CRLF as-is — TextPosition expects the same string).
                string text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                // Caret absolute offset = comprimento do texto do início do documento até o caret.
                // Conta CRLF e tabs como chars crus, exatamente como 'text' — correto por construção
                // (mesmo padrão dos demais comandos de refatoração; DisplayColumn expandiria tabs e
                // selecionaria/executaria o statement errado em scripts indentados com tab).
                TextSelection sel = doc.Selection;
                int caretOffset = doc.StartPoint.CreateEditPoint().GetText(sel.ActivePoint).Length;

                StatementBounds bounds = StatementScopeAnalyzer.GetStatementBoundsAt(text, caretOffset);

                if (bounds.Length == 0)
                {
                    ShowStatus("nenhum statement sob o cursor.");
                    return;
                }

                // Convert start and end offsets → 1-based line/col for MoveToLineAndOffset
                TextPosition.FromOffset(text, bounds.Start, out int startLine, out int startCol);
                TextPosition.FromOffset(text, bounds.Start + bounds.Length, out int endLine, out int endCol);

                // Select the statement range in the editor
                sel.MoveToLineAndOffset(startLine, startCol, Extend: false);
                sel.MoveToLineAndOffset(endLine, endCol, Extend: true);

                // Fire SSMS execute — ExecuteGuard intercepts this and handles confirmation/history
                dte.ExecuteCommand("Query.Execute");

                Log.Info($"RunCurrentStatement: executado statement em L{startLine}–L{endLine}.");
            }
            catch (Exception ex)
            {
                Log.Error("Executar statement atual", ex);
                ShowStatus("falha em Executar statement atual — veja Output > SQL Beaver");
            }
        }

        public static void AnalyzeScript()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Analisar script: nenhum documento ativo."); return; }

                string text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
                if (string.IsNullOrWhiteSpace(text)) { ShowStatus("Analisar script: documento vazio."); return; }

                var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true);
                System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
                Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment;
                using (var reader = new System.IO.StringReader(text))
                    fragment = parser.Parse(reader, out errors);

                if (errors != null && errors.Count > 0)
                {
                    var first = errors[0];
                    ShowStatus("Analisar script: erro de sintaxe na linha " + first.Line + " — " + first.Message);
                    return;
                }

                var ruleSet = Linting.LintRuleSet.CreateDefault();
                System.Collections.Generic.IReadOnlyList<Linting.LintDiagnostic> diags =
                    ruleSet.Inspect(fragment, Linting.LintSettingsStore.DisabledRuleIds);

                string report = Linting.LintReportFormatter.Format(
                    diags as System.Collections.Generic.IReadOnlyList<Linting.LintDiagnostic>
                    ?? new System.Collections.Generic.List<Linting.LintDiagnostic>(diags));

                Navigation.DefinitionService.OpenNewQueryWindow(report);
                ShowStatus("análise concluída: " + diags.Count + " aviso(s).");
                Log.Info("Analisar script: " + diags.Count + " diagnósticos.");
            }
            catch (Exception ex)
            {
                Log.Error("Analisar script", ex);
                ShowStatus("falha em Analisar script — veja Output > SQL Beaver");
            }
        }

        public static void InvalidObjects()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Navigation.InvalidObjectsService.Run();
            }
            catch (Exception ex)
            {
                Log.Error("Objetos inválidos", ex);
                ShowStatus("falha em Objetos inválidos — veja Output > SQL Beaver");
            }
        }

        public static void ManageFormatStyles()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                IntPtr hwnd;
                try { hwnd = new IntPtr((int)dte.MainWindow.HWnd); }
                catch { hwnd = IntPtr.Zero; }
                var owner = new NativeWindowWrapper(hwnd);
                Formatting.ManageFormatStylesDialog.ShowManager(owner);
            }
            catch (Exception ex)
            {
                Log.Error("Gerenciar estilos de formatação", ex);
                ShowStatus("falha ao abrir gerenciador de estilos — veja Output > SQL Beaver");
            }
        }

        public static void ManageSnippets()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                IntPtr hwnd;
                try { hwnd = new IntPtr((int)dte.MainWindow.HWnd); }
                catch { hwnd = IntPtr.Zero; }
                var owner = new NativeWindowWrapper(hwnd);
                Snippets.SnippetsManagerDialog.ShowManager(owner);
            }
            catch (Exception ex)
            {
                Log.Error("Snippets", ex);
                ShowStatus("falha ao abrir gerenciador de snippets — veja Output > SQL Beaver");
            }
        }

        public static void SummarizeScript()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Summarize Script: nenhum documento ativo."); return; }

                string text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
                if (string.IsNullOrWhiteSpace(text)) { ShowStatus("Summarize Script: documento vazio."); return; }

                System.Collections.Generic.IReadOnlyList<Analysis.OutlineItem> items =
                    Analysis.ScriptOutlineBuilder.Build(text);
                if (items.Count == 0) { ShowStatus("Summarize Script: nenhum statement encontrado."); return; }

                IntPtr hwnd;
                try { hwnd = new IntPtr((int)dte.MainWindow.HWnd); }
                catch { hwnd = IntPtr.Zero; }
                var owner = new NativeWindowWrapper(hwnd);
                Analysis.SummarizeScriptDialog.Show(items, doc, owner);

                ShowStatus($"estrutura: {items.Count} statement(s).");
            }
            catch (Exception ex)
            {
                Log.Error("Summarize Script", ex);
                ShowStatus("falha em Summarize Script — veja Output > SQL Beaver");
            }
        }

        public static void SetFormatStyle(string styleName)
        {
            try
            {
                Formatting.FormatStyleStore.SetActive(styleName);
                ShowStatus($"estilo de formatação ativo: {styleName}");
            }
            catch (Exception ex)
            {
                Log.Error("Trocar estilo de formatação", ex);
                ShowStatus("falha ao trocar estilo — veja Output > SQL Beaver");
            }
        }

        // ---------------------------------------------------------------
        // Shared helpers
        // ---------------------------------------------------------------

        private static Metadata.DbMetadata GetMetadataForCommands(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Connection.ActiveConnection conn = Connection.ConnectionService.GetActiveConnection();
            if (conn == null) return null;
            return Completion.SqlBeaverCompletionSourceProvider.Cache.TryGet(
                conn.Server, conn.Database,
                new Metadata.MetadataRequest
                {
                    ConnectionString       = conn.ConnectionString,
                    AccessToken            = conn.AccessToken,
                    ProviderConnectionType = conn.ProviderConnectionType,
                });
        }

        private sealed class NativeWindowWrapper : System.Windows.Forms.IWin32Window
        {
            private readonly IntPtr _handle;
            public NativeWindowWrapper(IntPtr handle) { _handle = handle; }
            public IntPtr Handle => _handle;
        }
    }
}
