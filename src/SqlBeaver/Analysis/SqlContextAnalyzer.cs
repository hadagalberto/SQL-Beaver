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

        private static readonly string[] FromKeywords = { "FROM", "INTO", "UPDATE" };
        private static readonly string[] ColumnKeywords = { "SELECT", "WHERE", "ON", "AND", "OR", "HAVING", "BY", "SET",
                                                             "CASE", "WHEN", "THEN", "ELSE", "IN", "LIKE", "BETWEEN", "NOT" };
        private static readonly string[] BlockedKeywords = { "GO", "AS", "DECLARE", "PROC", "PROCEDURE" };
        private static readonly string[] ExecKeywords   = { "EXEC", "EXECUTE" };

        public static SqlContext Analyze(string text, int caretPosition)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (caretPosition < 0 || caretPosition > text.Length)
                throw new ArgumentOutOfRangeException(nameof(caretPosition));

            int start = caretPosition > MaxAnalysisLength ? caretPosition - MaxAnalysisLength : 0;

            ScanState state = Scan(text, start, caretPosition);
            if (state.InsideCommentOrString)
                return SqlContext.None;

            // identificador parcial imediatamente antes do cursor
            int partialStart = caretPosition;
            while (partialStart > start && IsIdentifierChar(text[partialStart - 1]))
                partialStart--;
            string partial = text.Substring(partialStart, caretPosition - partialStart);

            // variáveis escalares (@) e temp tables (#) em contexto não-tabela não recebem completion.
            // Esta verificação é feita PÓS-detecção de contexto para permitir que FROM #t, #t. e @t.
            // ainda produzam AfterFromJoin / AfterDot (ver comentários abaixo — mudança intencional v5 C2).
            bool partialIsLocalName = partial.Length > 0 && (partial[0] == '@' || partial[0] == '#');

            int i = partialStart - 1;

            // caso "prefix.parcial"
            if (i >= start && text[i] == '.')
            {
                string prefix = ReadIdentifierBackwards(text, start, i - 1);
                // #t. e @t. : prefix pode começar com # ou @ — produz AfterDot (intencional v5 C2)
                return prefix.Length == 0
                    ? SqlContext.None
                    : new SqlContext(SqlContextKind.AfterDot, prefix, partial, partialStart);
            }

            // Se o partial começa com @ ou # e não estamos num contexto de tabela, bloquear agora.
            // A verificação de FROM/JOIN/etc. é feita logo abaixo; aguardamos mais.
            // Para o caso em que não há palavra anterior (ex.: "SELECT @x"), bloqueamos ao final.

            // palavra-chave anterior (separada por whitespace)
            int beforeWhitespace = i;
            while (i >= start && char.IsWhiteSpace(text[i]))
                i--;
            bool hasWhitespaceGap = i < beforeWhitespace;

            // vírgula no nível 0 → ColumnContext ou AfterFromJoin
            // vírgula dentro de parênteses (paren depth > 0) → ColumnContext (função/IN-list; mudança intencional)
            if (i >= start && text[i] == ',')
            {
                if (partialIsLocalName) return SqlContext.None; // @var / #tmp após vírgula = escalar
                if (state.ParenDepth != 0)
                    return new SqlContext(SqlContextKind.ColumnContext, null, partial, partialStart);

                return IsCommaInFromList(text, start, i)
                    ? new SqlContext(SqlContextKind.AfterFromJoin, null, partial, partialStart, "FROM")
                    : new SqlContext(SqlContextKind.ColumnContext, null, partial, partialStart);
            }

            int wordEnd = i + 1;
            while (i >= start && IsIdentifierChar(text[i]))
                i--;
            string previousWord = text.Substring(i + 1, wordEnd - (i + 1));

            if (hasWhitespaceGap && IsAny(previousWord, FromKeywords))
            {
                // FROM #t, UPDATE @t, INTO #t → AfterFromJoin (intencional v5 C2)
                return new SqlContext(SqlContextKind.AfterFromJoin, null, partial, partialStart,
                    previousWord.ToUpperInvariant());
            }

            if (hasWhitespaceGap && string.Equals(previousWord, "JOIN", StringComparison.OrdinalIgnoreCase))
            {
                // JOIN #t → AfterJoin (intencional v5 C2)
                return new SqlContext(SqlContextKind.AfterJoin, null, partial, partialStart, "JOIN");
            }

            // A partir daqui, bloquear @var / #tmp (contexto de expressão/coluna)
            if (partialIsLocalName) return SqlContext.None;

            if (hasWhitespaceGap && IsAny(previousWord, ColumnKeywords))
                return new SqlContext(SqlContextKind.ColumnContext, null, partial, partialStart);

            if (hasWhitespaceGap && IsAny(previousWord, BlockedKeywords))
                return SqlContext.None;

            if (hasWhitespaceGap && IsAny(previousWord, ExecKeywords))
                return new SqlContext(SqlContextKind.AfterExec, null, partial, partialStart, previousWord.ToUpperInvariant());

            if (hasWhitespaceGap && string.Equals(previousWord, "USE", StringComparison.OrdinalIgnoreCase))
                return new SqlContext(SqlContextKind.AfterUse, null, partial, partialStart, "USE");

            // Operador de comparação/aritmético antes do cursor (sem palavra anterior):
            // = < > ! + / % → ColumnContext. Excluído: '*' (SELECT *) e '-' (ambiguidade negativo/comentário).
            if (previousWord.Length == 0 && i >= start)
            {
                char prevChar = text[i];
                if (prevChar == '=' || prevChar == '<' || prevChar == '>' ||
                    prevChar == '!' || prevChar == '+' || prevChar == '/' || prevChar == '%')
                    return new SqlContext(SqlContextKind.ColumnContext, null, partial, partialStart);
            }

            return FreeIdentifierOrNone(partial, partialStart);
        }

        private static SqlContext FreeIdentifierOrNone(string partial, int partialStart)
        {
            if (partial.Length == 0)
                return SqlContext.None;

            // Digitação livre: sempre FreeIdentifier. O completion oferece keywords T-SQL,
            // snippets e tabelas/schemas; o matcher do VS rankeia o prefixo de keyword no topo.
            return new SqlContext(SqlContextKind.FreeIdentifier, null, partial, partialStart);
        }

        /// <summary>Estado de comentário/string na posição dada (início do texto como âncora).</summary>
        internal static bool IsInsideCommentOrStringAt(string text, int position)
            => IsInsideCommentOrString(text, 0, position);

        internal static bool IsInsideCommentOrString(string text, int start, int end)
            => Scan(text, start, end).InsideCommentOrString;

        internal struct ScanState
        {
            public bool InsideCommentOrString;
            public int ParenDepth;
        }

        internal static ScanState Scan(string text, int start, int end)
        {
            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int parenDepth = 0;

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
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }
                i++;
            }

            return new ScanState
            {
                InsideCommentOrString = inLineComment || blockCommentDepth > 0 || inString || inBracket || inQuotedIdent,
                ParenDepth = parenDepth
            };
        }

        // Anda para trás a partir da vírgula consumindo apenas tokens de lista de
        // tabelas (identificadores, '.', ',', espaços e pares [..]). Se a primeira
        // keyword encontrada for FROM, a vírgula pertence à lista de tabelas.
        private static bool IsCommaInFromList(string text, int start, int commaIndex)
        {
            int i = commaIndex - 1;
            while (i >= start)
            {
                char c = text[i];

                if (char.IsWhiteSpace(c) || c == ',' || c == '.')
                {
                    i--;
                    continue;
                }

                if (c == ']')
                {
                    int open = text.LastIndexOf('[', i - 1);
                    if (open < start) return false;
                    i = open - 1;
                    continue;
                }

                if (IsIdentifierChar(c))
                {
                    int wordEnd = i;
                    while (i >= start && IsIdentifierChar(text[i]))
                        i--;
                    string word = text.Substring(i + 1, wordEnd - i);

                    if (string.Equals(word, "FROM", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (SqlKeywords.All.Contains(word))
                        return false; // outra keyword fecha a questão (SELECT, JOIN...)
                    continue; // identificador comum (tabela/alias): segue andando
                }

                return false; // quote, parêntese, operador: não é lista de FROM
            }
            return false;
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
