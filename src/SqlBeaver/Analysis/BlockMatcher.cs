using System;

namespace SqlBeaver.Analysis
{
    public sealed class BlockMatch
    {
        public int OpenStart;
        public int OpenLength;
        public int CloseStart;
        public int CloseLength;
    }

    /// <summary>
    /// Encontra o par de keywords BEGIN/END correspondente ao caret.
    /// Classe pura: sem dependências do Visual Studio, totalmente testável.
    /// </summary>
    public static class BlockMatcher
    {
        /// <summary>
        /// Se o caret está sobre uma keyword de bloco (BEGIN ou END),
        /// devolve o par correspondente; null caso contrário.
        /// Balanceamento por contagem, fora de strings/comentários.
        /// </summary>
        public static BlockMatch Match(string text, int caretPosition)
        {
            if (text == null) return null;
            if (caretPosition < 0 || caretPosition > text.Length) return null;

            // Localiza o token sob o caret
            TokenInfo token = TokenAt(text, caretPosition);
            if (token == null) return null;

            string upper = token.Text.ToUpperInvariant();
            if (upper == "BEGIN")
                return MatchBeginForward(text, token);
            if (upper == "END")
                return MatchEndBackward(text, token);

            return null;
        }

        // Dado um token BEGIN em 'open', encontra o END que fecha o mesmo nível
        private static BlockMatch MatchBeginForward(string text, TokenInfo open)
        {
            int depth = 1;
            int i = open.Start + open.Length;
            int textLen = text.Length;

            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;

            while (i < textLen)
            {
                char c = text[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < textLen && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < textLen && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)     { if (c == '\'') inString = false;     i++; continue; }
                if (inBracket)    { if (c == ']')  inBracket = false;    i++; continue; }
                if (inQuotedIdent){ if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < textLen && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < textLen && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }

                // Tenta casar BEGIN ou END aqui (word-boundary)
                if (IsWordStart(text, i))
                {
                    int wordLen = ReadWordLength(text, i);
                    if (wordLen > 0)
                    {
                        string word = text.Substring(i, wordLen).ToUpperInvariant();
                        if (word == "BEGIN")
                        {
                            depth++;
                            i += wordLen;
                            continue;
                        }
                        if (word == "END")
                        {
                            depth--;
                            if (depth == 0)
                            {
                                return new BlockMatch
                                {
                                    OpenStart  = open.Start,
                                    OpenLength = open.Length,
                                    CloseStart  = i,
                                    CloseLength = wordLen
                                };
                            }
                            i += wordLen;
                            continue;
                        }
                    }
                }

                i++;
            }

            return null; // BEGIN sem END correspondente
        }

        // Dado um token END em 'close', encontra o BEGIN que o abre
        private static BlockMatch MatchEndBackward(string text, TokenInfo close)
        {
            // Percorre do início até justo antes do END, coletando todos os tokens BEGIN/END
            // e encontrando qual BEGIN corresponde ao END dado.
            // Estratégia: percorre do início, conta profundidade; quando chega no END,
            // a profundidade antes dele indica qual BEGIN é o par.
            // Collect only tokens BEFORE this END (up to close.Start, exclusive)
            var tokens = CollectBeginEndTokens(text, close.Start);

            // Simula o balanceamento para saber qual BEGIN abre este END
            int depth = 0;
            for (int k = tokens.Count - 1; k >= 0; k--)
            {
                var t = tokens[k];
                string upper = t.Text.ToUpperInvariant();
                if (upper == "END")
                {
                    depth++;
                }
                else if (upper == "BEGIN")
                {
                    if (depth == 0)
                    {
                        // Este BEGIN fecha o nosso END
                        return new BlockMatch
                        {
                            OpenStart   = t.Start,
                            OpenLength  = t.Length,
                            CloseStart  = close.Start,
                            CloseLength = close.Length
                        };
                    }
                    depth--;
                }
            }

            return null; // END sem BEGIN correspondente
        }

        // Coleta todos os tokens BEGIN/END até 'limit' (exclusive), fora de strings/comentários
        private static System.Collections.Generic.List<TokenInfo> CollectBeginEndTokens(string text, int limit)
        {
            var tokens = new System.Collections.Generic.List<TokenInfo>();
            int textLen = Math.Min(text.Length, limit);

            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int i = 0;

            while (i < textLen)
            {
                char c = text[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < textLen && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < textLen && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)     { if (c == '\'') inString = false;     i++; continue; }
                if (inBracket)    { if (c == ']')  inBracket = false;    i++; continue; }
                if (inQuotedIdent){ if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < textLen && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < textLen && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }

                if (IsWordStart(text, i))
                {
                    int wordLen = ReadWordLength(text, i);
                    if (wordLen > 0)
                    {
                        string word = text.Substring(i, wordLen).ToUpperInvariant();
                        if (word == "BEGIN" || word == "END")
                        {
                            tokens.Add(new TokenInfo { Start = i, Length = wordLen, Text = word });
                            i += wordLen;
                            continue;
                        }
                    }
                }

                i++;
            }

            return tokens;
        }

        // Encontra o token (BEGIN ou END) que contém ou está imediatamente sob caretPosition
        private static TokenInfo TokenAt(string text, int caretPosition)
        {
            // Expande o caret para encontrar os limites do identificador
            int end = caretPosition;
            if (end < text.Length && IsWordChar(text[end]))
            {
                while (end < text.Length && IsWordChar(text[end]))
                    end++;
            }
            int start = end;
            while (start > 0 && IsWordChar(text[start - 1]))
                start--;

            if (start == end) return null;
            if (caretPosition < start || caretPosition > end) return null;

            // Verifica word-boundary
            bool leftOk  = start == 0 || !IsWordChar(text[start - 1]);
            bool rightOk = end >= text.Length || !IsWordChar(text[end]);
            if (!leftOk || !rightOk) return null;

            // Verifica que não está em string/comentário
            if (IsInsideSkippedRegion(text, start)) return null;

            string word = text.Substring(start, end - start);
            string upper = word.ToUpperInvariant();
            if (upper != "BEGIN" && upper != "END") return null;

            return new TokenInfo { Start = start, Length = end - start, Text = upper };
        }

        private static bool IsWordStart(string text, int i)
        {
            return (i == 0 || !IsWordChar(text[i - 1])) && IsWordChar(text[i]);
        }

        private static int ReadWordLength(string text, int start)
        {
            int i = start;
            while (i < text.Length && IsWordChar(text[i]))
                i++;
            int len = i - start;
            // Verifica word-boundary no final
            if (i < text.Length && IsWordChar(text[i])) return 0;
            return len;
        }

        private static bool IsWordChar(char c)
            => char.IsLetterOrDigit(c) || c == '_';

        private static bool IsInsideSkippedRegion(string text, int position)
        {
            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;

            int i = 0;
            while (i < position && i < text.Length)
            {
                char c = text[i];

                if (inLineComment)  { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString)     { if (c == '\'') inString = false;     i++; continue; }
                if (inBracket)    { if (c == ']')  inBracket = false;    i++; continue; }
                if (inQuotedIdent){ if (c == '"')  inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[')  { inBracket = true; i++; continue; }
                if (c == '"')  { inQuotedIdent = true; i++; continue; }

                i++;
            }

            return inLineComment || blockCommentDepth > 0 || inString || inBracket || inQuotedIdent;
        }

        private sealed class TokenInfo
        {
            public int Start;
            public int Length;
            public string Text;
        }
    }
}
