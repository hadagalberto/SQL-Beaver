using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Analysis;
using SqlBeaver.Completion;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Editing
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("SQL Beaver QuickInfo")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [Order]
    internal sealed class SqlQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
            => textBuffer.Properties.GetOrCreateSingletonProperty(() => new SqlQuickInfoSource(textBuffer));
    }

    internal sealed class SqlQuickInfoSource : IAsyncQuickInfoSource
    {
        private const int MaxAnalysisWindow = 64 * 1024;

        private readonly ITextBuffer _buffer;

        public SqlQuickInfoSource(ITextBuffer buffer)
        {
            _buffer = buffer;
        }

        public Task<QuickInfoItem> GetQuickInfoItemAsync(
            IAsyncQuickInfoSession session,
            CancellationToken cancellationToken)
        {
            try
            {
                // Trigger point on our buffer's snapshot
                SnapshotPoint? triggerPointNullable = session.GetTriggerPoint(_buffer.CurrentSnapshot);
                if (triggerPointNullable == null)
                    return Task.FromResult<QuickInfoItem>(null);

                SnapshotPoint triggerPoint = triggerPointNullable.Value;
                ITextSnapshot snapshot = triggerPoint.Snapshot;
                int caretPos = triggerPoint.Position;

                // Build a bounded window of text around the trigger point
                int windowStart = Math.Max(0, caretPos - MaxAnalysisWindow);
                int windowEnd   = Math.Min(snapshot.Length, caretPos + MaxAnalysisWindow);
                string text     = snapshot.GetText(windowStart, windowEnd - windowStart);
                int offsetInWindow = caretPos - windowStart;

                // Identify the word under the mouse
                string word = OccurrenceFinder.IdentifierAt(text, offsetInWindow);
                if (string.IsNullOrEmpty(word))
                    return Task.FromResult<QuickInfoItem>(null);

                // Need an active connection
                ActiveConnection connection = ConnectionService.GetActiveConnection();
                if (connection == null)
                    return Task.FromResult<QuickInfoItem>(null);

                // Metadata (cache-only, never queries the DB)
                DbMetadata metadata = SqlBeaverCompletionSourceProvider.Cache.TryGet(
                    connection.Server,
                    connection.Database,
                    new MetadataRequest
                    {
                        ConnectionString      = connection.ConnectionString,
                        AccessToken           = connection.AccessToken,
                        ProviderConnectionType = connection.ProviderConnectionType,
                    });
                if (metadata == null)
                    return Task.FromResult<QuickInfoItem>(null);

                // Scope and locals
                IReadOnlyList<TableRef>     scope  = StatementScopeAnalyzer.GetTablesInScope(text, offsetInWindow);
                IReadOnlyList<LocalTableDef> locals = LocalObjectScanner.Scan(text, offsetInWindow);

                // Build the tooltip text
                string tooltipText = QuickInfoBuilder.Build(word, null, scope, metadata, locals);
                if (string.IsNullOrEmpty(tooltipText))
                    return Task.FromResult<QuickInfoItem>(null);

                // Find the span of the word in the snapshot
                // The word starts at (windowStart + identifierStartInWindow)
                int identifierEnd   = offsetInWindow;
                // Expand end to cover all identifier chars after offsetInWindow
                while (identifierEnd < text.Length && IsIdentifierChar(text[identifierEnd]))
                    identifierEnd++;
                // Expand start backwards
                int identifierStart = offsetInWindow;
                while (identifierStart > 0 && IsIdentifierChar(text[identifierStart - 1]))
                    identifierStart--;

                int spanStart  = windowStart + identifierStart;
                int spanLength = identifierEnd - identifierStart;
                if (spanStart < 0 || spanStart + spanLength > snapshot.Length)
                    return Task.FromResult<QuickInfoItem>(null);

                ITrackingSpan applicableToSpan = snapshot.CreateTrackingSpan(
                    new Span(spanStart, spanLength),
                    SpanTrackingMode.EdgeInclusive);

                // Build content: use ClassifiedTextElement with plain-text runs (one per line)
                string[] lines = tooltipText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var runs = new List<ClassifiedTextRun>(lines.Length * 2);
                for (int i = 0; i < lines.Length; i++)
                {
                    runs.Add(new ClassifiedTextRun(
                        PredefinedClassificationTypeNames.NaturalLanguage,
                        lines[i]));
                    if (i < lines.Length - 1)
                        runs.Add(new ClassifiedTextRun(
                            PredefinedClassificationTypeNames.NaturalLanguage,
                            Environment.NewLine));
                }

                var content = new ClassifiedTextElement(runs);
                var item    = new QuickInfoItem(applicableToSpan, content);
                return Task.FromResult(item);
            }
            catch (Exception ex)
            {
                Log.Error("SqlQuickInfoSource.GetQuickInfoItemAsync", ex);
                return Task.FromResult<QuickInfoItem>(null);
            }
        }

        public void Dispose() { }

        private static bool IsIdentifierChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
    }
}
