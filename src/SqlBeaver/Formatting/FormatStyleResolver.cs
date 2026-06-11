using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SqlBeaver.Formatting
{
    /// <summary>
    /// Lógica pura (sem I/O) para resolução do estilo de formatação ativo.
    /// Testável sem dependências de sistema de arquivos.
    /// </summary>
    public static class FormatStyleResolver
    {
        /// <summary>
        /// Dado os nomes de estilo disponíveis e o nome ativo pedido, decide o estilo efetivo:
        /// o ativo se existir (OrdinalIgnoreCase); senão o primeiro em ordem; null se não há nenhum
        /// (o chamador cai no default embutido).
        /// </summary>
        public static string ResolveActive(IReadOnlyList<string> availableStyles, string requestedActive)
        {
            if (availableStyles == null || availableStyles.Count == 0)
                return null;

            if (!string.IsNullOrEmpty(requestedActive))
            {
                foreach (string s in availableStyles)
                {
                    if (string.Equals(s, requestedActive, StringComparison.OrdinalIgnoreCase))
                        return s;
                }
            }

            // active not found — fall back to first
            return availableStyles[0];
        }

        /// <summary>
        /// Retorna um nome de arquivo seguro para um estilo (sanitiza chars inválidos do sistema
        /// de arquivos). null ou vazio → null.
        /// </summary>
        public static string ToFileName(string styleName)
        {
            if (string.IsNullOrEmpty(styleName))
                return null;

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(styleName.Length);
            foreach (char c in styleName)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? null : result + ".json";
        }

        /// <summary>Remove a extensão .json de um nome de arquivo para obter o nome do estilo.</summary>
        public static string FromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - 5);

            return fileName;
        }
    }
}
