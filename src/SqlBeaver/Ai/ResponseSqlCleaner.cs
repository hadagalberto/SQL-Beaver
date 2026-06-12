using System;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Extrai o SQL do texto retornado pela IA, mesmo quando vem cercado por prosa.
    /// Estratégia: (1) se houver um bloco cercado ```...``` em qualquer posição, usa o
    /// conteúdo do primeiro; (2) senão, descarta as linhas de prosa antes da primeira
    /// linha que começa com palavra-chave SQL; (3) senão, apenas faz trim. Puro e testável.
    /// </summary>
    public static class ResponseSqlCleaner
    {
        private static readonly string[] SqlStarters =
        {
            "WITH", "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
            "CREATE", "ALTER", "DROP", "TRUNCATE", "DECLARE", "EXEC", "EXECUTE", "USE", "SET",
        };

        public static string Clean(string aiText)
        {
            if (string.IsNullOrEmpty(aiText))
                return string.Empty;

            string s = aiText.Replace("\r\n", "\n").Trim();
            if (s.Length == 0)
                return string.Empty;

            // (1) Primeiro bloco cercado ```...``` em qualquer posição.
            int open = s.IndexOf("```", StringComparison.Ordinal);
            if (open >= 0)
            {
                int afterOpen = s.IndexOf('\n', open);
                if (afterOpen < 0) afterOpen = open + 3; // cerca de uma linha só
                else afterOpen += 1;                     // pula a linha "```sql"/"```"
                int close = s.IndexOf("```", afterOpen, StringComparison.Ordinal);
                string inner = close >= 0 ? s.Substring(afterOpen, close - afterOpen) : s.Substring(afterOpen);
                return inner.Trim();
            }

            // (2) Sem cerca: se já começa com SQL, devolve; senão descarta a prosa inicial.
            if (StartsWithSqlKeyword(s))
                return s;

            string[] lines = s.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (StartsWithSqlKeyword(lines[i].Trim()))
                    return string.Join("\n", SubArray(lines, i)).Trim();
            }

            // (3) Nada reconhecível — devolve o texto aparado (o chamador valida com LooksLikeSql).
            return s;
        }

        /// <summary>Heurística: o texto contém pelo menos uma palavra-chave SQL de abertura?
        /// Usada pelo comando "gerar" para não inserir prosa quando a IA não devolveu SQL.</summary>
        public static bool LooksLikeSql(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (StartsWithSqlKeyword(line.Trim()))
                    return true;
            }
            return false;
        }

        private static bool StartsWithSqlKeyword(string line)
        {
            if (string.IsNullOrEmpty(line))
                return false;

            foreach (string kw in SqlStarters)
            {
                if (line.Length >= kw.Length &&
                    line.StartsWith(kw, StringComparison.OrdinalIgnoreCase))
                {
                    // Precisa ser a palavra inteira (fim da linha ou seguida de não-letra).
                    if (line.Length == kw.Length || !char.IsLetterOrDigit(line[kw.Length]))
                        return true;
                }
            }
            return false;
        }

        private static string[] SubArray(string[] src, int start)
        {
            var dst = new string[src.Length - start];
            Array.Copy(src, start, dst, 0, dst.Length);
            return dst;
        }
    }
}
