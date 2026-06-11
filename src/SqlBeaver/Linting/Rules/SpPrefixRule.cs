using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta CREATE PROCEDURE com prefixo sp_ — id "sp-prefix".
    /// </summary>
    public sealed class SpPrefixRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "sp-prefix";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(CreateProcedureStatement node)
        {
            ProcedureReference procRef = node.ProcedureReference;
            SchemaObjectName name = procRef?.Name;
            Identifier baseId = name?.BaseIdentifier;
            string procName = baseId?.Value ?? string.Empty;

            if (!procName.StartsWith("sp_", StringComparison.OrdinalIgnoreCase))
                return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Evite o prefixo sp_ em procedures (conflita com o catálogo do sistema).",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
