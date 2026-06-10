using System;

namespace SqlBeaver.Analysis
{
    /// <summary>
    /// Classifica o contexto SQL no ponto do cursor olhando apenas o texto anterior.
    /// Classe pura: sem dependências do Visual Studio, totalmente testável.
    /// </summary>
    public static class SqlContextAnalyzer
    {
        // Documentos enormes: analisar só a janela final. Comentário de bloco aberto
        // antes da janela é um falso negativo aceito (popup supérfluo, nunca crash).
        private const int MaxAnalysisLength = 64 * 1024;

        private static readonly string[] TableContextKeywords = { "FROM", "JOIN", "INTO", "UPDATE" };
        private static readonly string[] BlockedKeywords = { "EXEC", "EXECUTE", "USE", "GO", "AS", "DECLARE", "PROC", "PROCEDURE" };

        public static SqlContext Analyze(string text, int caretPosition)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (caretPosition < 0 || caretPosition > text.Length)
                throw new ArgumentOutOfRangeException(nameof(caretPosition));

            int start = caretPosition > MaxAnalysisLength ? caretPosition - MaxAnalysisLength : 0;

            if (IsInsideCommentOrString(text, start, caretPosition))
                return SqlContext.None;

            // identificador parcial imediatamente antes do cursor
            int partialStart = caretPosition;
            while (partialStart > start && IsIdentifierChar(text[partialStart - 1]))
                partialStart--;
            string partial = text.Substring(partialStart, caretPosition - partialStart);

            // variáveis (@) e temp tables (#) não são tabelas de catálogo
            if (partial.Length > 0 && (partial[0] == '@' || partial[0] == '#'))
                return SqlContext.None;

            int i = partialStart - 1;

            // caso "schema.parcial"
            if (i >= start && text[i] == '.')
            {
                string schema = ReadIdentifierBackwards(text, start, i - 1);
                return schema.Length == 0
                    ? SqlContext.None
                    : new SqlContext(SqlContextKind.AfterSchemaDot, schema, partial, partialStart);
            }

            // palavra-chave anterior (separada por whitespace)
            int beforeWhitespace = i;
            while (i >= start && char.IsWhiteSpace(text[i]))
                i--;
            bool hasWhitespaceGap = i < beforeWhitespace;

            int wordEnd = i + 1;
            while (i >= start && IsIdentifierChar(text[i]))
                i--;
            string previousWord = text.Substring(i + 1, wordEnd - (i + 1));

            if (hasWhitespaceGap && IsAny(previousWord, TableContextKeywords))
                return new SqlContext(SqlContextKind.AfterFromJoin, null, partial, partialStart);

            if (hasWhitespaceGap && IsAny(previousWord, BlockedKeywords))
                return SqlContext.None;

            if (partial.Length == 0)
                return SqlContext.None;

            // Digitação livre: silêncio enquanto o parcial ainda pode ser uma keyword
            // (digitar "sele" a caminho de SELECT não deve sugerir tabelas).
            if (SqlKeywords.IsPrefixOfAny(partial))
                return SqlContext.None;

            return new SqlContext(SqlContextKind.FreeIdentifier, null, partial, partialStart);
        }

        /// <summary>Estado de comentário/string na posição dada (início do texto como âncora).</summary>
        internal static bool IsInsideCommentOrStringAt(string text, int position)
            => IsInsideCommentOrString(text, 0, position);

        internal static bool IsInsideCommentOrString(string text, int start, int end)
        {
            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;

            int i = start;
            while (i < end)
            {
                char c = text[i];

                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    i++;
                    continue;
                }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < end && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < end && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++;
                    continue;
                }
                if (inString)
                {
                    // 'it''s': sai na primeira aspa e reentra na seguinte — efeito líquido correto
                    if (c == '\'') inString = false;
                    i++;
                    continue;
                }
                if (inBracket)
                {
                    if (c == ']') inBracket = false;
                    i++;
                    continue;
                }
                if (inQuotedIdent)
                {
                    if (c == '"') inQuotedIdent = false;
                    i++;
                    continue;
                }

                if (c == '-' && i + 1 < end && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < end && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }
                i++;
            }

            return inLineComment || blockCommentDepth > 0 || inString || inBracket || inQuotedIdent;
        }

        private static string ReadIdentifierBackwards(string text, int start, int end)
        {
            if (end < start) return string.Empty;

            // forma com colchetes: [schema].
            if (text[end] == ']')
            {
                int open = end - 1;
                while (open >= start && text[open] != '[')
                    open--;
                return open < start ? string.Empty : text.Substring(open + 1, end - open - 1);
            }

            int identStart = end + 1;
            while (identStart > start && IsIdentifierChar(text[identStart - 1]))
                identStart--;
            return text.Substring(identStart, end + 1 - identStart);
        }

        private static bool IsIdentifierChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';

        private static bool IsAny(string word, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(word, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
