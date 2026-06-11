using System;
using System.Text.RegularExpressions;

namespace SqlBeaver.Environments
{
    /// <summary>Glob simples (* e ?) case-insensitive. Puro.</summary>
    public static class WildcardMatcher
    {
        /// <summary>
        /// Retorna true se <paramref name="value"/> casa com o glob <paramref name="pattern"/>.
        /// Suporta * (zero ou mais chars) e ? (exatamente um char), case-insensitive.
        /// Null em qualquer argumento retorna false; pattern "*" casa inclusive string vazia.
        /// </summary>
        public static bool IsMatch(string value, string pattern)
        {
            if (value == null || pattern == null)
                return false;

            // Traduz o glob para regex: escapa o padrão, depois restaura * e ?
            string regexPattern = "^" +
                Regex.Escape(pattern)
                     .Replace(@"\*", ".*")
                     .Replace(@"\?", ".") +
                "$";

            return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
    }
}
