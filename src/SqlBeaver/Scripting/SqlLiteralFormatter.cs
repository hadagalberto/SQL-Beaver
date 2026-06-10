using System;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Converte o texto de exibição de uma célula da grid em literal SQL,
    /// guiado pelo tipo CLR da coluna (matriz de tipos do AxialSqlTools, Apache-2.0).
    /// </summary>
    public static class SqlLiteralFormatter
    {
        public static string Format(string displayValue, Type clrType)
        {
            if (displayValue == null || displayValue == "NULL")
                return "NULL";

            if (clrType == typeof(bool))
            {
                if (displayValue == "1" || string.Equals(displayValue, "true", StringComparison.OrdinalIgnoreCase))
                    return "1";
                return "0";
            }

            if (SqlNumberNormalizer.IsNumericClrType(clrType))
            {
                string normalized = SqlNumberNormalizer.TryNormalize(displayValue);
                return normalized ?? QuoteString(displayValue);
            }

            if (clrType == typeof(Guid))
                return "'" + displayValue.Replace("'", "''") + "'";

            if (clrType == typeof(byte[]) &&
                displayValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return displayValue;

            return QuoteString(displayValue);
        }

        private static string QuoteString(string value)
            => "N'" + value.Replace("'", "''") + "'";
    }
}
