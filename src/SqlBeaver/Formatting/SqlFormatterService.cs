using System.IO;

namespace SqlBeaver.Formatting
{
    /// <summary>Format Document via ScriptDom (MIT). Erro de sintaxe → false, sem tocar
    /// no texto. Tipos do ScriptDom apenas em corpos de método (restrição MEF do SSMS).</summary>
    public static class SqlFormatterService
    {
        /// <summary>
        /// Formata usando as opções persistidas em
        /// %LOCALAPPDATA%\SqlBeaver\format.json (via <see cref="FormatOptionsStore"/>).
        /// </summary>
        public static bool TryFormat(string sql, out string formatted, out string error, out bool containsComments)
            => TryFormat(sql, FormatOptionsStore.Options, out formatted, out error, out containsComments);

        /// <summary>
        /// Formata usando as <paramref name="options"/> fornecidas.
        /// Permite testes unitários sem depender do sistema de arquivos.
        /// </summary>
        /// <remarks>
        /// Mapeamento de keywordCasing → <c>KeywordCasing</c> enum do ScriptDom:
        ///   "uppercase" → Uppercase,
        ///   "lowercase" → Lowercase,
        ///   qualquer outra coisa (incluindo "none") → PascalCase
        ///   (o enum não possui membro "None"; PascalCase é o mais próximo de "sem alteração").
        /// </remarks>
        public static bool TryFormat(string sql, FormatOptions options, out string formatted, out string error, out bool containsComments)
        {
            formatted       = null;
            error           = null;
            containsComments = false;

            if (options == null)
                options = FormatOptions.CreateDefault();

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

                // Mapeia keywordCasing string → enum. O ScriptDom não tem "None";
                // "none" é mapeado para PascalCase (mais neutro disponível).
                Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing keywordCasing;
                switch ((options.KeywordCasing ?? "uppercase").ToLowerInvariant())
                {
                    case "lowercase":
                        keywordCasing = Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.Lowercase;
                        break;
                    case "none":
                        keywordCasing = Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.PascalCase;
                        break;
                    default: // "uppercase" e qualquer outro valor desconhecido
                        keywordCasing = Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.Uppercase;
                        break;
                }

                var generatorOptions = new Microsoft.SqlServer.TransactSql.ScriptDom.SqlScriptGeneratorOptions
                {
                    KeywordCasing                              = keywordCasing,
                    IndentationSize                            = options.IndentationSize,
                    AlignClauseBodies                          = options.AlignClauseBodies,
                    AsKeywordOnOwnLine                         = options.AsKeywordOnOwnLine,
                    IncludeSemicolons                          = options.IncludeSemicolons,
                    IndentSetClause                            = options.IndentSetClause,
                    NewLineBeforeFromClause                    = options.NewLineBeforeFromClause,
                    NewLineBeforeWhereClause                   = options.NewLineBeforeWhereClause,
                    NewLineBeforeGroupByClause                 = options.NewLineBeforeGroupByClause,
                    NewLineBeforeOrderByClause                 = options.NewLineBeforeOrderByClause,
                    NewLineBeforeHavingClause                  = options.NewLineBeforeHavingClause,
                    NewLineBeforeJoinClause                    = options.NewLineBeforeJoinClause,
                    NewLineBeforeOpenParenthesisInMultilineList  = options.NewLineBeforeOpenParenthesisInMultilineList,
                    NewLineBeforeCloseParenthesisInMultilineList = options.NewLineBeforeCloseParenthesisInMultilineList,
                    MultilineSelectElementsList                = options.MultilineSelectElementsList,
                    MultilineInsertSourcesList                 = options.MultilineInsertSourcesList,
                    MultilineWherePredicatesList               = options.MultilineWherePredicatesList,
                    MultilineViewColumnsList                   = options.MultilineViewColumnsList,
                };

                var generator = new Microsoft.SqlServer.TransactSql.ScriptDom.Sql160ScriptGenerator(generatorOptions);
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
