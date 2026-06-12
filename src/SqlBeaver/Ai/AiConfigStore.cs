using System;
using System.IO;
using System.Text;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Persistência da config de IA em %LOCALAPPDATA%\SqlBeaver\ai.json (lazy, escrita atômica).
    /// O plaintext da chave nunca fica em campo — só o blob DPAPI (base64) é gravado.
    /// </summary>
    public static class AiConfigStore
    {
        private static readonly object _lock = new object();
        private static volatile AiConfig _cached;

        private static string BaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlBeaver");

        private static string ConfigPath => Path.Combine(BaseDir, "ai.json");

        /// <summary>Carrega a config (ausente/corrompida → defaults). Cacheada por sessão.</summary>
        public static AiConfig Load()
        {
            if (_cached != null)
                return _cached;

            lock (_lock)
            {
                if (_cached != null)
                    return _cached;

                _cached = LoadFromDisk();
                return _cached;
            }
        }

        private static AiConfig LoadFromDisk()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return AiConfig.CreateDefault();
                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                return AiConfig.Load(json);
            }
            catch (Exception ex)
            {
                Log.Error("AiConfigStore: falha ao carregar ai.json — usando defaults", ex);
                return AiConfig.CreateDefault();
            }
        }

        /// <summary>
        /// Salva a config. Se <paramref name="plaintextKeyOrNull"/> não for vazio, criptografa-o
        /// em KeyProtected; se for null, MANTÉM a chave protegida existente (não apaga).
        /// </summary>
        public static void Save(AiConfig cfg, string plaintextKeyOrNull)
        {
            if (cfg == null)
                throw new ArgumentNullException(nameof(cfg));

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(plaintextKeyOrNull))
                {
                    cfg.KeyProtected = AiSecretProtector.Protect(plaintextKeyOrNull);
                }
                // plaintextKey == null → mantém cfg.KeyProtected como veio (chave preservada).

                try
                {
                    Directory.CreateDirectory(BaseDir);
                    AtomicWrite(ConfigPath, cfg.Serialize());
                    _cached = cfg;
                    Log.Info("AiConfigStore: configuração de IA salva.");
                }
                catch (Exception ex)
                {
                    Log.Error("AiConfigStore: falha ao salvar ai.json", ex);
                }
            }
        }

        /// <summary>Chave de API em texto puro (descriptografada) ou null.</summary>
        public static string GetApiKey()
        {
            return AiSecretProtector.Unprotect(Load().KeyProtected);
        }

        /// <summary>True quando há provider definido e uma chave recuperável.</summary>
        public static bool IsConfigured()
        {
            AiConfig cfg = Load();
            return !string.IsNullOrEmpty(cfg.Provider) && !string.IsNullOrEmpty(GetApiKey());
        }

        /// <summary>Limpa o cache em memória (uso interno/testes).</summary>
        internal static void InvalidateCache()
        {
            _cached = null;
        }

        private static void AtomicWrite(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Encoding.UTF8);
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (FileNotFoundException)
            {
                File.Move(tmp, path);
            }
        }
    }
}
