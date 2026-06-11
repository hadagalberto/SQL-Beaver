using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    public sealed class FkJoinSuggestion
    {
        public string DisplayText { get; }
        public string InsertText { get; }

        public FkJoinSuggestion(string displayText, string insertText)
        {
            DisplayText = displayText;
            InsertText = insertText;
        }
    }

    /// <summary>
    /// Para cada FK ligando uma tabela do escopo a uma tabela FORA do escopo, gera a
    /// sugestão de JOIN com alias novo e ON completo (FK composta → pares com AND). Puro.
    /// </summary>
    public static class FkJoinSuggestionBuilder
    {
        public static IReadOnlyList<FkJoinSuggestion> Build(
            IReadOnlyList<TableRef> scopeTables, DbMetadata metadata)
        {
            var suggestions = new List<FkJoinSuggestion>();
            var seenInserts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolved = new List<ResolvedScopeTable>();

            foreach (TableRef tableRef in scopeTables)
            {
                if (tableRef.Alias != null)
                    usedAliases.Add(tableRef.Alias);

                string schema = tableRef.Schema ?? ResolveUniqueSchema(metadata, tableRef.Table);
                if (schema == null)
                    continue; // não qualificado e ambíguo/desconhecido: ignora

                string key = DbMetadata.TableKey(schema, tableRef.Table);
                scopeKeys.Add(key);
                resolved.Add(new ResolvedScopeTable(key, tableRef.Alias ?? tableRef.Table));
            }

            foreach (ResolvedScopeTable scopeTable in resolved)
            {
                if (!metadata.ForeignKeysByTable.TryGetValue(scopeTable.Key, out IReadOnlyList<ForeignKeyEntry> fks))
                    continue;

                foreach (ForeignKeyEntry fk in fks)
                {
                    string fromKey = DbMetadata.TableKey(fk.FromSchema, fk.FromTable);
                    bool scopeIsFromSide = string.Equals(fromKey, scopeTable.Key, StringComparison.OrdinalIgnoreCase);

                    string otherSchema = scopeIsFromSide ? fk.ToSchema : fk.FromSchema;
                    string otherTable = scopeIsFromSide ? fk.ToTable : fk.FromTable;
                    string otherKey = DbMetadata.TableKey(otherSchema, otherTable);

                    if (scopeKeys.Contains(otherKey))
                        continue; // a outra ponta já está na query

                    string otherAlias = AliasGenerator.Generate(otherTable, usedAliases);

                    IReadOnlyList<string> otherColumns = scopeIsFromSide ? fk.ToColumns : fk.FromColumns;
                    IReadOnlyList<string> scopeColumns = scopeIsFromSide ? fk.FromColumns : fk.ToColumns;

                    var on = new StringBuilder();
                    for (int i = 0; i < otherColumns.Count; i++)
                    {
                        if (i > 0) on.Append(" AND ");
                        on.Append(otherAlias).Append('.').Append(SqlIdentifier.Bracket(otherColumns[i]))
                          .Append(" = ")
                          .Append(SqlIdentifier.Bracket(scopeTable.Qualifier)).Append('.')
                          .Append(SqlIdentifier.Bracket(scopeColumns[i]));
                    }

                    string insertText =
                        SqlIdentifier.Bracket(otherSchema) + "." + SqlIdentifier.Bracket(otherTable) +
                        " " + otherAlias + " ON " + on;

                    if (seenInserts.Add(insertText))
                        suggestions.Add(new FkJoinSuggestion(insertText, insertText));
                }
            }

            return suggestions;
        }

        private static string ResolveUniqueSchema(DbMetadata metadata, string tableName)
        {
            string schema = null;
            foreach (TableEntry table in metadata.Tables)
            {
                if (string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    if (schema != null)
                        return null; // ambíguo
                    schema = table.Schema;
                }
            }
            return schema;
        }

        private sealed class ResolvedScopeTable
        {
            public string Key { get; }
            /// <summary>Alias do escopo, ou o nome da tabela quando sem alias.</summary>
            public string Qualifier { get; }

            public ResolvedScopeTable(string key, string qualifier)
            {
                Key = key;
                Qualifier = qualifier;
            }
        }
    }
}
