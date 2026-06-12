using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Ai;
using SqlBeaver.Commands;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Editing
{
    /// <summary>
    /// Ao pressionar Enter numa linha de comentário <c>--</c> com instrução real, dispara
    /// a geração de SQL pela IA (configurável; ligado por padrão). O newline é SEMPRE inserido
    /// primeiro (chama nextCommandHandler antes de qualquer lógica). Tudo é defensivo: nada
    /// pode lançar no editor. Ordenado após os handlers de completion/keyword-case.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver IA Enter")]
    [Order(After = "SQL Beaver keyword case")]
    [Order(After = PredefinedCompletionNames.CompletionCommandHandler)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class AiEnterCommandHandler : IChainedCommandHandler<ReturnKeyCommandArgs>
    {
        public string DisplayName => "SQL Beaver IA Enter";

        public CommandState GetCommandState(ReturnKeyCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(ReturnKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            // O newline é inserido SEMPRE primeiro — independe da lógica de IA.
            nextCommandHandler();

            try
            {
                MaybeTriggerGenerate(args.TextView);
            }
            catch (Exception ex)
            {
                // O newline já aconteceu; nunca lançar do handler de Enter.
                Log.Error("AiEnterCommandHandler", ex);
            }
        }

        private static void MaybeTriggerGenerate(ITextView textView)
        {
            if (textView == null) return;

            // Configurada, auto-gen ligado e nenhuma geração em andamento.
            if (!AiConfigStore.IsConfigured()) return;
            if (!AiConfigResolver.AutoGenerateOnEnter(AiConfigStore.Load())) return;
            if (EditorCommands.AiBusy) return;

            // Linha IMEDIATAMENTE ACIMA do caret (o newline já foi inserido, então o caret está
            // na nova linha; a linha do comentário é a anterior).
            SnapshotPoint caret = textView.Caret.Position.BufferPosition;
            ITextSnapshot snapshot = caret.Snapshot;
            ITextSnapshotLine caretLine = caret.GetContainingLine();
            int aboveLineNumber = caretLine.LineNumber - 1;
            if (aboveLineNumber < 0) return;

            string aboveText = snapshot.GetLineFromLineNumber(aboveLineNumber).GetText();
            if (!CommentTriggerDetector.IsTriggerCommentLine(aboveText)) return;

            // Dispara a geração canônica (lê o doc ativo via DTE, acha o comentário e insere o SQL).
            EditorCommands.AiGenerateFromComment();
        }
    }
}
