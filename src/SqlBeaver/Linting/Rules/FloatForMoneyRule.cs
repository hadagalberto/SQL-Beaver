using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting.Rules
{
    /// <summary>
    /// Detecta colunas/variáveis FLOAT/REAL com nome de contexto monetário — id "float-for-money".
    /// Heurística de nome: contém valor/preco/price/amount/total (case-insensitive).
    /// </summary>
    public sealed class FloatForMoneyRule : TSqlFragmentVisitor, ISqlLintRule
    {
        public string Id => "float-for-money";

        private static readonly string[] MoneyTokens =
            { "valor", "preco", "price", "amount", "total" };

        private readonly List<LintDiagnostic> _diagnostics = new List<LintDiagnostic>();

        public IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment)
        {
            _diagnostics.Clear();
            fragment.Accept(this);
            return _diagnostics.ToArray();
        }

        public override void Visit(ColumnDefinition node)
        {
            string name = node.ColumnIdentifier?.Value;
            Flag(name, node.DataType, node);
        }

        public override void Visit(DeclareVariableElement node)
        {
            string name = node.VariableName?.Value;
            Flag(name, node.DataType, node);
        }

        private void Flag(string name, DataTypeReference dataType, TSqlFragment node)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!IsMoneyName(name)) return;
            if (!IsFloatOrReal(dataType)) return;

            if (node.ScriptTokenStream == null || node.FirstTokenIndex < 0 ||
                node.FirstTokenIndex >= node.ScriptTokenStream.Count)
                return;

            var token = node.ScriptTokenStream[node.FirstTokenIndex];
            _diagnostics.Add(new LintDiagnostic(
                Id,
                "FLOAT/REAL para valor monetário causa imprecisão; use DECIMAL.",
                token.Line,
                token.Column,
                token.Text.Length > 0 ? token.Text.Length : 1));
        }

        private static bool IsMoneyName(string name)
        {
            string lower = name.ToLowerInvariant();
            foreach (string t in MoneyTokens)
                if (lower.Contains(t)) return true;
            return false;
        }

        private static bool IsFloatOrReal(DataTypeReference dataType)
        {
            return dataType is SqlDataTypeReference sql &&
                   (sql.SqlDataTypeOption == SqlDataTypeOption.Float ||
                    sql.SqlDataTypeOption == SqlDataTypeOption.Real);
        }
    }
}
