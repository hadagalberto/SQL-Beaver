using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace SqlBeaver.Session
{
    /// <summary>Entrada de uma aba/snapshot no índice de sessão.</summary>
    [DataContract(Namespace = "")]
    public sealed class SessionEntry
    {
        [DataMember(Name = "file")]
        public string File { get; set; }

        [DataMember(Name = "caption")]
        public string Caption { get; set; }

        [DataMember(Name = "server")]
        public string Server { get; set; }

        [DataMember(Name = "database")]
        public string Database { get; set; }

        /// <summary>ISO 8601 ("o" format).</summary>
        [DataMember(Name = "savedAt")]
        public string SavedAt { get; set; }

        [DataMember(Name = "contentHash")]
        public string ContentHash { get; set; }

        /// <summary>Caminho REAL do arquivo salvo no disco (null para rascunho/untitled).
        /// Quando presente e existente, a restauração reabre o ARQUIVO (mantendo a
        /// referência de arquivo salvo), não o conteúdo como query nova.</summary>
        [DataMember(Name = "originalPath", EmitDefaultValue = false)]
        public string OriginalPath { get; set; }
    }

    /// <summary>Contrato de serialização para index.json.</summary>
    [DataContract(Namespace = "")]
    public sealed class SessionIndexFile
    {
        [DataMember(Name = "entries")]
        public SessionEntry[] Entries { get; set; }
    }

    /// <summary>
    /// Lógica pura para carregar, serializar e fazer upsert de entradas no índice de sessão.
    /// </summary>
    public static class SessionIndex
    {
        private const int MaxEntries = 50;

        /// <summary>Desserializa o JSON do índice. JSON inválido/nulo → lista vazia.</summary>
        public static IReadOnlyList<SessionEntry> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<SessionEntry>();

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                    bytes, XmlDictionaryReaderQuotas.Max))
                {
                    var serializer = new DataContractJsonSerializer(typeof(SessionIndexFile));
                    var file = (SessionIndexFile)serializer.ReadObject(reader);
                    if (file?.Entries == null)
                        return Array.Empty<SessionEntry>();
                    return file.Entries;
                }
            }
            catch
            {
                return Array.Empty<SessionEntry>();
            }
        }

        /// <summary>Serializa as entradas para JSON estável.</summary>
        public static string Serialize(IReadOnlyList<SessionEntry> entries)
        {
            var file = new SessionIndexFile
            {
                Entries = entries?.ToArray() ?? Array.Empty<SessionEntry>()
            };

            var serializer = new DataContractJsonSerializer(typeof(SessionIndexFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, file);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>
        /// Upsert por caption: substitui a entrada quando o hash mudou (novo arquivo),
        /// mantém quando igual; corta para as 50 mais recentes (SavedAt desc).
        /// </summary>
        public static IReadOnlyList<SessionEntry> Upsert(IReadOnlyList<SessionEntry> entries, SessionEntry entry)
        {
            var list = entries == null
                ? new List<SessionEntry>()
                : new List<SessionEntry>(entries);

            int existingIndex = list.FindIndex(e =>
                string.Equals(e.Caption, entry.Caption, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                if (list[existingIndex].ContentHash == entry.ContentHash)
                {
                    // Same hash → keep original (no duplicate, no update)
                    return list;
                }
                // Different hash → replace
                list[existingIndex] = entry;
            }
            else
            {
                // New caption → append
                list.Add(entry);
            }

            // Sort descending by SavedAt, cap at MaxEntries
            list.Sort((a, b) => string.Compare(b.SavedAt ?? "", a.SavedAt ?? "", StringComparison.Ordinal));

            if (list.Count > MaxEntries)
                list = list.Take(MaxEntries).ToList();

            return list;
        }
    }
}
