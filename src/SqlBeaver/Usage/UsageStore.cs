using System;
using System.IO;
using System.Threading.Tasks;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Usage
{
    /// <summary>
    /// Persiste e consulta os contadores de uso (tabelas e pares de join) em
    /// %LOCALAPPDATA%\SqlBeaver\usage.json. Carga lazy; gravação atômica
    /// (tmp + File.Replace/Move) em background. Falhas degradam para no-op.
    /// </summary>
    internal static class UsageStore
    {
        // volatile permite leitura lock-free no padrão do EnvironmentStore
        private static volatile UsageData _data;
        private static readonly object _saveLock = new object();
        private static bool _errorLogged;

        public static string FilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "usage.json");

        private static UsageData Data
        {
            get
            {
                if (_data == null)
                    _data = LoadData();
                return _data;
            }
        }

        private static string DbKey(string server, string database) => server + "|" + database;

        /// <summary>Contador de uso de uma tabela no banco ativo (0 se desconhecida/sem conexão).</summary>
        public static int GetTableCount(string server, string database, string tableKey)
        {
            if (server == null || database == null) return 0;
            return Data.GetTableCount(DbKey(server, database), tableKey);
        }

        /// <summary>Contador de uso de um par de join no banco ativo (0 se desconhecido/sem conexão).</summary>
        public static int GetJoinCount(string server, string database, string pairKey)
        {
            if (server == null || database == null) return 0;
            return Data.GetJoinCount(DbKey(server, database), pairKey);
        }

        /// <summary>
        /// Extrai as tabelas usadas do script executado e incrementa os contadores.
        /// Fire-and-forget — nunca bloqueia o Execute. No-op sem conexão/sql/metadata.
        /// </summary>
#pragma warning disable VSTHRD200 // fire-and-forget intencional; sufixo Async indica o disparo em background
        public static void RecordExecutionAsync(string server, string database, string sql, DbMetadata metadataOrNull)
#pragma warning restore VSTHRD200
        {
            if (server == null || database == null || string.IsNullOrEmpty(sql))
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    ExtractionResult extraction = UsedTablesExtractor.Extract(sql, metadataOrNull);
                    if (extraction.TableKeys.Count == 0)
                        return;

                    UsageData data = Data;
                    data.Record(DbKey(server, database), extraction.TableKeys, extraction.JoinPairKeys);
                    Save(data);
                }
                catch (Exception ex)
                {
                    if (!_errorLogged)
                    {
                        _errorLogged = true;
                        Log.Error("UsageStore.RecordExecutionAsync: falha ao gravar uso", ex);
                    }
                }
            });
        }

        private static void Save(UsageData data)
        {
            lock (_saveLock)
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = data.Serialize();
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                try
                {
                    File.Replace(tmp, path, null);
                }
                catch (FileNotFoundException)
                {
                    // Target ainda não existe — move simples é seguro
                    File.Move(tmp, path);
                }
            }
        }

        private static UsageData LoadData()
        {
            try
            {
                string path = FilePath;
                string json = File.Exists(path) ? File.ReadAllText(path) : null;
                return UsageData.Load(json);
            }
            catch (Exception ex)
            {
                Log.Error("UsageStore: falha ao carregar usage.json — ranking por uso desativado nesta sessão", ex);
                return UsageData.Load(null);
            }
        }
    }
}
