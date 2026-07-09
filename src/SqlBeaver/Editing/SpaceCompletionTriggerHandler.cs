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

                SnapshotPoint caret = view.Caret.Position.BufferPosition;
                ITextSnapshot snap = caret.Snapshot;
                int pos = caret.Position;
                int ws = Math.Max(0, pos - Window);
                string before = snap.GetText(ws, pos - ws);

                SqlContext context = SqlContextAnalyzer.Analyze(before, before.Length);
                if (!ShouldAutoPopup(context.Kind))
                    return;

                // ColumnContext sem tabela no escopo só ofereceria funções built-in (ruído) —
                // ex.: "SELECT " sem FROM. Não força popup; deixa o usuário digitar. Com FROM
                // (mesmo à frente do cursor) o escopo resolve e as colunas aparecem.
                if (context.Kind == SqlContextKind.ColumnContext)
                {
                    int posAfter = Math.Min(snap.Length, pos + Window);
                    string after = snap.GetText(pos, posAfter - pos);
                    var scope = StatementScopeAnalyzer.GetTablesInScope(before + after, before.Length);
                    if (scope == null || scope.Count == 0)
                        return;
                }

                if (ConnectionService.GetActiveConnection() == null)
                    return;

                // Sessão presa: ao digitar "SELECT" abre uma sessão FreeIdentifier (keywords) que
                // o VS mantém aberta enquanto se digita " * FROM " — só FILTRA a lista velha, nunca
                // recalcula para AfterFromJoin. Descarta a sessão antiga para reabrir no contexto certo.
                IAsyncCompletionSession existing = _broker.GetSession(view);
                if (existing != null)
                    existing.Dismiss();

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
