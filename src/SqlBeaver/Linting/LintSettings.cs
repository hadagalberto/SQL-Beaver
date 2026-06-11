using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace SqlBeaver.Linting
{
    /// <summary>
    /// Configuração de lint persistida em %LOCALAPPDATA%\SqlBeaver\lint.json.
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class LintSettings
    {
        [DataMember(Name = "disabledRules")]
        public string[] DisabledRules { get; set; }

        public static LintSettings CreateDefault() => new LintSettings
        {
            DisabledRules = new string[0],
        };

        /// <summary>
        /// Desserializa o JSON. JSON nulo/vazio/inválido retorna defaults — nunca lança.
        /// </summary>
        public static LintSettings Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return CreateDefault();

            try
            {
                LintSettings settings = CreateDefault();
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                    bytes, XmlDictionaryReaderQuotas.Max))
                {
                    reader.Read(); // <root>

                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element) continue;

                        if (reader.Name == "disabledRules")
                        {
                            // Array element — collect item children
                            var items = new List<string>();
                            // The array is encoded as:
                            //   <disabledRules type="array">
                            //     <item type="string">rule-id</item>
                            //     ...
                            //   </disabledRules>
                            using (XmlReader sub = reader.ReadSubtree())
                            {
                                sub.Read(); // <disabledRules>
                                while (sub.Read())
                                {
                                    if (sub.NodeType == XmlNodeType.Element && sub.Name == "item")
                                    {
                                        if (sub.Read() && sub.NodeType == XmlNodeType.Text)
                                            items.Add(sub.Value);
                                    }
                                }
                            }
                            settings.DisabledRules = items.ToArray();
                        }
                    }
                }

                return settings;
            }
            catch
            {
                return CreateDefault();
            }
        }
    }
}
