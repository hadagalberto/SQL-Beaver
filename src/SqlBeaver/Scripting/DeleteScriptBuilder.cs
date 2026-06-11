using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Gera script DELETE FROM … WHERE … a partir de um GridData.
    /// Um DELETE por linha; PKs identificam o registro.
    /// </summary>
    public static class DeleteScriptBuilder
    {
        private const string NoPkComment =
            "-- ATENÇÃO: chave primária não resolvida; ajuste o WHERE\r\n";

        public static string Build(GridData data, string tableName, IReadOnlyList<string> pkColumns)
        {
            if (data.Rows.Count == 0)
                return string.Empty;

            bool hasPk = pkColumns != null && pkColumns.Count > 0;

            // Identify PK column indexes (case-insensitive)
            var pkIndexes = new List<int>();
            if (hasPk)
            {
                for (int c = 0; c < data.Columns.Count; c++)
                {
                    foreach (string pk in pkColumns)
                    {
                        if (string.Equals(data.Columns[c].Name, pk, StringComparison.OrdinalIgnoreCase))
                        {
                            pkIndexes.Add(c);
                            break;
                        }
                    }
                }
            }

            var sb = new StringBuilder();

            if (!hasPk)
                sb.Append(NoPkComment);

            foreach (string[] row in data.Rows)
            {
                sb.Append("DELETE FROM ");
                sb.Append(tableName);
                sb.Append(" WHERE ");

                if (!hasPk)
                {
                    sb.Append("/* defina a chave */ 1 = 0");
                }
                else
                {
                    bool first = true;
                    foreach (int pkIdx in pkIndexes)
                    {
                        if (!first) sb.Append(" AND ");
                        first = false;

                        sb.Append('[');
                        sb.Append(data.Columns[pkIdx].Name.Replace("]", "]]"));
                        sb.Append("] = ");
                        sb.Append(SqlLiteralFormatter.Format(row[pkIdx], data.Columns[pkIdx].ClrType));
                    }
                }

                sb.Append(";\r\n");
            }

            return sb.ToString();
        }
    }
}
