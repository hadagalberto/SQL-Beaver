using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Editing
{
    /// <summary>
    /// Auto-fecha aspa simples (<c>'</c>): ao digitar <c>'</c> insere o par e deixa o cursor no meio.
    /// Pula por cima quando o próximo char já é a aspa de fechamento; não pareia dentro de
    /// string/comentário. Nunca quebra a digitação — toda falha cai no comportamento normal.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver auto-close quote")]
    [Order(After = PredefinedCompletionNames.CompletionCommandHandler)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class AutoCloseQuoteHandler : IChainedCommandHandler<TypeCharCommandArgs>
    {
        private const int Back = 64 * 1024;
        private const int Fwd = 1024;

        public string DisplayName => "SQL Beaver auto-close quote";

        public CommandState GetCommandState(TypeCharCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            if (args.TypedChar != '\'')
            {
                nextCommandHandler();
                return;
            }

            QuoteAction action = QuoteAction.None;
            try
            {
                ITextView view = args.TextView;
                SnapshotPoint caret = view.Caret.Position.BufferPosition;
                ITextSnapshot snap = caret.Snapshot;
                int pos = caret.Position;

                int ws = Math.Max(0, pos - Back);
                int we = Math.Min(snap.Length, pos + Fwd);
                string text = snap.GetText(ws, we - ws);
                action = AutoCloseQuote.Decide(text, pos - ws);

                if (action == QuoteAction.SkipOver)
                {
                    // Cursor pula a aspa de fechamento; não insere nada.
                    view.Caret.MoveTo(new SnapshotPoint(snap, pos + 1));
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("AutoCloseQuoteHandler (decisão)", ex);
                action = QuoteAction.None;
            }

            // Insere a aspa digitada normalmente.
            nextCommandHandler();

            if (action != QuoteAction.InsertPair)
                return;

            try
            {
                ITextView view = args.TextView;
                int insertAt = view.Caret.Position.BufferPosition.Position;
                using (ITextEdit edit = view.TextBuffer.CreateEdit())
                {
                    edit.Insert(insertAt, "'");
                    edit.Apply();
                }
                // Cursor volta para ANTES da aspa de fechamento (entre o par).
                ITextSnapshot s2 = view.TextBuffer.CurrentSnapshot;
                if (insertAt <= s2.Length)
                    view.Caret.MoveTo(new SnapshotPoint(s2, insertAt));
            }
            catch (Exception ex)
            {
                Log.Error("AutoCloseQuoteHandler (par)", ex);
            }
        }
    }
}
