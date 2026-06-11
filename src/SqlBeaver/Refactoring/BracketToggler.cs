using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlBeaver.Analysis;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Adds or removes square brackets around regular identifiers, operating on the
    /// ScriptDom token stream (textual offsets, edits applied descending). Strings,
    /// comments and keywords are never touched because they are not
    /// <see cref="TSqlTokenType.Identifier"/> / <see cref="TSqlTokenType.QuotedIdentifier"/>.
    /// Pure, no VS dependencies. Returns the original text on tokenizer failure.
    /// </summary>
    public static class BracketToggler
    {
        /// <summary>Wraps every regular (unbracketed) identifier token in <c>[ ]</c>.</summary>
        public static string AddBrackets(string sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));
            if (sql.Length == 0) return sql;

            IList<TSqlParserToken> tokens = TryTokenize(sql);
            if (tokens == null) return sql;

            var edits = new List<TextReplacement>();
            foreach (TSqlParserToken token in tokens)
            {
                if (token.TokenType != TSqlTokenType.Identifier) continue;
                string text = token.Text;
                if (string.IsNullOrEmpty(text)) continue;
                if (text[0] == '[') continue; // already bracketed (defensive)

                edits.Add(new TextReplacement
                {
                    Start = token.Offset,
                    Length = text.Length,
                    NewText = "[" + text + "]"
                });
            }

            return ApplyDescending(sql, edits);
        }

        /// <summary>Unwraps each <c>[x]</c> quoted identifier whose inner text is a valid regular
        /// identifier and is not a reserved keyword. Names with spaces/special chars or keywords
        /// stay bracketed.</summary>
        public static string RemoveBrackets(string sql)
        {
            if (sql == null) throw new ArgumentNullException(nameof(sql));
            if (sql.Length == 0) return sql;

            IList<TSqlParserToken> tokens = TryTokenize(sql);
            if (tokens == null) return sql;

            var edits = new List<TextReplacement>();
            foreach (TSqlParserToken token in tokens)
            {
                if (token.TokenType != TSqlTokenType.QuotedIdentifier) continue;
                string text = token.Text;
                if (string.IsNullOrEmpty(text) || text[0] != '[' || text[text.Length - 1] != ']') continue;

                string inner = text.Substring(1, text.Length - 2);
                if (!IsRegularIdentifier(inner)) continue;
                if (SqlKeywords.All.Contains(inner)) continue;

                edits.Add(new TextReplacement
                {
                    Start = token.Offset,
                    Length = text.Length,
                    NewText = inner
                });
            }

            return ApplyDescending(sql, edits);
        }

        // ---------------------------------------------------------------

        private static bool IsRegularIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char first = s[0];
            if (!(char.IsLetter(first) || first == '_')) return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            }
            return true;
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

                // We never corrupt text on a syntax error.
                if (errors != null && errors.Count > 0) return null;
                if (fragment == null) return null;
                return fragment.ScriptTokenStream;
            }
            catch
            {
                return null;
            }
        }

        private static string ApplyDescending(string sql, List<TextReplacement> edits)
        {
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
    }
}
