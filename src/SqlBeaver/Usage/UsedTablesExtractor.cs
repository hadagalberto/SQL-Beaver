using System;
using System.Collections.Generic;
using SqlBeaver.Metadata;

namespace SqlBeaver.Usage
{
    /// <summary>Resultado de uma extração de tabelas usadas num script.</summary>
    public sealed class ExtractionResult
    {
        /// <summary>
        /// Chaves "schema.tabela" distintas (case-insensitive) extraídas do script,
        /// contando cada tabela no máximo uma vez por chamada Extract.
        /// </summary>
        public IReadOnlyList<string> TableKeys { get; }

        /// <summary>
        /// Pares não-ordenados distintos de tabelas co-usadas num mesmo statement,
        /// contando cada par no máximo uma vez por chamada Extract.
        /// Statements com mais de 6 tabelas não geram pares.
        /// </summary>
        public IReadOnlyList<string> JoinPairKeys { get; }

        public ExtractionResult(IReadOnlyList<string> tableKeys, IReadOnlyList<string> joinPairKeys)
        {
            TableKeys = tableKeys;
            JoinPairKeys = joinPairKeys;
        }
    }

    /// <summary>
    /// Extrai as tabelas usadas (e pares co-usados por statement) de um script executado.
    /// Nomes não qualificados resolvem via <see cref="DbMetadata.ResolveUniqueSchema"/>;
    /// irresolvíveis são ignorados.
    /// Statements com mais de 6 tabelas não geram pares.
    /// Puro, sem dependências de VS.
    /// </summary>
    public static class UsedTablesExtractor
    {
        private const int PairMaxTables = 6;

