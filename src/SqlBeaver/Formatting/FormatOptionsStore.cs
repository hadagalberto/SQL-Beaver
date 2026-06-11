using System;
using System.IO;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Formatting
{
    /// <summary>
    /// Carrega as opções de formatação uma vez por sessão a partir de
    /// %LOCALAPPDATA%\SqlBeaver\format.json.
    /// Cria o arquivo de exemplo na primeira execução com TODOS os knobs e seus
    /// valores default (para o usuário descobrir as opções ao abrir o arquivo).
    /// Falhas degradam para os defaults — nunca lança.
    /// </summary>
    internal static class FormatOptionsStore
    {
        private static readonly Lazy<FormatOptions> _options =
            new Lazy<FormatOptions>(LoadOptions);

        public static FormatOptions Options => _options.Value;

        private static FormatOptions LoadOptions()
        {
            try
            {
                string dir  = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlBeaver");
                string path = Path.Combine(dir, "format.json");

                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(path, DefaultJson);
                    Log.Info("format.json de exemplo criado em " + path);
                }

                string json       = File.ReadAllText(path);
                FormatOptions opts = FormatOptions.Load(json);
                Log.Info("Opções de formatação carregadas de " + path);
                return opts;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar format.json — usando defaults de formatação", ex);
                return FormatOptions.CreateDefault();
            }
        }

        // Arquivo de exemplo com todos os knobs e comentários explicativos.
        // Valores aqui são os defaults que correspondem ao comportamento anterior.
        private const string DefaultJson =
            "{\r\n" +
            "  \"keywordCasing\": \"uppercase\",\r\n" +
            "  \"indentationSize\": 4,\r\n" +
            "  \"alignClauseBodies\": false,\r\n" +
            "  \"asKeywordOnOwnLine\": false,\r\n" +
            "  \"includeSemicolons\": true,\r\n" +
            "  \"indentSetClause\": false,\r\n" +
            "  \"newLineBeforeFromClause\": true,\r\n" +
            "  \"newLineBeforeWhereClause\": true,\r\n" +
            "  \"newLineBeforeGroupByClause\": true,\r\n" +
            "  \"newLineBeforeOrderByClause\": true,\r\n" +
            "  \"newLineBeforeHavingClause\": true,\r\n" +
            "  \"newLineBeforeJoinClause\": true,\r\n" +
            "  \"newLineBeforeOpenParenthesisInMultilineList\": false,\r\n" +
            "  \"newLineBeforeCloseParenthesisInMultilineList\": false,\r\n" +
            "  \"multilineSelectElementsList\": true,\r\n" +
            "  \"multilineInsertSourcesList\": true,\r\n" +
            "  \"multilineWherePredicatesList\": false,\r\n" +
            "  \"multilineViewColumnsList\": false\r\n" +
            "}\r\n";
    }
}
