using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlBeaver.Snippets
{
    /// <summary>
    /// Serializa a lista de snippets de USUÁRIO para o mesmo formato que o
    /// SnippetStore lê: { "snippets": [ { shortcut, title, expansion, description } ] }.
    /// Apenas snippets de usuário são gravados — os built-ins não entram no arquivo.
    /// </summary>
    public static class SnippetJsonWriter
    {
        public static string Serialize(IReadOnlyList<SnippetDefinition> snippets)
        {
            var file = new SnippetFile
            {
                Snippets = snippets == null ? new SnippetDefinition[0] : ToArray(snippets)
            };

            var serializer = new DataContractJsonSerializer(typeof(SnippetFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, file);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static SnippetDefinition[] ToArray(IReadOnlyList<SnippetDefinition> snippets)
        {
            var arr = new SnippetDefinition[snippets.Count];
            for (int i = 0; i < snippets.Count; i++)
                arr[i] = snippets[i];
            return arr;
        }
    }
}
