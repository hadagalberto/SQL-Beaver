using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta ORDER BY por posição ordinal (1, 2, ...) — id "order-by-ordinal".
    /// </summary>
    public sealed class OrderByOrdinalRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "order-by-ordinal";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(OrderByClause node)
        {
            if (node.OrderByElements == null) return;

            foreach (ExpressionWithSortOrder element in node.OrderByElements)
            {
                if (!(element.Expression is IntegerLiteral)) continue;

                if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                    node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                    return;

                var token = node.ScriptTokenStream[node.FirstTokenIndex];
                _diagnostics.Add(new LintDiagnostic(
                    Id,
                    "ORDER BY por posição (1,2) é frágil; use nomes de coluna.",
                    token.Line,
                    token.Column,
                    token.Text.Length > 0 ? token.Text.Length : 1));
                return; // one diagnostic per ORDER BY clause is enough
            }
        }
    }
}
