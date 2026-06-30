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
            "Você é um especialista em T-SQL (Microsoft SQL Server). A partir da descrição do usuário, " +
            "produza UM script T-SQL pronto para executar. REGRAS OBRIGATÓRIAS: " +
            "(1) responda APENAS com o script SQL; " +
            "(2) NÃO escreva explicações em prosa, NÃO faça perguntas, NÃO descreva seu raciocínio; " +
            "(3) NÃO use cercas de código (nada de ```sql nem ```); " +
            "(4) se algo for ambíguo, ASSUMA a interpretação mais provável e gere o script mesmo assim — " +
            "se precisar registrar uma suposição, faça-o como comentário SQL ('-- ...') dentro do próprio script; " +
            "(5) comece a resposta diretamente pela primeira palavra-chave SQL (SELECT, WITH, INSERT, etc.); " +
            "(6) quando um schema for fornecido, use exclusivamente as tabelas e colunas existentes nele.";

        private const string RewriteSystem =
            "Você é um especialista em T-SQL (Microsoft SQL Server). Reescreva o SQL fornecido aplicando a " +
            "instrução do usuário. REGRAS OBRIGATÓRIAS: " +
            "(1) responda APENAS com o SQL final completo, pronto para executar; " +
            "(2) NÃO escreva prosa, NÃO faça perguntas, NÃO descreva seu raciocínio; " +
            "(3) NÃO use cercas de código (nada de ```sql nem ```); " +
            "(4) preserve tudo que a instrução NÃO pede para mudar (tabelas, aliases, colunas, estilo); " +
            "(5) se algo for ambíguo, ASSUMA a interpretação mais provável e gere o SQL mesmo assim; " +
            "(6) comece a resposta diretamente pela primeira palavra-chave SQL; " +
            "(7) quando um schema for fornecido, use exclusivamente as tabelas e colunas existentes nele.";

        private const string ExplainSystem =
            "Você é um especialista em T-SQL (Microsoft SQL Server). " +
            "Explique em português (PT-BR), passo a passo, o que o SQL a seguir faz. " +
            "Seja claro e objetivo.";

        private const string OptimizeSystem =
            "Você é um especialista em performance de T-SQL (Microsoft SQL Server). " +
            "Analise o SQL a seguir quanto a desempenho (índices, sargabilidade, SELECT *, " +
            "junções, funções em colunas, etc.) em português (PT-BR) e proponha uma versão melhorada. " +
            "Explique as mudanças.";

        /// <summary>
        /// Gera SQL a partir de um comentário (o líder de comentário é removido).
        /// <paramref name="existingSql"/>: SQL já existente no editor/seleção, anexado como contexto.
        /// <paramref name="rewrite"/>: quando true, a IA reescreve o <paramref name="existingSql"/>
        /// aplicando a instrução (modo "alterar a seleção"); quando false, apenas usa como contexto.
        /// </summary>
        public static AiPrompt BuildGenerateFromComment(
            string comentario, string schemaContext, string existingSql = null, bool rewrite = false)
        {
            string cleaned = CleanComment(comentario);
            return new AiPrompt
            {
                System = rewrite ? RewriteSystem : GenerateSystem,
                User = ComposeUserWithContext(cleaned, schemaContext, existingSql, rewrite),
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

        // Como ComposeUser, mas insere uma seção de "SQL existente" (contexto ou alvo de reescrita)
        // antes do schema. Usado só pela geração a partir de comentário.
        private static string ComposeUserWithContext(string body, string schemaContext, string existingSql, bool rewrite)
        {
            var sb = new StringBuilder();
            sb.Append(body);

            if (!string.IsNullOrWhiteSpace(existingSql))
            {
                sb.Append(rewrite
                    ? "\n\nSQL a reescrever (aplique a instrução acima a ESTE SQL e devolva a versão final completa, " +
                      "preservando o que não foi pedido para mudar):\n"
                    : "\n\nSQL já existente no editor (use como CONTEXTO — mantenha consistência de tabelas, aliases e " +
                      "estilo; gere algo que complemente, sem repetir o que já existe):\n");
                sb.Append(existingSql.Trim());
            }

            if (!string.IsNullOrWhiteSpace(schemaContext))
            {
                sb.Append("\n\nSchema do banco em TOON (formato tabular compacto: cada bloco 'nome[N]{campos}:' ");
                sb.Append("é seguido de N linhas com os valores separados por vírgula, na ordem dos campos; ");
                sb.Append("valores com vírgula vêm entre aspas; em columns, pk=1 indica chave primária). ");
                sb.Append("Use SOMENTE estas tabelas e colunas — não invente nomes; ");
                sb.Append("qualifique sempre a tabela com o schema (ex.: Cadastro.Pessoas):\n");
                sb.Append(schemaContext.Trim());
            }

            return sb.ToString();
        }

        private static string ComposeUser(string body, string schemaContext)
        {
            if (string.IsNullOrWhiteSpace(schemaContext))
                return body;

            var sb = new StringBuilder();
            sb.Append(body);
            sb.Append("\n\nSchema do banco em TOON (formato tabular compacto: cada bloco 'nome[N]{campos}:' ");
            sb.Append("é seguido de N linhas com os valores separados por vírgula, na ordem dos campos; ");
            sb.Append("valores com vírgula vêm entre aspas; em columns, pk=1 indica chave primária). ");
            sb.Append("Use SOMENTE estas tabelas e colunas — não invente nomes; ");
            sb.Append("qualifique sempre a tabela com o schema (ex.: Cadastro.Pessoas):\n");
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
