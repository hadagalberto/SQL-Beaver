using System;
using System.Collections.Generic;
using System.IO;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Environments
{
    /// <summary>
    /// Carrega as regras de ambiente uma vez por sessão a partir de
    /// %LOCALAPPDATA%\SqlBeaver\environments.json.
    /// Cria o arquivo de exemplo na primeira execução.
    /// Falhas degradam para lista vazia.
    /// </summary>
    internal static class EnvironmentStore
    {
        private static readonly Lazy<IReadOnlyList<EnvironmentRule>> _rules =
            new Lazy<IReadOnlyList<EnvironmentRule>>(LoadRules);

        public static IReadOnlyList<EnvironmentRule> Rules => _rules.Value;

        /// <summary>Convenience: delega ao classificador com as regras carregadas.</summary>
        public static EnvironmentRule MatchActive(string server, string database)
            => EnvironmentClassifier.Match(Rules, server, database);

        private static IReadOnlyList<EnvironmentRule> LoadRules()
        {
            try
            {
                string dir  = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlBeaver");
                string path = Path.Combine(dir, "environments.json");

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path, DefaultJson);
                    Log.Info("environments.json de exemplo criado em " + path);
                }

                string json  = File.ReadAllText(path);
                IReadOnlyList<EnvironmentRule> rules = EnvironmentClassifier.Load(json);
                Log.Info($"Ambientes carregados: {rules.Count} regra(s).");
                return rules;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar environments.json — sem classificação de ambientes", ex);
                return Array.Empty<EnvironmentRule>();
            }
        }

        private const string DefaultJson =
            "{\r\n" +
            "  \"environments\": [\r\n" +
            "    { \"name\": \"Produção\",    \"color\": \"#C42B1C\", \"servers\": [ \"*prd*\", \"*prod*\" ],                                              \"databases\": [ \"*\" ], \"confirmExecute\": true  },\r\n" +
            "    { \"name\": \"Homologação\", \"color\": \"#9D5D00\", \"servers\": [ \"*hml*\", \"*homolog*\", \"*tst*\", \"*teste*\" ],                   \"databases\": [ \"*\" ], \"confirmExecute\": false },\r\n" +
            "    { \"name\": \"Desenvolvimento\", \"color\": \"#0E700E\", \"servers\": [ \"*dev*\", \"localhost\", \"127.0.0.1\", \"*sqlexpress*\" ], \"databases\": [ \"*\" ], \"confirmExecute\": false }\r\n" +
            "  ]\r\n" +
            "}\r\n";
    }
}
