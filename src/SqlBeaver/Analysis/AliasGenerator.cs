using System;
using System.Collections.Generic;
using System.Text;

namespace SqlBeaver.Analysis
{
    /// <summary>Gera alias curto para tabela: iniciais PascalCase em minúsculo, sem colidir
    /// com aliases em uso nem com keywords. Puro.</summary>
    public static class AliasGenerator
    {
        public static string Generate(string tableName, ICollection<string> usedAliases)
        {
            string baseAlias = BuildBase(tableName);

            string candidate = baseAlias;
            int n = 2;
            while (SqlKeywords.All.Contains(candidate) || ContainsIgnoreCase(usedAliases, candidate))
            {
                candidate = baseAlias + n;
                n++;
            }
            return candidate;
        }

        private static string BuildBase(string tableName)
        {
            var initials = new StringBuilder();
            int upperCount = 0, letterCount = 0;
            char firstLetter = 't';
            bool hasFirstLetter = false;

            foreach (char c in tableName)
            {
                if (!char.IsLetter(c)) continue;
                letterCount++;
                if (!hasFirstLetter) { firstLetter = c; hasFirstLetter = true; }
                if (char.IsUpper(c)) { upperCount++; initials.Append(char.ToLowerInvariant(c)); }
            }

            // sem maiúsculas (tudo minúsculo) ou tudo maiúsculo: usa a primeira letra
            if (initials.Length == 0 || upperCount == letterCount)
                return hasFirstLetter ? char.ToLowerInvariant(firstLetter).ToString() : "t";

            return initials.ToString();
        }

        private static bool ContainsIgnoreCase(ICollection<string> values, string candidate)
        {
            foreach (string value in values)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
