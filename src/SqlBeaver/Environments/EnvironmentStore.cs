using System;
using System.Collections.Generic;
using System.IO;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Environments
{
    /// <summary>
    /// Carrega e persiste as regras de ambiente a partir de
    /// %LOCALAPPDATA%\SqlBeaver\environments.json.
    /// Cria o arquivo de exemplo na primeira execução.
    /// Falhas degradam para lista vazia.
    /// Suporta recarga ao vivo via Save().
    /// </summary>
    internal static class EnvironmentStore
    {
        // volatile permite leitura/escrita lock-free em cenário de um único writer
        private static volatile IReadOnlyList<EnvironmentRule> _rules;
        private static readonly object _saveLock = new object();

        public static string FilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "environments.json");

        public static IReadOnlyList<EnvironmentRule> Rules
        {
            get
            {
                if (_rules == null)
                    _rules = LoadRules();
                return _rules;
            }
        }

        /// <summary>Convenience: delega ao classificador com as regras carregadas.</summary>
        public static EnvironmentRule MatchActive(string server, string database)
            => EnvironmentClassifier.Match(Rules, server, database);

        /// <summary>
        /// Serializa as regras, grava o arquivo atomicamente (tmp + File.Replace/Move)
        /// e actualiza a referência em memória.
        /// </summary>
        public static void Save(IReadOnlyList<EnvironmentRule> rules)
        {
            lock (_saveLock)
            {
                try
                {
                    string path = FilePath;
                    string dir  = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    string json = EnvironmentClassifier.Serialize(rules);
                    string tmp  = path + ".tmp";
                    File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch (FileNotFoundException)
                    {
                        // Target does not exist yet — plain move is safe
                        File.Move(tmp, path);
                    }

                    // Update in-memory reference
                    _rules = rules;
                    Log.Info($"Ambientes salvos: {rules.Count} regra(s).");
                }
                catch (Exception ex)
                {
                    Log.Error("Falha ao salvar environments.json", ex);
                    throw;
                }
            }
        }

        private static IReadOnlyList<EnvironmentRule> LoadRules()
        {
            try
            {
                string path = FilePath;
                string dir  = Path.GetDirectoryName(path);

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
