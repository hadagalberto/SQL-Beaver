using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Appends a <c>;</c> to the end of each statement that lacks one, using the v4
    /// statement splitter (<see cref="StatementScopeAnalyzer.EnumerateStatements"/>).
    /// GO lines are preserved; already-terminated statements are untouched; the ';' is
    /// placed right after the last content character (before trailing whitespace/comments).
    /// Pure, no VS dependencies.
    /// </summary>
    public static class SemicolonInserter
    {
        public static string AddSemicolons(string sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));
            if (sql.Length == 0) return sql;

            IReadOnlyList<StatementScopeAnalyzer.StatementSpan> spans =
                StatementScopeAnalyzer.EnumerateStatements(sql);

            // Collect insertion offsets (descending so earlier offsets stay valid).
            var insertAt = new List<int>();
            foreach (StatementScopeAnalyzer.StatementSpan span in spans)
            {
                if (span.EndedWithSemicolon) continue;

                int contentEnd = LastContentOffset(sql, span.RawStart, span.RawEnd);
                if (contentEnd < 0) continue; // statement had no real content (comment only)
                insertAt.Add(contentEnd);
            }

            if (insertAt.Count == 0) return sql;

            insertAt.Sort();
            insertAt.Reverse();

            var sb = new StringBuilder(sql);
            foreach (int offset in insertAt)
                sb.Insert(offset, ';');

            return sb.ToString();
        }

        /// <summary>
        /// Returns the offset just AFTER the last non-whitespace content character within
        /// [start, end), skipping trailing line/block comments and whitespace. -1 if the
        /// statement has no real content.
        /// </summary>
        private static int LastContentOffset(string text, int start, int end)
        {
            // Walk forward tracking the offset just after the last "real" (non comment,
            // non whitespace) character. This is robust against trailing comments.
            int lastContent = -1;
            bool inLineComment = false, inString = false, inQuotedIdent = false, inBracket = false;
            int blockDepth = 0;

            int i = start;
            while (i < end)
            {
                char c = text[i];

                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockDepth > 0)
                {
                    if (c == '*' && i + 1 < end && text[i + 1] == '/') { blockDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < end && text[i + 1] == '*') { blockDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { lastContent = i + 1; if (c == '\'') inString = false; i++; continue; }
                if (inQuotedIdent) { lastContent = i + 1; if (c == '"') inQuotedIdent = false; i++; continue; }
                if (inBracket) { lastContent = i + 1; if (c == ']') inBracket = false; i++; continue; }

                if (c == '-' && i + 1 < end && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < end && text[i + 1] == '*') { blockDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; lastContent = i + 1; i++; continue; }
                if (c == '"') { inQuotedIdent = true; lastContent = i + 1; i++; continue; }
                if (c == '[') { inBracket = true; lastContent = i + 1; i++; continue; }

                if (!char.IsWhiteSpace(c))
                    lastContent = i + 1;
                i++;
            }

            return lastContent;
        }
    }
}
