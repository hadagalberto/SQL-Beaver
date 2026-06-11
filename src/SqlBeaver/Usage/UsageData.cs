using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlBeaver.Usage
{
    [DataContract(Namespace = "")]
    public sealed class UsageCount
    {
        [DataMember(Name = "db")]
        public string Db { get; set; }

        [DataMember(Name = "key")]
        public string Key { get; set; }

        [DataMember(Name = "count")]
        public int Count { get; set; }
    }

    [DataContract(Namespace = "")]
    public sealed class UsageFile
    {
        [DataMember(Name = "tables")]
        public UsageCount[] Tables { get; set; }

        [DataMember(Name = "joins")]
        public UsageCount[] Joins { get; set; }
    }

    /// <summary>
    /// Estado em memória de contadores de uso (tabelas e pares de join). Thread-safe via lock interno.
    /// </summary>
    public sealed class UsageData
    {
        private const int Cap = 100_000;

        private readonly object _lock = new object();

        // dbKey+"|"+tableKey → count
        private readonly Dictionary<string, int> _tables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // dbKey+"|"+pairKey → count
        private readonly Dictionary<string, int> _joins = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private UsageData() { }

        private static string CompositeKey(string dbKey, string entityKey) => dbKey + "|" + entityKey;

        /// <summary>
        /// Deserializa a partir do JSON. JSON nulo ou inválido retorna instância vazia.
        /// </summary>
        public static UsageData Load(string json)
        {
            var data = new UsageData();
            if (string.IsNullOrWhiteSpace(json))
                return data;

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using (var ms = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(UsageFile));
                    var file = (UsageFile)serializer.ReadObject(ms);
                    if (file == null)
                        return data;

                    if (file.Tables != null)
                    {
                        foreach (var entry in file.Tables)
                        {
                            if (entry?.Db == null || entry.Key == null) continue;
                            data._tables[CompositeKey(entry.Db, entry.Key)] = entry.Count;
                        }
                    }

                    if (file.Joins != null)
                    {
                        foreach (var entry in file.Joins)
                        {
                            if (entry?.Db == null || entry.Key == null) continue;
                            data._joins[CompositeKey(entry.Db, entry.Key)] = entry.Count;
                        }
                    }
                }
            }
            catch
            {
                return new UsageData();
            }

            return data;
        }

        /// <summary>Serializa o estado atual para JSON.</summary>
        public string Serialize()
        {
            UsageFile file;
            lock (_lock)
            {
                file = BuildFile();
            }

            var serializer = new DataContractJsonSerializer(typeof(UsageFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, file);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private UsageFile BuildFile()
        {
            var tables = new List<UsageCount>(_tables.Count);
            foreach (var kv in _tables)
            {
                int sep = kv.Key.IndexOf('|');
                if (sep < 0) continue;
                tables.Add(new UsageCount
                {
                    Db = kv.Key.Substring(0, sep),
                    Key = kv.Key.Substring(sep + 1),
                    Count = kv.Value
                });
            }

            var joins = new List<UsageCount>(_joins.Count);
            foreach (var kv in _joins)
            {
                int sep = kv.Key.IndexOf('|');
                if (sep < 0) continue;
                joins.Add(new UsageCount
                {
                    Db = kv.Key.Substring(0, sep),
                    Key = kv.Key.Substring(sep + 1),
                    Count = kv.Value
                });
            }

            return new UsageFile
            {
                Tables = tables.ToArray(),
                Joins = joins.ToArray()
            };
        }

        /// <summary>
        /// Incrementa em 1 cada tabela e cada par de join. Respeita cap de 100 000 entradas
        /// no total por dicionário (novas entradas além do cap são ignoradas).
        /// </summary>
        public void Record(string dbKey, IReadOnlyList<string> tableKeys, IReadOnlyList<string> joinPairKeys)
        {
            if (dbKey == null) return;

            lock (_lock)
            {
                if (tableKeys != null)
                {
                    foreach (string tk in tableKeys)
                    {
                        if (tk == null) continue;
                        string ck = CompositeKey(dbKey, tk);
                        if (_tables.TryGetValue(ck, out int existing))
                        {
                            _tables[ck] = existing + 1;
                        }
                        else if (_tables.Count < Cap)
                        {
                            _tables[ck] = 1;
                        }
                    }
                }

                if (joinPairKeys != null)
                {
                    foreach (string pk in joinPairKeys)
                    {
                        if (pk == null) continue;
                        string ck = CompositeKey(dbKey, pk);
                        if (_joins.TryGetValue(ck, out int existing))
                        {
                            _joins[ck] = existing + 1;
                        }
                        else if (_joins.Count < Cap)
                        {
                            _joins[ck] = 1;
                        }
                    }
                }
            }
        }

        /// <summary>Retorna o contador de uso de uma tabela (0 se desconhecida).</summary>
        public int GetTableCount(string dbKey, string tableKey)
        {
            if (dbKey == null || tableKey == null) return 0;
            lock (_lock)
            {
                return _tables.TryGetValue(CompositeKey(dbKey, tableKey), out int c) ? c : 0;
            }
        }

        /// <summary>Retorna o contador de uso de um par de join (0 se desconhecido).</summary>
        public int GetJoinCount(string dbKey, string pairKey)
        {
            if (dbKey == null || pairKey == null) return 0;
            lock (_lock)
            {
                return _joins.TryGetValue(CompositeKey(dbKey, pairKey), out int c) ? c : 0;
            }
        }
    }
}
