using System;
using System.Collections.Generic;

namespace SqlBeaver.Session
{
    /// <summary>Linha exibida no diálogo de recuperação, com o texto do snapshot já carregado.</summary>
    public sealed class SnapshotRow
    {
        public string Caption { get; set; }
        public string Server { get; set; }
        public string Database { get; set; }
        public string When { get; set; }
        public string ContentText { get; set; }
    }

    /// <summary>
    /// Filtro puro para a busca as-you-type da recuperação de consultas:
    /// retorna as linhas cuja Caption OU ContentText contém a query (case-insensitive).
    /// Query vazia/nula → todas as linhas.
    /// </summary>
    public static class SnapshotSearchFilter
    {
        public static IReadOnlyList<SnapshotRow> Filter(IReadOnlyList<SnapshotRow> rows, string query)
        {
            if (rows == null)
                return Array.Empty<SnapshotRow>();

            if (string.IsNullOrWhiteSpace(query))
                return rows;

            string q = query.Trim();
            var result = new List<SnapshotRow>();
            foreach (SnapshotRow row in rows)
            {
                if (row == null) continue;
                bool captionMatch = row.Caption != null &&
                    row.Caption.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                bool contentMatch = row.ContentText != null &&
                    row.ContentText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (captionMatch || contentMatch)
                    result.Add(row);
            }
            return result;
        }
    }
}
