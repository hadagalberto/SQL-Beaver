namespace SqlBeaver.Editing
{
    /// <summary>
    /// Heurística PURA (sem dependências do editor VS) para decidir se uma linha é um
    /// comentário <c>--</c> com instrução real para disparar a geração de SQL ao Enter.
    /// </summary>
    public static class CommentTriggerDetector
    {
        /// <summary>
        /// True quando a linha é um comentário <c>--</c> de linha única com ao menos 8 caracteres
        /// não-brancos de instrução após o líder. Não cobre blocos <c>/* */</c> (só linhas <c>--</c>).
        /// </summary>
        public static bool IsTriggerCommentLine(string lineText)
        {
            if (string.IsNullOrEmpty(lineText)) return false;

            string trimmed = lineText.TrimStart();
            if (!trimmed.StartsWith("--")) return false;

            string body = trimmed.Substring(2);

            int nonWhite = 0;
            foreach (char c in body)
                if (!char.IsWhiteSpace(c)) nonWhite++;

            return nonWhite >= 8;
        }
    }
}
