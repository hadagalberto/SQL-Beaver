using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta INSERT sem lista de colunas explícita — id "insert-no-columns".
    /// Só é acionada quando há uma origem de valores/select (ou seja, não é um
    /// INSERT ... EXECUTE sem colunas — ainda assim flagado, pois a regra se aplica).
    /// </summary>
    public sealed class InsertWithoutColumnsRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "insert-no-columns";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(InsertStatement node)
        {
            InsertSpecification spec = node.InsertSpecification;
            if (spec == null) return;

            // Only flag when there is a VALUES or SELECT source
            if (spec.InsertSource == null) return;

            if (spec.Columns != null && spec.Columns.Count > 0) return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Liste as colunas no INSERT para não depender da ordem física.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
