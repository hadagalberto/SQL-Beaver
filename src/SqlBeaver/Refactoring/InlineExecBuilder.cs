using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Builds the replacement text that inlines an EXEC call: a comment header, one
    /// DECLARE per procedure parameter (value taken from the call argument mapped by name
    /// then position, falling back to the parameter default), and the procedure body.
    /// Pure, no VS dependencies.
    /// </summary>
    public static class InlineExecBuilder
    {
        public static string Build(ExecCall call, ProcBody proc)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (proc == null) throw new ArgumentNullException(nameof(proc));

            string nl = "\r\n";
            var sb = new StringBuilder();

            string qualified = string.IsNullOrEmpty(call.Schema) ? call.Proc : call.Schema + "." + call.Proc;
            sb.Append("-- inline de ").Append(qualified).Append(nl);

            if (proc.ContainsReturnWithValue)
                sb.Append("-- aviso: a proc tinha RETURN com valor; revise o fluxo").Append(nl);

            // Split call args into named and positional.
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positional = new List<string>();
            foreach (ExecArg a in call.Args)
            {
                if (a.Name != null) named[a.Name] = a.ValueText;
                else positional.Add(a.ValueText);
            }

            int posIndex = 0;
            foreach (ProcParameter p in proc.Parameters)
            {
                string type = string.IsNullOrEmpty(p.Type) ? "sql_variant" : p.Type;
                string value;

                if (p.Name != null && named.TryGetValue(p.Name, out string namedVal))
                {
                    value = namedVal;
                }
                else if (named.Count == 0 && posIndex < positional.Count)
                {
                    value = positional[posIndex++];
                }
                else if (!string.IsNullOrEmpty(p.DefaultOrNull))
                {
                    value = p.DefaultOrNull;
                }
                else
                {
                    value = "NULL /* sem argumento */";
                }

                sb.Append("DECLARE ").Append(p.Name).Append(' ').Append(type)
                  .Append(" = ").Append(value).Append(';').Append(nl);
            }

            if (!string.IsNullOrEmpty(proc.BodyText))
                sb.Append(proc.BodyText);

            return sb.ToString();
        }
    }
}
