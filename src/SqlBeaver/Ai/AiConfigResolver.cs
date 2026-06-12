using System;

namespace SqlBeaver.Ai
{
    /// <summary>Normalização pura dos valores persistidos da config de IA.</summary>
    public static class AiConfigResolver
    {
        /// <summary>"none"/"all"/"scope" → enum; desconhecido → Scope.</summary>
        public static AiSchemaScope NormalizeScope(string scope)
        {
            if (!string.IsNullOrEmpty(scope))
            {
                if (string.Equals(scope, "none", StringComparison.OrdinalIgnoreCase))
                    return AiSchemaScope.None;
                if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
                    return AiSchemaScope.All;
            }
            return AiSchemaScope.Scope;
        }

        /// <summary>Id de provider válido; desconhecido/nulo → "anthropic".</summary>
        public static string NormalizeProvider(string provider)
        {
            return AiProviders.ById(provider).Id;
        }

        /// <summary>Geração ao pressionar Enter num comentário: ligada por padrão.
        /// Só "false" (case-insensitive) desliga; null/ausente/qualquer outro valor → ligada
        /// (mantém o default ON para ai.json antigos que não têm o campo).</summary>
        public static bool AutoGenerateOnEnter(AiConfig cfg)
        {
            if (cfg == null) return true;
            return !string.Equals(cfg.AutoGenerateOnEnter, "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
