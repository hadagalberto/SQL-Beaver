using System.Text;

namespace SqlBeaver.Ai
{
    /// <summary>Par (System, User) pronto para enviar a um <see cref="IAiProvider"/>.</summary>
    public struct AiPrompt
    {
        public string System;
        public string User;
    }

    /// <summary>
    /// Monta os prompts (System/User) das três funções de IA. Puro e sem dependências
    /// de VS. O bloco de schema é anexado apenas quando não vazio.
    /// </summary>
    public static class AiPromptBuilder
    {
        private const string GenerateSystem =
            "Você é um especialista em T-SQL (Microsoft SQL Server). " +
            "Gere SOMENTE SQL válido para SQL Server a partir da descrição do usuário. " +
            "Responda apenas com o SQL — sem explicações, sem comentários, " +
            "e sem cercas de código (não use ```sql nem ```).";

        private const string ExplainSystem =
            "Você é um especialista em T-SQL (Microsoft SQL Server). " +
            "Explique em português (PT-BR), passo a passo, o que o SQL a seguir faz. " +
            "Seja claro e objetivo.";

        private const string OptimizeSystem =
            "Você é um especialista em performance de T-SQL (Microsoft SQL Server). " +
            "Analise o SQL a seguir quanto a desempenho (índices, sargabilidade, SELECT *, " +
            "junções, funções em colunas, etc.) em português (PT-BR) e proponha uma versão melhorada. " +
            "Explique as mudanças.";

        /// <summary>Gera SQL a partir de um comentário (o líder de comentário é removido).</summary>
        public static AiPrompt BuildGenerateFromComment(string comentario, string schemaContext)
        {
            string cleaned = CleanComment(comentario);
            return new AiPrompt
            {
                System = GenerateSystem,
                User = ComposeUser(cleaned, schemaContext),
            };
        }

        public static AiPrompt BuildExplain(string sql, string schemaContext)
        {
            return new AiPrompt
            {
                System = ExplainSystem,
                User = ComposeUser(sql == null ? string.Empty : sql.Trim(), schemaContext),
            };
        }

        public static AiPrompt BuildOptimize(string sql, string schemaContext)
        {
            return new AiPrompt
            {
                System = OptimizeSystem,
                User = ComposeUser(sql == null ? string.Empty : sql.Trim(), schemaContext),
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ComposeUser(string body, string schemaContext)
        {
            if (string.IsNullOrWhiteSpace(schemaContext))
                return body;

            var sb = new StringBuilder();
            sb.Append(body);
            sb.Append("\n\nSchema disponível:\n");
            sb.Append(schemaContext.Trim());
            return sb.ToString();
        }

        /// <summary>Remove líderes de comentário T-SQL (-- de linha e /* */ de bloco) e espaços de borda.</summary>
        internal static string CleanComment(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string s = text.Trim();

            // Bloco /* ... */
            if (s.StartsWith("/*"))
            {
                s = s.Substring(2);
                int end = s.IndexOf("*/");
                if (end >= 0)
                    s = s.Substring(0, end);
            }

            // Linhas: remove o "--" inicial de cada linha
            string[] lines = s.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("--"))
                    line = line.Substring(2).Trim();
                if (i > 0) sb.Append('\n');
                sb.Append(line);
            }

            return sb.ToString().Trim();
        }
    }
}
