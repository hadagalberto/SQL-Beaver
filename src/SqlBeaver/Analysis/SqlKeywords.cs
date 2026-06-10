using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    /// <summary>Keywords T-SQL compartilhadas pelo auto-uppercase e pelo supressor
    /// de sugestões em digitação livre.</summary>
    public static class SqlKeywords
    {
        public static readonly HashSet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS",
            "APPLY", "ON", "AND", "OR", "NOT", "NULL", "IS", "IN", "EXISTS", "LIKE", "BETWEEN",
            "GROUP", "BY", "ORDER", "HAVING", "DISTINCT", "TOP", "AS", "UNION", "ALL",
            "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "MERGE", "OUTPUT",
            "CASE", "WHEN", "THEN", "ELSE", "END", "BEGIN", "IF", "WHILE", "RETURN",
            "DECLARE", "EXEC", "EXECUTE", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX",
            "PROCEDURE", "PROC", "FUNCTION", "TRIGGER", "USE", "GO", "WITH", "OVER", "PARTITION",
            "ASC", "DESC",
        };

        /// <summary>True se value é prefixo (ou igual, case-insensitive) de alguma keyword.</summary>
        public static bool IsPrefixOfAny(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (string keyword in All)
            {
                if (keyword.Length >= value.Length &&
                    keyword.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
