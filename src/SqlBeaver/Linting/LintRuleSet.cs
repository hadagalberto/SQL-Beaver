using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting
{
    /// <summary>
    /// Conjunto de regras de lint. Executa todas as regras habilitadas sobre o fragmento
    /// fornecido e agrega os diagnósticos.
    /// </summary>
    public sealed class LintRuleSet
    {
        private readonly IReadOnlyList<ISqlLintRule> _rules;

        public LintRuleSet(IReadOnlyList<ISqlLintRule> rules)
        {
            _rules = rules ?? new List<ISqlLintRule>();
        }

        /// <summary>
        /// Executa as regras habilitadas (não presentes em <paramref name="disabledRuleIds"/>)
        /// sobre <paramref name="fragment"/> e retorna todos os diagnósticos.
        /// </summary>
        public IReadOnlyList<LintDiagnostic> Inspect(
            TSqlFragment fragment,
            IReadOnlyCollection<string> disabledRuleIds)
        {
            var result = new List<LintDiagnostic>();
            if (fragment == null) return result;

            foreach (ISqlLintRule rule in _rules)
            {
                if (disabledRuleIds != null && IsDisabled(rule.Id, disabledRuleIds))
                    continue;

                foreach (LintDiagnostic d in rule.Inspect(fragment))
                    result.Add(d);
            }

            return result;
        }

        private static bool IsDisabled(string ruleId, IReadOnlyCollection<string> disabledIds)
        {
            foreach (string id in disabledIds)
            {
                if (string.Equals(id, ruleId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Cria um <see cref="LintRuleSet"/> com as cinco regras padrão.
        /// </summary>
        public static LintRuleSet CreateDefault()
        {
            return new LintRuleSet(new ISqlLintRule[]
            {
                new Rules.SelectStarRule(),
                new Rules.MissingSchemaRule(),
                new Rules.NoLockRule(),
                new Rules.InsertWithoutColumnsRule(),
                new Rules.JoinWithoutOnRule(),
            });
        }
    }
}
