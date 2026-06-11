using System;
using System.Collections.Generic;
using System.IO;
using SqlBeaver.Metadata;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Adds or removes schema qualifiers from unqualified (or uniquely-qualified)
    /// table references in a SQL script.
    /// Uses ScriptDom for parsing but performs TEXTUAL edits so original formatting
    /// is preserved.
    /// </summary>
    public static class NameQualifier
    {
        /// <summary>
        /// Returns textual edits that add the schema prefix to unqualified table references
        /// where the schema can be uniquely resolved.
        /// Returns an empty list when nothing to do; null on syntax error.
        /// </summary>
        public static IReadOnlyList<TextReplacement> Qualify(string sql, DbMetadata metadata)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment;
            if (!TryParse(sql, out fragment))
                return null;

            var edits = new List<TextReplacement>();
            var visitor = new QualifyVisitor(sql, metadata, edits, qualify: true);
            fragment.Accept(visitor);
            edits.Sort((a, b) => b.Start.CompareTo(a.Start));
            return edits;
        }

        /// <summary>
        /// Returns textual edits that remove the schema prefix from table references
        /// where the table name is unique across all schemas.
        /// Returns an empty list when nothing to do; null on syntax error.
        /// </summary>
        public static IReadOnlyList<TextReplacement> Unqualify(string sql, DbMetadata metadata)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment;
            if (!TryParse(sql, out fragment))
                return null;

            var edits = new List<TextReplacement>();
            var visitor = new QualifyVisitor(sql, metadata, edits, qualify: false);
            fragment.Accept(visitor);
            edits.Sort((a, b) => b.Start.CompareTo(a.Start));
            return edits;
        }

        /// <summary>
        /// Applies a list of edits (assumed to be sorted DESCENDING by Start) to <paramref name="sql"/>.
        /// </summary>
        public static string Apply(string sql, IReadOnlyList<TextReplacement> edits)
        {
            if (edits == null || edits.Count == 0) return sql;
            // edits are sorted descending — apply from end to start so earlier offsets stay valid
            char[] buf = sql.ToCharArray();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(sql);
            foreach (TextReplacement edit in edits)
            {
                sb.Remove(edit.Start, edit.Length);
                sb.Insert(edit.Start, edit.NewText);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------
        // Parsing
        // ---------------------------------------------------------------

        private static bool TryParse(string sql, out Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment)
        {
            var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true);
            System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
            using (var reader = new StringReader(sql))
                fragment = parser.Parse(reader, out errors);
            return errors == null || errors.Count == 0;
        }

        // ---------------------------------------------------------------
        // Visitor
        // ---------------------------------------------------------------

        private sealed class QualifyVisitor
            : Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragmentVisitor
        {
            private readonly string _sql;
            private readonly DbMetadata _metadata;
            private readonly List<TextReplacement> _edits;
            private readonly bool _qualify; // true = add schema, false = remove schema

            public QualifyVisitor(string sql, DbMetadata metadata, List<TextReplacement> edits, bool qualify)
            {
                _sql = sql;
                _metadata = metadata;
                _edits = edits;
                _qualify = qualify;
            }

            public override void ExplicitVisit(
                Microsoft.SqlServer.TransactSql.ScriptDom.NamedTableReference node)
            {
                var schemaObject = node.SchemaObject;
                if (schemaObject == null) return;

                var baseId = schemaObject.BaseIdentifier;
                if (baseId == null) return;

                string tableName = baseId.Value;
                if (string.IsNullOrEmpty(tableName)) return;

                var tokenStream = node.ScriptTokenStream;
                if (tokenStream == null) return;

                if (_qualify)
                {
                    // Add schema if not already qualified
                    if (schemaObject.SchemaIdentifier != null) return; // already qualified

                    string schema = _metadata.ResolveUniqueSchema(tableName);
                    if (schema == null) return; // ambiguous or unknown

                    // Insert "schema." before the BaseIdentifier token
                    int tokenIdx = baseId.FirstTokenIndex;
                    int offset = tokenStream[tokenIdx].Offset;

                    _edits.Add(new TextReplacement
                    {
                        Start = offset,
                        Length = 0,
                        NewText = schema + "."
                    });
                }
                else
                {
                    // Remove schema when unique
                    if (schemaObject.SchemaIdentifier == null) return; // not qualified

                    string schema = schemaObject.SchemaIdentifier.Value;
                    if (string.IsNullOrEmpty(schema)) return;

                    // Only remove if name is unique (ResolveUniqueSchema returns non-null)
                    string resolved = _metadata.ResolveUniqueSchema(tableName);
                    if (resolved == null) return; // ambiguous — leave it

                    // Find the schema token and the dot after it
                    int schemaTokenIdx = schemaObject.SchemaIdentifier.FirstTokenIndex;
                    int dotTokenIdx = FindDotAfter(tokenStream, schemaObject.SchemaIdentifier.LastTokenIndex);
                    if (dotTokenIdx < 0) return;

                    int schemaOffset = tokenStream[schemaTokenIdx].Offset;
                    int dotOffset = tokenStream[dotTokenIdx].Offset;

                    // Remove from start of schema identifier token through and including the dot
                    int removeLen = dotOffset - schemaOffset + 1; // +1 for the dot char

                    // But we also need to handle bracketed schema like [dbo] — token text length
                    // The schema token text is tokenStream[schemaTokenIdx].Text
                    // Actually we remove: schemaOffset..(dotOffset inclusive) = dotOffset - schemaOffset + 1
                    _edits.Add(new TextReplacement
                    {
                        Start = schemaOffset,
                        Length = removeLen,
                        NewText = string.Empty
                    });
                }
            }

            private static int FindDotAfter(
                System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken> tokens,
                int afterIdx)
            {
                for (int i = afterIdx + 1; i < tokens.Count; i++)
                {
                    var tt = tokens[i].TokenType;
                    if (tt == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Dot) return i;
                    if (tt == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace) continue;
                    break; // something else: no dot found
                }
                return -1;
            }
        }
    }
}
