using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta DISTINCT redundante junto de GROUP BY — id "distinct-with-group-by".
    /// </summary>
    public sealed class DistinctWithGroupByRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "distinct-with-group-by";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(QuerySpecification node)
        {
            if (node.UniqueRowFilter != UniqueRowFilter.Distinct) return;
            if (node.GroupByClause == null) return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "DISTINCT com GROUP BY é redundante.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
