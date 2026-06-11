using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta comparações = NULL / &lt;&gt; NULL — id "null-comparison".
    /// </summary>
    public sealed class NullComparisonRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "null-comparison";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(BooleanComparisonExpression node)
        {
            if (node.ComparisonType != BooleanComparisonType.Equals &&
                node.ComparisonType != BooleanComparisonType.NotEqualToBrackets &&
                node.ComparisonType != BooleanComparisonType.NotEqualToExclamation)
                return;

            bool hasNull = node.FirstExpression is NullLiteral ||
                           node.SecondExpression is NullLiteral;
            if (!hasNull) return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Use IS NULL / IS NOT NULL em vez de = NULL.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
