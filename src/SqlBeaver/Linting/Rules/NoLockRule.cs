using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta hints NOLOCK e READUNCOMMITTED — id "nolock".
    /// </summary>
    public sealed class NoLockRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "nolock";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(TableHint node)
        {
            if (node.HintKind != TableHintKind.NoLock &&
                node.HintKind != TableHintKind.ReadUncommitted)
                return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "NOLOCK/READUNCOMMITTED pode ler dados não confirmados.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
