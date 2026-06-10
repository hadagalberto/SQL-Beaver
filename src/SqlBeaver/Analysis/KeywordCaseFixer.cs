using System;
using System.Collections.Generic;

namespace SqlBeaver.Analysis
{
    /// <summary>
    /// Decide se a palavra imediatamente antes do separador recém-digitado é uma
    /// keyword T-SQL que deve virar maiúscula. Classe pura, sem dependências de VS.
    /// </summary>
    public static class KeywordCaseFixer
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS",
            "APPLY", "ON", "AND", "OR", "NOT", "NULL", "IS", "IN", "EXISTS", "LIKE", "BETWEEN",
            "GROUP", "BY", "ORDER", "HAVING", "DISTINCT", "TOP", "AS", "UNION", "ALL",
            "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "MERGE", "OUTPUT",
            "CASE", "WHEN", "THEN", "ELSE", "END", "BEGIN", "IF", "WHILE", "RETURN",
            "DECLARE", "EXEC", "EXECUTE", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX",
            "PROCEDURE", "PROC", "FUNCTION", "TRIGGER", "USE", "GO", "WITH", "OVER", "PARTITION",
            "ASC", "DESC",
        };

        /// <summary>
        /// text deve terminar no separador recém-digitado (último char). Retorna true
        /// com o span da palavra a substituir e o texto maiúsculo.
        /// </summary>
        public static bool TryGetReplacement(string text, out int wordStart, out int wordLength, out string replacement)
        {
            wordStart = 0;
            wordLength = 0;
            replacement = null;

            if (text == null || text.Length < 2)
                return false;

            // Contrato autodefensivo: o último char precisa ser o separador recém-digitado.
            // (O Enter pode ser consumido por outro handler — ex.: commit de completion —
            // e aí o texto termina em identificador; agir nesse caso corromperia o texto.)
            char separator = text[text.Length - 1];
            if (!char.IsWhiteSpace(separator) &&
                separator != '(' && separator != ')' && separator != ',' &&
                separator != ';' && separator != '=')
                return false;

            // pula whitespace entre a palavra e o separador (cobre CRLF do Enter)
            int i = text.Length - 2;
            while (i >= 0 && (text[i] == ' ' || text[i] == '\t' || text[i] == '\r' || text[i] == '\n'))
                i--;

            int wordEnd = i;
            while (i >= 0 && IsIdentifierChar(text[i]))
                i--;
            if (wordEnd == i)
                return false; // sem palavra antes do separador

            // parte de identificador qualificado/especial: não mexer
            if (i >= 0 && (text[i] == '.' || text[i] == '[' || text[i] == '"'))
                return false;

            string word = text.Substring(i + 1, wordEnd - i);
            if (word[0] == '@' || word[0] == '#')
                return false;
            if (!Keywords.Contains(word))
                return false;

            string upper = word.ToUpperInvariant();
            if (string.Equals(word, upper, StringComparison.Ordinal))
                return false; // já está maiúscula

            if (SqlContextAnalyzer.IsInsideCommentOrStringAt(text, i + 1))
                return false;

            wordStart = i + 1;
            wordLength = word.Length;
            replacement = upper;
            return true;
        }

        private static bool IsIdentifierChar(char c)
            => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';
    }
}
