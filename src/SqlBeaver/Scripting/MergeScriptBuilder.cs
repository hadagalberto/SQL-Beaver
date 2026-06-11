using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Gera um script MERGE … USING (VALUES …) com colunas separadas em ON (PK) /
    /// WHEN MATCHED (SET não-PK) / WHEN NOT MATCHED (INSERT).
    /// Limita a 1000 linhas de fonte.
    /// </summary>
    public static class MergeScriptBuilder
    {
        private const int MaxRows = 1000;
        private const string NoPkComment =
            "-- ATENÇÃO: chave primária não resolvida; ajuste o ON\r\n";

        public static string Build(GridData data, string tableName, IReadOnlyList<string> pkColumns)
        {
            bool hasPk = pkColumns != null && pkColumns.Count > 0;

            // Identify PK and non-PK column indexes
            var pkIndexes    = new List<int>();
            var nonPkIndexes = new List<int>();

            for (int c = 0; c < data.Columns.Count; c++)
            {
                bool isPk = false;
                if (hasPk)
                {
                    foreach (string pk in pkColumns)
                    {
                        if (string.Equals(data.Columns[c].Name, pk, StringComparison.OrdinalIgnoreCase))
                        {
                            isPk = true;
                            break;
                        }
                    }
                }

                if (isPk) pkIndexes.Add(c);
                else      nonPkIndexes.Add(c);
            }

            // PK known but none of its columns exist in this grid projection → treat as unresolved
            if (hasPk && pkIndexes.Count == 0)
                hasPk = false;

            int rowCount = Math.Min(data.Rows.Count, MaxRows);
            bool truncated = data.Rows.Count > MaxRows;

            var sb = new StringBuilder();

            if (!hasPk)
                sb.Append(NoPkComment);

            if (truncated)
                sb.Append("-- ATENÇÃO: resultado truncado em " + MaxRows + " linhas\r\n");

            // Column list for USING alias
            var colList = new StringBuilder();
            for (int c = 0; c < data.Columns.Count; c++)
            {
                if (c > 0) colList.Append(", ");
                colList.Append('[');
                colList.Append(data.Columns[c].Name.Replace("]", "]]"));
                colList.Append(']');
            }

            sb.Append("MERGE ");
            sb.Append(tableName);
            sb.Append(" AS alvo\r\n");
            sb.Append("USING (VALUES\r\n");

            for (int r = 0; r < rowCount; r++)
            {
                string[] row = data.Rows[r];
                sb.Append("    (");
                for (int c = 0; c < data.Columns.Count; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append(SqlLiteralFormatter.Format(row[c], data.Columns[c].ClrType));
                }
                sb.Append(')');
                if (r < rowCount - 1) sb.Append(',');
                sb.Append("\r\n");
            }

            sb.Append(") AS origem (");
            sb.Append(colList);
            sb.Append(")\r\n");

            // ON clause
            sb.Append("ON ");
            if (!hasPk)
            {
                sb.Append("/* defina a chave */ 1 = 0\r\n");
            }
            else
            {
                for (int i = 0; i < pkIndexes.Count; i++)
                {
                    if (i > 0) sb.Append(" AND ");
                    int c = pkIndexes[i];
                    string col = "[" + data.Columns[c].Name.Replace("]", "]]") + "]";
                    sb.Append("alvo." + col + " = origem." + col);
                }
                sb.Append("\r\n");
            }

            // WHEN MATCHED THEN UPDATE
            if (nonPkIndexes.Count > 0)
            {
                sb.Append("WHEN MATCHED THEN UPDATE SET ");
                for (int i = 0; i < nonPkIndexes.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    int c = nonPkIndexes[i];
                    string col = "[" + data.Columns[c].Name.Replace("]", "]]") + "]";
                    sb.Append("alvo." + col + " = origem." + col);
                }
                sb.Append("\r\n");
            }

            // WHEN NOT MATCHED THEN INSERT
            sb.Append("WHEN NOT MATCHED THEN INSERT (");
            sb.Append(colList);
            sb.Append(")\r\nVALUES (");
            for (int c = 0; c < data.Columns.Count; c++)
            {
                if (c > 0) sb.Append(", ");
                sb.Append("origem.[" + data.Columns[c].Name.Replace("]", "]]") + "]");
            }
            sb.Append(");\r\n");

            return sb.ToString();
        }
    }
}
