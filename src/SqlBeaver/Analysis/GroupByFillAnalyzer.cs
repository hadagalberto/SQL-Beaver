using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    /// <summary>
    /// Dado o texto e o caret logo após GROUP BY, devolve a lista de expressões NÃO agregadas
    /// do SELECT do mesmo statement (qualificadas como aparecem no SELECT, alias removido),
    /// ou vazio se não houver.
    /// </summary>
    public static class GroupByFillAnalyzer
    {
        private static readonly string[] AggregateFunctions =
        {
            "SUM(", "COUNT(", "AVG(", "MIN(", "MAX(",
            "COUNT_BIG(", "STRING_AGG(", "STDEV(", "VAR(",
            "STDEVP(", "VARP(", "CHECKSUM_AGG("
        };

        /// <summary>
        /// Extrai os itens NÃO agregados da lista de SELECT do statement que contém o caret.
        /// </summary>
        public static IReadOnlyList<string> NonAggregatedSelectColumns(string text, int caretPosition)
        {
            if (string.IsNullOrEmpty(text) || caretPosition < 0 || caretPosition > text.Length)
                return Array.Empty<string>();

            // Obter bounds do statement atual
            StatementBounds bounds = StatementScopeAnalyzer.GetStatementBoundsAt(text, caretPosition);
            if (bounds.Length == 0)
                return Array.Empty<string>();

            string stmt = text.Substring(bounds.Start, bounds.Length);

            // Localizar SELECT e FROM no nível 0 de parênteses
            int selectPos = FindKeywordAtDepthZero(stmt, "SELECT");
            if (selectPos < 0)
                return Array.Empty<string>();

            // Encontrar o FROM mais próximo ao SELECT em depth 0
            int fromPos = FindKeywordAtDepthZero(stmt, "FROM", startFrom: selectPos + 6);
            if (fromPos < 0)
                return Array.Empty<string>();

            // Texto entre SELECT e FROM (a lista de colunas)
            int listStart = selectPos + 6; // após "SELECT"
            string selectList = stmt.Substring(listStart, fromPos - listStart).Trim();

            // Remover DISTINCT/TOP/ALL se presentes no início
            selectList = TrimLeadingModifiers(selectList);

            if (string.IsNullOrWhiteSpace(selectList))
                return Array.Empty<string>();

            // Dividir em itens por vírgula de nível 0
            IReadOnlyList<string> items = SplitTopLevelComma(selectList);

            var result = new List<string>();
            foreach (string item in items)
            {
                string trimmed = item.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (IsAggregate(trimmed)) continue;

                // Remover alias AS xxx ou alias simples no final
                string expr = RemoveAlias(trimmed);
                if (!string.IsNullOrWhiteSpace(expr))
                    result.Add(expr);
            }

            return result;
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        private static string TrimLeadingModifiers(string s)
        {
            // Remove DISTINCT / ALL / TOP N / TOP (n) PERCENT etc.
            string t = s.TrimStart();

            if (StartsWithKeyword(t, "DISTINCT", out int d))
                t = t.Substring(d).TrimStart();
            else if (StartsWithKeyword(t, "ALL", out int a))
                t = t.Substring(a).TrimStart();

            if (StartsWithKeyword(t, "TOP", out int topLen))
            {
                // consume TOP, optional whitespace, then number or (expr), optional PERCENT
                t = t.Substring(topLen).TrimStart();
                if (t.Length > 0 && t[0] == '(')
                {
                    int close = t.IndexOf(')');
                    if (close >= 0) t = t.Substring(close + 1).TrimStart();
                }
                else
                {
                    while (t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '.'))
                        t = t.Substring(1);
                    t = t.TrimStart();
                }
                if (StartsWithKeyword(t, "PERCENT", out int pLen))
                    t = t.Substring(pLen).TrimStart();
                if (StartsWithKeyword(t, "WITH TIES", out int wtLen))
                    t = t.Substring(wtLen).TrimStart();
            }

            return t;
        }

        private static bool StartsWithKeyword(string s, string keyword, out int length)
        {
            length = keyword.Length;
            if (s.Length < keyword.Length) return false;
            if (!string.Equals(s.Substring(0, keyword.Length), keyword, StringComparison.OrdinalIgnoreCase))
                return false;
            if (s.Length > keyword.Length && IsIdentChar(s[keyword.Length]))
                return false;
            return true;
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';

        private static IReadOnlyList<string> SplitTopLevelComma(string text)
        {
            var result = new List<string>();
            int parenDepth = 0;
            int start = 0;
            bool inString = false, inBracket = false, inQuoted = false;
            bool inLineComment = false;
            int blockDepth = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inLineComment) { if (c == '\n') inLineComment = false; continue; }
                if (blockDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockDepth--; i++; }
                    else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth++; i++; }
                    continue;
                }
                if (inString) { if (c == '\'') inString = false; continue; }
                if (inBracket) { if (c == ']') inBracket = false; continue; }
                if (inQuoted) { if (c == '"') inQuoted = false; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth = 1; i++; continue; }
                if (c == '\'') { inString = true; continue; }
                if (c == '[') { inBracket = true; continue; }
                if (c == '"') { inQuoted = true; continue; }
                if (c == '(') { parenDepth++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; continue; }

                if (c == ',' && parenDepth == 0)
                {
                    result.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }

            result.Add(text.Substring(start));
            return result;
        }

        private static bool IsAggregate(string item)
        {
            string upper = item.TrimStart().ToUpperInvariant();
            foreach (string agg in AggregateFunctions)
            {
                if (upper.StartsWith(agg, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Remove trailing "AS alias" ou "alias" simples do item.</summary>
        private static string RemoveAlias(string item)
        {
            // Tokenizar o item: pegar os tokens separados por whitespace no nível 0
            // O alias é o último token se tiver pelo menos dois tokens; se o
            // penúltimo for AS, remover os dois últimos; senão só o último.
            var tokens = TokenizeTopLevel(item);
            if (tokens.Count == 0) return item.Trim();
            if (tokens.Count == 1) return tokens[0];

            // Check if second-to-last token is AS
            if (tokens.Count >= 2)
            {
                string last = tokens[tokens.Count - 1];
                string secondLast = tokens[tokens.Count - 2];

                if (string.Equals(secondLast, "AS", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove last two tokens
                    return RebuildTokens(tokens, 0, tokens.Count - 2);
                }

                // If last token looks like an identifier alias (not an operator/keyword in our list)
                // and there are at least 2 tokens, the last one might be an alias
                // Only strip if it's a simple identifier (no dots, no special chars except _)
                if (IsSimpleIdentifier(last) && tokens.Count >= 2)
                {
                    return RebuildTokens(tokens, 0, tokens.Count - 1);
                }
            }

            return item.Trim();
        }

        private static string RebuildTokens(List<string> tokens, int start, int end)
        {
            // We need to reconstruct the original text for the tokens up to 'end'
            // Simple: join with space (they were split by whitespace anyway)
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < end; i++)
            {
                if (i > start) sb.Append(' ');
                sb.Append(tokens[i]);
            }
            return sb.ToString().Trim();
        }

        private static bool IsSimpleIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s[0] == '[' && s[s.Length - 1] == ']') return true; // bracketed identifier
            foreach (char c in s)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '@' && c != '#' && c != '.')
                    return false;
            }
            return true;
        }

        private static List<string> TokenizeTopLevel(string item)
        {
            // Split item into whitespace-separated tokens at paren depth 0
            var tokens = new List<string>();
            int parenDepth = 0;
            bool inString = false, inBracket = false;
            int start = -1;

            for (int i = 0; i < item.Length; i++)
            {
                char c = item[i];

                if (inString) { if (c == '\'') inString = false; if (start < 0) start = i; continue; }
                if (inBracket)
                {
                    if (c == ']') inBracket = false;
                    if (start < 0) start = i;
                    continue;
                }

                if (c == '\'') { inString = true; if (start < 0) start = i; continue; }
                if (c == '[') { inBracket = true; if (start < 0) start = i; continue; }
                if (c == '(') { parenDepth++; if (start < 0) start = i; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; if (start < 0) start = i; continue; }

                if (char.IsWhiteSpace(c) && parenDepth == 0)
                {
                    if (start >= 0)
                    {
                        tokens.Add(item.Substring(start, i - start));
                        start = -1;
                    }
                }
                else
                {
                    if (start < 0) start = i;
                }
            }

            if (start >= 0)
                tokens.Add(item.Substring(start));

            return tokens;
        }

        private static int FindKeywordAtDepthZero(string text, string keyword, int startFrom = 0)
        {
            int parenDepth = 0;
            bool inString = false, inBracket = false, inQuoted = false, inLineComment = false;
            int blockDepth = 0;
            int kwLen = keyword.Length;

            for (int i = startFrom; i < text.Length; i++)
            {
                char c = text[i];

                if (inLineComment) { if (c == '\n') inLineComment = false; continue; }
                if (blockDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockDepth--; i++; }
                    else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth++; i++; }
                    continue;
                }
                if (inString) { if (c == '\'') inString = false; continue; }
                if (inBracket) { if (c == ']') inBracket = false; continue; }
                if (inQuoted) { if (c == '"') inQuoted = false; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth = 1; i++; continue; }
                if (c == '\'') { inString = true; continue; }
                if (c == '[') { inBracket = true; continue; }
                if (c == '"') { inQuoted = true; continue; }
                if (c == '(') { parenDepth++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; continue; }

                if (parenDepth == 0 && i + kwLen <= text.Length)
                {
                    if (string.Compare(text, i, keyword, 0, kwLen, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        // Check boundaries: must not be preceded/followed by identifier chars
                        bool prevOk = i == 0 || !IsIdentChar(text[i - 1]);
                        bool nextOk = i + kwLen >= text.Length || !IsIdentChar(text[i + kwLen]);
                        if (prevOk && nextOk)
                            return i;
                    }
                }
            }

            return -1;
        }
    }
}
