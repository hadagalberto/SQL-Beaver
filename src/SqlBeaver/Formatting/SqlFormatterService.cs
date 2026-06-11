using System.IO;

namespace SqlBeaver.Formatting
{
    /// <summary>Format Document via ScriptDom (MIT). Erro de sintaxe → false, sem tocar
    /// no texto. Tipos do ScriptDom apenas em corpos de método (restrição MEF do SSMS).</summary>
    public static class SqlFormatterService
    {
        public static bool TryFormat(string sql, out string formatted, out string error, out bool containsComments)
        {
            formatted = null;
            error = null;
            containsComments = false;
            try
            {
                var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true);
                Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment;
                System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
                using (var reader = new StringReader(sql))
                {
                    fragment = parser.Parse(reader, out errors);
                }

                if (errors != null && errors.Count > 0)
                {
                    error = $"erro de sintaxe na linha {errors[0].Line}: {errors[0].Message}";
                    return false;
                }

                if (fragment.ScriptTokenStream != null)
                {
                    foreach (Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken token in fragment.ScriptTokenStream)
                    {
                        if (token.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.SingleLineComment ||
                            token.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.MultilineComment)
                        {
                            containsComments = true;
                            break;
                        }
                    }
                }

                var options = new Microsoft.SqlServer.TransactSql.ScriptDom.SqlScriptGeneratorOptions
                {
                    KeywordCasing = Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.Uppercase,
                    IndentationSize = 4,
                    AlignClauseBodies = false,
                    NewLineBeforeFromClause = true,
                    NewLineBeforeWhereClause = true,
                    NewLineBeforeGroupByClause = true,
                    NewLineBeforeOrderByClause = true,
                    NewLineBeforeHavingClause = true,
                    NewLineBeforeJoinClause = true,
                    IncludeSemicolons = true,
                };

                var generator = new Microsoft.SqlServer.TransactSql.ScriptDom.Sql160ScriptGenerator(options);
                generator.GenerateScript(fragment, out formatted);
                return !string.IsNullOrEmpty(formatted);
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
