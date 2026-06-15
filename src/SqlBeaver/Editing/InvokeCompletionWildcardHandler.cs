using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Analysis;
using SqlBeaver.Commands;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Editing
{
    /// <summary>
    /// Ctrl+Espaço com o cursor ao lado de um <c>*</c> (ou <c>alias.*</c>) num SELECT abre o
    /// seletor de colunas (Inserir colunas) com o escopo do statement e, no OK, SUBSTITUI o
    /// <c>*</c> pelas colunas escolhidas (uma por linha). Em qualquer outro contexto, o
    /// completion normal segue (<c>nextCommandHandler</c>). Tudo defensivo: qualquer falha
    /// degrada para o completion normal — nunca lança no editor.
    ///
    /// O comando do Ctrl+Espaço é <see cref="InvokeCompletionListCommandArgs"/> (binding nativo
    /// do editor para "Invocar lista de conclusão"). Ordenado ANTES do handler de completion
    /// para poder interceptar o '*' antes da UI de conclusão aparecer.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver Ctrl+Espaço no *")]
    [Order(Before = PredefinedCompletionNames.CompletionCommandHandler)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class InvokeCompletionWildcardHandler :
        IChainedCommandHandler<InvokeCompletionListCommandArgs>
    {
        public string DisplayName => "SQL Beaver Ctrl+Espaço no *";

        public CommandState GetCommandState(InvokeCompletionListCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(InvokeCompletionListCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            try
            {
                if (!IsCaretAdjacentToWildcard(args.TextView))
                {
                    nextCommandHandler();
                    return;
                }

                // É um '*' aplicável: tenta abrir o seletor de colunas.
                bool handled = EditorCommands.PickColumnsForWildcard();
                if (handled)
                    return; // consome o Ctrl+Espaço (não chama o completion normal)

                // Sem metadata/escopo/etc.: cai para o completion normal.
                nextCommandHandler();
            }
            catch (Exception ex)
            {
                Log.Error("InvokeCompletionWildcardHandler", ex);
                nextCommandHandler();
            }
        }

        /// <summary>
        /// True quando o char em caret-1 ou em caret é um <c>*</c> e não está dentro de
        /// comentário/string. Janela limitada ao redor do caret para custo constante.
        /// </summary>
        private static bool IsCaretAdjacentToWildcard(ITextView textView)
        {
            if (textView == null) return false;

            SnapshotPoint caret = textView.Caret.Position.BufferPosition;
            ITextSnapshot snapshot = caret.Snapshot;
            int caretPos = caret.Position;

            bool starAtCaret = caretPos < snapshot.Length && snapshot[caretPos] == '*';
            bool starBeforeCaret = caretPos > 0 && snapshot[caretPos - 1] == '*';
            if (!starAtCaret && !starBeforeCaret) return false;

            int starPos = starAtCaret ? caretPos : caretPos - 1;

            // Janela final até o '*' (inclusive) para classificar comentário/string.
            const int Window = 64 * 1024;
            int windowStart = Math.Max(0, starPos - Window);
            string text = snapshot.GetText(windowStart, (starPos - windowStart) + 1);
            int starInWindow = starPos - windowStart;

            return !SqlContextAnalyzer.IsInsideCommentOrStringAt(text, starInWindow);
        }
    }
}
