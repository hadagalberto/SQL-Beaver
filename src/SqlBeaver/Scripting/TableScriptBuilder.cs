using System.Collections.Generic;
using System.Text;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    /// <summary>Gera um CREATE TABLE a partir do cache de metadata (puro, sem I/O).</summary>
    public static class TableScriptBuilder
    {
        public static string Build(string schema, string table, IReadOnlyList<ColumnEntry> columns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Definição gerada pelo SQL Beaver a partir do cache de metadata");
            sb.AppendLine("CREATE TABLE " + SqlIdentifier.Bracket(schema) + "." + SqlIdentifier.Bracket(table));
            sb.AppendLine("(");

            // Collect PK columns for the constraint line
            var pkColumns = new List<string>();
            foreach (ColumnEntry col in columns)
            {
                if (col.IsPrimaryKey)
                    pkColumns.Add(col.Name);
            }

            int lastDataColumn = columns.Count - 1;
            bool hasPk = pkColumns.Count > 0;

            for (int i = 0; i <= lastDataColumn; i++)
            {
                ColumnEntry col = columns[i];
                bool isLast = (i == lastDataColumn) && !hasPk;
                string nullability = col.IsNullable ? "NULL" : "NOT NULL";
                string comma = isLast ? "" : ",";
                sb.AppendLine("    " + SqlIdentifier.Bracket(col.Name) + " " + col.SqlType + " " + nullability + comma);
            }

            if (hasPk)
            {
                var pkCols = new StringBuilder();
                for (int i = 0; i < pkColumns.Count; i++)
                {
                    if (i > 0) pkCols.Append(", ");
                    pkCols.Append(SqlIdentifier.Bracket(pkColumns[i]));
                }
                sb.AppendLine("    CONSTRAINT " + SqlIdentifier.Bracket("PK_" + table) +
                              " PRIMARY KEY (" + pkCols + ")");
            }

            sb.Append(");");
            return sb.ToString();
        }
    }
}
