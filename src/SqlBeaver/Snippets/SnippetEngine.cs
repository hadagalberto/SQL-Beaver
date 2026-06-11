using System.Collections.Generic;
using SqlBeaver.Analysis;

namespace SqlBeaver.Snippets
{
    public sealed class SnippetExpansion
    {
        /// <summary>Início do shortcut no texto analisado.</summary>
        public int WordStart { get; set; }
        public int WordLength { get; set; }
        /// <summary>Expansão sem o marcador $cursor$.</summary>
        public string ReplacementText { get; set; }
        /// <summary>Offset do caret dentro de ReplacementText.</summary>
        public int CaretOffset { get; set; }
    }

    /// <summary>Decide se a palavra antes do caret é um shortcut e calcula a expansão. Puro.</summary>
    public static class SnippetEngine
    {
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
            int marker = raw.IndexOf(CursorMarker, System.StringComparison.Ordinal);
            string replacement = marker < 0 ? raw : raw.Remove(marker, CursorMarker.Length);

            expansion = new SnippetExpansion
            {
                WordStart = wordStart,
                WordLength = wordLength,
                ReplacementText = replacement,
                CaretOffset = marker < 0 ? replacement.Length : marker,
            };
            return true;
        }

        private static bool IsWordChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
    }
}
