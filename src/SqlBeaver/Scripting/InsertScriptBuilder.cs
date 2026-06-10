using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Gera script INSERT INTO ... VALUES ... a partir de um GridData.
    /// Agrupa em lotes de no máximo 1000 linhas (limite do SQL Server).
    /// </summary>
    public static class InsertScriptBuilder
    {
        private const int BatchSize = 1000;

        public static string Build(GridData data, string tableName)
        {
            if (data.Rows.Count == 0)
                return string.Empty;

            // Build column header: [Col1], [Col2], ...
            var colHeader = BuildColumnHeader(data.Columns);

            var sb = new StringBuilder();
            int rowCount = data.Rows.Count;
            int batchStart = 0;

            while (batchStart < rowCount)
            {
                int batchEnd = Math.Min(batchStart + BatchSize, rowCount);

                if (batchStart > 0)
                    sb.Append("\r\n");  // blank line between batches

                sb.Append("INSERT INTO ");
                sb.Append(tableName);
                sb.Append(" (");
                sb.Append(colHeader);
                sb.Append(")\r\nVALUES\r\n");

                for (int r = batchStart; r < batchEnd; r++)
                {
                    sb.Append("  (");
                    var row = data.Rows[r];
                    for (int c = 0; c < data.Columns.Count; c++)
                    {
                        if (c > 0)
                            sb.Append(", ");
                        sb.Append(SqlLiteralFormatter.Format(row[c], data.Columns[c].ClrType));
                    }
                    sb.Append(')');

                    bool isLastInBatch = (r == batchEnd - 1);
                    if (isLastInBatch)
                        sb.Append(";\r\n");
                    else
                        sb.Append(",\r\n");
                }

                batchStart = batchEnd;
            }

            return sb.ToString();
        }

        private static string BuildColumnHeader(IReadOnlyList<GridColumn> columns)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append('[');
                sb.Append(columns[i].Name.Replace("]", "]]"));
                sb.Append(']');
            }
            return sb.ToString();
        }
    }
}
