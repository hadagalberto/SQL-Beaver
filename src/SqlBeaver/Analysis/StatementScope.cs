using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    public sealed class TableRef
    {
        /// <summary>Schema, ou null quando o nome não foi qualificado.</summary>
        public string Schema { get; }
        public string Table { get; }
        /// <summary>Alias, ou null quando ausente.</summary>
        public string Alias { get; }

        public TableRef(string schema, string table, string alias)
        {
            Schema = schema;
            Table = table;
            Alias = alias;
        }
    }

    /// <summary>
    /// Extrai as tabelas (com aliases) do statement que contém o cursor, num único
    /// passe para frente sobre a janela. Subqueries (parênteses) são ignoradas;
    /// CTEs degradam para "sem tabela". Puro e sem dependências de VS.
    /// </summary>
    public static class StatementScopeAnalyzer
    {
        /// <summary>Keywords que iniciam um statement implícito em depth 0 (T-SQL não exige ';').
        /// SET fica fora (UPDATE t SET quebraria); BEGIN/IF/WHILE fora (blocos não resetam escopo).</summary>
        private static readonly HashSet<string> StatementStarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "WITH", "DECLARE", "EXEC", "EXECUTE",
            "CREATE", "ALTER", "DROP", "TRUNCATE", "RETURN", "PRINT", "USE",
        };

        /// <summary>DMLs que podem ser absorvidos por um INSERT/WITH anterior (INSERT…SELECT; WITH cte AS (…) DML).</summary>
        private static readonly HashSet<string> DmlStarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "UPDATE", "INSERT", "DELETE", "MERGE",
        };

        private static readonly HashSet<string> AbsorbingStarters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INSERT", "WITH",
        };

        /// <summary>Última palavra antes de SELECT que indica query composta (não inicia novo statement).</summary>
        private static readonly HashSet<string> CompoundConnectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "UNION", "ALL", "EXCEPT", "INTERSECT",
        };

        public static IReadOnlyList<TableRef> GetTablesInScope(string text, int caretPosition)
        {
            if (string.IsNullOrEmpty(text) || caretPosition < 0 || caretPosition > text.Length)
                return Array.Empty<TableRef>();

            var current = new List<TableRef>();
            int statementStart = 0;
            int parenDepth = 0;
            string statementStarter = null; // primeira starter-keyword do statement atual
            bool absorbedDml = false;       // INSERT…SELECT / WITH…DML já absorvido?
            string lastWord = null;         // último identificador em depth 0 (regra do UNION)
            bool inLineComment = false, inString = false, inQuotedIdent = false;
            int blockCommentDepth = 0;

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                // ---- estados de comentário/string (mesma semântica do SqlContextAnalyzer) ----
                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockCommentDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockCommentDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { if (c == '\'') inString = false; i++; continue; }
                if (inQuotedIdent) { if (c == '"') inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockCommentDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

                // ---- fim de statement ----
                if (c == ';' && parenDepth == 0)
                {
                    if (caretPosition >= statementStart && caretPosition <= i)
                        return current;
                    current = new List<TableRef>();
                    statementStart = i + 1;
                    parenDepth = 0;
                    statementStarter = null;
                    absorbedDml = false;
                    lastWord = null;
                    i++;
                    continue;
                }

                // ---- tokens ----
                if (c == '[' || IsIdentifierStart(c))
                {
                    int tokenStart = i;
                    string token = ReadIdentifier(text, ref i);

                    if (parenDepth == 0 && string.Equals(token, "GO", StringComparison.OrdinalIgnoreCase))
                    {
                        if (caretPosition >= statementStart && caretPosition <= tokenStart)
                            return current;
                        current = new List<TableRef>();
                        statementStart = i;
                        statementStarter = null;
                        absorbedDml = false;
                        lastWord = null;
                        continue;
                    }

                    // ---- divisão implícita de statements (T-SQL não exige ';') ----
                    if (parenDepth == 0 && StatementStarters.Contains(token))
                    {
                        if (statementStarter == null)
                        {
                            // primeira keyword do statement atual: ela o inicia, não divide
                            statementStarter = token;
                        }
                        else if (string.Equals(token, "SELECT", StringComparison.OrdinalIgnoreCase) &&
                                 lastWord != null && CompoundConnectors.Contains(lastWord))
                        {
                            // UNION [ALL] / EXCEPT / INTERSECT: query composta, mesmo statement
                        }
                        else if (DmlStarters.Contains(token) && AbsorbingStarters.Contains(statementStarter) && !absorbedDml)
                        {
                            // INSERT…SELECT; WITH cte AS (…) <DML>
                            absorbedDml = true;
                        }
                        else
                        {
                            // novo statement implícito: caret EM tokenStart pertence ao novo
                            if (caretPosition >= statementStart && caretPosition < tokenStart)
                                return current;
                            current = new List<TableRef>();
                            statementStart = tokenStart;
                            statementStarter = token;
                            absorbedDml = false;
                            // segue para o tratamento FROM/JOIN/UPDATE deste mesmo token
                        }
                    }

                    if (parenDepth == 0)
                        lastWord = token;

                    if (parenDepth == 0 &&
                        (string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(token, "JOIN", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(token, "UPDATE", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(token, "INTO", StringComparison.OrdinalIgnoreCase)))
                    {
                        bool allowCommaList = string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase);
                        do
                        {
                            SkipWhitespace(text, ref i);
                            TableRef tableRef = TryReadTableRef(text, ref i);
                            if (tableRef == null)
                                break;
                            current.Add(tableRef);
                            SkipWhitespace(text, ref i);
                        } while (allowCommaList && i < text.Length && text[i] == ',' && ++i > 0);
                    }
                    continue;
                }

                i++;
            }

            return caretPosition >= statementStart ? current : Array.Empty<TableRef>();
        }

        private static TableRef TryReadTableRef(string text, ref int i)
        {
            if (i >= text.Length || (text[i] != '[' && !IsIdentifierStart(text[i])))
                return null; // subquery "(", VALUES etc.

            var parts = new List<string> { ReadIdentifier(text, ref i) };
            while (i < text.Length && text[i] == '.')
            {
                i++;
                if (i >= text.Length || (text[i] != '[' && !IsIdentifierStart(text[i])))
                    break;
                parts.Add(ReadIdentifier(text, ref i));
            }

            // até 3 partes (db.schema.tabela): usa as duas últimas
            string table = parts[parts.Count - 1];
            string schema = parts.Count >= 2 ? parts[parts.Count - 2] : null;

            // alias opcional: [AS] palavra que não seja keyword
            int save = i;
            SkipWhitespace(text, ref i);
            string alias = null;
            if (i < text.Length && (text[i] == '[' || IsIdentifierStart(text[i])))
            {
                string word = ReadIdentifier(text, ref i);
                if (string.Equals(word, "AS", StringComparison.OrdinalIgnoreCase))
                {
                    SkipWhitespace(text, ref i);
                    if (i < text.Length && (text[i] == '[' || IsIdentifierStart(text[i])))
                    {
                        string aliasWord = ReadIdentifier(text, ref i);
                        if (!SqlKeywords.All.Contains(aliasWord))
                            alias = aliasWord;
                    }
                }
                else if (!SqlKeywords.All.Contains(word))
                {
                    alias = word;
                }
                else
                {
                    i = save; // keyword (WHERE/INNER/ON...): devolve para o passe principal
                }
            }
            return new TableRef(schema, table, alias);
        }

        private static string ReadIdentifier(string text, ref int i)
        {
            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close < 0) { string rest = text.Substring(i + 1); i = text.Length; return rest; }
                string name = text.Substring(i + 1, close - i - 1);
                i = close + 1;
                return name;
            }

            int startPos = i;
            while (i < text.Length && IsIdentifierChar(text[i]))
                i++;
            return text.Substring(startPos, i - startPos);
        }

        private static void SkipWhitespace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
    }
}
