using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Diagnostics;
using SqlBeaver.Linting;

namespace SqlBeaver.Editing
{
    [Export(typeof(ITaggerProvider))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TagType(typeof(IErrorTag))]
    internal sealed class SqlLintTaggerProvider : ITaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
            => buffer.Properties.GetOrCreateSingletonProperty(() => new SqlLintTagger(buffer)) as ITagger<T>;
    }

    /// <summary>
    /// Squiggles de aviso de lint via ScriptDom AST em background com debounce (~750ms).
    /// Só emite avisos quando o documento NÃO tem erros de sintaxe (o syntax tagger cuida disso).
    /// Documentos > 200KB são ignorados.
    /// Nunca lança no caminho do editor.
    /// </summary>
    internal sealed class SqlLintTagger : ITagger<IErrorTag>
    {
        private const int MaxDocumentLength = 200 * 1024;
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

        private sealed class WarnEntry { public SnapshotSpan Span; public string Message; }

        private readonly ITextBuffer _buffer;
        private readonly DispatcherTimer _timer;
        private volatile IReadOnlyList<WarnEntry> _warnings = new List<WarnEntry>();
        private bool _parseQueued;
        private static bool _loggedTooBig;

        private static readonly LintRuleSet _ruleSet = LintRuleSet.CreateDefault();

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public SqlLintTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = Debounce };
            _timer.Tick += (s, e) => { _timer.Stop(); QueueParse(); };
            _buffer.Changed += (s, e) => { _timer.Stop(); _timer.Start(); };
            QueueParse();
        }

        private void QueueParse()
        {
            if (_parseQueued) return;
            _parseQueued = true;

            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            _ = Task.Run(() => ParseAndPublish(snapshot));
        }

        private void ParseAndPublish(ITextSnapshot snapshot)
        {
            try
            {
                var entries = new List<WarnEntry>();

                if (snapshot.Length <= MaxDocumentLength)
                {
                    foreach (var warn in LintWarnings(snapshot.GetText()))
                    {
                        int line = Math.Max(0, Math.Min(warn.Line - 1, snapshot.LineCount - 1));
                        ITextSnapshotLine snapshotLine = snapshot.GetLineFromLineNumber(line);
                        int start = Math.Min(snapshotLine.Start.Position + Math.Max(0, warn.Column - 1),
                            Math.Max(snapshotLine.Start.Position, snapshotLine.End.Position - 1));
                        int length = Math.Max(1, Math.Min(warn.Length, snapshotLine.End.Position - start));
                        if (start + length > snapshot.Length) length = Math.Max(1, snapshot.Length - start);
                        if (start >= snapshot.Length) continue;
                        entries.Add(new WarnEntry
                        {
                            Span    = new SnapshotSpan(snapshot, start, length),
                            Message = warn.Message,
                        });
                    }
                }
                else if (!_loggedTooBig)
                {
                    _loggedTooBig = true;
                    Log.Info("Lint pulado: documento acima de 200KB.");
                }

                // Single atomic publish
                _warnings = entries;
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(snapshot, 0, snapshot.Length)));
            }
            catch (Exception ex)
            {
                Log.Error("SqlLintTagger", ex);
            }
            finally
            {
                _parseQueued = false;
                // If the buffer changed while we were parsing, requeue immediately.
                if (_buffer.CurrentSnapshot != snapshot)
                    QueueParse();
            }
        }

        private struct LintWarn { public int Line; public int Column; public int Length; public string Message; }

        private static IEnumerable<LintWarn> LintWarnings(string sql)
        {
            // Tipos ScriptDom só em corpo de método (restrição MEF do SSMS)
            var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true);
            System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
            Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment;
            using (var reader = new System.IO.StringReader(sql))
            {
                fragment = parser.Parse(reader, out errors);
            }

            // If the document has syntax errors, emit nothing — the syntax tagger handles those.
            if (errors != null && errors.Count > 0)
                return System.Array.Empty<LintWarn>();

            IReadOnlyCollection<string> disabled = LintSettingsStore.DisabledRuleIds;
            IReadOnlyList<LintDiagnostic> diagnostics = _ruleSet.Inspect(fragment, disabled);

            var result = new List<LintWarn>(diagnostics.Count);
            foreach (LintDiagnostic d in diagnostics)
                result.Add(new LintWarn { Line = d.Line, Column = d.Column, Length = d.Length, Message = d.Message });
            return result;
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection requestedSpans)
        {
            if (requestedSpans.Count == 0) yield break;
            ITextSnapshot snapshot = requestedSpans[0].Snapshot;

            IReadOnlyList<WarnEntry> warnings = _warnings;

            for (int i = 0; i < warnings.Count; i++)
            {
                SnapshotSpan span = warnings[i].Span;
                if (span.Snapshot != snapshot)
                {
                    // Traduz para o snapshot pedido (edições desde o parse)
                    span = span.TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive);
                }
                if (requestedSpans.IntersectsWith(new NormalizedSnapshotSpanCollection(span)))
                    yield return new TagSpan<IErrorTag>(span,
                        new ErrorTag(PredefinedErrorTypeNames.Warning, warnings[i].Message));
            }
        }
    }
}
