using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Gera um script CRUD completo (SELECT / INSERT / UPDATE / DELETE) com parâmetros @param
    /// para uso em procedures ou ad-hoc. PK identifica o WHERE de SELECT, UPDATE e DELETE.
    /// </summary>
    public static class CrudScriptBuilder
    {
        public static string Build(string schema, string table, IReadOnlyList<ColumnEntry> columns)
        {
            string qualifiedTable = string.IsNullOrEmpty(schema)
                ? "[" + table.Replace("]", "]]") + "]"
                : "[" + schema.Replace("]", "]]") + "].[" + table.Replace("]", "]]") + "]";

            var pkCols    = new List<ColumnEntry>();
            var allCols   = new List<ColumnEntry>(columns);

            foreach (ColumnEntry col in columns)
            {
                if (col.IsPrimaryKey)
                    pkCols.Add(col);
            }

            bool hasPk = pkCols.Count > 0;
            string whereClause = hasPk
                ? BuildWhere(pkCols)
                : "/* defina a chave */ 1 = 0 -- ATENÇÃO: chave primária não resolvida";

            var sb = new StringBuilder();

            // ── SELECT ────────────────────────────────────────────────────
            sb.Append("-- SELECT\r\n");
            sb.Append("SELECT ");
            for (int i = 0; i < allCols.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(BracketCol(allCols[i].Name));
            }
            sb.Append("\r\nFROM ");
            sb.Append(qualifiedTable);
            sb.Append("\r\nWHERE ");
            sb.Append(whereClause);
            sb.Append(";\r\n\r\n");

            // ── INSERT ────────────────────────────────────────────────────
            sb.Append("-- INSERT\r\n");
            sb.Append("INSERT INTO ");
            sb.Append(qualifiedTable);
            sb.Append(" (");
            for (int i = 0; i < allCols.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(BracketCol(allCols[i].Name));
            }
            sb.Append(")\r\nVALUES (");
            for (int i = 0; i < allCols.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("@" + allCols[i].Name);
            }
            sb.Append(");\r\n\r\n");

            // ── UPDATE ────────────────────────────────────────────────────
            sb.Append("-- UPDATE\r\n");
            sb.Append("UPDATE ");
            sb.Append(qualifiedTable);
            sb.Append("\r\nSET ");
            bool firstSet = true;
            foreach (ColumnEntry col in allCols)
            {
                if (hasPk && col.IsPrimaryKey) continue;
                if (!firstSet) sb.Append(", ");
                firstSet = false;
                sb.Append(BracketCol(col.Name));
                sb.Append(" = @" + col.Name);
            }
            // If all columns are PK (rare) still emit something
            if (firstSet)
            {
                sb.Append(BracketCol(allCols[0].Name));
                sb.Append(" = @" + allCols[0].Name);
            }
            sb.Append("\r\nWHERE ");
            sb.Append(whereClause);
            sb.Append(";\r\n\r\n");

            // ── DELETE ────────────────────────────────────────────────────
            sb.Append("-- DELETE\r\n");
            sb.Append("DELETE FROM ");
            sb.Append(qualifiedTable);
            sb.Append("\r\nWHERE ");
            sb.Append(whereClause);
            sb.Append(";\r\n");

            return sb.ToString();
        }

        private static string BuildWhere(IReadOnlyList<ColumnEntry> pkCols)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pkCols.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                sb.Append(BracketCol(pkCols[i].Name));
                sb.Append(" = @" + pkCols[i].Name);
            }
            return sb.ToString();
        }

        private static string BracketCol(string name)
            => "[" + name.Replace("]", "]]") + "]";
    }
}
