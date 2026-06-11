using System;
using System.Collections.Generic;
using System.IO;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Linting
{
    /// <summary>
    /// Carrega as configurações de lint uma vez por sessão a partir de
    /// %LOCALAPPDATA%\SqlBeaver\lint.json.
    /// Cria o arquivo de exemplo na primeira execução.
    /// Falhas degradam para os defaults — nunca lança.
    /// </summary>
    internal static class LintSettingsStore
    {
        private static readonly Lazy<LintSettings> _settings =
            new Lazy<LintSettings>(LoadSettings);

        public static IReadOnlyCollection<string> DisabledRuleIds
        {
            get
            {
                string[] rules = _settings.Value.DisabledRules;
                return rules ?? Array.Empty<string>();
            }
        }

        private static LintSettings LoadSettings()
        {
            try
            {
                string dir  = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SqlBeaver");
                string path = Path.Combine(dir, "lint.json");

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path, DefaultJson);
                    Log.Info("lint.json de exemplo criado em " + path);
                }

                string json         = File.ReadAllText(path);
                LintSettings settings = LintSettings.Load(json);
                Log.Info("Configurações de lint carregadas de " + path);
                return settings;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar lint.json — usando defaults de lint", ex);
                return LintSettings.CreateDefault();
            }
        }

        // Arquivo de exemplo com todos os knobs e comentário explicativo.
        private const string DefaultJson =
            "{\r\n" +
            "  \"disabledRules\": []\r\n" +
            "}\r\n";
    }
}
