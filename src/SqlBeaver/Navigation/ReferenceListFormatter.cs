using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Navigation
{
    /// <summary>Formata a lista de referências como um script SQL comentado. Puro.</summary>
    public static class ReferenceListFormatter
    {
        public sealed class ReferencedObject
        {
            public string Schema { get; }
            public string Name { get; }

            public ReferencedObject(string schema, string name)
            {
                Schema = schema;
                Name = name;
            }
        }

        public static string Format(
            string objectName,
            string server,
            string database,
            IReadOnlyList<ReferencedObject> references)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Referências a '" + objectName + "' em [" + server + "].[" + database + "]");
            sb.AppendLine("-- " + references.Count + " objeto(s):");
            foreach (ReferencedObject r in references)
            {
                sb.AppendLine("-- [" + r.Schema + "].[" + r.Name + "]");
            }
            return sb.ToString();
        }
    }
}
