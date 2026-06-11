using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta VARCHAR/NVARCHAR/CHAR/NCHAR sem tamanho — id "varchar-no-length".
    /// </summary>
    public sealed class VarcharNoLengthRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "varchar-no-length";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(SqlDataTypeReference node)
        {
            if (node.SqlDataTypeOption != SqlDataTypeOption.VarChar &&
                node.SqlDataTypeOption != SqlDataTypeOption.NVarChar &&
                node.SqlDataTypeOption != SqlDataTypeOption.Char &&
                node.SqlDataTypeOption != SqlDataTypeOption.NChar)
                return;

            // Empty Parameters list = no length specified (e.g. just VARCHAR).
            if (node.Parameters != null && node.Parameters.Count > 0)
                return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Especifique o tamanho do VARCHAR/CHAR (evita o default 1/30).",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
