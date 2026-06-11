using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta QualifiedJoin (INNER/LEFT/RIGHT/FULL JOIN) sem cláusula ON — id "join-no-on".
    /// CROSS JOIN não é um QualifiedJoin, logo não é flagado.
    /// </summary>
    public sealed class JoinWithoutOnRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "join-no-on";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(QualifiedJoin node)
        {
            if (node.SearchCondition != null) return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "JOIN sem ON.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
