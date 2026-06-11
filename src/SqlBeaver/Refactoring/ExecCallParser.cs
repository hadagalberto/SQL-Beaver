using System;
using System.Collections.Generic;
using SqlBeaver.Analysis;

namespace SqlBeaver.Refactoring
{
    /// <summary>One argument of an EXEC call: <c>@name = value</c> (Name set) or positional (Name null).</summary>
    public sealed class ExecArg
    {
        /// <summary>Parameter name (including leading '@'), or null for a positional argument.</summary>
        public string Name { get; }
        /// <summary>Raw value text as written in the call.</summary>
        public string ValueText { get; }

        public ExecArg(string name, string valueText)
        {
            Name = name;
            ValueText = valueText;
        }
    }

    /// <summary>A parsed <c>EXEC [schema.]proc [args]</c> call.</summary>
    public sealed class ExecCall
    {
        public string Schema { get; }
        public string Proc { get; }
        public IReadOnlyList<ExecArg> Args { get; }
        /// <summary>Raw [Start, End) span of the EXEC statement within the source text.</summary>
        public int SpanStart { get; }
        public int SpanLength { get; }

        public ExecCall(string schema, string proc, IReadOnlyList<ExecArg> args, int spanStart, int spanLength)
        {
            Schema = schema;
            Proc = proc;
            Args = args;
            SpanStart = spanStart;
            SpanLength = spanLength;
        }
    }

    /// <summary>
    /// Parses an <c>EXEC [schema.]proc [@a = expr | expr [, ...]]</c> statement at the caret.
    /// Pure, no VS dependencies; null when the caret statement is not an EXEC call.
    /// </summary>
    public static class ExecCallParser
    {
        public static ExecCall Parse(string text, int caret)
        {
            if (string.IsNullOrEmpty(text)) return null;

            StatementBounds bounds = StatementScopeAnalyzer.GetStatementBoundsAt(text, caret);
            if (bounds.Length == 0) return null;

            int start = bounds.Start;
            int end = bounds.Start + bounds.Length;

            int i = start;
            SkipWs(text, ref i, end);

            // EXEC / EXECUTE keyword
            string kw = ReadWord(text, ref i, end);
            if (!string.Equals(kw, "EXEC", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kw, "EXECUTE", StringComparison.OrdinalIgnoreCase))
                return null;

            SkipWs(text, ref i, end);

            // EXEC('...') string form is not inlinable.
            if (i < end && text[i] == '(') return null;

            // [schema.]proc — up to two dotted parts
            string part1 = ReadIdentifier(text, ref i, end);
            if (string.IsNullOrEmpty(part1)) return null;

            string schema = null;
            string proc = part1;
            SkipWs(text, ref i, end);
            if (i < end && text[i] == '.')
            {
                i++;
                SkipWs(text, ref i, end);
                string part2 = ReadIdentifier(text, ref i, end);
                if (string.IsNullOrEmpty(part2)) return null;
                schema = part1;
                proc = part2;
            }

            // Arguments
            var args = new List<ExecArg>();
            SkipWs(text, ref i, end);
            while (i < end)
            {
                SkipWs(text, ref i, end);
                if (i >= end) break;

                string name = null;
                // named: @x = value
                if (text[i] == '@')
                {
                    int save = i;
                    string varName = ReadVariable(text, ref i, end);
                    int afterName = i;
                    SkipWs(text, ref i, end);
                    if (i < end && text[i] == '=' && (i + 1 >= end || text[i + 1] != '='))
                    {
                        name = varName;
                        i++; // consume '='
                        SkipWs(text, ref i, end);
                    }
                    else
                    {
                        // positional whose value is itself a @var
                        i = save;
                    }
                }

                string value = ReadValue(text, ref i, end);
                if (string.IsNullOrEmpty(value) && name == null) break;
                args.Add(new ExecArg(name, value));

                SkipWs(text, ref i, end);
                if (i < end && text[i] == ',') { i++; continue; }
                break;
            }

            return new ExecCall(schema, proc, args, bounds.Start, bounds.Length);
        }

        // ---------------------------------------------------------------

        private static string ReadValue(string text, ref int i, int end)
        {
            SkipWs(text, ref i, end);
            int start = i;
            int parenDepth = 0;
            while (i < end)
            {
                char c = text[i];
                if (c == '\'')
                {
                    i++;
                    while (i < end && text[i] != '\'') i++;
                    if (i < end) i++; // closing quote
                    continue;
                }
                if (c == '(') { parenDepth++; i++; continue; }
                if (c == ')') { if (parenDepth == 0) break; parenDepth--; i++; continue; }
                if (parenDepth == 0 && (c == ',' || c == ';')) break;
                if (parenDepth == 0 && (c == 'O' || c == 'o'))
                {
                    // stop on trailing OUTPUT/OUT keyword
                    int save = i;
                    string w = ReadWord(text, ref i, end);
                    if (string.Equals(w, "OUTPUT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(w, "OUT", StringComparison.OrdinalIgnoreCase))
                    {
                        i = save;
                        break;
                    }
                    // not a keyword: i already advanced past the word
                    continue;
                }
                i++;
            }
            return text.Substring(start, i - start).Trim();
        }

        private static void SkipWs(string text, ref int i, int end)
        {
            while (i < end && char.IsWhiteSpace(text[i])) i++;
        }

        private static string ReadWord(string text, ref int i, int end)
        {
            int start = i;
            while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
            return text.Substring(start, i - start);
        }

        private static string ReadVariable(string text, ref int i, int end)
        {
            int start = i;
            if (i < end && text[i] == '@') i++;
            while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '@')) i++;
            return text.Substring(start, i - start);
        }

        private static string ReadIdentifier(string text, ref int i, int end)
        {
            if (i >= end) return null;
            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close < 0 || close >= end) { return null; }
                string name = text.Substring(i + 1, close - i - 1);
                i = close + 1;
                return name;
            }
            int start = i;
            while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '#')) i++;
            return i > start ? text.Substring(start, i - start) : null;
        }
    }
}
