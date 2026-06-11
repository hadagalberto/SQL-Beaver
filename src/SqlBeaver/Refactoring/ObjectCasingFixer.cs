using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlBeaver.Metadata;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Fixes the casing of identifiers to match the canonical casing in the database
    /// metadata (schemas, tables, columns, objects). Operates on ScriptDom
    /// <see cref="TSqlTokenType.Identifier"/> tokens, applying edits descending so
    /// offsets stay valid. Ambiguous names (two metadata names differing only by case)
    /// are skipped. Strings, comments and keywords are never touched (not Identifier
    /// tokens). Pure, no VS dependencies. Returns the original text on parse failure.
    /// </summary>
    public static class ObjectCasingFixer
    {
        public static string Fix(string sql, DbMetadata metadata)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (sql.Length == 0) return sql;

            Dictionary<string, string> canonical = BuildCanonicalMap(metadata);
            if (canonical.Count == 0) return sql;

            IList<TSqlParserToken> tokens = TryTokenize(sql);
            if (tokens == null) return sql;

            var edits = new List<TextReplacement>();
            foreach (TSqlParserToken token in tokens)
            {
                if (token.TokenType != TSqlTokenType.Identifier) continue;
                string text = token.Text;
                if (string.IsNullOrEmpty(text)) continue;

                if (canonical.TryGetValue(text, out string fixedName) &&
                    !string.Equals(fixedName, text, StringComparison.Ordinal))
                {
                    edits.Add(new TextReplacement
                    {
                        Start = token.Offset,
                        Length = text.Length,
                        NewText = fixedName
                    });
                }
            }

            if (edits.Count == 0) return sql;
            edits.Sort((a, b) => b.Start.CompareTo(a.Start));

            var sb = new StringBuilder(sql);
            foreach (TextReplacement edit in edits)
            {
                sb.Remove(edit.Start, edit.Length);
                sb.Insert(edit.Start, edit.NewText);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Maps lower-cased name → canonical casing. A name that appears with two distinct
        /// casings in metadata is marked ambiguous and removed from the map (so it is skipped).
        /// </summary>
        private static Dictionary<string, string> BuildCanonicalMap(DbMetadata metadata)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                if (ambiguous.Contains(name)) return;
                if (map.TryGetValue(name, out string existing))
                {
                    if (!string.Equals(existing, name, StringComparison.Ordinal))
                    {
                        ambiguous.Add(name);
                        map.Remove(name);
                    }
                }
                else
                {
                    map[name] = name;
                }
            }

            if (metadata.Schemas != null)
                foreach (string s in metadata.Schemas) Add(s);

            if (metadata.Tables != null)
                foreach (TableEntry t in metadata.Tables) Add(t.Name);

            if (metadata.Objects != null)
                foreach (ObjectEntry o in metadata.Objects) Add(o.Name);

            if (metadata.ColumnsByTable != null)
                foreach (KeyValuePair<string, IReadOnlyList<ColumnEntry>> kv in metadata.ColumnsByTable)
                    foreach (ColumnEntry c in kv.Value) Add(c.Name);

            return map;
        }

        private static IList<TSqlParserToken> TryTokenize(string sql)
        {
            try
            {
                var parser = new TSql160Parser(true);
                IList<ParseError> errors;
                TSqlFragment fragment;
                using (var reader = new StringReader(sql))
                    fragment = parser.Parse(reader, out errors);

                if (errors != null && errors.Count > 0) return null;
                if (fragment == null) return null;
                return fragment.ScriptTokenStream;
            }
            catch
            {
                return null;
            }
        }
    }
}
