using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Analysis;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Editing
{
    /// <summary>
    /// Converte keywords T-SQL para maiúsculas ao digitar o separador seguinte
    /// (espaço, parêntese, vírgula, ponto e vírgula, igual ou Enter).
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver keyword case")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class KeywordCaseCommandHandler :
        IChainedCommandHandler<TypeCharCommandArgs>,
        IChainedCommandHandler<ReturnKeyCommandArgs>
    {
        private const int MaxAnalysisWindow = 64 * 1024;

        public string DisplayName => "SQL Beaver keyword case";

        public CommandState GetCommandState(TypeCharCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            nextCommandHandler(); // insere o caractere primeiro

            try
            {
                char typed = args.TypedChar;
                if (typed != ' ' && typed != '\t' && typed != '(' && typed != ')' &&
                    typed != ',' && typed != ';' && typed != '=')
                    return;

                FixKeywordBeforeCaret(args.TextView);
            }
            catch (Exception ex)
            {
                Log.Error("KeywordCaseCommandHandler (TypeChar)", ex);
            }
        }

        public CommandState GetCommandState(ReturnKeyCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(ReturnKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            nextCommandHandler(); // quebra a linha primeiro

            try
            {
                FixKeywordBeforeCaret(args.TextView);
            }
            catch (Exception ex)
            {
                Log.Error("KeywordCaseCommandHandler (Return)", ex);
            }
        }

        private static void FixKeywordBeforeCaret(ITextView textView)
        {
            SnapshotPoint caret = textView.Caret.Position.BufferPosition;
            int windowStart = Math.Max(0, caret.Position - MaxAnalysisWindow);
            string text = caret.Snapshot.GetText(windowStart, caret.Position - windowStart);

            if (!KeywordCaseFixer.TryGetReplacement(text, out int wordStart, out int wordLength, out string replacement))
                return;

            using (ITextEdit edit = textView.TextBuffer.CreateEdit())
            {
                edit.Replace(windowStart + wordStart, wordLength, replacement);
                edit.Apply();
            }
        }
    }
}
