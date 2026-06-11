using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    public sealed class DangerousStatement
    {
        public string Keyword { get; }
        /// <summary>Linha 1-based do DELETE/UPDATE.</summary>
        public int Line { get; }

        public DangerousStatement(string keyword, int line)
        {
            Keyword = keyword;
            Line = line;
        }
    }

    /// <summary>Encontra DELETE/UPDATE de nível superior sem WHERE no mesmo statement.
    /// Mesma máquina de estados de comentários/strings dos outros analisadores. Puro.</summary>
    public static class DangerousStatementDetector
    {
        public static IReadOnlyList<DangerousStatement> Find(string sql)
        {
            var result = new List<DangerousStatement>();
            if (string.IsNullOrEmpty(sql))
                return result;

            int line = 1;
            int parenDepth = 0;
            bool inLineComment = false, inString = false, inQuotedIdent = false, inBracket = false;
            int blockCommentDepth = 0;

            string pendingKeyword = null;
            int pendingLine = 0;
            bool hasTopLevelWhere = false;

            void EndStatement()
            {
                if (pendingKeyword != null && !hasTopLevelWhere)
                    result.Add(new DangerousStatement(pendingKeyword, pendingLine));
                pendingKeyword = null;
                hasTopLevelWhere = false;
            }

            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];
                if (c == '\n') line++;

                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < sql.Length && sql[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { if (c == '\'') inString = false; i++; continue; }
                if (inQuotedIdent) { if (c == '"') inQuotedIdent = false; i++; continue; }
                if (inBracket) { if (c == ']') inBracket = false; i++; continue; }

                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                if (c == ';' && parenDepth == 0)
                {
                    EndStatement();
                    i++;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                        i++;
                    string word = sql.Substring(start, i - start);

                    if (parenDepth == 0)
                    {
                        if (string.Equals(word, "GO", StringComparison.OrdinalIgnoreCase))
                        {
                            EndStatement();
                        }
                        else if (string.Equals(word, "DELETE", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(word, "UPDATE", StringComparison.OrdinalIgnoreCase))
                        {
                            EndStatement(); // statement anterior sem ';' explícito
                            pendingKeyword = word.ToUpperInvariant();
                            pendingLine = line;
                        }
                        else if (string.Equals(word, "WHERE", StringComparison.OrdinalIgnoreCase))
                        {
                            hasTopLevelWhere = true;
                        }
                        else if (string.Equals(word, "SELECT", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(word, "INSERT", StringComparison.OrdinalIgnoreCase))
                        {
                            // novo statement implícito encerra o pendente
                            EndStatement();
                        }
                    }
                    continue;
                }

                i++;
            }

            EndStatement();
            return result;
        }
    }
}
