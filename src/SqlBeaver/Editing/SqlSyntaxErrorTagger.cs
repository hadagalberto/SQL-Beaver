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

namespace SqlBeaver.Editing
{
    [Export(typeof(ITaggerProvider))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TagType(typeof(IErrorTag))]
    internal sealed class SqlSyntaxErrorTaggerProvider : ITaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
            => buffer.Properties.GetOrCreateSingletonProperty(() => new SqlSyntaxErrorTagger(buffer)) as ITagger<T>;
    }

    /// <summary>Squiggles de erro de sintaxe via parse ScriptDom em background com
    /// debounce (~750ms após a última mudança). Documentos > 200KB são ignorados.
    /// Nunca lança no caminho do editor.</summary>
    internal sealed class SqlSyntaxErrorTagger : ITagger<IErrorTag>
    {
        private const int MaxDocumentLength = 200 * 1024;
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

        private sealed class ErrorEntry { public SnapshotSpan Span; public string Message; }

        private readonly ITextBuffer _buffer;
        private readonly DispatcherTimer _timer;
        private volatile IReadOnlyList<ErrorEntry> _errors = new List<ErrorEntry>();
        private bool _parseQueued;
        private static bool _loggedTooBig;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public SqlSyntaxErrorTagger(ITextBuffer buffer)
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
                var entries = new List<ErrorEntry>();

                if (snapshot.Length <= MaxDocumentLength)
                {
                    foreach (var error in ParseErrors(snapshot.GetText()))
                    {
                        int line = Math.Max(0, Math.Min(error.Line - 1, snapshot.LineCount - 1));
                        ITextSnapshotLine snapshotLine = snapshot.GetLineFromLineNumber(line);
                        int start = Math.Min(snapshotLine.Start.Position + Math.Max(0, error.Column - 1),
                            Math.Max(snapshotLine.Start.Position, snapshotLine.End.Position - 1));
                        int length = Math.Max(1, Math.Min(snapshotLine.End.Position - start, 30));
                        if (start + length > snapshot.Length) length = Math.Max(1, snapshot.Length - start);
                        if (start >= snapshot.Length) continue;
                        entries.Add(new ErrorEntry
                        {
                            Span = new SnapshotSpan(snapshot, start, length),
                            Message = error.Message
                        });
                    }
                }
                else if (!_loggedTooBig)
                {
                    _loggedTooBig = true;
                    Log.Info("Checagem de sintaxe pulada: documento acima de 200KB.");
                }

                // Single atomic publish
                _errors = entries;
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(snapshot, 0, snapshot.Length)));
            }
            catch (Exception ex)
            {
                Log.Error("SqlSyntaxErrorTagger", ex);
            }
            finally
            {
                _parseQueued = false;
                // If the buffer changed while we were parsing, requeue immediately
                // so the last edit's errors are not silently dropped.
                if (_buffer.CurrentSnapshot != snapshot)
                    QueueParse();
            }
        }

        private struct ParsedError { public int Line; public int Column; public string Message; }

        private static IEnumerable<ParsedError> ParseErrors(string sql)
        {
            // tipos ScriptDom só em corpo de método (restrição MEF do SSMS)
            var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true);
            System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
            using (var reader = new System.IO.StringReader(sql))
            {
                parser.Parse(reader, out errors);
            }
            var result = new List<ParsedError>();
            if (errors != null)
            {
                foreach (var error in errors)
                    result.Add(new ParsedError { Line = error.Line, Column = error.Column, Message = error.Message });
            }
            return result;
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection requestedSpans)
        {
            if (requestedSpans.Count == 0) yield break;
            ITextSnapshot snapshot = requestedSpans[0].Snapshot;

            // Read the list once into a local — single atomic read of the volatile field.
            IReadOnlyList<ErrorEntry> errors = _errors;

            for (int i = 0; i < errors.Count; i++)
            {
                SnapshotSpan span = errors[i].Span;
                if (span.Snapshot != snapshot)
                {
                    // traduz para o snapshot pedido (edições desde o parse)
                    span = span.TranslateTo(snapshot, SpanTrackingMode.EdgeExclusive);
                }
                if (requestedSpans.IntersectsWith(new NormalizedSnapshotSpanCollection(span)))
                    yield return new TagSpan<IErrorTag>(span,
                        new ErrorTag(PredefinedErrorTypeNames.SyntaxError, errors[i].Message));
            }
        }
    }
}
