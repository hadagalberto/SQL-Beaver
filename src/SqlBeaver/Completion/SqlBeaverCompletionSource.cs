using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
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
using SqlBeaver.Scripting;
using SqlBeaver.Snippets;

namespace SqlBeaver.Completion
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SQL Beaver completion")]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    public sealed class SqlBeaverCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        internal static MetadataCache Cache { get; } = CreateCache();

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
        private static readonly ImageElement ColumnIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Column), "Coluna");
        private static readonly ImageElement KeyIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Key), "Chave primária");
        private static readonly ImageElement JoinIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Link), "JOIN por FK");
        private static readonly ImageElement SnippetIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Snippet), "Snippet");
        private static readonly ImageElement ProcIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Method), "Procedure/Function");
        private static readonly ImageElement DatabaseIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Database), "Banco de dados");

        private static readonly DatabaseListCache DbListCache = CreateDbListCache();

        private static DatabaseListCache CreateDbListCache()
        {
            var cache = new DatabaseListCache();
            cache.LoadFailed += ex => Log.Error("Falha ao carregar lista de bancos", ex);
            return cache;
        }

        private readonly MetadataCache _cache;
        private ActiveConnection _connection;
        private bool _loggedContentType;
        private static bool _loggedFirstItems;

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

                SqlContext context = AnalyzeContextAt(triggerLocation);
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
                SqlContext context = AnalyzeAt(triggerLocation, out IReadOnlyList<TableRef> scope);
                ActiveConnection connection = _connection;
                if (context.Kind == SqlContextKind.None || connection == null)
                    return Task.FromResult(CompletionContext.Empty);

                DbMetadata metadata = _cache.TryGet(
                    connection.Server, connection.Database,
                    new MetadataRequest { ConnectionString = connection.ConnectionString, AccessToken = connection.AccessToken, ProviderConnectionType = connection.ProviderConnectionType });
                if (metadata == null)
                    return Task.FromResult(CompletionContext.Empty); // carga disparada em background

                ImmutableArray<CompletionItem> items = BuildItems(context, metadata, scope, connection);

                if (!_loggedFirstItems)
                {
                    _loggedFirstItems = true;
                    Log.Info($"Completion: contexto={context.Kind}, parcial='{context.Partial}', {items.Length} item(ns), db={connection.Database}.");
                }

                return Task.FromResult(new CompletionContext(items));
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

        private ImmutableArray<CompletionItem> BuildItems(
            SqlContext context, DbMetadata metadata, IReadOnlyList<TableRef> scope,
            ActiveConnection connection)
        {
            var items = ImmutableArray.CreateBuilder<CompletionItem>();

            switch (context.Kind)
            {
                case SqlContextKind.AfterDot:
                    BuildDotItems(items, context.DotPrefix, metadata, scope);
                    break;

                case SqlContextKind.ColumnContext:
                    BuildColumnItems(items, metadata, scope);
                    break;

                case SqlContextKind.AfterJoin:
                    BuildFkJoinItems(items, metadata, scope, connection, context.Partial);
                    BuildTableAndSchemaItems(items, metadata, scope, connection, withAlias: true);
                    break;

                case SqlContextKind.AfterFromJoin:
                    bool alias = string.Equals(context.TriggerKeyword, "FROM", StringComparison.OrdinalIgnoreCase);
                    BuildTableAndSchemaItems(items, metadata, scope, connection, withAlias: alias);
                    break;

                case SqlContextKind.AfterExec:
                    BuildExecItems(items, metadata, connection);
                    break;

                case SqlContextKind.AfterUse:
                    BuildUseItems(items, connection);
                    break;

                default: // FreeIdentifier
                    BuildSnippetItems(items);
                    BuildTableAndSchemaItems(items, metadata, scope, connection, withAlias: false);
                    break;
            }

            return items.ToImmutable();
        }

        private void BuildExecItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata,
            ActiveConnection connection)
        {
            string server   = connection?.Server;
            string database = connection?.Database;

            foreach (ObjectEntry obj in metadata.Objects)
            {
                if (obj.Type != DbObjectType.Procedure &&
                    obj.Type != DbObjectType.ScalarFunction &&
                    obj.Type != DbObjectType.TableFunction)
                    continue;

                string key = DbMetadata.TableKey(obj.Schema, obj.Name);
                IReadOnlyList<ParameterEntry> parameters;
                if (!metadata.ParametersByObject.TryGetValue(key, out parameters))
                    parameters = new ParameterEntry[0];

                string insertText = ProcCallBuilder.BuildExecInsertText(obj.Schema, obj.Name, parameters);

                int usageCount = Usage.UsageStore.GetTableCount(server, database, key);

                items.Add(new CompletionItem(
                    displayText: obj.Name,
                    source: this,
                    icon: ProcIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: obj.Schema + " · " + (obj.Type == DbObjectType.Procedure ? "proc" : "fn"),
                    insertText: insertText,
                    sortText: Usage.UsageRanker.TableSortText(usageCount, obj.Name),
                    filterText: obj.Name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        private void BuildUseItems(
            ImmutableArray<CompletionItem>.Builder items,
            ActiveConnection connection)
        {
            if (connection == null) return;

            var request = new MetadataRequest
            {
                ConnectionString      = connection.ConnectionString,
                AccessToken           = connection.AccessToken,
                ProviderConnectionType = connection.ProviderConnectionType,
            };

            IReadOnlyList<string> databases = DbListCache.TryGet(connection.Server, request);
            if (databases == null) return; // carga em andamento — popup vazio

            foreach (string db in databases)
            {
                items.Add(new CompletionItem(
                    displayText: db,
                    source: this,
                    icon: DatabaseIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: string.Empty,
                    insertText: SqlIdentifier.Bracket(db),
                    sortText: db,
                    filterText: db,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        private void BuildDotItems(
            ImmutableArray<CompletionItem>.Builder items, string prefix,
            DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            // 1) alias (ou nome de tabela usado como qualificador) do escopo → colunas
            string tableKey = ResolveScopeTableKey(prefix, metadata, scope);
            if (tableKey != null &&
                metadata.ColumnsByTable.TryGetValue(tableKey, out IReadOnlyList<ColumnEntry> columns))
            {
                foreach (ColumnEntry column in columns)
                    items.Add(ColumnItem(column, qualifier: null));
                return;
            }

            // 2) schema → tabelas dele (comportamento v1)
            foreach (TableEntry table in metadata.Tables)
            {
                if (string.Equals(table.Schema, prefix, StringComparison.OrdinalIgnoreCase))
                    items.Add(new CompletionItem(SqlIdentifier.Bracket(table.Name), this, TableIcon));
            }
        }

        private void BuildColumnItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            bool qualify = scope.Count > 1;
            foreach (TableRef tableRef in scope)
            {
                string key = ResolveTableKey(tableRef, metadata);
                if (key == null ||
                    !metadata.ColumnsByTable.TryGetValue(key, out IReadOnlyList<ColumnEntry> columns))
                    continue;

                string qualifier = qualify ? (tableRef.Alias ?? tableRef.Table) : null;
                string origin = tableRef.Alias ?? tableRef.Table;
                foreach (ColumnEntry column in columns)
                    items.Add(ColumnItem(column, qualifier, origin));
            }
        }

        private CompletionItem ColumnItem(ColumnEntry column, string qualifier, string origin = null)
        {
            string insert = qualifier == null
                ? SqlIdentifier.Bracket(column.Name)
                : SqlIdentifier.Bracket(qualifier) + "." + SqlIdentifier.Bracket(column.Name);
            string suffix = origin == null ? column.SqlType : column.SqlType + " — " + origin;

            return new CompletionItem(
                displayText: column.Name,
                source: this,
                icon: column.IsPrimaryKey ? KeyIcon : ColumnIcon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: suffix,
                insertText: insert,
                sortText: column.Name,
                filterText: column.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
        }

        private void BuildFkJoinItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata, IReadOnlyList<TableRef> scope,
            ActiveConnection connection, string partial = null)
        {
            // O parcial digitado depois do JOIN entra no escopo como TableRef sem alias;
            // sem a poda, a própria sugestão FK que o usuário está digitando é descartada
            // como "já em escopo".
            if (!string.IsNullOrEmpty(partial))
            {
                var pruned = new List<TableRef>(scope.Count);
                foreach (TableRef tableRef in scope)
                {
                    bool isTypedPartial = tableRef.Alias == null && tableRef.Schema == null &&
                        string.Equals(tableRef.Table, partial, StringComparison.OrdinalIgnoreCase);
                    if (!isTypedPartial)
                        pruned.Add(tableRef);
                }
                scope = pruned;
            }

            // Ranking por uso: pares de join mais executados primeiro. OrderByDescending
            // é estável — empates (count 0) preservam a ordem do builder (mesmo schema antes).
            string server = connection?.Server;
            string database = connection?.Database;
            List<FkJoinSuggestion> suggestions = FkJoinSuggestionBuilder.Build(scope, metadata)
                .OrderByDescending(s => Usage.UsageStore.GetJoinCount(server, database, s.PairKey))
                .ToList();

            int index = 0;
            foreach (FkJoinSuggestion suggestion in suggestions)
            {
                items.Add(new CompletionItem(
                    displayText: suggestion.DisplayText,
                    source: this,
                    icon: JoinIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: "FK",
                    insertText: suggestion.InsertText,
                    sortText: "0_" + index.ToString("D3"), // topo da lista; preserva ordem pós-ranking
                    filterText: suggestion.FilterText,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
                index++;
            }
        }

        private void BuildSnippetItems(ImmutableArray<CompletionItem>.Builder items)
        {
            foreach (SnippetDefinition snippet in SnippetStore.Catalog.Values)
            {
                string insert = snippet.Expansion?.Replace("$cursor$", string.Empty) ?? string.Empty;
                items.Add(new CompletionItem(
                    displayText: snippet.Shortcut,
                    source: this,
                    icon: SnippetIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: snippet.Title,
                    insertText: insert,
                    sortText: "zz_" + snippet.Shortcut, // depois de tabelas/schemas
                    filterText: snippet.Shortcut,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        private void BuildTableAndSchemaItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata, IReadOnlyList<TableRef> scope,
            ActiveConnection connection, bool withAlias)
        {
            foreach (string schema in metadata.Schemas)
                items.Add(new CompletionItem(SqlIdentifier.Bracket(schema), this, SchemaIcon));

            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TableRef tableRef in scope)
            {
                if (tableRef.Alias != null)
                    usedAliases.Add(tableRef.Alias);
            }

            foreach (TableEntry table in metadata.Tables)
            {
                string qualified = SqlIdentifier.Bracket(table.Schema) + "." + SqlIdentifier.Bracket(table.Name);
                string insert = withAlias
                    ? qualified + " " + AliasGenerator.Generate(table.Name, usedAliases)
                    : qualified;

                int usageCount = Usage.UsageStore.GetTableCount(
                    connection?.Server, connection?.Database,
                    DbMetadata.TableKey(table.Schema, table.Name));

                items.Add(new CompletionItem(
                    displayText: table.Name,
                    source: this,
                    icon: TableIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: table.Schema,
                    insertText: insert,
                    sortText: Usage.UsageRanker.TableSortText(usageCount, table.Name),
                    filterText: table.Name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        private static string ResolveScopeTableKey(
            string prefix, DbMetadata metadata, IReadOnlyList<TableRef> scope)
        {
            foreach (TableRef tableRef in scope)
            {
                bool aliasMatch = string.Equals(tableRef.Alias, prefix, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = tableRef.Alias == null &&
                                 string.Equals(tableRef.Table, prefix, StringComparison.OrdinalIgnoreCase);
                if (aliasMatch || nameMatch)
                    return ResolveTableKey(tableRef, metadata);
            }
            return null;
        }

        private static string ResolveTableKey(TableRef tableRef, DbMetadata metadata)
        {
            if (tableRef.Schema != null)
                return DbMetadata.TableKey(tableRef.Schema, tableRef.Table);

            string schema = metadata.ResolveUniqueSchema(tableRef.Table);
            return schema == null ? null : DbMetadata.TableKey(schema, tableRef.Table);
        }

        /// <summary>
        /// Versão leve para a thread de UI: janela apenas para trás, sem leitura forward
        /// nem cálculo de escopo. Ver AnalyzeAt para a versão completa usada em background.
        /// </summary>
        private static SqlContext AnalyzeContextAt(SnapshotPoint point)
        {
            ITextSnapshot snapshot = point.Snapshot;
            int caret = point.Position;
            int windowStart = Math.Max(0, caret - MaxAnalysisWindow);
            string textBefore = snapshot.GetText(windowStart, caret - windowStart);

            SqlContext context = SqlContextAnalyzer.Analyze(textBefore, textBefore.Length);
            if (context.Kind == SqlContextKind.None || windowStart == 0)
                return context;

            // reprojetar PartialStart da janela para coordenadas do snapshot
            // (mesma lógica de re-projeção de AnalyzeAt — ver abaixo)
            return new SqlContext(context.Kind, context.DotPrefix, context.Partial,
                context.PartialStart + windowStart, context.TriggerKeyword);
        }

        // Usado em GetCompletionContextAsync (thread de background): lê janela forward
        // para calcular o escopo completo. AnalyzeContextAt é a versão leve para a UI.
        private static SqlContext AnalyzeAt(SnapshotPoint point, out IReadOnlyList<TableRef> scope)
        {
            ITextSnapshot snapshot = point.Snapshot;
            int caret = point.Position;
            int windowStart = Math.Max(0, caret - MaxAnalysisWindow);
            // janela do escopo inclui o texto DEPOIS do caret (SELECT | FROM ...)
            int windowEnd = Math.Min(snapshot.Length, caret + MaxAnalysisWindow);
            string textBefore = snapshot.GetText(windowStart, caret - windowStart);
            string fullWindow = caret == windowEnd
                ? textBefore
                : textBefore + snapshot.GetText(caret, windowEnd - caret);

            scope = StatementScopeAnalyzer.GetTablesInScope(fullWindow, textBefore.Length);

            SqlContext context = SqlContextAnalyzer.Analyze(textBefore, textBefore.Length);
            if (context.Kind == SqlContextKind.None || windowStart == 0)
                return context;

            // reprojetar PartialStart da janela para coordenadas do snapshot
            return new SqlContext(context.Kind, context.DotPrefix, context.Partial,
                context.PartialStart + windowStart, context.TriggerKeyword);
        }
    }
}
