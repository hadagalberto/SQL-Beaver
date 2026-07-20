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
        private static readonly ImageElement KeywordIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.IntellisenseKeyword), "Palavra-chave");
        private static readonly ImageElement BuiltinIcon =
            new ImageElement(new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Method), "Função built-in");

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
        private static bool _loggedMetadataWait;

        // Tabelas locais do batch atual; populado em GetCompletionContextAsync antes de BuildItems.
        private IReadOnlyList<LocalTableDef> _pendingLocals;

        // Texto completo da janela e posição do caret nela — usados por BuildGroupByFillItem.
        private string _pendingFullText;
        private int    _pendingCaretInWindow;

        // Diagnóstico: contador global de instâncias. Se aparecer #1 E #2 nos logs,
        // há mais de uma extensão SQL Beaver carregada (instalação duplicada) — duas
        // fontes de completion competindo quebram o span de filtragem.
        private static int _instanceCounter;
        private readonly int _instanceId;

        public SqlBeaverCompletionSource(MetadataCache cache)
        {
            _cache = cache;
            _instanceId = System.Threading.Interlocked.Increment(ref _instanceCounter);
            Log.Info($"[completion] fonte criada — instância #{_instanceId} (total de instâncias: {_instanceCounter}).");
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

                // Metadata ainda carregando: NÃO abrir sessão. Abrir uma sessão vazia deixa o popup
                // preso (o framework só FILTRA a lista, não recalcula) e o IntelliSense parece morto
                // mesmo depois que a carga termina. TryGet dispara a carga em background; assim que
                // ela chega, a próxima tecla participa normalmente.
                if (_cache.TryGet(_connection.Server, _connection.Database,
                        new MetadataRequest
                        {
                            ConnectionString       = _connection.ConnectionString,
                            AccessToken            = _connection.AccessToken,
                            ProviderConnectionType = _connection.ProviderConnectionType,
                        }) == null)
                {
                    if (!_loggedMetadataWait)
                    {
                        _loggedMetadataWait = true;
                        Log.Info("[completion] metadata ainda carregando — as sugestões aparecem assim que a carga terminar.");
                    }
                    return CompletionStartData.DoesNotParticipateInCompletion;
                }

                var applicableToSpan = new SnapshotSpan(
                    triggerLocation.Snapshot,
                    context.PartialStart,
                    triggerLocation.Position - context.PartialStart);

                return new CompletionStartData(CompletionParticipation.ProvidesItems, applicableToSpan);
            }
            catch (Exception ex)
            {
                Log.Error($"[completion] #{_instanceId} InitializeCompletion", ex);
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

                // Scan local objects (O(64 KB)) para #temp, @tabela, CTEs.
                _pendingLocals = ScanLocals(triggerLocation);

                // Captura o texto completo da janela e posição do caret para GroupBy fill.
                {
                    ITextSnapshot snap = triggerLocation.Snapshot;
                    int caretPos = triggerLocation.Position;
                    int wStart = Math.Max(0, caretPos - MaxAnalysisWindow);
                    int wEnd   = Math.Min(snap.Length, caretPos + MaxAnalysisWindow);
                    _pendingFullText      = snap.GetText(wStart, wEnd - wStart);
                    _pendingCaretInWindow = caretPos - wStart;
                }

                ImmutableArray<CompletionItem> items = BuildItems(context, metadata, scope, connection);

                if (!_loggedFirstItems)
                {
                    _loggedFirstItems = true;
                    Log.Info($"[completion] #{_instanceId} primeiro popup: ctx={context.Kind}, partial='{context.Partial}', itens={items.Length}, tabelas={metadata.Tables.Count}, db={connection.Database}.");
                }

                return Task.FromResult(new CompletionContext(items));
            }
            catch (Exception ex)
            {
                Log.Error($"[completion] #{_instanceId} GetCompletionContextAsync", ex);
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

            // Scan once per popup — O(64 KB), acceptable.
            // We need the full buffer text + caret; triggerLocation is available in GetCompletionContextAsync
            // but not here. We pass locals through BuildItems via the AnalyzeAt result path — see below.
            // (locals are captured via the _pendingLocals field set just before BuildItems is called)
            IReadOnlyList<LocalTableDef> locals = _pendingLocals ?? Array.Empty<LocalTableDef>();

            switch (context.Kind)
            {
                case SqlContextKind.AfterDot:
                    BuildDotItems(items, context.DotPrefix, metadata, scope, locals);
                    break;

                case SqlContextKind.ColumnContext:
                    BuildColumnItems(items, metadata, scope, locals);
                    break;

                case SqlContextKind.AfterJoin:
                {
                    var fkItems = BuildFkJoinItemsList(metadata, scope, connection, context.Partial);
                    foreach (var item in fkItems) items.Add(item);
                    BuildNameMatchJoinItems(items, metadata, scope, fkItems);
                    BuildTableAndSchemaItems(items, metadata, scope, connection, withAlias: true, locals);
                    break;
                }

                case SqlContextKind.AfterFromJoin:
                {
                    bool alias = string.Equals(context.TriggerKeyword, "FROM", StringComparison.OrdinalIgnoreCase);
                    bool isInto = string.Equals(context.TriggerKeyword, "INTO", StringComparison.OrdinalIgnoreCase);
                    BuildTableAndSchemaItems(items, metadata, scope, connection, withAlias: alias, locals);
                    if (isInto)
                        BuildInsertFillItems(items, metadata);
                    break;
                }

                case SqlContextKind.AfterGroupBy:
                    BuildGroupByFillItem(items, metadata, scope, context);
                    break;

                case SqlContextKind.AfterExec:
                    BuildExecItems(items, metadata, connection);
                    break;

                case SqlContextKind.AfterUse:
                    BuildUseItems(items, connection);
                    break;

                default: // FreeIdentifier
                    BuildKeywordItems(items);
                    BuildSnippetItems(items);
                    BuildTableAndSchemaItems(items, metadata, scope, connection, withAlias: false, locals);
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
            DbMetadata metadata, IReadOnlyList<TableRef> scope,
            IReadOnlyList<LocalTableDef> locals)
        {
            // 0) #temp. / @tabela. / cte. → colunas da definição local (antes do cache)
            foreach (LocalTableDef local in locals)
            {
                bool directMatch  = string.Equals(local.Name, prefix, StringComparison.OrdinalIgnoreCase);
                // via alias: TableRef cujo Table == local.Name
                bool aliasedMatch = false;
                foreach (TableRef tr in scope)
                {
                    if (tr.Alias != null &&
                        string.Equals(tr.Alias, prefix, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(tr.Table, local.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        aliasedMatch = true;
                        break;
                    }
                }
                if (directMatch || aliasedMatch)
                {
                    foreach (ColumnEntry col in local.Columns)
                        items.Add(LocalColumnItem(col, qualifier: null, local.Name));
                    return;
                }
            }

            // 1) alias (ou nome de tabela usado como qualificador) do escopo → colunas do cache
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
            DbMetadata metadata, IReadOnlyList<TableRef> scope,
            IReadOnlyList<LocalTableDef> locals)
        {
            bool qualify = scope.Count > 1;

            // Colunas de tabelas locais que estão no escopo
            foreach (TableRef tableRef in scope)
            {
                LocalTableDef local = FindLocal(locals, tableRef.Table);
                if (local == null) continue;

                string qualifier = qualify ? (tableRef.Alias ?? tableRef.Table) : null;
                string origin = tableRef.Alias ?? tableRef.Table;
                foreach (ColumnEntry col in local.Columns)
                    items.Add(LocalColumnItem(col, qualifier, origin));
            }

            // Colunas do cache
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

            // Funções built-in (sortText "4_", abaixo das colunas reais)
            foreach ((string name, string signature) in SqlBuiltins.Functions)
            {
                items.Add(new CompletionItem(
                    displayText: name,
                    source: this,
                    icon: BuiltinIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: signature,
                    insertText: name,
                    sortText: "4_" + name,
                    filterText: name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
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
                sortText: "1_" + column.Name, // colunas do catálogo ANTES das funções built-in ("4_")
                filterText: column.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
        }

        /// <summary>Builds FK join items and returns them (for dedup with name-match suggestions).</summary>
        private List<CompletionItem> BuildFkJoinItemsList(
            DbMetadata metadata, IReadOnlyList<TableRef> scope,
            ActiveConnection connection, string partial = null)
        {
            // Pruning: typed partial may appear as a phantom TableRef in scope.
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

            string server   = connection?.Server;
            string database = connection?.Database;
            List<FkJoinSuggestion> suggestions = FkJoinSuggestionBuilder.Build(scope, metadata)
                .OrderByDescending(s => Usage.UsageStore.GetJoinCount(server, database, s.PairKey))
                .ToList();

            var result = new List<CompletionItem>(suggestions.Count);
            int index = 0;
            foreach (FkJoinSuggestion suggestion in suggestions)
            {
                result.Add(new CompletionItem(
                    displayText: suggestion.DisplayText,
                    source: this,
                    icon: JoinIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: "FK",
                    insertText: suggestion.InsertText,
                    sortText: "0_" + index.ToString("D3"),
                    filterText: suggestion.FilterText,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
                index++;
            }
            return result;
        }

        /// <summary>Adds name-match JOIN items after FK items (sortText "0_z{i:D3}").</summary>
        private void BuildNameMatchJoinItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata, IReadOnlyList<TableRef> scope,
            List<CompletionItem> fkItems)
        {
            // Collect FK pair keys already suggested (for dedup).
            var fkPairKeys = new List<string>(fkItems.Count);
            // We don't have direct access to FkJoinSuggestion pairkeys here — pass empty;
            // NameMatchJoinSuggester deduplicates on table pairs regardless.
            IReadOnlyList<Scripting.NameJoinSuggestion> nameSuggestions =
                Scripting.NameMatchJoinSuggester.Suggest(scope, metadata, fkPairKeys);

            int index = 0;
            foreach (Scripting.NameJoinSuggestion s in nameSuggestions)
            {
                items.Add(new CompletionItem(
                    displayText: s.DisplayText,
                    source: this,
                    icon: JoinIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: "nome",
                    insertText: s.InsertText,
                    sortText: "0_z" + index.ToString("D3"),
                    filterText: s.FilterText,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
                index++;
            }
        }

        /// <summary>
        /// For AfterFromJoin with INTO: adds an "INSERT completo" item per table
        /// (sortText immediately after the normal table item, using "1_" prefix).
        /// Strategy (b): plain text with comment-hint placeholders, no snippet session.
        /// </summary>
        private void BuildInsertFillItems(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata)
        {
            foreach (TableEntry table in metadata.Tables)
            {
                string tableKey = DbMetadata.TableKey(table.Schema, table.Name);
                if (!metadata.ColumnsByTable.TryGetValue(tableKey, out IReadOnlyList<ColumnEntry> columns))
                    continue;
                if (columns.Count == 0) continue;

                string tableDisplay =
                    Scripting.SqlIdentifier.Bracket(table.Schema) + "." +
                    Scripting.SqlIdentifier.Bracket(table.Name);

                string insertText = Scripting.InsertFillBuilder.Build(tableDisplay, columns);

                items.Add(new CompletionItem(
                    displayText: table.Name + " — INSERT completo",
                    source: this,
                    icon: TableIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: table.Schema,
                    insertText: insertText,
                    sortText: "1_" + table.Name, // after normal table items (Usage.UsageRanker prefix)
                    filterText: table.Name,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        /// <summary>
        /// For AfterGroupBy: adds a single "(colunas não agregadas do SELECT)" item
        /// when GroupByFillAnalyzer returns ≥1 column.
        /// </summary>
        private void BuildGroupByFillItem(
            ImmutableArray<CompletionItem>.Builder items,
            DbMetadata metadata,
            IReadOnlyList<TableRef> scope,
            SqlContext context)
        {
            // We need the full text around the caret. We can't get it here without the snapshot;
            // instead we store it in context.TriggerKeyword side-channel or use a field.
            // Since BuildItems is called from GetCompletionContextAsync which has the snapshot,
            // we pass the full window text via a field set before BuildItems.
            string fullText = _pendingFullText;
            int caretInWindow = _pendingCaretInWindow;
            if (fullText == null) return;

            IReadOnlyList<string> cols =
                Analysis.GroupByFillAnalyzer.NonAggregatedSelectColumns(fullText, caretInWindow);
            if (cols == null || cols.Count == 0) return;

            string insertText = string.Join(", ", cols);

            items.Add(new CompletionItem(
                displayText: "(colunas não agregadas do SELECT)",
                source: this,
                icon: ColumnIcon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: insertText.Length > 60 ? insertText.Substring(0, 57) + "…" : insertText,
                insertText: insertText,
                sortText: "0_group_by_fill",
                filterText: string.Empty,
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }

        private void BuildKeywordItems(ImmutableArray<CompletionItem>.Builder items)
        {
            foreach (string keyword in SqlKeywordCompletions.Keywords)
            {
                items.Add(new CompletionItem(
                    displayText: keyword,
                    source: this,
                    icon: KeywordIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: string.Empty,
                    insertText: keyword,
                    sortText: "2_" + keyword,
                    filterText: keyword,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
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
            ActiveConnection connection, bool withAlias,
            IReadOnlyList<LocalTableDef> locals = null)
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

            // Tabelas locais: #temp, @tabela, CTEs (sortText "4_", abaixo das tabelas do catálogo)
            if (locals != null)
            {
                foreach (LocalTableDef local in locals)
                {
                    string kindSuffix = local.Kind == LocalTableKind.Temp    ? "#temp"
                                      : local.Kind == LocalTableKind.TableVar ? "@tabela"
                                      : "CTE";
                    items.Add(new CompletionItem(
                        displayText: local.Name,
                        source: this,
                        icon: TableIcon,
                        filters: ImmutableArray<CompletionFilter>.Empty,
                        suffix: kindSuffix,
                        insertText: local.Name,
                        sortText: "4_" + local.Name,
                        filterText: local.Name,
                        attributeIcons: ImmutableArray<ImageElement>.Empty));
                }
            }

            // Views de sistema (sortText "4_", mesmo bucket das locais)
            foreach (string sysView in SqlBuiltins.SystemViews)
            {
                // displayText = parte após o último '.' para brevidade; filterText = nome completo
                int dot = sysView.LastIndexOf('.');
                string display = dot >= 0 ? sysView.Substring(dot + 1) : sysView;
                items.Add(new CompletionItem(
                    displayText: display,
                    source: this,
                    icon: TableIcon,
                    filters: ImmutableArray<CompletionFilter>.Empty,
                    suffix: dot >= 0 ? sysView.Substring(0, dot) : string.Empty,
                    insertText: sysView,
                    sortText: "4_" + sysView,
                    filterText: sysView,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        // ---- helpers para objetos locais ----

        private static IReadOnlyList<LocalTableDef> ScanLocals(SnapshotPoint point)
        {
            ITextSnapshot snapshot = point.Snapshot;
            int caret = point.Position;
            int windowStart = Math.Max(0, caret - MaxAnalysisWindow);
            int windowEnd   = Math.Min(snapshot.Length, caret + MaxAnalysisWindow);
            string fullText = snapshot.GetText(windowStart, windowEnd - windowStart);
            int caretInWindow = caret - windowStart;
            return LocalObjectScanner.Scan(fullText, caretInWindow);
        }

        private static LocalTableDef FindLocal(IReadOnlyList<LocalTableDef> locals, string tableName)
        {
            foreach (LocalTableDef local in locals)
            {
                if (string.Equals(local.Name, tableName, StringComparison.OrdinalIgnoreCase))
                    return local;
            }
            return null;
        }

        private CompletionItem LocalColumnItem(ColumnEntry column, string qualifier, string origin)
        {
            string insert = qualifier == null
                ? SqlIdentifier.Bracket(column.Name)
                : SqlIdentifier.Bracket(qualifier) + "." + SqlIdentifier.Bracket(column.Name);
            string typePart = string.IsNullOrEmpty(column.SqlType) ? "" : column.SqlType + " ";
            string suffix   = typePart + "— " + origin;

            return new CompletionItem(
                displayText: column.Name,
                source: this,
                icon: ColumnIcon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: suffix,
                insertText: insert,
                sortText: "0_" + column.Name, // colunas locais (#temp/@tabela/CTE) no topo
                filterText: column.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
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
