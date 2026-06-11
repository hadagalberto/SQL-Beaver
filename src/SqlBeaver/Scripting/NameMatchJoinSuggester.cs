using System;
using System.Collections.Generic;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    /// <summary>Sugestão de JOIN por nome de coluna (bancos sem FK declarada).</summary>
    public sealed class NameJoinSuggestion
    {
        public string Schema { get; set; }
        public string Table { get; set; }
        public string Alias { get; set; }
        public string OnClause { get; set; }
        /// <summary>Chave canônica par + coluna para dedup (tablekeyA|tablekeyB|col).</summary>
        public string PairKey { get; set; }

        // Para exibição no popup
        public string InsertText { get; set; }
        public string DisplayText { get; set; }
        public string FilterText { get; set; }
    }

    /// <summary>
    /// JOINs sugeridos por NOME de coluna (bancos sem FK declarada):
    /// para cada tabela do db cujo nome de coluna casa EXATAMENTE com uma coluna de uma
    /// tabela do escopo, restrito a colunas que terminam em Id/ID OU são PK numa das pontas.
    /// Exclui pares já cobertos por FK (mesma dupla de tabelas+coluna) e tabelas já no escopo.
    /// </summary>
    public static class NameMatchJoinSuggester
    {
        private const int MaxSuggestions = 20;

        public static IReadOnlyList<NameJoinSuggestion> Suggest(
            IReadOnlyList<TableRef> scope,
            DbMetadata metadata,
            IReadOnlyCollection<string> fkPairKeysAlreadySuggested)
        {
            if (scope == null || scope.Count == 0) return Array.Empty<NameJoinSuggestion>();
            if (metadata == null) return Array.Empty<NameJoinSuggestion>();

            // Build scope table keys and aliases
            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolvedScope = new List<(string Key, string Schema, string Qualifier, IReadOnlyList<ColumnEntry> Columns)>();

            foreach (TableRef tr in scope)
            {
                if (tr.Alias != null)
                    usedAliases.Add(tr.Alias);

                string schema = tr.Schema ?? metadata.ResolveUniqueSchema(tr.Table);
                if (schema == null) continue;

                string key = DbMetadata.TableKey(schema, tr.Table);
                scopeKeys.Add(key);

                IReadOnlyList<ColumnEntry> cols;
                metadata.ColumnsByTable.TryGetValue(key, out cols);
                if (cols == null) cols = Array.Empty<ColumnEntry>();

                resolvedScope.Add((key, schema, tr.Alias ?? tr.Table, cols));
            }

            if (resolvedScope.Count == 0) return Array.Empty<NameJoinSuggestion>();

            // Build set of FK pair keys for dedup
            var fkSet = new HashSet<string>(
                fkPairKeysAlreadySuggested ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // Build index of scope column names → (scopeTableKey, scopeQualifier, columnEntry)
            // Only columns ending in Id/ID or that are PK
            var scopeColIndex = new Dictionary<string, List<(string Key, string Qualifier, ColumnEntry Col)>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var (key, schema, qualifier, cols) in resolvedScope)
            {
                foreach (ColumnEntry col in cols)
                {
                    if (!IsIdOrPk(col)) continue;
                    if (!scopeColIndex.TryGetValue(col.Name, out var list))
                    {
                        list = new List<(string, string, ColumnEntry)>();
                        scopeColIndex[col.Name] = list;
                    }
                    list.Add((key, qualifier, col));
                }
            }

            if (scopeColIndex.Count == 0) return Array.Empty<NameJoinSuggestion>();

            var seenPairKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var suggestions = new List<NameJoinSuggestion>();

            foreach (TableEntry candidateTable in metadata.Tables)
            {
                string candidateKey = DbMetadata.TableKey(candidateTable.Schema, candidateTable.Name);

                // Skip tables already in scope
                if (scopeKeys.Contains(candidateKey)) continue;

                IReadOnlyList<ColumnEntry> candidateCols;
                if (!metadata.ColumnsByTable.TryGetValue(candidateKey, out candidateCols))
                    continue;

                foreach (ColumnEntry candidateCol in candidateCols)
                {
                    // Must be Id/PK column
                    if (!IsIdOrPk(candidateCol)) continue;

                    // Must match by name to a scope column
                    if (!scopeColIndex.TryGetValue(candidateCol.Name, out var scopeMatches))
                        continue;

                    foreach (var (scopeKey, scopeQualifier, scopeCol) in scopeMatches)
                    {
                        // Build a pair key: canonical (sorted) pair + column name
                        string rawPairKey = BuildPairKey(candidateKey, scopeKey, candidateCol.Name);

                        // Skip if already suggested or covered by FK
                        if (seenPairKeys.Contains(rawPairKey)) continue;
                        if (fkSet.Contains(rawPairKey)) continue;

                        // Also skip if the FK set contains the table-pair (any column)
                        string tablePairKey = BuildTablePairKey(candidateKey, scopeKey);
                        if (fkSet.Contains(tablePairKey)) continue;

                        seenPairKeys.Add(rawPairKey);

                        string alias = AliasGenerator.Generate(candidateTable.Name, usedAliases);
                        usedAliases.Add(alias);

                        string onClause = SqlIdentifier.Bracket(alias) + "." +
                                          SqlIdentifier.Bracket(candidateCol.Name) +
                                          " = " +
                                          SqlIdentifier.Bracket(scopeQualifier) + "." +
                                          SqlIdentifier.Bracket(scopeCol.Name);

                        string insertText = SqlIdentifier.Bracket(candidateTable.Schema) + "." +
                                            SqlIdentifier.Bracket(candidateTable.Name) +
                                            " " + alias + " ON " + onClause;

                        string displayText = SqlIdentifier.Bracket(candidateTable.Schema) + "." +
                                             SqlIdentifier.Bracket(candidateTable.Name) +
                                             " " + alias + " — ON " + onClause;

                        suggestions.Add(new NameJoinSuggestion
                        {
                            Schema = candidateTable.Schema,
                            Table = candidateTable.Name,
                            Alias = alias,
                            OnClause = onClause,
                            PairKey = rawPairKey,
                            InsertText = insertText,
                            DisplayText = displayText,
                            FilterText = candidateTable.Name,
                        });

                        if (suggestions.Count >= MaxSuggestions)
                            return suggestions;
                    }
                }
            }

            return suggestions;
        }

        private static bool IsIdOrPk(ColumnEntry col)
        {
            if (col.IsPrimaryKey) return true;
            string name = col.Name;
            return name.EndsWith("Id", StringComparison.Ordinal) ||
                   name.EndsWith("ID", StringComparison.Ordinal) ||
                   name.EndsWith("id", StringComparison.Ordinal);
        }

        private static string BuildPairKey(string keyA, string keyB, string columnName)
        {
            string a = keyA;
            string b = keyB;
            if (string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0)
            { string t = a; a = b; b = t; }
            return a + "|" + b + "|" + columnName;
        }

        private static string BuildTablePairKey(string keyA, string keyB)
        {
            string a = keyA;
            string b = keyB;
            if (string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0)
            { string t = a; a = b; b = t; }
            return a + "|" + b;
        }
    }
}
