using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Gera script UPDATE … SET … WHERE … a partir de um GridData.
    /// Um UPDATE por linha. PKs vão no WHERE; demais colunas no SET.
    /// </summary>
    public static class UpdateScriptBuilder
    {
        private const string NoPkComment =
            "-- ATENÇÃO: chave primária não resolvida; ajuste o WHERE\r\n";

        public static string Build(GridData data, string tableName, IReadOnlyList<string> pkColumns)
        {
            if (data.Rows.Count == 0)
                return string.Empty;

            bool hasPk = pkColumns != null && pkColumns.Count > 0;

            // Identify PK column indexes (case-insensitive)
            var pkIndexes = new System.Collections.Generic.HashSet<int>();
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
                // PK known but none of its columns exist in this grid projection → treat as unresolved
                if (pkIndexes.Count == 0)
                    hasPk = false;
            }

            var sb = new StringBuilder();

            if (!hasPk)
                sb.Append(NoPkComment);

            foreach (string[] row in data.Rows)
            {
                sb.Append("UPDATE ");
                sb.Append(tableName);
                sb.Append(" SET ");

                bool firstSet = true;
                for (int c = 0; c < data.Columns.Count; c++)
                {
                    if (hasPk && pkIndexes.Contains(c)) continue;

                    if (!firstSet) sb.Append(", ");
                    firstSet = false;

                    sb.Append('[');
                    sb.Append(data.Columns[c].Name.Replace("]", "]]"));
                    sb.Append("] = ");
                    sb.Append(SqlLiteralFormatter.Format(row[c], data.Columns[c].ClrType));
                }

                sb.Append(" WHERE ");

                if (!hasPk)
                {
                    sb.Append("/* defina a chave */ 1 = 0");
                }
                else
                {
                    bool firstWhere = true;
                    foreach (int pkIdx in pkIndexes)
                    {
                        if (!firstWhere) sb.Append(" AND ");
                        firstWhere = false;

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
