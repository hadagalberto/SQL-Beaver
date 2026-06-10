using System;
using System.Text.RegularExpressions;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Tenta extrair o nome da primeira tabela alvo de um SELECT FROM na query SQL.
    /// Strip de comentários é feito antes da busca.
    /// </summary>
    public static class TableNameHeuristic
    {
        // Matches a table reference: optionally schema-qualified, up to 3 parts
        // Each part: [bracketed] or plain identifier
        private static readonly Regex TableRefPattern = new Regex(
            @"\bFROM\s+" +
            @"((?:\[[^\]]+\]|[A-Za-z_]\w*)(?:\s*\.\s*(?:\[[^\]]+\]|[A-Za-z_]\w*)){0,2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string TryExtract(string query)
        {
            if (query == null)
                return null;

            string stripped = StripComments(query);

            var match = TableRefPattern.Match(stripped);
            if (!match.Success)
                return null;

            // Collapse any internal whitespace (e.g. "dbo . Pessoas" → "dbo.Pessoas")
            // and return the reference exactly as written (preserving brackets).
            string tableRef = match.Groups[1].Value;
            tableRef = Regex.Replace(tableRef, @"\s*\.\s*", ".");

            return tableRef;
        }

        private static string StripComments(string sql)
        {
            // Strip line comments first: -- to EOL. Must happen before block-comment strip
            // so that a /* inside a -- comment (e.g. "-- note /*") does not eat subsequent lines.
            sql = Regex.Replace(sql, @"--[^\r\n]*", " ");

            // Strip block comments /* ... */ (non-greedy; handle unclosed by going to end)
            sql = Regex.Replace(sql, @"/\*.*?(\*/|$)", " ", RegexOptions.Singleline);

            return sql;
        }
    }
}
