using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlBeaver.Environments
{
    /// <summary>
    /// Lógica pura de classificação de ambientes: carrega regras do JSON
    /// e determina qual regra casa com o servidor/banco da conexão ativa.
    /// </summary>
    public static class EnvironmentClassifier
    {
        /// <summary>
        /// Carrega as regras do JSON (DataContractJsonSerializer).
        /// JSON inválido ou nulo retorna lista vazia — nunca lança.
        /// </summary>
        public static IReadOnlyList<EnvironmentRule> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<EnvironmentRule>();

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(EnvironmentFile));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var file = serializer.ReadObject(stream) as EnvironmentFile;
                    if (file?.Environments != null)
                        return file.Environments;
                }
            }
            catch
            {
                // JSON inválido: retorna lista vazia
            }

            return Array.Empty<EnvironmentRule>();
        }

        /// <summary>
        /// Retorna a primeira regra cujos globs de servidor E banco casem (case-insensitive).
        /// Arrays null/vazio são tratados como ["*"] (casa qualquer coisa).
        /// Regras com Name null/vazio são ignoradas.
        /// Null server retorna null imediatamente.
        /// </summary>
        public static EnvironmentRule Match(IReadOnlyList<EnvironmentRule> rules, string server, string database)
        {
            if (rules == null || rules.Count == 0)
                return null;

            if (server == null)
                return null;

            foreach (EnvironmentRule rule in rules)
            {
                if (string.IsNullOrEmpty(rule?.Name))
                    continue;

                if (MatchesGlobs(server,   rule.Servers)   &&
                    MatchesGlobs(database, rule.Databases))
                    return rule;
            }

            return null;
        }

        /// <summary>
        /// Retorna true se <paramref name="value"/> casa com pelo menos um glob do array.
        /// Array null/vazio → trata como ["*"] (casa sempre).
        /// </summary>
        private static bool MatchesGlobs(string value, string[] globs)
        {
            if (globs == null || globs.Length == 0)
                return true; // sem restrição → casa qualquer valor

            foreach (string glob in globs)
            {
                if (WildcardMatcher.IsMatch(value, glob))
                    return true;
            }

            return false;
        }
    }
}
