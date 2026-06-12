using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Ai
{
    /// <summary>Nível de contexto de schema enviado à IA.</summary>
    public enum AiSchemaScope
    {
        /// <summary>Apenas as tabelas do escopo do statement atual.</summary>
        Scope,
        /// <summary>Nenhum schema é enviado.</summary>
        None,
        /// <summary>Todas as tabelas do metadata (limitado a 60).</summary>
        All,
    }

    /// <summary>
    /// Renderiza as tabelas (colunas + tipos + marcador PK) como texto compacto para o
    /// prompt. Uma linha por tabela: <c>Tabela: schema.nome (Col tipo [PK], ...)</c>.
    /// </summary>
    public static class AiSchemaContext
    {
        private const int MaxTables = 60;

        public static string Render(IReadOnlyList<TableRef> scope, DbMetadata metadata, AiSchemaScope level)
        {
            if (level == AiSchemaScope.None || metadata == null)
                return string.Empty;

            var lines = new List<string>();

            if (level == AiSchemaScope.All)
            {
                int count = 0;
                int total = metadata.Tables != null ? metadata.Tables.Count : 0;
                if (metadata.Tables != null)
                {
                    foreach (TableEntry t in metadata.Tables)
                    {
                        if (count >= MaxTables) break;
                        lines.Add(RenderTable(t.Schema, t.Name, metadata));
                        count++;
                    }
                }
                if (total > MaxTables)
                    lines.Add($"... (+{total - MaxTables} tabelas)");
            }
            else // Scope
            {
                if (scope != null)
                {
                    foreach (TableRef r in scope)
                    {
                        string schema = r.Schema ?? metadata.ResolveUniqueSchema(r.Table);
                        if (schema == null)
                            continue; // não resolvido — pula
                        lines.Add(RenderTable(schema, r.Table, metadata));
                    }
                }
            }

            return string.Join("\n", lines);
        }

        private static string RenderTable(string schema, string table, DbMetadata metadata)
        {
            var sb = new StringBuilder();
            sb.Append("Tabela: ").Append(schema).Append('.').Append(table);

            IReadOnlyList<ColumnEntry> columns;
            if (metadata.ColumnsByTable != null &&
                metadata.ColumnsByTable.TryGetValue(DbMetadata.TableKey(schema, table), out columns) &&
                columns != null && columns.Count > 0)
            {
                sb.Append(" (");
                for (int i = 0; i < columns.Count; i++)
                {
                    ColumnEntry c = columns[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(c.Name).Append(' ').Append(c.SqlType);
                    if (c.IsPrimaryKey) sb.Append(" PK");
                }
                sb.Append(')');
            }

            return sb.ToString();
        }
    }
}
