using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Gera script SELECT a partir das colunas de um GridData.
    /// Nenhuma linha é necessária — apenas os cabeçalhos.
    /// </summary>
    public static class SelectScriptBuilder
    {
        public static string Build(GridData data, string tableName)
        {
            var sb = new StringBuilder();
            sb.Append("SELECT ");

            for (int i = 0; i < data.Columns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('[');
                sb.Append(data.Columns[i].Name.Replace("]", "]]"));
                sb.Append(']');
            }

            sb.Append("\r\nFROM ");
            sb.Append(tableName);
            sb.Append(';');

            return sb.ToString();
        }
    }
}
