using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Wraps a SQL selection in a CREATE PROCEDURE, turning <c>@vars</c> that are used but
    /// not DECLAREd inside the selection into parameters. Parameter types are inferred from
    /// a <c>DECLARE @x TYPE</c> appearing ABOVE the selection in the same batch text, else
    /// fall back to <c>sql_variant /* ajuste o tipo */</c>. Pure, no VS dependencies.
    /// </summary>
    public static class ProcEncapsulator
    {
        private const string UnknownType = "sql_variant /* ajuste o tipo */";

        /// <summary>Convenience overload when only the selection text is available (no upward lookup).</summary>
        public static string Build(string selectedSql, string schema, string procName)
            => Build(selectedSql ?? string.Empty, 0, (selectedSql ?? string.Empty).Length, schema, procName);

        /// <summary>
        /// Builds the CREATE PROCEDURE from the selection [selStart, selStart+selLen) within
        /// <paramref name="fullText"/>, inferring parameter types from DECLAREs above the selection.
        /// </summary>
        public static string Build(string fullText, int selStart, int selLen, string schema, string procName)
        {
            if (fullText == null) throw new ArgumentNullException(nameof(fullText));
            selStart = Math.Max(0, Math.Min(selStart, fullText.Length));
            int selEnd = Math.Max(selStart, Math.Min(selStart + selLen, fullText.Length));
            string selection = fullText.Substring(selStart, selEnd - selStart);

            // Collect used variables (in order) and declared variables within the selection.
            var declaredInSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedOrder = new List<string>();
            var usedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ScanVariables(selection, usedOrder, usedSet, declaredInSelection);

            // Build parameter list: used but not declared inside.
            var declaresAbove = ScanDeclaredTypes(fullText, selStart);

            var sbParams = new StringBuilder();
            int paramCount = 0;
            foreach (string v in usedOrder)
            {
                if (declaredInSelection.Contains(v)) continue;
                string type = declaresAbove.TryGetValue(v, out string t) ? t : UnknownType;
                if (paramCount > 0) sbParams.Append(", ");
                sbParams.Append(v).Append(' ').Append(type);
                paramCount++;
            }

            string name = BracketIfNeeded(procName);
            string qualified = string.IsNullOrEmpty(schema)
                ? name
                : BracketIfNeeded(schema) + "." + name;

            var sb = new StringBuilder();
            sb.Append("CREATE PROCEDURE ").Append(qualified);
            if (paramCount > 0)
                sb.Append(" (").Append(sbParams).Append(')');
            sb.Append("\r\nAS\r\nBEGIN\r\n");
            sb.Append(selection.TrimEnd());
            sb.Append("\r\nEND");
            return sb.ToString();
        }

        // ---------------------------------------------------------------

        /// <summary>
        /// Scans <paramref name="text"/> for @variables, skipping strings/comments/brackets.
        /// A variable immediately following a DECLARE keyword (at statement level) is recorded
        /// as declared; otherwise it is a use.
        /// </summary>
        private static void ScanVariables(
            string text, List<string> usedOrder, HashSet<string> usedSet, HashSet<string> declared)
        {
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int blockDepth = 0;
            string lastWord = null;       // last keyword/identifier seen
            bool pendingDeclare = false;  // inside a DECLARE list

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockDepth > 0)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { if (c == '\'') inString = false; i++; continue; }
                if (inBracket) { if (c == ']') inBracket = false; i++; continue; }
                if (inQuotedIdent) { if (c == '"') inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { blockDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }

                if (c == ';') { pendingDeclare = false; lastWord = null; i++; continue; }

                if (c == '@')
                {
                    int start = i;
                    i++;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '@')) i++;
                    string v = text.Substring(start, i - start);
                    if (v.Length <= 1) continue; // stray '@'

                    if (pendingDeclare ||
                        string.Equals(lastWord, "DECLARE", StringComparison.OrdinalIgnoreCase))
                    {
                        declared.Add(v);
                        pendingDeclare = true; // subsequent @vars in same DECLARE list also declared
                    }
                    else if (usedSet.Add(v))
                    {
                        usedOrder.Add(v);
                    }
                    lastWord = null;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string w = text.Substring(start, i - start);
                    // SELECT/FROM/etc end a DECLARE list scope.
                    if (string.Equals(w, "DECLARE", StringComparison.OrdinalIgnoreCase))
                    {
                        lastWord = w;
                    }
                    else
                    {
                        if (pendingDeclare && IsStatementStarter(w)) pendingDeclare = false;
                        lastWord = w;
                    }
                    continue;
                }

                i++;
            }
        }

        /// <summary>
        /// Scans the text BEFORE <paramref name="limit"/> for <c>DECLARE @x TYPE</c> declarations
        /// and returns a map @x → type text. Skips strings/comments.
        /// </summary>
        private static Dictionary<string, string> ScanDeclaredTypes(string text, int limit)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inLineComment = false, inString = false, inBracket = false, inQuotedIdent = false;
            int blockDepth = 0;

            int i = 0;
            while (i < limit)
            {
                char c = text[i];

                if (inLineComment) { if (c == '\n') inLineComment = false; i++; continue; }
                if (blockDepth > 0)
                {
                    if (c == '*' && i + 1 < limit && text[i + 1] == '/') { blockDepth--; i += 2; continue; }
                    if (c == '/' && i + 1 < limit && text[i + 1] == '*') { blockDepth++; i += 2; continue; }
                    i++; continue;
                }
                if (inString) { if (c == '\'') inString = false; i++; continue; }
                if (inBracket) { if (c == ']') inBracket = false; i++; continue; }
                if (inQuotedIdent) { if (c == '"') inQuotedIdent = false; i++; continue; }

                if (c == '-' && i + 1 < limit && text[i + 1] == '-') { inLineComment = true; i += 2; continue; }
                if (c == '/' && i + 1 < limit && text[i + 1] == '*') { blockDepth = 1; i += 2; continue; }
                if (c == '\'') { inString = true; i++; continue; }
                if (c == '[') { inBracket = true; i++; continue; }
                if (c == '"') { inQuotedIdent = true; i++; continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < limit && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string w = text.Substring(start, i - start);
                    if (string.Equals(w, "DECLARE", StringComparison.OrdinalIgnoreCase))
                        ParseDeclareList(text, ref i, limit, map);
                    continue;
                }

                i++;
            }
            return map;
        }

        private static void ParseDeclareList(string text, ref int i, int limit, Dictionary<string, string> map)
        {
            while (i < limit)
            {
                SkipWs(text, ref i, limit);
                if (i >= limit || text[i] != '@') return;

                int vs = i;
                i++;
                while (i < limit && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '@')) i++;
                string v = text.Substring(vs, i - vs);

                SkipWs(text, ref i, limit);
                // type text up to ',' or '=' or ';' or end (balanced parens for varchar(50))
                int ts = i;
                int depth = 0;
                while (i < limit)
                {
                    char c = text[i];
                    if (c == '(') { depth++; i++; continue; }
                    if (c == ')') { if (depth > 0) depth--; i++; continue; }
                    if (depth == 0 && (c == ',' || c == ';' || c == '=')) break;
                    if (depth == 0 && c == '\n') break;
                    i++;
                }
                string type = text.Substring(ts, i - ts).Trim();
                if (!string.IsNullOrEmpty(v) && !string.IsNullOrEmpty(type) && !map.ContainsKey(v))
                    map[v] = type;

                // skip a default value, if any
                SkipWs(text, ref i, limit);
                if (i < limit && text[i] == '=')
                {
                    i++;
                    while (i < limit && text[i] != ',' && text[i] != ';' && text[i] != '\n') i++;
                }

                SkipWs(text, ref i, limit);
                if (i < limit && text[i] == ',') { i++; continue; }
                return;
            }
        }

        private static void SkipWs(string text, ref int i, int limit)
        {
            while (i < limit && char.IsWhiteSpace(text[i])) i++;
        }

        private static bool IsStatementStarter(string w)
        {
            return string.Equals(w, "SELECT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "INSERT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "UPDATE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "DELETE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "SET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "EXEC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "IF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "WHILE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, "BEGIN", StringComparison.OrdinalIgnoreCase);
        }

        private static string BracketIfNeeded(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.StartsWith("[", StringComparison.Ordinal)) return name;
            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return "[" + name + "]";
            }
            return name;
        }
    }
}
