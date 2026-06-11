using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta função sobre coluna em um lado de uma comparação — id "non-sargable".
    /// Ex.: UPPER(col) = 'X', YEAR(data) = 2020, ISNULL(col, 0) > 0.
    /// </summary>
    public sealed class NonSargableRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "non-sargable";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(BooleanComparisonExpression node)
        {
            if (!IsFunctionOverColumn(node.FirstExpression) &&
                !IsFunctionOverColumn(node.SecondExpression))
                return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Função sobre coluna no predicado impede uso de índice (non-sargable).",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }

        private static bool IsFunctionOverColumn(ScalarExpression expr)
        {
            // FunctionCall (UPPER/LOWER/YEAR/...) or unary cast/convert expressed as
            // FunctionCall — flag when its arguments contain a column reference.
            if (expr is FunctionCall fn)
                return ContainsColumnReference(fn);

            // CAST(col AS ...) / CONVERT(...) parse as CastCall/ConvertCall.
            if (expr is CastCall cast)
                return ContainsColumnReference(cast.Parameter);
            if (expr is ConvertCall conv)
                return ContainsColumnReference(conv.Parameter);

            return false;
        }

        private static bool ContainsColumnReference(TSqlFragment fragment)
        {
            if (fragment == null) return false;
            var finder = new ColumnRefFinder();
            fragment.Accept(finder);
            return finder.Found;
        }

        private sealed class ColumnRefFinder : TSqlFragmentVisitor
        {
            public bool Found { get; private set; }

            public override void Visit(ColumnReferenceExpression node) => Found = true;
        }
    }
}
