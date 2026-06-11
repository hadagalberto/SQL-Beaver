using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta os tipos obsoletos TEXT/NTEXT/IMAGE — id "deprecated-types".
    /// </summary>
    public sealed class DeprecatedTypesRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "deprecated-types";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(SqlDataTypeReference node)
        {
            if (node.SqlDataTypeOption != SqlDataTypeOption.Text &&
                node.SqlDataTypeOption != SqlDataTypeOption.NText &&
                node.SqlDataTypeOption != SqlDataTypeOption.Image)
                return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Tipo obsoleto: use VARCHAR(MAX)/NVARCHAR(MAX)/VARBINARY(MAX).",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
