using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Serialização JSON via <see cref="DataContractJsonSerializer"/> — escapa o SQL
    /// corretamente nas requisições e ignora membros desconhecidos nas respostas.
    /// </summary>
    internal static class AiJson
    {
        internal static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        internal static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
            {
                return (T)serializer.ReadObject(ms);
            }
        }
    }
}
