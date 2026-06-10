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

        // Detects if the character right after the matched table name starts a subquery
        private static readonly Regex SubqueryStart = new Regex(
            @"^\s*\(", RegexOptions.Compiled);

        public static string TryExtract(string query)
        {
            if (query == null)
                return null;

            string stripped = StripComments(query);

            var match = TableRefPattern.Match(stripped);
            if (!match.Success)
                return null;

            // Check for subquery: if FROM is followed by '(' instead of a table name.
            // The regex above won't match '(' as a table name, so this is fine as-is.
            // But we also need to check that the char right after FROM isn't '('
            // which would mean FROM (subquery). Since our regex requires the table to
            // start with '[' or a letter, '(' won't be captured — so no extra check needed.

            // Collapse any internal whitespace (e.g. "dbo . Pessoas" → "dbo.Pessoas")
            // and return the reference exactly as written (preserving brackets).
            string tableRef = match.Groups[1].Value;
            // Collapse whitespace around dots
            tableRef = Regex.Replace(tableRef, @"\s*\.\s*", ".");

            return tableRef;
        }

        private static string StripComments(string sql)
        {
            // Strip block comments /* ... */ (non-greedy; handle unclosed by going to end)
            sql = Regex.Replace(sql, @"/\*.*?(\*/|$)", " ", RegexOptions.Singleline);

            // Strip line comments -- ... to end of line
            sql = Regex.Replace(sql, @"--[^\r\n]*", " ");

            return sql;
        }
    }
}
