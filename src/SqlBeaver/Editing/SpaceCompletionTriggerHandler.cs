using System;
using System.ComponentModel.Composition;
using System.Threading;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Analysis;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Editing
{
    /// <summary>
    /// Abre a lista de conclusão automaticamente ao digitar ESPAÇO logo após uma palavra-chave
    /// que justifica sugestão (FROM/JOIN/INTO/UPDATE → tabelas; SELECT/WHERE/ON/… → colunas;
    /// EXEC → procs; USE → bancos). O editor do VS só auto-dispara completion em caractere de
    /// identificador ou Ctrl+Espaço — nunca no espaço — então, sem isto, "SELECT * FROM " ficava
    /// sem popup até o usuário digitar uma letra. Espelha o comportamento do SQL Prompt.
    ///
    /// Tudo defensivo: qualquer falha apenas não abre o popup, nunca lança no editor.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver auto-trigger após espaço")]
    [Order(After = PredefinedCompletionNames.CompletionCommandHandler)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class SpaceCompletionTriggerHandler : IChainedCommandHandler<TypeCharCommandArgs>
    {
        private const int Window = 64 * 1024;

        private readonly IAsyncCompletionBroker _broker;

        [ImportingConstructor]
        public SpaceCompletionTriggerHandler(IAsyncCompletionBroker broker)
        {
            _broker = broker;
        }

        public string DisplayName => "SQL Beaver auto-trigger após espaço";

        public CommandState GetCommandState(TypeCharCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            // Insere o espaço normalmente primeiro.
            nextCommandHandler();

            if (args.TypedChar != ' ')
                return;

            try
            {
                ITextView view = args.TextView;
                if (_broker.IsCompletionActive(view))
                    return; // já há popup aberto: não reabrir

                SnapshotPoint caret = view.Caret.Position.BufferPosition;
                ITextSnapshot snap = caret.Snapshot;
                int pos = caret.Position;
                int ws = Math.Max(0, pos - Window);
                string before = snap.GetText(ws, pos - ws);

                SqlContext context = SqlContextAnalyzer.Analyze(before, before.Length);
                if (!ShouldAutoPopup(context.Kind))
                    return;

                if (ConnectionService.GetActiveConnection() == null)
                    return;

                CancellationToken token = executionContext.OperationContext.UserCancellationToken;

                // typedChar = ' ' para que InitializeCompletion trate como whitespace e participe.
                var trigger = new CompletionTrigger(CompletionTriggerReason.Insertion, snap, ' ');
                IAsyncCompletionSession session = _broker.TriggerCompletion(view, trigger, caret, token);
                session?.OpenOrUpdate(trigger, caret, token);
            }
            catch (Exception ex)
            {
                Log.Error("SpaceCompletionTriggerHandler", ex);
            }
        }

        // Contextos pós-keyword onde uma lista é útil ao digitar espaço.
        private static bool ShouldAutoPopup(SqlContextKind kind)
            => kind == SqlContextKind.AfterFromJoin
            || kind == SqlContextKind.AfterJoin
            || kind == SqlContextKind.AfterExec
            || kind == SqlContextKind.AfterUse
            || kind == SqlContextKind.ColumnContext;
    }
}
