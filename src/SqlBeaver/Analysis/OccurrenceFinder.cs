using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    public sealed class Occurrence
    {
        public int Start;
        public int Length;
    }

    /// <summary>
    /// Localiza ocorrências de um identificador SQL no texto, respeitando
    /// strings/comentários/colchetes/identificadores entre aspas.
    /// Classe pura: sem dependências do Visual Studio, totalmente testável.
    /// </summary>
    public static class OccurrenceFinder
    {
        /// <summary>
        /// Identificador sob (ou imediatamente antes de) caretPosition; null se não houver.
        /// </summary>
        public static string IdentifierAt(string text, int caretPosition)
        {
            if (text == null) return null;
            if (caretPosition < 0 || caretPosition > text.Length) return null;

            // Caret pode estar logo após o fim do identificador
            int end = caretPosition;
            // Se o char na posição do caret é um ident char, expande para frente também
            if (end < text.Length && IsIdentifierChar(text[end]))
            {
                while (end < text.Length && IsIdentifierChar(text[end]))
                    end++;
            }

            // Expande para trás a partir de end
            int start = end;
            while (start > 0 && IsIdentifierChar(text[start - 1]))
                start--;

            if (start == end) return null;

            // Verifica se o caret está dentro ou imediatamente após o token
            // caretPosition deve estar em [start, end]
            if (caretPosition < start || caretPosition > end) return null;

            // Não retornar se estiver dentro de string/comentário/bracket
            // Verificamos a posição inicial do token
            if (IsInsideSkippedRegion(text, start)) return null;

            string ident = text.Substring(start, end - start);
            return ident.Length == 0 ? null : ident;
        }

        /// <summary>
        /// Todas as ocorrências do identificador (word-boundary, OrdinalIgnoreCase,
        /// fora de strings/comentários/colchetes) em text.
        /// Vazio se name nulo/curto (&lt; 2 chars para evitar ruído).
        /// </summary>
        public static IReadOnlyList<Occurrence> FindAll(string text, string name)
        {
            var result = new List<Occurrence>();
            if (text == null || string.IsNullOrEmpty(name) || name.Length < 2)
                return result;

            int nameLen = name.Length;
            int textLen = text.Length;

            // Estado da máquina de scan para percorrer o texto
            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;

            int i = 0;
            while (i <= textLen - nameLen)
            {
                char c = text[i];

                // Avança pelas regiões a ignorar, atualizando estado
                if (inLineComment)
                {
                    if (c == '\n') inLineComment = false;
                    i++;
                    continue;
                }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < textLen && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < textLen && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++;
                    continue;
                }
                if (inString)
                {
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

                // Detecta início de regiões a ignorar
                if (c == '-' && i + 1 < textLen && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < textLen && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }

                // Tenta casar o nome aqui
                if (string.Compare(text, i, name, 0, nameLen, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // Verifica word-boundary
                    bool leftOk  = i == 0 || !IsIdentifierChar(text[i - 1]);
                    bool rightOk = (i + nameLen >= textLen) || !IsIdentifierChar(text[i + nameLen]);
                    if (leftOk && rightOk)
                    {
                        result.Add(new Occurrence { Start = i, Length = nameLen });
                    }
                }

                i++;
            }

            return result;
        }

        // Verifica se a posição está dentro de uma região ignorada (string/comentário/colchete)
        private static bool IsInsideSkippedRegion(string text, int position)
        {
            int blockCommentDepth = 0;
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;

            int i = 0;
            while (i < position && i < text.Length)
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
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++;
                    continue;
                }
                if (inString)
                {
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

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }

                i++;
            }

            return inLineComment || blockCommentDepth > 0 || inString || inBracket || inQuotedIdent;
        }

        internal static bool IsIdentifierChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
    }
}
