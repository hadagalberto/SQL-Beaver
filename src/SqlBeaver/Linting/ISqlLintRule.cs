using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlBeaver.Linting
{
    /// <summary>
    /// Regra de lint pura: recebe o fragmento já parseado (sem erros de sintaxe)
    /// e devolve os diagnósticos encontrados.
    /// </summary>
    public interface ISqlLintRule
    {
        string Id { get; }

        IEnumerable<LintDiagnostic> Inspect(TSqlFragment fragment);
    }
}
