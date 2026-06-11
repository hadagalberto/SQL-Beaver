using System;
using System.Collections.Generic;

namespace SqlBeaver.Completion
{
    /// <summary>
    /// Listas estáticas de funções built-in T-SQL e views de sistema para sugestão em completion.
    /// </summary>
    public static class SqlBuiltins
    {
        /// <summary>~80 funções built-in com assinatura curta.</summary>
        public static readonly IReadOnlyList<(string Name, string Signature)> Functions =
            new (string, string)[]
            {
                // Date/time
                ("GETDATE",             "()"),
                ("SYSDATETIME",         "()"),
                ("GETUTCDATE",          "()"),
                ("CURRENT_TIMESTAMP",   ""),
                ("DATEADD",             "(datepart, n, date)"),
                ("DATEDIFF",            "(datepart, start, end)"),
                ("DATEDIFF_BIG",        "(datepart, start, end)"),
                ("DATEPART",            "(datepart, date)"),
                ("DATENAME",            "(datepart, date)"),
                ("YEAR",                "(date)"),
                ("MONTH",               "(date)"),
                ("DAY",                 "(date)"),
                ("EOMONTH",             "(date)"),
                ("DATEFROMPARTS",       "(y, m, d)"),
                ("FORMAT",              "(value, format)"),

                // Conversão
                ("CAST",                "(expr AS tipo)"),
                ("CONVERT",             "(tipo, expr)"),
                ("TRY_CAST",            "(expr AS tipo)"),
                ("TRY_CONVERT",         "(tipo, expr)"),
                ("TRY_PARSE",           "(str AS tipo)"),

                // Nulo / condicional
                ("ISNULL",              "(expr, replacement)"),
                ("COALESCE",            "(val1, val2, ...)"),
                ("NULLIF",              "(expr1, expr2)"),
                ("IIF",                 "(cond, true_val, false_val)"),
                ("CHOOSE",              "(index, val1, val2, ...)"),

                // String
                ("LEN",                 "(str)"),
                ("DATALENGTH",          "(expr)"),
                ("LEFT",                "(str, n)"),
                ("RIGHT",               "(str, n)"),
                ("SUBSTRING",           "(str, start, len)"),
                ("CHARINDEX",           "(search, str)"),
                ("PATINDEX",            "('%pat%', str)"),
                ("REPLACE",             "(str, old, new)"),
                ("STUFF",               "(str, start, len, replacement)"),
                ("CONCAT",              "(str1, str2, ...)"),
                ("CONCAT_WS",           "(sep, str1, str2, ...)"),
                ("STRING_AGG",          "(expr, sep)"),
                ("STRING_SPLIT",        "(str, sep)"),
                ("TRIM",                "(str)"),
                ("LTRIM",               "(str)"),
                ("RTRIM",               "(str)"),
                ("UPPER",               "(str)"),
                ("LOWER",               "(str)"),
                ("REPLICATE",           "(str, n)"),
                ("SPACE",               "(n)"),
                ("REVERSE",             "(str)"),

                // Matemática
                ("ROUND",               "(n, decimals)"),
                ("FLOOR",               "(n)"),
                ("CEILING",             "(n)"),
                ("ABS",                 "(n)"),
                ("POWER",               "(base, exp)"),
                ("SQRT",                "(n)"),
                ("RAND",                "()"),

                // ID / misc
                ("NEWID",               "()"),
                ("SCOPE_IDENTITY",      "()"),
                ("@@IDENTITY",          ""),
                ("@@ROWCOUNT",          ""),
                ("@@ERROR",             ""),
                ("ERROR_MESSAGE",       "()"),
                ("ERROR_NUMBER",        "()"),
                ("OBJECT_ID",           "(name)"),
                ("OBJECT_NAME",         "(id)"),
                ("DB_NAME",             "()"),
                ("SUSER_SNAME",         "()"),
                ("ISNUMERIC",           "(expr)"),

                // Janela
                ("ROW_NUMBER",          "() OVER (...)"),
                ("RANK",                "() OVER (...)"),
                ("DENSE_RANK",          "() OVER (...)"),
                ("NTILE",               "(n) OVER (...)"),
                ("LAG",                 "(expr) OVER (...)"),
                ("LEAD",                "(expr) OVER (...)"),
                ("FIRST_VALUE",         "(expr) OVER (...)"),
                ("LAST_VALUE",          "(expr) OVER (...)"),

                // Agregação
                ("SUM",                 "(expr)"),
                ("COUNT",               "(expr)"),
                ("COUNT_BIG",           "(expr)"),
                ("AVG",                 "(expr)"),
                ("MIN",                 "(expr)"),
                ("MAX",                 "(expr)"),
                ("STDEV",               "(expr)"),
                ("VAR",                 "(expr)"),

                // JSON
                ("JSON_VALUE",          "(json, path)"),
                ("JSON_QUERY",          "(json, path)"),
                ("OPENJSON",            "(json)"),
            };

        /// <summary>~30 views de sistema.</summary>
        public static readonly IReadOnlyList<string> SystemViews = new string[]
        {
            "sys.objects",
            "sys.tables",
            "sys.columns",
            "sys.schemas",
            "sys.indexes",
            "sys.index_columns",
            "sys.foreign_keys",
            "sys.foreign_key_columns",
            "sys.parameters",
            "sys.types",
            "sys.procedures",
            "sys.views",
            "sys.databases",
            "sys.sql_modules",
            "sys.sql_expression_dependencies",
            "sys.partitions",
            "sys.allocation_units",
            "sys.dm_exec_requests",
            "sys.dm_exec_sessions",
            "sys.dm_exec_connections",
            "sys.dm_exec_query_stats",
            "sys.dm_os_wait_stats",
            "sys.dm_db_index_usage_stats",
            "sys.dm_db_index_physical_stats",
            "sys.dm_tran_locks",
            "information_schema.tables",
            "information_schema.columns",
            "information_schema.routines",
        };
    }
}
