using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Analysis;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Editing
{
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [TagType(typeof(ITextMarkerTag))]
    internal sealed class BlockMatchTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null) return null;
            return textView.Properties.GetOrCreateSingletonProperty(
                typeof(BlockMatchTagger),
                () => new BlockMatchTagger(textView)) as ITagger<T>;
        }
    }

    /// <summary>
    /// Realça o par BEGIN/END correspondente ao cursor.
    /// Debounce de 150 ms; janela de 64 KB ao redor do caret.
    /// Nunca lança no caminho do editor.
    /// </summary>
    internal sealed class BlockMatchTagger : ITagger<ITextMarkerTag>
    {
        // "bracehighlight" / "BraceHighlight" é o estilo embutido do VS para matching de chaves.
        private const string MarkerType = "bracehighlight";
        private const int WindowSize = 64 * 1024;
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

        private readonly ITextView _view;
        private readonly DispatcherTimer _timer;
        private volatile IReadOnlyList<SnapshotSpan> _spans = new List<SnapshotSpan>();

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public BlockMatchTagger(ITextView textView)
        {
            _view = textView;
            _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = Debounce };
            _timer.Tick += (s, e) => { _timer.Stop(); Refresh(); };
            _view.Caret.PositionChanged += (s, e) => { _timer.Stop(); _timer.Start(); };
            _view.Closed += (s, e) => _timer.Stop();
        }

        private void Refresh()
        {
            try
            {
                ITextSnapshot snapshot = _view.TextSnapshot;
                int caretPos = _view.Caret.Position.BufferPosition.Position;

                int start  = Math.Max(0, caretPos - WindowSize / 2);
                int end    = Math.Min(snapshot.Length, start + WindowSize);
                string window = snapshot.GetText(start, end - start);
                int caretInWindow = caretPos - start;

                BlockMatch match = BlockMatcher.Match(window, caretInWindow);
                var newSpans = new List<SnapshotSpan>();

                if (match != null)
                {
                    int openAbs  = start + match.OpenStart;
                    int closeAbs = start + match.CloseStart;
                    if (openAbs  + match.OpenLength  <= snapshot.Length)
                        newSpans.Add(new SnapshotSpan(snapshot, openAbs,  match.OpenLength));
                    if (closeAbs + match.CloseLength <= snapshot.Length)
                        newSpans.Add(new SnapshotSpan(snapshot, closeAbs, match.CloseLength));
                }

                _spans = newSpans;
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(snapshot, 0, snapshot.Length)));
            }
            catch (Exception ex)
            {
                Log.Error("BlockMatchTagger", ex);
            }
        }

        public IEnumerable<ITagSpan<ITextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) yield break;
            ITextSnapshot current = spans[0].Snapshot;
            IReadOnlyList<SnapshotSpan> mine = _spans;

            foreach (var span in mine)
            {
                SnapshotSpan translated = span.Snapshot == current
                    ? span
                    : span.TranslateTo(current, SpanTrackingMode.EdgeExclusive);

                if (spans.IntersectsWith(new NormalizedSnapshotSpanCollection(translated)))
                    yield return new TagSpan<ITextMarkerTag>(translated,
                        new TextMarkerTag(MarkerType));
            }
        }
    }
}
