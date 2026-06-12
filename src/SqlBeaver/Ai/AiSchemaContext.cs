using System;
using System.Collections.Generic;
using System.Globalization;
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

        private const int MaxDetailed = 40;     // tabelas detalhadas (com colunas) na geração
        private const int MaxCatalog = 1000;    // nomes schema.tabela no catálogo

        /// <summary>
        /// Contexto para a GERAÇÃO por comentário: como o escopo costuma estar vazio (ainda não há
        /// FROM), ancora a IA nas tabelas REAIS. Detalha (com colunas) as tabelas do escopo e as que
        /// casam com palavras do comentário; depois lista o catálogo de nomes <c>schema.tabela</c> do
        /// banco para a IA escolher a tabela certa. <paramref name="level"/> None → vazio.
        /// </summary>
        public static string RenderForGenerate(string description, IReadOnlyList<TableRef> scope,
            DbMetadata metadata, AiSchemaScope level)
        {
            if (level == AiSchemaScope.None || metadata == null || metadata.Tables == null)
                return string.Empty;

            var detailedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var detailed = new List<string>();

            // 1) Tabelas do escopo (quando houver).
            if (scope != null)
            {
                foreach (TableRef r in scope)
                {
                    string schema = r.Schema ?? metadata.ResolveUniqueSchema(r.Table);
                    if (schema == null) continue;
                    string key = DbMetadata.TableKey(schema, r.Table);
                    if (detailedKeys.Add(key))
                        detailed.Add(RenderTable(schema, r.Table, metadata));
                }
            }

            // 2) Tabelas que casam com as palavras do comentário (até o limite).
            List<string> keywords = Keywords(description);
            foreach (TableEntry t in metadata.Tables)
            {
                if (detailed.Count >= MaxDetailed) break;
                string key = DbMetadata.TableKey(t.Schema, t.Name);
                if (detailedKeys.Contains(key)) continue;
                if (level == AiSchemaScope.All || Matches(t.Schema, t.Name, keywords))
                {
                    detailedKeys.Add(key);
                    detailed.Add(RenderTable(t.Schema, t.Name, metadata));
                }
            }

            // 3) Catálogo de nomes (as demais tabelas), para a IA conhecer o que existe.
            var catalog = new List<string>();
            int total = metadata.Tables.Count;
            foreach (TableEntry t in metadata.Tables)
            {
                if (catalog.Count >= MaxCatalog) break;
                if (detailedKeys.Contains(DbMetadata.TableKey(t.Schema, t.Name))) continue;
                catalog.Add("- " + t.Schema + "." + t.Name);
            }

            var sb = new StringBuilder();
            if (detailed.Count > 0)
            {
                sb.Append("Tabelas relevantes (com colunas):\n");
                sb.Append(string.Join("\n", detailed));
            }
            if (catalog.Count > 0)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append("Outras tabelas do banco (nome qualificado):\n");
                sb.Append(string.Join("\n", catalog));
                int shown = detailed.Count + catalog.Count;
                if (total > shown)
                    sb.Append("\n... (+").Append(total - shown).Append(" tabelas)");
            }
            return sb.ToString();
        }

        /// <summary>Palavras significativas do comentário (sem acentos, minúsculas, ≥ 4 chars).</summary>
        internal static List<string> Keywords(string description)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(description)) return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in StripAccents(description).ToLowerInvariant()
                         .Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '.', '(', ')', '\'', '"', '/', '-' },
                                StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Length >= 4 && seen.Add(raw))
                    result.Add(raw);
            }
            return result;
        }

        /// <summary>Casa o nome/schema da tabela com alguma palavra do comentário
        /// (substring nos dois sentidos ou prefixo comum de 4 — pega plural/conjugação).</summary>
        internal static bool Matches(string schema, string table, List<string> keywords)
        {
            if (keywords == null || keywords.Count == 0) return false;
            string n = StripAccents(table ?? string.Empty).ToLowerInvariant();
            string s = StripAccents(schema ?? string.Empty).ToLowerInvariant();
            foreach (string k in keywords)
            {
                if (n.Contains(k) || k.Contains(n) || s.Contains(k)) return true;
                if (n.Length >= 4 && k.Length >= 4 && n.Substring(0, 4) == k.Substring(0, 4)) return true;
            }
            return false;
        }

        internal static string StripAccents(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            string formD = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (char c in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
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
