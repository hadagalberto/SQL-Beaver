using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SqlBeaver.Analysis;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Completion
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SQL Beaver completion")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    public sealed class SqlBeaverCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        private static readonly MetadataCache Cache = CreateCache();

        private static MetadataCache CreateCache()
        {
            var cache = new MetadataCache(
                new SqlMetadataSource(),
                ttl: TimeSpan.FromMinutes(10),
                failureCooldown: TimeSpan.FromSeconds(30),
                utcNow: () => DateTime.UtcNow);
            cache.LoadFailed += ex => Log.Error("Falha ao carregar metadata", ex);
            return cache;
        }

        public IAsyncCompletionSource GetOrCreate(ITextView textView)
            => textView.Properties.GetOrCreateSingletonProperty(
                () => new SqlBeaverCompletionSource(Cache));
    }

    public sealed class SqlBeaverCompletionSource : IAsyncCompletionSource
    {
        private const int MaxAnalysisWindow = 64 * 1024;

        private static readonly ImageElement TableIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Table), "Tabela");
        private static readonly ImageElement SchemaIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.DatabaseSchema), "Schema");

        private readonly MetadataCache _cache;
        private ActiveConnection _connection;
        private bool _loggedContentType;

        public SqlBeaverCompletionSource(MetadataCache cache)
        {
            _cache = cache;
        }

        // Chamado na thread de UI a cada tecla; precisa ser rápido.
        public CompletionStartData InitializeCompletion(
            CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            try
            {
                if (!_loggedContentType)
                {
                    _loggedContentType = true;
                    Log.Info("Completion ativado em content type: " +
                             triggerLocation.Snapshot.ContentType.TypeName);
                }

                // Early-out barato: tecla que não inicia/estende identificador, ponto ou
                // espaço (popup pós-FROM) não justifica varrer o buffer.
                if (trigger.Reason == CompletionTriggerReason.Insertion)
                {
                    char typed = trigger.Character;
                    if (!char.IsLetterOrDigit(typed) && typed != '_' && typed != '.' && !char.IsWhiteSpace(typed))
                        return CompletionStartData.DoesNotParticipateInCompletion;
                }

                SqlContext context = AnalyzeAt(triggerLocation);
                if (context.Kind == SqlContextKind.None)
                    return CompletionStartData.DoesNotParticipateInCompletion;

                // Reflection barata (leitura de propriedades) — ok na thread de UI.
                _connection = ConnectionService.GetActiveConnection();
                if (_connection == null)
                    return CompletionStartData.DoesNotParticipateInCompletion;

                var applicableToSpan = new SnapshotSpan(
                    triggerLocation.Snapshot,
                    context.PartialStart,
                    triggerLocation.Position - context.PartialStart);

                return new CompletionStartData(CompletionParticipation.ProvidesItems, applicableToSpan);
            }
            catch (Exception ex)
            {
                Log.Error("InitializeCompletion", ex);
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation,
            SnapshotSpan applicableToSpan, CancellationToken token)
        {
            try
            {
                SqlContext context = AnalyzeAt(triggerLocation);
                ActiveConnection connection = _connection;
                if (context.Kind == SqlContextKind.None || connection == null)
                    return Task.FromResult(CompletionContext.Empty);

                DbMetadata metadata = _cache.TryGet(
                    connection.Server, connection.Database, connection.ConnectionString, connection.AccessToken);
                if (metadata == null)
                    return Task.FromResult(CompletionContext.Empty); // carga disparada em background

                return Task.FromResult(new CompletionContext(BuildItems(context, metadata)));
            }
            catch (Exception ex)
            {
                Log.Error("GetCompletionContextAsync", ex);
                return Task.FromResult(CompletionContext.Empty);
            }
        }

        public Task<object> GetDescriptionAsync(
            IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
            => Task.FromResult<object>(item.InsertText);

        private ImmutableArray<CompletionItem> BuildItems(SqlContext context, DbMetadata metadata)
        {
            var items = ImmutableArray.CreateBuilder<CompletionItem>();

            if (context.Kind == SqlContextKind.AfterSchemaDot)
            {
                foreach (TableEntry table in metadata.Tables)
                {
                    if (string.Equals(table.Schema, context.SchemaPrefix, StringComparison.OrdinalIgnoreCase))
                        items.Add(new CompletionItem(BracketIfNeeded(table.Name), this, TableIcon));
                }
                return items.ToImmutable();
            }

            // AfterFromJoin e FreeIdentifier: schemas + tabelas qualificadas
            foreach (string schema in metadata.Schemas)
                items.Add(new CompletionItem(BracketIfNeeded(schema), this, SchemaIcon));

            foreach (TableEntry table in metadata.Tables)
            {
                string qualified = BracketIfNeeded(table.Schema) + "." + BracketIfNeeded(table.Name);
                items.Add(new CompletionItem(
                    displayText: table.Name,   // nome simples: é o que o usuário digita
                    source: this,
                    icon: TableIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: table.Schema,      // schema visível à direita, estilo nativo
                    insertText: qualified,     // inserção continua qualificada
                    sortText: table.Name + " " + qualified,
                    filterText: table.Name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }

            return items.ToImmutable();
        }

        private static string BracketIfNeeded(string identifier)
        {
            foreach (char c in identifier)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return "[" + identifier.Replace("]", "]]") + "]";
            }
            return identifier;
        }

        private static SqlContext AnalyzeAt(SnapshotPoint point)
        {
            ITextSnapshot snapshot = point.Snapshot;
            int caret = point.Position;
            int windowStart = Math.Max(0, caret - MaxAnalysisWindow);
            string text = snapshot.GetText(windowStart, caret - windowStart);

            SqlContext context = SqlContextAnalyzer.Analyze(text, text.Length);
            if (context.Kind == SqlContextKind.None || windowStart == 0)
                return context;

            // reprojetar PartialStart da janela para coordenadas do snapshot
            return new SqlContext(context.Kind, context.SchemaPrefix, context.Partial,
                context.PartialStart + windowStart);
        }
    }
}
