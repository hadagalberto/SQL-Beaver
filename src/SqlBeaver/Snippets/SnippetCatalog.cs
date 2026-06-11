using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SqlBeaver.Snippets
{
    /// <summary>Catálogo de snippets: padrões embutidos + merge do JSON do usuário
    /// (sobrescreve por shortcut). Puro — o IO do arquivo fica no SnippetStore.</summary>
    public static class SnippetCatalog
    {
        public static IReadOnlyList<SnippetDefinition> Defaults { get; } = BuildDefaults();

        public static IReadOnlyDictionary<string, SnippetDefinition> Load(string userJson)
        {
            var catalog = new Dictionary<string, SnippetDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (SnippetDefinition snippet in Defaults)
                catalog[snippet.Shortcut] = snippet;

            if (!string.IsNullOrWhiteSpace(userJson))
            {
                try
                {
                    var serializer = new DataContractJsonSerializer(typeof(SnippetFile));
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(userJson)))
                    {
                        var file = serializer.ReadObject(stream) as SnippetFile;
                        if (file?.Snippets != null)
                        {
                            foreach (SnippetDefinition snippet in file.Snippets)
                            {
                                if (!string.IsNullOrWhiteSpace(snippet?.Shortcut) &&
                                    !string.IsNullOrWhiteSpace(snippet.Expansion))
                                    catalog[snippet.Shortcut] = snippet;
                            }
                        }
                    }
                }
                catch
                {
                    // JSON inválido: fica só com os padrões
                }
            }

            return catalog;
        }

        private static IReadOnlyList<SnippetDefinition> BuildDefaults()
        {
            SnippetDefinition S(string shortcut, string title, string expansion)
                => new SnippetDefinition { Shortcut = shortcut, Title = title, Expansion = expansion, Description = title };

            return new List<SnippetDefinition>
            {
                S("s",     "SELECT",                  "SELECT $cursor$"),
                S("ssf",   "SELECT * FROM",           "SELECT * FROM $cursor$"),
                S("sf",    "SELECT ... FROM",         "SELECT $cursor$ FROM "),
                S("st1",   "SELECT TOP 1",            "SELECT TOP 1 * FROM $cursor$"),
                S("st10",  "SELECT TOP 10",           "SELECT TOP 10 * FROM $cursor$"),
                S("st100", "SELECT TOP 100",          "SELECT TOP 100 * FROM $cursor$"),
                S("wh",    "WHERE",                   "WHERE $cursor$"),
                S("ob",    "ORDER BY",                "ORDER BY $cursor$"),
                S("gb",    "GROUP BY",                "GROUP BY $cursor$"),
                S("hv",    "HAVING",                  "HAVING $cursor$"),
                S("jn",    "INNER JOIN",              "INNER JOIN $cursor$ ON "),
                S("lj",    "LEFT JOIN",               "LEFT JOIN $cursor$ ON "),
                S("rj",    "RIGHT JOIN",              "RIGHT JOIN $cursor$ ON "),
                S("fj",    "FULL OUTER JOIN",         "FULL OUTER JOIN $cursor$ ON "),
                S("iit",   "INSERT INTO",             "INSERT INTO $cursor$ () VALUES ()"),
                S("ut",    "UPDATE SET WHERE",        "UPDATE $cursor$ SET  WHERE "),
                S("del",   "DELETE FROM WHERE",       "DELETE FROM $cursor$ WHERE "),
                S("ex",    "EXISTS",                  "EXISTS (SELECT 1 FROM $cursor$ WHERE )"),
                S("cte",   "CTE",                     "WITH cte AS (\r\n    SELECT $cursor$\r\n)\r\nSELECT * FROM cte"),
                S("tmp",   "Temp table",              "DROP TABLE IF EXISTS #tmp;\r\nCREATE TABLE #tmp ($cursor$)"),
                S("sinto", "SELECT INTO #tmp",        "SELECT $cursor$ INTO #tmp FROM "),
                S("dv",    "DECLARE variável",        "DECLARE @$cursor$ "),
                S("iff",   "IF BEGIN END",            "IF $cursor$\r\nBEGIN\r\n\r\nEND"),
                S("bgt",   "BEGIN TRAN/COMMIT",       "BEGIN TRANSACTION;\r\n$cursor$\r\nCOMMIT TRANSACTION;"),
                S("btry",  "TRY/CATCH",               "BEGIN TRY\r\n    $cursor$\r\nEND TRY\r\nBEGIN CATCH\r\n    THROW;\r\nEND CATCH"),
            };
        }
    }
}
