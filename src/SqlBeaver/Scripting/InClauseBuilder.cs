using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Constrói a cláusula IN a partir de valores selecionados na grid.
    /// Remove duplicatas (preservando ordem), pula NULLs e quebra linhas a cada 10 valores.
    /// </summary>
    public static class InClauseBuilder
    {
        private const int ValuesPerLine = 10;

        public static string Build(IEnumerable<string> displayValues, Type clrType)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var literals = new List<string>();

            foreach (var val in displayValues)
            {
                if (val == null || val == "NULL")
                    continue;
                if (!seen.Add(val))
                    continue;
                literals.Add(SqlLiteralFormatter.Format(val, clrType));
            }

            if (literals.Count == 0)
                return "()";

            var sb = new StringBuilder();
            sb.Append('(');
            for (int i = 0; i < literals.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                    // line break after every ValuesPerLine items (after item at index ValuesPerLine-1, 2*ValuesPerLine-1, ...)
                    if (i % ValuesPerLine == 0)
                        sb.Append("\r\n");
                }
                sb.Append(literals[i]);
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}
