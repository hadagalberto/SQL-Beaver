using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Refactoring
{
    /// <summary>One parameter of a CREATE PROCEDURE.</summary>
    public sealed class ProcParameter
    {
        /// <summary>Name including leading '@'.</summary>
        public string Name { get; }
        /// <summary>SQL type text, e.g. "int", "varchar(50)". Null when undeterminable.</summary>
        public string Type { get; }
        /// <summary>Default value text, or null.</summary>
        public string DefaultOrNull { get; }
        public bool IsOutput { get; }

        public ProcParameter(string name, string type, string defaultOrNull, bool isOutput)
        {
            Name = name;
            Type = type;
            DefaultOrNull = defaultOrNull;
            IsOutput = isOutput;
        }
    }

    /// <summary>Result of extracting a procedure's parameters and body from its CREATE script.</summary>
    public sealed class ProcBody
    {
        public IReadOnlyList<ProcParameter> Parameters { get; }
        /// <summary>Text of the statement list after AS. Empty when undeterminable.</summary>
        public string BodyText { get; }
        public bool ContainsReturnWithValue { get; }

        public ProcBody(IReadOnlyList<ProcParameter> parameters, string bodyText, bool containsReturnWithValue)
        {
            Parameters = parameters;
            BodyText = bodyText;
            ContainsReturnWithValue = containsReturnWithValue;
        }
    }

    /// <summary>
    /// Extracts parameters and body from a CREATE PROCEDURE script using ScriptDom.
    /// Pure, no VS dependencies. Returns an empty body (and no params) when the script
    /// can't be parsed (e.g. encrypted/unavailable definition).
    /// </summary>
    public static class ProcBodyExtractor
    {
        public static ProcBody Extract(string createScript)
        {
            var empty = new ProcBody(new List<ProcParameter>(), string.Empty, false);
            if (string.IsNullOrWhiteSpace(createScript)) return empty;

            TSqlFragment fragment;
            IList<ParseError> errors;
            try
            {
                var parser = new TSql160Parser(true);
                using (var reader = new StringReader(createScript))
                    fragment = parser.Parse(reader, out errors);
            }
            catch
            {
                return empty;
            }
            if (fragment == null) return empty;

            var visitor = new ProcVisitor();
            fragment.Accept(visitor);
            if (visitor.Proc == null) return empty;

            IList<TSqlParserToken> tokens = fragment.ScriptTokenStream;
            var parameters = new List<ProcParameter>();
            foreach (ProcedureParameter p in visitor.Proc.Parameters)
            {
                string name = p.VariableName != null ? p.VariableName.Value : null;
                string type = TokensText(tokens, p.DataType);
                string def = p.Value != null ? TokensText(tokens, p.Value) : null;
                bool isOutput = p.Modifier == ParameterModifier.Output;
                parameters.Add(new ProcParameter(name, type, def, isOutput));
            }

            string body = string.Empty;
            bool returnWithValue = false;
            StatementList list = visitor.Proc.StatementList;
            if (list != null && list.Statements != null && list.Statements.Count > 0)
            {
                body = StatementsText(createScript, tokens, list);
                var rv = new ReturnVisitor();
                list.Accept(rv);
                returnWithValue = rv.HasReturnWithValue;
            }

            return new ProcBody(parameters, body, returnWithValue);
        }

        private static string TokensText(IList<TSqlParserToken> tokens, TSqlFragment frag)
        {
            if (frag == null || tokens == null) return null;
            if (frag.FirstTokenIndex < 0 || frag.LastTokenIndex < 0) return null;
            var sb = new StringBuilder();
            for (int i = frag.FirstTokenIndex; i <= frag.LastTokenIndex && i < tokens.Count; i++)
                sb.Append(tokens[i].Text);
            return sb.ToString().Trim();
        }

        private static string StatementsText(string source, IList<TSqlParserToken> tokens, StatementList list)
        {
            int firstIdx = list.Statements[0].FirstTokenIndex;
            int lastIdx = list.Statements[list.Statements.Count - 1].LastTokenIndex;
            if (firstIdx < 0 || lastIdx < 0 || firstIdx >= tokens.Count) return string.Empty;

            int startOffset = tokens[firstIdx].Offset;
            int endIdx = Math.Min(lastIdx, tokens.Count - 1);
            int endOffset = tokens[endIdx].Offset + (tokens[endIdx].Text != null ? tokens[endIdx].Text.Length : 0);
            if (startOffset < 0 || endOffset > source.Length || endOffset <= startOffset) return string.Empty;

            return source.Substring(startOffset, endOffset - startOffset).Trim();
        }

        private sealed class ProcVisitor : TSqlFragmentVisitor
        {
            public CreateProcedureStatement Proc;
            public override void ExplicitVisit(CreateProcedureStatement node)
            {
                if (Proc == null) Proc = node;
            }
            public override void ExplicitVisit(CreateOrAlterProcedureStatement node)
            {
                if (Proc == null)
                {
                    // CreateOrAlter shares the same shape; not assignable, so skip.
                    base.ExplicitVisit(node);
                }
            }
        }

        private sealed class ReturnVisitor : TSqlFragmentVisitor
        {
            public bool HasReturnWithValue;
            public override void ExplicitVisit(ReturnStatement node)
            {
                if (node.Expression != null) HasReturnWithValue = true;
            }
        }
    }
}
