using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Refactoring
{
    /// <summary>Represents a text replacement (start offset, length, new text).</summary>
    public sealed class TextReplacement
    {
        public int Start;
        public int Length;
        public string NewText;
    }

    /// <summary>
    /// Expands a <c>*</c> or <c>alias.*</c> in a SELECT to the concrete column list.
    /// Pure class, no VS dependencies.
    /// </summary>
    public static class WildcardExpander
    {
        /// <summary>
        /// If the caret is on/adjacent to a <c>*</c> (or <c>alias.*</c>) of a SELECT,
        /// returns the edit that replaces it with the column list from scope.
        /// Returns null when not applicable.
        /// </summary>
        public static TextReplacement TryExpand(string text, int caretPosition, DbMetadata metadata)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (caretPosition < 0 || caretPosition > text.Length) return null;

            // Localiza o '*' (ou alias.*) no/adjacente ao caret e o span de substituição.
            if (!TryFindWildcardAt(text, caretPosition, out int replStart, out int starEnd, out string aliasOrNull))
                return null;

            int starPos = starEnd - 1;

            // Get tables in scope
            IReadOnlyList<TableRef> tables = StatementScopeAnalyzer.GetTablesInScope(text, caretPosition);

            int replLen = starEnd - replStart;

            // Indentação de continuação = coluna (offset) de replStart na sua linha, para que
            // as colunas seguintes fiquem alinhadas sob a primeira (mudança intencional v1:
            // cada coluna em sua própria linha, no estilo trailing-comma).
            string continuationIndent = new string(' ', ColumnOffsetOnLine(text, replStart));

            // Build column list
            string columns;
            if (aliasOrNull != null)
            {
                // alias.* — only that table's columns, bare names
                TableRef matched = FindByAlias(tables, aliasOrNull);
                if (matched == null) return null;

                IReadOnlyList<ColumnEntry> cols = GetColumns(matched, metadata);
                if (cols == null || cols.Count == 0) return null;

                columns = JoinColumns(cols, null, continuationIndent);
            }
            else
            {
                // bare * — all scope tables
                if (tables.Count == 0) return null;

                bool qualify = tables.Count > 1;
                var sb = new StringBuilder();
                bool first = true;
                bool anyResolved = false;

                foreach (TableRef tref in tables)
                {
                    IReadOnlyList<ColumnEntry> cols = GetColumns(tref, metadata);
                    if (cols == null || cols.Count == 0) continue;

                    anyResolved = true;
                    string qualifier = qualify ? (tref.Alias ?? tref.Table) : null;
                    foreach (ColumnEntry col in cols)
                    {
                        // v1: uma coluna por linha (trailing-comma), alinhada sob a primeira.
                        if (!first) { sb.Append(",\n"); sb.Append(continuationIndent); }
                        if (qualifier != null) { sb.Append(qualifier); sb.Append('.'); }
                        sb.Append(col.Name);
                        first = false;
                    }
                }

                if (!anyResolved) return null;
                columns = sb.ToString();
            }

            return new TextReplacement { Start = replStart, Length = replLen, NewText = columns };
        }

        /// <summary>
        /// Localiza um <c>*</c> (ou <c>alias.*</c>) no/adjacente ao caret e, quando aplicável a um
        /// SELECT, devolve o span de substituição: <paramref name="replStart"/> (início do <c>alias</c>
        /// ou do <c>*</c>), <paramref name="starEnd"/> (offset logo após o <c>*</c>, exclusivo) e
        /// <paramref name="aliasOrNull"/> (o alias quando for <c>alias.*</c>, senão null). Puro;
        /// devolve false quando não há wildcard ou ele não está num contexto SELECT, ou está dentro
        /// de comentário/string. Compartilhado por <see cref="TryExpand"/> e pelo comando Ctrl+Espaço.
        /// </summary>
        public static bool TryFindWildcardAt(string text, int caretPosition,
            out int replStart, out int starEnd, out string aliasOrNull)
        {
            replStart = -1;
            starEnd = -1;
            aliasOrNull = null;

            if (string.IsNullOrEmpty(text)) return false;
            if (caretPosition < 0 || caretPosition > text.Length) return false;

            int starPos = FindStarAtCaret(text, caretPosition);
            if (starPos < 0) return false;

            // Must not be inside a comment or string
            if (SqlContextAnalyzer.IsInsideCommentOrStringAt(text, starPos)) return false;
            if (starPos < text.Length - 1 && SqlContextAnalyzer.IsInsideCommentOrStringAt(text, starPos + 1)) return false;

            // Check if this is alias.* form
            int aliasStart = -1;
            int aliasEnd = -1;
            if (starPos > 0 && text[starPos - 1] == '.')
            {
                int dotPos = starPos - 1;
                int i = dotPos - 1;
                while (i >= 0 && IsIdentChar(text[i])) i--;
                aliasStart = i + 1;
                aliasEnd = dotPos; // exclusive
                if (aliasEnd <= aliasStart) return false; // ".*" sem alias
            }

            // Verify this * is in a SELECT-ish context
            if (!IsSelectContext(text, starPos, aliasStart >= 0 ? aliasStart : starPos)) return false;

            replStart = aliasStart >= 0 ? aliasStart : starPos;
            starEnd = starPos + 1;
            aliasOrNull = aliasStart >= 0 ? text.Substring(aliasStart, aliasEnd - aliasStart) : null;
            return true;
        }

        /// <summary>Número de chars do início da linha de <paramref name="pos"/> até <paramref name="pos"/>
        /// (o offset de coluna, base 0), usado como largura de indentação de continuação.</summary>
        private static int ColumnOffsetOnLine(string text, int pos)
        {
            int lineStart = pos;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
                lineStart--;
            return pos - lineStart;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static int FindStarAtCaret(string text, int caret)
        {
            // Check: char at caret == '*'
            if (caret < text.Length && text[caret] == '*') return caret;
            // Check: char immediately before caret == '*'
            if (caret > 0 && text[caret - 1] == '*') return caret - 1;
            return -1;
        }

        /// <summary>
        /// Checks whether the '*' at starPos is preceded (ignoring alias.prefix) by a
        /// SELECT-ish token: SELECT, DISTINCT, a comma, an open paren, or a dot (alias.* case).
        /// </summary>
        private static bool IsSelectContext(string text, int starPos, int scanEnd)
        {
            // scanEnd is where we start looking backwards (just before alias or before *)
            int i = scanEnd - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
            if (i < 0) return false;

            char c = text[i];
            if (c == ',' || c == '(' || c == '.') return true;

            // scan back for a keyword
            if (IsIdentChar(c))
            {
                int end = i + 1;
                while (i >= 0 && IsIdentChar(text[i])) i--;
                string word = text.Substring(i + 1, end - (i + 1));
                return string.Equals(word, "SELECT", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(word, "DISTINCT", StringComparison.OrdinalIgnoreCase)
                    || (word.Length > 0 && char.IsDigit(word[0])); // TOP n
            }

            return false;
        }

        private static TableRef FindByAlias(IReadOnlyList<TableRef> tables, string alias)
        {
            foreach (TableRef t in tables)
            {
                if (string.Equals(t.Alias, alias, StringComparison.OrdinalIgnoreCase)) return t;
                if (t.Alias == null && string.Equals(t.Table, alias, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        private static IReadOnlyList<ColumnEntry> GetColumns(TableRef tref, DbMetadata metadata)
        {
            string schema = tref.Schema ?? metadata.ResolveUniqueSchema(tref.Table);
            if (schema == null) return null;

            string key = DbMetadata.TableKey(schema, tref.Table);
            if (metadata.ColumnsByTable.TryGetValue(key, out IReadOnlyList<ColumnEntry> cols))
                return cols;
            return null;
        }

        // v1: emite uma coluna por linha (trailing-comma), com as linhas de continuação
        // prefixadas por continuationIndent para alinhar sob a primeira coluna.
        private static string JoinColumns(IReadOnlyList<ColumnEntry> cols, string qualifier, string continuationIndent)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cols.Count; i++)
            {
                if (i > 0) { sb.Append(",\n"); sb.Append(continuationIndent); }
                if (qualifier != null) { sb.Append(qualifier); sb.Append('.'); }
                sb.Append(cols[i].Name);
            }
            return sb.ToString();
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
