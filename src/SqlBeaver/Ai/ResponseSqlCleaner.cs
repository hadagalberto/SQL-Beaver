namespace SqlBeaver.Ai
{
    /// <summary>
    /// Limpa o texto retornado pela IA: remove cercas de código (```sql / ```)
    /// e espaços de borda. Sem cercas, apenas faz trim. Puro e testável.
    /// </summary>
    public static class ResponseSqlCleaner
    {
        public static string Clean(string aiText)
        {
            if (string.IsNullOrEmpty(aiText))
                return string.Empty;

            string s = aiText.Trim();
            if (s.Length == 0)
                return string.Empty;

            if (s.StartsWith("```"))
            {
                // Remove a primeira linha de cerca (incluindo eventual tag de linguagem: ```sql, ```tsql, etc.).
                int firstNewline = s.IndexOf('\n');
                if (firstNewline >= 0)
                    s = s.Substring(firstNewline + 1);
                else
                    s = s.Substring(3); // só uma linha "```..." sem corpo

                // Remove a cerca de fechamento final.
                string trimmedEnd = s.TrimEnd();
                if (trimmedEnd.EndsWith("```"))
                {
                    int lastFence = trimmedEnd.LastIndexOf("```");
                    s = trimmedEnd.Substring(0, lastFence);
                }
            }

            return s.Trim();
        }
    }
}
