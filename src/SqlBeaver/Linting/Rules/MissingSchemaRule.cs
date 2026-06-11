using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta referências a tabelas sem qualificação de schema — id "missing-schema".
    /// Tabelas temporárias (#) e variáveis de tabela (@) são ignoradas.
    /// </summary>
    public sealed class MissingSchemaRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "missing-schema";

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(NamedTableReference node)
        {
            SchemaObjectName obj = node.SchemaObject;
            if (obj == null) return;

            // Only flag if there is NO schema qualifier
            if (obj.SchemaIdentifier != null) return;

            Identifier baseId = obj.BaseIdentifier;
            if (baseId == null) return;

            string name = baseId.Value ?? string.Empty;

            // Skip temp tables (#) and table variables (@)
            if (name.StartsWith("#") || name.StartsWith("@"))
                return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "Qualifique a tabela com o schema (ex.: dbo.X).",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }
    }
}
