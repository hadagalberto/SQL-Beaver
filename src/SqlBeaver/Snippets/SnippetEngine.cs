using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;

namespace SqlBeaver.Snippets
{
    /// <summary>Descreve um placeholder $N$ (ou ${N:default}$) dentro do ReplacementText já limpo.</summary>
    public sealed class PlaceholderSpan
    {
        /// <summary>Offset dentro de ReplacementText (após a limpeza dos marcadores).</summary>
        public int Offset { get; set; }
        /// <summary>Comprimento do texto padrão (0 para marcadores vazios como $1$).</summary>
        public int Length { get; set; }
        /// <summary>Número do placeholder: 1-9 são editáveis em ordem, 0 é a posição final do caret.</summary>
        public int Order { get; set; }
    }

    public sealed class SnippetExpansion
    {
        /// <summary>Início do shortcut no texto analisado.</summary>
        public int WordStart { get; set; }
        public int WordLength { get; set; }
        /// <summary>Expansão sem os marcadores de placeholder.</summary>
        public string ReplacementText { get; set; }
        /// <summary>Offset do caret dentro de ReplacementText (= offset do placeholder 1, ou 0, ou fim).</summary>
        public int CaretOffset { get; set; }
        /// <summary>Placeholders em ordem de visita (1,2,...,0). Pode ser vazio.</summary>
        public IReadOnlyList<PlaceholderSpan> Placeholders { get; set; } = Array.Empty<PlaceholderSpan>();
    }

    /// <summary>Decide se a palavra antes do caret é um shortcut e calcula a expansão. Puro.</summary>
    public static class SnippetEngine
    {
        // $cursor$ é sinônimo de $0$
        private const string CursorMarker = "$cursor$";

        public static bool TryExpand(
            string textBeforeCaret,
            IReadOnlyDictionary<string, SnippetDefinition> snippets,
            out SnippetExpansion expansion)
        {
            expansion = null;
            if (string.IsNullOrEmpty(textBeforeCaret))
                return false;

            int wordStart = textBeforeCaret.Length;
            while (wordStart > 0 && IsWordChar(textBeforeCaret[wordStart - 1]))
                wordStart--;
            int wordLength = textBeforeCaret.Length - wordStart;
            if (wordLength == 0)
                return false;

            string word = textBeforeCaret.Substring(wordStart, wordLength);
            if (word[0] == '@' || word[0] == '#')
                return false;
            if (wordStart > 0)
            {
                char before = textBeforeCaret[wordStart - 1];
                if (before == '.' || before == '[' || before == '"')
                    return false;
            }
            if (!snippets.TryGetValue(word, out SnippetDefinition snippet))
                return false;
            if (SqlContextAnalyzer.IsInsideCommentOrStringAt(textBeforeCaret, wordStart))
                return false;

            string raw = snippet.Expansion ?? string.Empty;
            // Normalise $cursor$ → $0$
            raw = raw.Replace(CursorMarker, "$0$");

            string replacementText;
            IReadOnlyList<PlaceholderSpan> placeholders;
            int caretOffset;
            ParseExpansion(raw, out replacementText, out placeholders, out caretOffset);

            expansion = new SnippetExpansion
            {
                WordStart = wordStart,
                WordLength = wordLength,
                ReplacementText = replacementText,
                CaretOffset = caretOffset,
                Placeholders = placeholders,
            };
            return true;
        }

        /// <summary>
        /// Analisa <paramref name="raw"/> que pode conter:
        ///   $N$         — placeholder vazio de ordem N (N = 0..9)
        ///   ${N:texto}$ — placeholder com texto padrão
        /// Retorna o texto limpo (marcadores removidos, defaults inseridos) e a lista de spans.
        /// </summary>
        internal static void ParseExpansion(
            string raw,
            out string replacementText,
            out IReadOnlyList<PlaceholderSpan> placeholders,
            out int caretOffset)
        {
            var sb = new StringBuilder(raw.Length);
            var spans = new List<PlaceholderSpan>();
            int i = 0;

            while (i < raw.Length)
            {
                if (raw[i] == '$')
                {
                    // Tenta ${N:texto}$
                    if (i + 1 < raw.Length && raw[i + 1] == '{')
                    {
                        int close = raw.IndexOf("}$", i + 2, StringComparison.Ordinal);
                        if (close > i + 2)
                        {
                            int colon = raw.IndexOf(':', i + 2);
                            if (colon > i + 2 && colon < close)
                            {
                                string orderStr = raw.Substring(i + 2, colon - (i + 2));
                                string defaultText = raw.Substring(colon + 1, close - colon - 1);
                                if (int.TryParse(orderStr, out int order) && order >= 0 && order <= 9)
                                {
                                    int offset = sb.Length;
                                    sb.Append(defaultText);
                                    spans.Add(new PlaceholderSpan { Offset = offset, Length = defaultText.Length, Order = order });
                                    i = close + 2; // pula "}$"
                                    continue;
                                }
                            }
                        }
                    }

                    // Tenta $N$ (N = dígito único)
                    if (i + 2 < raw.Length && char.IsDigit(raw[i + 1]) && raw[i + 2] == '$')
                    {
                        int order = raw[i + 1] - '0';
                        spans.Add(new PlaceholderSpan { Offset = sb.Length, Length = 0, Order = order });
                        i += 3; // pula "$N$"
                        continue;
                    }

                    // $ literal
                    sb.Append(raw[i]);
                    i++;
                }
                else
                {
                    sb.Append(raw[i]);
                    i++;
                }
            }

            replacementText = sb.ToString();
            placeholders = spans;

            // CaretOffset: offset do placeholder ordem 1 (menor ordem ≠ 0), senão ordem 0, senão fim
            caretOffset = replacementText.Length;
            int? lowestNonZero = null;
            int? zeroOffset = null;
            foreach (var span in spans)
            {
                if (span.Order == 0)
                {
                    if (zeroOffset == null) zeroOffset = span.Offset;
                }
                else
                {
                    if (lowestNonZero == null || span.Order < lowestNonZero)
                        lowestNonZero = span.Order;
                }
            }
            if (lowestNonZero.HasValue)
            {
                // encontra o offset do primeiro span com essa ordem
                foreach (var span in spans)
                    if (span.Order == lowestNonZero.Value) { caretOffset = span.Offset; break; }
            }
            else if (zeroOffset.HasValue)
            {
                caretOffset = zeroOffset.Value;
            }
        }

        private static bool IsWordChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
    }
}
