using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Diagnostics;
using SqlBeaver.Snippets;

namespace SqlBeaver.Editing
{
    /// <summary>Expande snippets no Tab (ssf → SELECT * FROM |). Não interfere quando
    /// há sessão de completion aberta (Tab confirma o item) nem fora de shortcut (Tab indenta).</summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver snippets")]
    [Order(After = PredefinedCompletionNames.CompletionCommandHandler)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class SnippetCommandHandler : IChainedCommandHandler<TabKeyCommandArgs>
    {
        private const int MaxAnalysisWindow = 64 * 1024;

        private readonly IAsyncCompletionBroker _completionBroker;

        [ImportingConstructor]
        public SnippetCommandHandler(IAsyncCompletionBroker completionBroker)
        {
            _completionBroker = completionBroker;
        }

        public string DisplayName => "SQL Beaver snippets";

        public CommandState GetCommandState(TabKeyCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(TabKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            bool nextCalled = false;
            bool editApplied = false;
            try
            {
                if (_completionBroker.GetSession(args.TextView) != null)
                {
                    nextCalled = true;
                    nextCommandHandler();
                    return;
                }

                if (TryExpandSnippet(args.TextView, ref editApplied))
                    return;

                nextCalled = true;
                nextCommandHandler();
            }
            catch (Exception ex)
            {
                Log.Error("SnippetCommandHandler", ex);
                // nunca duplicar o Tab: só repassa se ainda não repassou E a expansão não aplicou
                if (!nextCalled && !editApplied)
                    nextCommandHandler();
            }
        }

        private static bool TryExpandSnippet(ITextView textView, ref bool editApplied)
        {
            SnapshotPoint caret = textView.Caret.Position.BufferPosition;
            int windowStart = Math.Max(0, caret.Position - MaxAnalysisWindow);
            string text = caret.Snapshot.GetText(windowStart, caret.Position - windowStart);

            if (!SnippetEngine.TryExpand(text, SnippetStore.Catalog, out SnippetExpansion expansion))
                return false;

            int replaceStart = windowStart + expansion.WordStart;
            ITextSnapshot afterEdit;
            using (ITextEdit edit = textView.TextBuffer.CreateEdit())
            {
                edit.Replace(replaceStart, expansion.WordLength, expansion.ReplacementText);
                afterEdit = edit.Apply();
            }
            editApplied = true;

            textView.Caret.MoveTo(new SnapshotPoint(afterEdit, replaceStart + expansion.CaretOffset));
            return true;
        }
    }
}
