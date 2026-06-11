namespace SqlBeaver.Completion
{
    /// <summary>Lista curada de keywords T-SQL para sugestão no completion (UPPERCASE).</summary>
    public static class SqlKeywordCompletions
    {
        public static readonly System.Collections.Generic.IReadOnlyList<string> Keywords = new[]
        {
            "SELECT","FROM","WHERE","INNER JOIN","LEFT JOIN","RIGHT JOIN","FULL OUTER JOIN","CROSS JOIN",
            "JOIN","ON","GROUP BY","ORDER BY","HAVING","UNION","UNION ALL","EXCEPT","INTERSECT",
            "INSERT INTO","VALUES","UPDATE","SET","DELETE","MERGE","OUTPUT","TOP","DISTINCT",
            "AND","OR","NOT","NULL","IS NULL","IS NOT NULL","IN","EXISTS","LIKE","BETWEEN","AS",
            "CASE","WHEN","THEN","ELSE","END","CAST","CONVERT","COALESCE","ISNULL",
            "BEGIN","COMMIT","ROLLBACK","TRANSACTION","TRY","CATCH","THROW","RETURN",
            "DECLARE","WITH","OVER","PARTITION BY","ASC","DESC",
            "CREATE","ALTER","DROP","TABLE","VIEW","PROCEDURE","FUNCTION","INDEX","TRIGGER",
            "IF","WHILE","EXEC","USE",
        };
    }
}
