using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta EXEC('...') / EXEC(@sql) de string dinâmica — id "exec-string".
    /// </summary>
    public sealed class ExecStringRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "exec-string";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(ExecuteStatement node)
        {
            ExecuteSpecification spec = node.ExecuteSpecification;
            if (spec == null) return;
            if (!(spec.ExecutableEntity is ExecutableStringList)) return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "EXEC de string dinâmica: risco de injeção; prefira sp_executesql com parâmetros.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
