using System.Collections.Generic;
using System.Text;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    /// <summary>Gera o texto de inserção ao aceitar uma procedure em contexto EXEC:
    /// nome qualificado + parâmetros nomeados. OUTPUT marcado. Sem parâmetros → só o nome.</summary>
    public static class ProcCallBuilder
    {
        public static string BuildExecInsertText(
            string schema,
            string proc,
            IReadOnlyList<ParameterEntry> parameters)
        {
            string qualifiedName = SqlIdentifier.Bracket(schema) + "." + SqlIdentifier.Bracket(proc);

            if (parameters == null || parameters.Count == 0)
                return qualifiedName;

            var sb = new StringBuilder();
            sb.Append(qualifiedName);
            sb.Append(" ");

            for (int i = 0; i < parameters.Count; i++)
            {
                ParameterEntry p = parameters[i];
                if (i > 0)
                    sb.Append(", ");
                sb.Append(p.Name);
                sb.Append(" = ");
                if (p.IsOutput)
                    sb.Append(" OUTPUT");
            }

            return sb.ToString();
        }
    }
}
