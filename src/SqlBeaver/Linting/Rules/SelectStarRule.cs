using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta SELECT * — id "select-star".
    /// </summary>
    public sealed class SelectStarRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "select-star";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(SelectStarExpression node)
        {
            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Evite SELECT *: liste as colunas explicitamente.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
