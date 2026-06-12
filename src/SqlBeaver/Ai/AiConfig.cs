using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Configuração de IA persistida em %LOCALAPPDATA%\SqlBeaver\ai.json.
    /// A chave é guardada em <see cref="KeyProtected"/> (base64 do blob DPAPI) — nunca em texto puro.
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class AiConfig
    {
        [DataMember(Name = "provider")]
        public string Provider { get; set; }

        [DataMember(Name = "model")]
        public string Model { get; set; }

        /// <summary>"scope" | "none" | "all".</summary>
        [DataMember(Name = "schemaScope")]
        public string SchemaScope { get; set; }

        /// <summary>Base64 do blob DPAPI da chave de API (ou null).</summary>
        [DataMember(Name = "keyProtected")]
        public string KeyProtected { get; set; }

        public static AiConfig CreateDefault() => new AiConfig
        {
            Provider = "anthropic",
            Model = "",
            SchemaScope = "scope",
            KeyProtected = null,
        };

        /// <summary>Serializa no mesmo formato do arquivo ai.json. Não inclui o plaintext da chave.</summary>
        public string Serialize()
        {
            return
                "{\r\n" +
                $"  \"provider\": \"{Escape(Provider ?? "anthropic")}\",\r\n" +
                $"  \"model\": \"{Escape(Model ?? "")}\",\r\n" +
                $"  \"schemaScope\": \"{Escape(SchemaScope ?? "scope")}\",\r\n" +
                $"  \"keyProtected\": {(KeyProtected == null ? "null" : "\"" + Escape(KeyProtected) + "\"")}\r\n" +
                "}\r\n";
        }

        /// <summary>
        /// Desserializa o JSON com defaults garantidos para membros ausentes.
        /// JSON nulo/vazio/inválido retorna defaults — nunca lança.
        /// </summary>
        public static AiConfig Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return CreateDefault();

            try
            {
                AiConfig cfg = CreateDefault();
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                    bytes, XmlDictionaryReaderQuotas.Max))
                {
                    reader.Read(); // <root type="object">

                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                            continue;

                        string key = reader.Name;

                        // keyProtected pode ser null no JSON: Element seguido de EndElement.
                        if (!reader.Read())
                            break;

                        if (reader.NodeType != XmlNodeType.Text)
                        {
                            // valor null/vazio: keyProtected fica null
                            if (key == "keyProtected")
                                cfg.KeyProtected = null;
                            continue;
                        }

                        string value = reader.Value;
                        switch (key)
                        {
                            case "provider": cfg.Provider = value; break;
                            case "model": cfg.Model = value; break;
                            case "schemaScope": cfg.SchemaScope = value; break;
                            case "keyProtected": cfg.KeyProtected = value; break;
                        }
                    }
                }

                return cfg;
            }
            catch
            {
                return CreateDefault();
            }
        }

        private static string Escape(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
