using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Analysis
{
    /// <summary>Um item da estrutura ("outline") de um script: um por statement de topo.</summary>
    public sealed class OutlineItem
    {
        public string Kind { get; }
        public int Line { get; }
        public string Summary { get; }

        public OutlineItem(string kind, int line, string summary)
        {
            Kind = kind;
            Line = line;
            Summary = summary;
        }
    }

    /// <summary>
    /// Constrói a estrutura de um script SQL: percorre os batches e seus statements
    /// de topo, produzindo um <see cref="OutlineItem"/> por statement com o tipo,
    /// a linha inicial (1-based) e um resumo (≤ 80 chars, espaços colapsados).
    /// Em erro de parse, degrada para um único item informando o erro (nunca lança).
    /// </summary>
    public static class ScriptOutlineBuilder
    {
        private const int MaxSummary = 80;

        public static IReadOnlyList<OutlineItem> Build(string sql)
        {
            var items = new List<OutlineItem>();
            if (string.IsNullOrWhiteSpace(sql))
                return items;

            try
            {
                var parser = new TSql160Parser(true);
                IList<ParseError> errors;
                TSqlFragment fragment;
                using (var reader = new StringReader(sql))
                    fragment = parser.Parse(reader, out errors);

                var script = fragment as TSqlScript;
                if (script == null || (errors != null && errors.Count > 0))
                {
                    // Parse falhou (ou fragmento inesperado): item único informando o erro.
                    string msg = errors != null && errors.Count > 0
                        ? "linha " + errors[0].Line + ": " + errors[0].Message
                        : "não foi possível analisar o script";
                    items.Add(new OutlineItem("PARSE ERROR", 1, Truncate(msg)));
                    return items;
                }

                foreach (TSqlBatch batch in script.Batches)
                {
                    foreach (TSqlStatement statement in batch.Statements)
                    {
                        int line = statement.StartLine > 0 ? statement.StartLine : 1;
                        string kind = KindOf(statement);
                        string summary = Truncate(CollapseWhitespace(ExtractText(sql, statement)));
                        items.Add(new OutlineItem(kind, line, summary));
                    }
                }

                return items;
            }
            catch (Exception)
            {
                items.Clear();
                items.Add(new OutlineItem("PARSE ERROR", 1, Truncate("falha ao analisar o script")));
                return items;
            }
        }

        // ---------------------------------------------------------------
        // Kind a partir do tipo do statement
        // ---------------------------------------------------------------
        private static string KindOf(TSqlStatement statement)
        {
            switch (statement)
            {
                case SelectStatement _: return "SELECT";
                case InsertStatement _: return "INSERT";
                case UpdateStatement _: return "UPDATE";
                case DeleteStatement _: return "DELETE";
                case MergeStatement _: return "MERGE";
                case CreateProcedureStatement _: return "CREATE PROCEDURE";
                case AlterProcedureStatement _: return "ALTER PROCEDURE";
                case CreateFunctionStatement _: return "CREATE FUNCTION";
                case AlterFunctionStatement _: return "ALTER FUNCTION";
                case CreateTableStatement _: return "CREATE TABLE";
                case AlterTableStatement _: return "ALTER TABLE";
                case CreateViewStatement _: return "CREATE VIEW";
                case AlterViewStatement _: return "ALTER VIEW";
                case CreateIndexStatement _: return "CREATE INDEX";
                case CreateTriggerStatement _: return "CREATE TRIGGER";
                case DropTableStatement _:
                case DropProcedureStatement _:
                case DropViewStatement _:
                case DropFunctionStatement _:
                case DropIndexStatement _: return "DROP";
                case TruncateTableStatement _: return "TRUNCATE TABLE";
                case IfStatement _: return "IF";
                case WhileStatement _: return "WHILE";
                case BeginEndBlockStatement _: return "BEGIN/END";
                case TryCatchStatement _: return "TRY/CATCH";
                case DeclareVariableStatement _: return "DECLARE";
                case DeclareTableVariableStatement _: return "DECLARE";
                case SetVariableStatement _: return "SET";
                case ExecuteStatement _: return "EXEC";
                case BeginTransactionStatement _: return "BEGIN TRANSACTION";
                case CommitTransactionStatement _: return "COMMIT";
                case RollbackTransactionStatement _: return "ROLLBACK";
                case PredicateSetStatement _: return "SET";
                case WaitForStatement _: return "WAITFOR";
                case ReturnStatement _: return "RETURN";
                case ThrowStatement _: return "THROW";
            }

            // Default: derive do nome da classe (ex.: "GrantStatement" → "GRANT").
            string name = statement.GetType().Name;
            const string suffix = "Statement";
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - suffix.Length);
            return name.ToUpperInvariant();
        }

        // ---------------------------------------------------------------
        // Texto do statement a partir dos tokens (ScriptTokenStream)
        // ---------------------------------------------------------------
        private static string ExtractText(string sql, TSqlStatement statement)
        {
            try
            {
                if (statement.ScriptTokenStream != null &&
                    statement.FirstTokenIndex >= 0 &&
                    statement.LastTokenIndex >= statement.FirstTokenIndex)
                {
                    var sb = new StringBuilder();
                    var stream = statement.ScriptTokenStream;
                    for (int i = statement.FirstTokenIndex; i <= statement.LastTokenIndex && i < stream.Count; i++)
                        sb.Append(stream[i].Text);
                    return sb.ToString();
                }
            }
            catch
            {
                // cai no fallback abaixo
            }

            // Fallback: tipo do statement como resumo.
            return statement.GetType().Name;
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);
            bool lastWasSpace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && sb.Length > 0)
                        sb.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private static string Truncate(string text)
        {
            if (text == null) return string.Empty;
            if (text.Length <= MaxSummary) return text;
            return text.Substring(0, MaxSummary);
        }
    }
}
