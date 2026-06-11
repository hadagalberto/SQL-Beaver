using System;
using System.Collections.Generic;

namespace SqlBeaver.Snippets
{
    /// <summary>
    /// Validação pura do shortcut de um snippet de usuário: obrigatório e único
    /// (case-insensitive, com trim) entre os shortcuts já existentes.
    /// </summary>
    public static class SnippetShortcutValidator
    {
        /// <summary>True quando o shortcut, após trim, é não-vazio e não colide
        /// (case-insensitive) com nenhum dos existentes.</summary>
        public static bool IsValidUnique(string shortcut, IEnumerable<string> existingShortcuts)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
                return false;

            string candidate = shortcut.Trim();
            if (candidate.Length == 0)
                return false;

            if (existingShortcuts != null)
            {
                foreach (string existing in existingShortcuts)
                {
                    if (existing == null) continue;
                    if (string.Equals(existing.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            return true;
        }
    }
}
