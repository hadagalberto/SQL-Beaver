using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta CREATE PROCEDURE cujo primeiro statement não é SET NOCOUNT ON — id "nocount-missing".
    /// </summary>
    public sealed class NocountMissingRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "nocount-missing";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(CreateProcedureStatement node)
        {
            StatementList body = node.StatementList;
            if (body == null || body.Statements == null || body.Statements.Count == 0)
                return; // empty body — nothing to advise

            TSqlStatement first = body.Statements[0];

            if (IsSetNocountOn(first))
                return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Considere SET NOCOUNT ON no início da procedure.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }

        private static bool IsSetNocountOn(TSqlStatement stmt)
        {
            if (stmt is PredicateSetStatement set)
            {
                return set.IsOn && (set.Options & SetOptions.NoCount) == SetOptions.NoCount;
            }
            return false;
        }
    }
}
