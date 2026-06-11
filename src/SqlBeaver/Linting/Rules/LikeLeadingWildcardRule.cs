using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta LIKE com '%' no início do padrão — id "like-leading-wildcard".
    /// </summary>
    public sealed class LikeLeadingWildcardRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "like-leading-wildcard";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(LikePredicate node)
        {
            if (!(node.SecondExpression is StringLiteral pattern)) return;
            string value = pattern.Value ?? string.Empty;
            if (!value.StartsWith("%")) return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "LIKE com '%' no início impede uso de índice.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
