using System;
using System.Globalization;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Normaliza o texto de exibição de células numéricas da grid para forma invariante.
    /// Aceita invariante ("3.14", "1E-08") e pt-BR ("3,14", "1.234,56"). Null se não parsear.
    /// </summary>
    public static class SqlNumberNormalizer
    {
        public static string TryNormalize(string display)
        {
            if (string.IsNullOrEmpty(display))
                return null;

            // sem vírgula: invariante (ponto decimal, expoente permitido p/ float/real)
            if (display.IndexOf(',') < 0)
            {
                if (decimal.TryParse(display, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal invariant))
                    return invariant.ToString(CultureInfo.InvariantCulture);
                // double cobre expoentes além do alcance de decimal
                if (double.TryParse(display, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbl))
                    return dbl.ToString("R", CultureInfo.InvariantCulture);
                return null;
            }

            // com vírgula: pt-BR (vírgula decimal, ponto de milhar)
            if (decimal.TryParse(display, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out decimal ptBr))
                return ptBr.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        public static bool IsNumericClrType(Type type)
            => type == typeof(int) || type == typeof(long) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(decimal) || type == typeof(double) ||
               type == typeof(float);
    }
}
