using System;
using System.Globalization;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Converte o texto de exibição de uma célula da grid em literal SQL,
    /// guiado pelo tipo CLR da coluna (matriz de tipos do AxialSqlTools, Apache-2.0).
    /// </summary>
    public static class SqlLiteralFormatter
    {
        // pt-BR: vírgula como separador decimal, ponto como separador de milhar.
        private static readonly CultureInfo PtBr = new CultureInfo("pt-BR");

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

            if (IsNumeric(clrType))
                return FormatNumeric(displayValue);

            if (clrType == typeof(Guid))
                return "'" + displayValue.Replace("'", "''") + "'";

            if (clrType == typeof(byte[]) &&
                displayValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return displayValue;

            return QuoteString(displayValue);
        }

        private static string FormatNumeric(string displayValue)
        {
            // Strategy: determine the decimal separator in use so we parse correctly.
            //
            // Case 1: string contains '.' but no ','  → invariant culture (e.g. "3.14", "1000.5")
            // Case 2: string contains ',' but no '.'  → comma is the decimal separator (pt-BR); parse with pt-BR (e.g. "3,14" → 3.14)
            // Case 3: string contains both '.' and ',' → pt-BR format "1.234,56" (dot=group, comma=decimal) → parse with pt-BR
            // Case 4: neither → plain integer; parse invariant (e.g. "42", "-7")

            bool hasDot = displayValue.IndexOf('.') >= 0;
            bool hasComma = displayValue.IndexOf(',') >= 0;

            CultureInfo culture;
            if (hasComma)
                culture = PtBr;   // covers "3,14" and "1.234,56"
            else
                culture = CultureInfo.InvariantCulture;  // covers "3.14", "42", "-7"

            if (decimal.TryParse(displayValue, NumberStyles.Number, culture, out decimal parsed))
                return parsed.ToString(CultureInfo.InvariantCulture);

            // fallback — shouldn't normally happen but satisfies the "abc" → N'abc' test
            return QuoteString(displayValue);
        }

        private static bool IsNumeric(Type type)
            => type == typeof(int) || type == typeof(long) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(decimal) || type == typeof(double) ||
               type == typeof(float);

        private static string QuoteString(string value)
            => "N'" + value.Replace("'", "''") + "'";
    }
}
