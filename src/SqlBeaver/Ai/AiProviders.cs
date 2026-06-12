using System;
using System.Collections.Generic;

namespace SqlBeaver.Ai
{
    /// <summary>Registro dos provedores de IA disponíveis.</summary>
    public static class AiProviders
    {
        public static IReadOnlyList<IAiProvider> All { get; } = new IAiProvider[]
        {
            new AnthropicProvider(),
            new OpenAiProvider(),
            new GeminiProvider(),
        };

        /// <summary>Provedor pelo Id; Anthropic se o Id for desconhecido/nulo.</summary>
        public static IAiProvider ById(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (IAiProvider provider in All)
                {
                    if (string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase))
                        return provider;
                }
            }
            return All[0]; // default Anthropic
        }
    }
}
