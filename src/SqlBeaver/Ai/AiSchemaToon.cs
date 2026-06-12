using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Serializa o schema do banco em TOON (Token-Oriented Object Notation) — formato tabular
    /// compacto: cada array declara os campos UMA vez (<c>arr[N]{f1,f2}:</c>) e depois lista só
    /// as linhas de valores. Para schema (muitas tabelas/colunas uniformes) gasta uma fração dos
    /// tokens de JSON/texto, permitindo enviar o contexto COMPLETO. Puro e testável.
    /// </summary>
    public static class AiSchemaToon
    {
        // Tetos de segurança (bancos enormes) — refletidos no [N] emitido, então o TOON segue válido.
        private const int MaxTables = 20000;
        private const int MaxColumns = 100000;

        /// <summary>Schema COMPLETO (todas as tabelas + colunas) em TOON.</summary>
        public static string EncodeFull(DbMetadata metadata)
        {
            if (metadata == null || metadata.Tables == null)
                return string.Empty;
            return Encode(metadata.Tables, metadata);
        }

        /// <summary>Apenas as tabelas do escopo (resolvendo schema único quando necessário) em TOON.</summary>
        public static string EncodeSubset(IReadOnlyList<TableRef> scope, DbMetadata metadata)
        {
            if (metadata == null || scope == null)
                return string.Empty;

            var picked = new List<TableEntry>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (TableRef r in scope)
            {
                string schema = r.Schema ?? metadata.ResolveUniqueSchema(r.Table);
                if (schema == null) continue;
                if (seen.Add(DbMetadata.TableKey(schema, r.Table)))
                    picked.Add(new TableEntry(schema, r.Table));
            }
            return Encode(picked, metadata);
        }

        // ── Núcleo ────────────────────────────────────────────────────────────

        private static string Encode(IReadOnlyList<TableEntry> tables, DbMetadata metadata)
        {
            // Linhas de tabelas e de colunas (achatadas) — dois arrays TOON.
            var tableRows = new List<string>();
            var columnRows = new List<string>();

            foreach (TableEntry t in tables)
            {
                if (tableRows.Count >= MaxTables) break;
                tableRows.Add(Esc(t.Schema) + "," + Esc(t.Name));

                if (columnRows.Count >= MaxColumns) continue;
                IReadOnlyList<ColumnEntry> cols;
                if (metadata.ColumnsByTable != null &&
                    metadata.ColumnsByTable.TryGetValue(DbMetadata.TableKey(t.Schema, t.Name), out cols) &&
                    cols != null)
                {
                    foreach (ColumnEntry c in cols)
                    {
                        if (columnRows.Count >= MaxColumns) break;
                        columnRows.Add(
                            Esc(t.Schema) + "," + Esc(t.Name) + "," + Esc(c.Name) + "," +
                            Esc(c.SqlType) + "," + (c.IsPrimaryKey ? "1" : "0"));
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("tables[").Append(tableRows.Count).Append("]{schema,name}:");
            foreach (string row in tableRows)
                sb.Append('\n').Append("  ").Append(row);

            sb.Append('\n');
            sb.Append("columns[").Append(columnRows.Count).Append("]{schema,table,name,type,pk}:");
            foreach (string row in columnRows)
                sb.Append('\n').Append("  ").Append(row);

            return sb.ToString();
        }

        /// <summary>Escapa um valor TOON: aspas quando contém delimitador/aspas/quebra ou bordas em branco.</summary>
        internal static string Esc(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            bool needsQuote =
                value.IndexOf(',') >= 0 || value.IndexOf(':') >= 0 ||
                value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0 ||
                value[0] == ' ' || value[value.Length - 1] == ' ';

            if (!needsQuote)
                return value;

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
