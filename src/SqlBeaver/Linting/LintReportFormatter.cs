using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SqlBeaver.Linting
{
    /// <summary>
    /// Formata uma lista de diagnósticos de lint como um relatório em comentários SQL,
    /// agrupado por RuleId e ordenado alfabeticamente pelo id. Puro/testável.
    /// </summary>
    public static class LintReportFormatter
    {
        public static string Format(IReadOnlyList<LintDiagnostic> diagnostics)
        {
            var sb = new StringBuilder();

            if (diagnostics == null || diagnostics.Count == 0)
            {
                sb.AppendLine("/* SQL Beaver — Análise de código");
                sb.AppendLine("   nenhum aviso encontrado");
                sb.AppendLine("   ============================ */");
                return sb.ToString();
            }

            var groups = diagnostics
                .GroupBy(d => d.RuleId)
                .OrderBy(g => g.Key, System.StringComparer.Ordinal)
                .ToList();

            int totalWarnings = diagnostics.Count;
            int ruleCount = groups.Count;

            sb.AppendLine("/* SQL Beaver — Análise de código");
            sb.AppendLine("   " + totalWarnings + " aviso(s) em " + ruleCount + " regra(s)");
            sb.AppendLine("   ============================ */");

            foreach (var group in groups)
            {
                var items = group.OrderBy(d => d.Line).ToList();
                sb.AppendLine("-- [" + group.Key + "] " + items.Count + " ocorrência(s)");
                foreach (LintDiagnostic d in items)
                {
                    sb.AppendLine("--   linha " + d.Line + ": " + d.Message);
                }
            }

            return sb.ToString();
        }
    }
}