        public static ExtractionResult Extract(string sql, DbMetadata metadata)
        {
            if (string.IsNullOrEmpty(sql) || metadata == null)
                return new ExtractionResult(Array.Empty<string>(), Array.Empty<string>());

            // Global dedup sets (across all statements)
            var allTableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allPairKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int i = 0;
            int len = sql.Length;

            // Per-statement state
            var stmtTableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool inLineComment = false;
            bool inString = false;
            bool inQuotedIdent = false;
            int blockCommentDepth = 0;
            int parenDepth = 0;

            while (i < len)
            {
                char c = sql[i];

                // ── states: comment / string / quoted ident ────────────────────
                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    i++;
                    continue;
                }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < len && sql[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < len && sql[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++;
                    continue;
                }
                if (inString)
                {
                    if (c == '\'') inString = false;
                    i++;
                    continue;
                }
                if (inQuotedIdent)
                {
                    if (c == '"') inQuotedIdent = false;
                    i++;
                    continue;
                }

                if (c == '-' && i + 1 < len && sql[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < len && sql[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                // ── end of statement: ';' ──────────────────────────────────────
                if (c == ';' && parenDepth == 0)
                {
                    FlushStatement(stmtTableKeys, allTableKeys, allPairKeys);
                    stmtTableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    i++;
                    continue;
                }

                // ── tokens ─────────────────────────────────────────────────────
                if (c == '[' || IsIdentifierStart(c))
                {
                    int tokenStart = i;
                    string token = ReadIdentifier(sql, ref i);

                    // GO batch separator
                    if (parenDepth == 0 && string.Equals(token, "GO", StringComparison.OrdinalIgnoreCase))
                    {
                        FlushStatement(stmtTableKeys, allTableKeys, allPairKeys);
                        stmtTableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        continue;
                    }

                    if (parenDepth == 0 &&
                        (string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(token, "JOIN", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(token, "UPDATE", StringComparison.OrdinalIgnoreCase)))
                    {
                        bool allowComma = string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase);
                        do
                        {
                            SkipWhitespace(sql, ref i);
                            string resolvedKey = TryReadTableKey(sql, ref i, metadata);
                            if (resolvedKey == null)
                                break;
                            stmtTableKeys.Add(resolvedKey);
                            SkipWhitespace(sql, ref i);
                        } while (allowComma && i < len && sql[i] == ',' && ++i > 0);
                    }
                    continue;
                }

                i++;
            }

            // Flush last statement (no trailing ; or GO)
            FlushStatement(stmtTableKeys, allTableKeys, allPairKeys);

            var tableList = new List<string>(allTableKeys);
            var pairList = new List<string>(allPairKeys);
            return new ExtractionResult(tableList, pairList);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void FlushStatement(
            HashSet<string> stmtTableKeys,
            HashSet<string> allTableKeys,
            HashSet<string> allPairKeys)
        {
            if (stmtTableKeys.Count == 0)
                return;

            // Add tables to global set
            foreach (string k in stmtTableKeys)
                allTableKeys.Add(k);

            // Generate pairs only when ≤ 6 distinct tables in this statement
            if (stmtTableKeys.Count <= PairMaxTables)
            {
                var keys = new List<string>(stmtTableKeys);
                for (int a = 0; a < keys.Count; a++)
                {
                    for (int b = a + 1; b < keys.Count; b++)
                    {
                        allPairKeys.Add(UsageRanker.PairKey(keys[a], keys[b]));
                    }
                }
            }
        }

        /// <summary>
        /// Tenta ler uma referência de tabela (possivelmente qualificada) e retorna
        /// a TableKey resolvida, ou null se não qualificado + ambíguo/desconhecido.
        /// Consome o alias opcional, que é descartado.
        /// </summary>
        private static string TryReadTableKey(string sql, ref int i, DbMetadata metadata)
        {
            int len = sql.Length;
            if (i >= len || (sql[i] != '[' && !IsIdentifierStart(sql[i])))
                return null; // subquery "(", VALUES, etc.

            var parts = new List<string> { ReadIdentifier(sql, ref i) };
            while (i < len && sql[i] == '.')
            {
                i++;
                if (i >= len || (sql[i] != '[' && !IsIdentifierStart(sql[i])))
                    break;
                parts.Add(ReadIdentifier(sql, ref i));
            }

            // até 3 partes (db.schema.tabela): usa as duas últimas
            string table = parts[parts.Count - 1];
            string schema = parts.Count >= 2 ? parts[parts.Count - 2] : null;

            // Resolve schema se não qualificado
            if (schema == null)
            {
                schema = metadata.ResolveUniqueSchema(table);
                if (schema == null)
                    return null; // ambíguo ou desconhecido
            }

            // Consome alias (AS palavra | palavra-não-keyword) mas descarta
            int save = i;
            SkipWhitespace(sql, ref i);
            if (i < len && (sql[i] == '[' || IsIdentifierStart(sql[i])))
            {
                string word = ReadIdentifier(sql, ref i);
                if (!string.Equals(word, "AS", StringComparison.OrdinalIgnoreCase) &&
                    SqlBeaver.Analysis.SqlKeywords.All.Contains(word))
                {
                    i = save; // keyword que não é AS: devolve
                }
                else if (string.Equals(word, "AS", StringComparison.OrdinalIgnoreCase))
                {
                    SkipWhitespace(sql, ref i);
                    if (i < len && (sql[i] == '[' || IsIdentifierStart(sql[i])))
                        ReadIdentifier(sql, ref i); // consume alias name, discard
                }
                // else: non-keyword word = alias, already consumed, discard
            }

            return DbMetadata.TableKey(schema, table);
        }

        private static string ReadIdentifier(string sql, ref int i)
        {
            int len = sql.Length;
            if (sql[i] == '[')
            {
                int close = sql.IndexOf(']', i + 1);
                if (close < 0) { string rest = sql.Substring(i + 1); i = len; return rest; }
                string name = sql.Substring(i + 1, close - i - 1);
                i = close + 1;
                return name;
            }

            int start = i;
            while (i < len && IsIdentifierChar(sql[i]))
                i++;
            return sql.Substring(start, i - start);
        }

        private static void SkipWhitespace(string sql, ref int i)
        {
            while (i < sql.Length && char.IsWhiteSpace(sql[i]))
                i++;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
    }
}
