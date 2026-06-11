using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
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
    // ─── Sessão de placeholders ──────────────────────────────────────────────

    /// <summary>
    /// Estado de uma sessão de navegação entre placeholders após uma expansão de snippet.
    /// Cada PlaceholderGroup representa uma ordem (1, 2, ... e por último 0).
    /// </summary>
    internal sealed class SnippetSession
    {
        /// <summary>Grupos de tracking spans ordenados pela ordem de visita (1,2,...,9 depois 0).</summary>
        public List<PlaceholderGroup> Groups { get; } = new List<PlaceholderGroup>();

        /// <summary>Índice atual em Groups.</summary>
        public int CurrentIndex { get; set; }

        public bool HasNext => CurrentIndex < Groups.Count - 1;
        public bool HasPrev => CurrentIndex > 0;

        public PlaceholderGroup Current => Groups.Count > 0 ? Groups[CurrentIndex] : null;

        // ── Dados de lifetime para desinscrição de eventos ────────────────────
        /// <summary>View à qual os eventos foram associados.</summary>
        public ITextView TextView { get; set; }

        /// <summary>Handler inscrito em <see cref="ITextCaret.PositionChanged"/>.</summary>
        public EventHandler<CaretPositionChangedEventArgs> CaretHandler { get; set; }

        /// <summary>Handler inscrito em <see cref="ITextBuffer.Changed"/>.</summary>
        public EventHandler<TextContentChangedEventArgs> BufferHandler { get; set; }
    }

    /// <summary>Conjunto de tracking spans que representam um mesmo placeholder (mirror fields).</summary>
    internal sealed class PlaceholderGroup
    {
        public int Order { get; }
        public List<ITrackingSpan> Spans { get; } = new List<ITrackingSpan>();

        public PlaceholderGroup(int order) => Order = order;
    }

    // ─── Command Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Expande snippets no Tab (ssf → SELECT * FROM |) e navega entre placeholders $1$/$2$/$0$.
    ///
    /// PLAN B documentado: se a navegação multi-span se mostrar instável, o fallback é
    /// apenas posicionar o caret no placeholder de menor ordem e inserir os defaults como literal,
    /// sem sessão interativa. Isso é melhor que o comportamento pré-v4 e é o fallback seguro.
    /// Nesta implementação foi realizada a versão completa (sessão com ITrackingSpan).
    ///
    /// Shift+Tab (BackTabKeyCommandArgs) é implementado se o tipo estiver disponível em tempo de compilação.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("SQL Beaver snippets")]
    [Order(After = PredefinedCompletionNames.CompletionCommandHandler)]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class SnippetCommandHandler :
        IChainedCommandHandler<TabKeyCommandArgs>,
        IChainedCommandHandler<EscapeKeyCommandArgs>,
        IChainedCommandHandler<BackTabKeyCommandArgs>
    {
        private const int MaxAnalysisWindow = 64 * 1024;

        /// <summary>Chave para armazenar a sessão nas Properties do ITextView.</summary>
        private static readonly object SessionKey = new object();

        private readonly IAsyncCompletionBroker _completionBroker;

        [ImportingConstructor]
        public SnippetCommandHandler(IAsyncCompletionBroker completionBroker)
        {
            _completionBroker = completionBroker;
        }

        public string DisplayName => "SQL Beaver snippets";

        // ── Tab ──────────────────────────────────────────────────────────────

        public CommandState GetCommandState(TabKeyCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(TabKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            bool nextCalled = false;
            bool editApplied = false;
            try
            {
                // Se há completion popup aberta, deixar o Tab confirmar o item
                if (_completionBroker.GetSession(args.TextView) != null)
                {
                    nextCalled = true;
                    nextCommandHandler();
                    return;
                }

                // Se há sessão ativa, navegar para o próximo placeholder
                if (TryNavigateNext(args.TextView))
                    return;

                // Tentar expandir um snippet
                if (TryExpandSnippet(args.TextView, ref editApplied))
                    return;

                nextCalled = true;
                nextCommandHandler();
            }
            catch (Exception ex)
            {
                Log.Error("SnippetCommandHandler.Tab", ex);
                EndSession(args.TextView);
                if (!nextCalled && !editApplied)
                    nextCommandHandler();
            }
        }

        // ── Escape ───────────────────────────────────────────────────────────

        public CommandState GetCommandState(EscapeKeyCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(EscapeKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            try
            {
                // Sempre encerrar a sessão se houver uma ativa, mas repassar o Esc normalmente
                EndSession(args.TextView);
            }
            catch (Exception ex)
            {
                Log.Error("SnippetCommandHandler.Escape", ex);
            }
            finally
            {
                nextCommandHandler();
            }
        }

        // ── Shift+Tab (BackTab) ───────────────────────────────────────────────

        public CommandState GetCommandState(BackTabKeyCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(BackTabKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            try
            {
                if (TryNavigatePrev(args.TextView))
                    return;
            }
            catch (Exception ex)
            {
                Log.Error("SnippetCommandHandler.BackTab", ex);
                EndSession(args.TextView);
            }
            nextCommandHandler();
        }

        // ── Navegação de sessão ───────────────────────────────────────────────

        private static bool TryNavigateNext(ITextView textView)
        {
            SnippetSession session = GetSession(textView);
            if (session == null) return false;

            if (session.HasNext)
            {
                session.CurrentIndex++;
                SelectCurrentGroup(textView, session);
                return true;
            }
            else
            {
                // Último placeholder: posiciona o caret e encerra
                SelectCurrentGroup(textView, session);
                EndSession(textView);
                return true;
            }
        }

        private static bool TryNavigatePrev(ITextView textView)
        {
            SnippetSession session = GetSession(textView);
            if (session == null) return false;
            if (!session.HasPrev) return false;

            session.CurrentIndex--;
            SelectCurrentGroup(textView, session);
            return true;
        }

        private static void SelectCurrentGroup(ITextView textView, SnippetSession session)
        {
            PlaceholderGroup group = session.Current;
            if (group == null || group.Spans.Count == 0) return;

            ITextSnapshot snapshot = textView.TextBuffer.CurrentSnapshot;
            ITrackingSpan first = group.Spans[0];
            SnapshotSpan span = first.GetSpan(snapshot);

            // Selecionar o primeiro span do grupo e mover o caret para o final da seleção
            textView.Selection.Select(span, false);
            textView.Caret.MoveTo(span.End);
        }

        // ── Expansão de snippet ───────────────────────────────────────────────

        private bool TryExpandSnippet(ITextView textView, ref bool editApplied)
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

            // Calcular a posição do caret
            SnapshotPoint caretPoint = new SnapshotPoint(afterEdit, replaceStart + expansion.CaretOffset);
            textView.Caret.MoveTo(caretPoint);

            // Criar sessão de navegação se houver placeholders editáveis (ordem ≠ 0)
            TryStartSession(textView, afterEdit, replaceStart, expansion);

            return true;
        }

        private static void TryStartSession(
            ITextView textView,
            ITextSnapshot snapshot,
            int replaceStart,
            SnippetExpansion expansion)
        {
            // Verifica se há placeholders de edição (ordem 1-9)
            bool hasEditable = expansion.Placeholders.Any(p => p.Order > 0);
            if (!hasEditable) return;

            // Agrupa por ordem, visitando 1..9 e depois 0
            var byOrder = expansion.Placeholders
                .GroupBy(p => p.Order)
                .ToDictionary(g => g.Key, g => g.ToList());

            var visitOrder = Enumerable.Range(1, 9)
                .Where(o => byOrder.ContainsKey(o))
                .ToList();

            // Adicionar placeholder 0 (posição final) se existir
            if (byOrder.ContainsKey(0))
                visitOrder.Add(0);

            if (visitOrder.Count == 0) return;

            var session = new SnippetSession();

            foreach (int order in visitOrder)
            {
                var group = new PlaceholderGroup(order);
                foreach (var ph in byOrder[order])
                {
                    int spanStart = replaceStart + ph.Offset;
                    int spanLength = ph.Length;
                    ITrackingSpan ts = snapshot.CreateTrackingSpan(
                        spanStart, spanLength,
                        SpanTrackingMode.EdgeInclusive);
                    group.Spans.Add(ts);
                }
                session.Groups.Add(group);
            }

            if (session.Groups.Count == 0) return;

            // Começa no grupo de ordem 1 (já está posicionado pelo expand; índice 0)
            session.CurrentIndex = 0;

            // Selecionar o primeiro grupo
            SelectCurrentGroup(textView, session);

            // Armazenar sessão e conectar eventos de cancelamento
            StoreSession(textView, session);
        }

        // ── Gestão da sessão ──────────────────────────────────────────────────

        private static void StoreSession(ITextView textView, SnippetSession session)
        {
            // Remove sessão anterior se houver (desinscreve eventos da sessão antiga)
            EndSession(textView);

            // Criar delegates nomeados para poder desinscrever depois
            EventHandler<CaretPositionChangedEventArgs> caretHandler =
                (s, e) => OnCaretPositionChanged(textView, e);
            EventHandler<TextContentChangedEventArgs> bufferHandler =
                (s, e) => OnBufferChanged(textView, e);

            // Salvar referências no objeto de sessão para desinscrição em EndSession
            session.TextView      = textView;
            session.CaretHandler  = caretHandler;
            session.BufferHandler = bufferHandler;

            textView.Properties[SessionKey] = session;

            // Cancelar sessão se o caret sair dos spans ou o buffer mudar fora de um span
            textView.Caret.PositionChanged += caretHandler;
            textView.TextBuffer.Changed    += bufferHandler;
        }

        private static SnippetSession GetSession(ITextView textView)
        {
            if (textView.Properties.TryGetProperty(SessionKey, out SnippetSession session))
                return session;
            return null;
        }

        private static void EndSession(ITextView textView)
        {
            if (textView.Properties.TryGetProperty(SessionKey, out SnippetSession session))
            {
                // Desinscrever os eventos antes de remover a sessão
                try
                {
                    if (session.CaretHandler != null)
                        textView.Caret.PositionChanged -= session.CaretHandler;
                }
                catch { /* nunca lançar de EndSession */ }

                try
                {
                    if (session.BufferHandler != null)
                        textView.TextBuffer.Changed -= session.BufferHandler;
                }
                catch { /* nunca lançar de EndSession */ }

                textView.Properties.RemoveProperty(SessionKey);
            }
        }

        private static void OnCaretPositionChanged(ITextView textView, CaretPositionChangedEventArgs e)
        {
            try
            {
                SnippetSession session = GetSession(textView);
                if (session == null) return;

                // Se o caret sair de todos os spans do grupo atual, encerrar sessão
                PlaceholderGroup current = session.Current;
                if (current == null) { EndSession(textView); return; }

                ITextSnapshot snapshot = textView.TextBuffer.CurrentSnapshot;
                int caretPos = e.NewPosition.BufferPosition.Position;

                bool inside = current.Spans.Any(ts =>
                {
                    SnapshotSpan span = ts.GetSpan(snapshot);
                    return caretPos >= span.Start && caretPos <= span.End;
                });

                if (!inside)
                    EndSession(textView);
            }
            catch
            {
                EndSession(textView);
            }
        }

        private static void OnBufferChanged(ITextView textView, TextContentChangedEventArgs e)
        {
            try
            {
                SnippetSession session = GetSession(textView);
                if (session == null) return;

                // Se houve uma edição e o caret não está dentro de um span do grupo atual → encerrar
                // (editando dentro do span é normal durante a sessão)
                PlaceholderGroup current = session.Current;
                if (current == null) { EndSession(textView); return; }

                ITextSnapshot snapshot = e.After;
                int caretPos = textView.Caret.Position.BufferPosition.Position;

                bool inside = current.Spans.Any(ts =>
                {
                    SnapshotSpan span = ts.GetSpan(snapshot);
                    return caretPos >= span.Start && caretPos <= span.End;
                });

                if (!inside)
                    EndSession(textView);
            }
            catch
            {
                EndSession(textView);
            }
        }
    }
}
