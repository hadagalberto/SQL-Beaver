using System;
using System.Collections.Generic;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Renames all token-aware (word-boundary, outside strings/comments/brackets)
    /// occurrences of <paramref name="oldName"/> within a text range.
    /// Pure, no VS dependencies.
    /// </summary>
    public static class TokenRenamer
    {
        /// <summary>
        /// Returns textual replacements (sorted DESCENDING by Start) that rename
        /// all occurrences of <paramref name="oldName"/> within [rangeStart, rangeEnd)
        /// that are outside strings, comments, and bracket identifiers.
        /// Comparison is OrdinalIgnoreCase.
        /// The <paramref name="oldName"/> may start with '@' (variable).
        /// </summary>
        public static IReadOnlyList<TextReplacement> Rename(
            string text, int rangeStart, int rangeEnd,
            string oldName, string newName)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (oldName == null) throw new ArgumentNullException(nameof(oldName));
            if (newName == null) throw new ArgumentNullException(nameof(newName));
            if (rangeStart < 0) rangeStart = 0;
            if (rangeEnd > text.Length) rangeEnd = text.Length;

            var edits = new List<TextReplacement>();
            if (string.IsNullOrEmpty(oldName) || rangeStart >= rangeEnd) return edits;

            // State machine tracking comment/string/bracket context
            bool inLineComment = false;
            bool inString = false;
            bool inBracket = false;
            bool inQuotedIdent = false;
            int blockDepth = 0;

            int i = rangeStart;
            while (i < rangeEnd)
            {
                char c = text[i];

                // --- Comment/string state transitions ---
                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    i++; continue;
                }
                if (blockDepth > 0)
                {
                    if (c == '*' && i + 1 < rangeEnd && text[i + 1] == '/') { blockDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < rangeEnd && text[i + 1] == '*') { blockDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)
                {
                    if (c == '\'') inString = false;
                    i++; continue;
                }
                if (inBracket)
                {
                    if (c == ']') inBracket = false;
                    i++; continue;
                }
                if (inQuotedIdent)
                {
                    if (c == '"') inQuotedIdent = false;
                    i++; continue;
                }

                if (c == '-' && i + 1 < rangeEnd && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < rangeEnd && text[i + 1] == '*') { blockDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }

                // --- Identifier / token matching ---
                if (IsIdentStart(c) || (c == '@' && i < rangeEnd))
                {
                    int tokenStart = i;
                    // Read the full identifier (including leading @)
                    while (i < rangeEnd && IsIdentContinue(text[i])) i++;
                    int tokenLen = i - tokenStart;
                    string token = text.Substring(tokenStart, tokenLen);

                    if (tokenLen == oldName.Length &&
                        string.Equals(token, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Word-boundary check: char after token (i is already there)
                        if (i < text.Length && IsIdentContinue(text[i])) continue; // part of longer token

                        edits.Add(new TextReplacement
                        {
                            Start = tokenStart,
                            Length = tokenLen,
                            NewText = newName
                        });
                    }
                    continue;
                }

                i++;
            }

            edits.Sort((a, b) => b.Start.CompareTo(a.Start));
            return edits;
        }

        // ---------------------------------------------------------------
        // StatementBounds helper
        // ---------------------------------------------------------------

        /// <summary>
        /// Returns the [start, end) bounds of the statement containing <paramref name="caret"/>.
        /// Statement delimiters are ';' and lines consisting only of GO (outside strings/comments).
        /// </summary>
        public static (int start, int end) StatementBounds(string text, int caret)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (caret < 0) caret = 0;
            if (caret > text.Length) caret = text.Length;

            // Collect statement boundary positions (positions of ';' or GO lines)
            var boundaries = new List<int> { -1 }; // sentinel before start

            bool inLineComment = false;
            bool inString = false, inBracket = false, inQuotedIdent = false;
            int blockDepth = 0;

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { if (c == '\'') inString = false; i++; continue; }
                if (inBracket) { if (c == ']') inBracket = false; i++; continue; }
                if (inQuotedIdent) { if (c == '"') inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }

                if (c == ';')
                {
                    boundaries.Add(i);
                    i++; continue;
                }

                // GO on its own line
                if ((c == 'G' || c == 'g') &&
                    i + 1 < text.Length && (text[i + 1] == 'O' || text[i + 1] == 'o'))
                {
                    // Check that after GO there's only whitespace/end-of-line
                    int j = i + 2;
                    while (j < text.Length && text[j] != '\n' && char.IsWhiteSpace(text[j])) j++;
                    bool goAtEndOfLine = (j >= text.Length || text[j] == '\n' || text[j] == '\r');

                    // Check that before GO on the same line there's only whitespace
                    int k = i - 1;
                    while (k >= 0 && text[k] != '\n' && char.IsWhiteSpace(text[k])) k--;
                    bool goAtStartOfLine = (k < 0 || text[k] == '\n');

                    if (goAtEndOfLine && goAtStartOfLine)
                    {
                        boundaries.Add(i - 1); // boundary just before the GO
                        boundaries.Add(i + 2); // boundary just after GO
                        i += 2; continue;
                    }
                }

                i++;
            }

            boundaries.Add(text.Length); // sentinel after end

            // Find the two boundaries that straddle the caret
            int stmtStart = 0;
            int stmtEnd = text.Length;

            for (int b = 0; b < boundaries.Count - 1; b++)
            {
                int bStart = boundaries[b] + 1; // character after the delimiter
                int bEnd = boundaries[b + 1];   // character of the delimiter (exclusive: the delimiter itself)

                // Trim leading whitespace
                int actualStart = bStart;
                while (actualStart < bEnd && char.IsWhiteSpace(text[actualStart])) actualStart++;

                if (caret >= bStart && caret <= bEnd)
                {
                    stmtStart = bStart;
                    stmtEnd = bEnd;
                    break;
                }
            }

            return (stmtStart, stmtEnd);
        }

        // ---------------------------------------------------------------
        // Character class helpers
        // ---------------------------------------------------------------

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '@';
        private static bool IsIdentContinue(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';
    }
}
