using System;

namespace SqlBeaver.Usage
{
    /// <summary>
    /// Utilitários puros de ranking por uso: chave de par canônica e texto de ordenação com bucket.
    /// </summary>
    public static class UsageRanker
    {
        /// <summary>
        /// Par não-ordenado canônico: o menor (OrdinalIgnoreCase) vem primeiro, unidos por "+".
        /// Ex.: PairKey("dbo.Titulos", "dbo.Pessoas") == PairKey("dbo.Pessoas", "dbo.Titulos").
        /// </summary>
        public static string PairKey(string tableKeyA, string tableKeyB)
        {
            if (string.Compare(tableKeyA, tableKeyB, StringComparison.OrdinalIgnoreCase) <= 0)
                return tableKeyA + "+" + tableKeyB;
            return tableKeyB + "+" + tableKeyA;
        }

        /// <summary>
        /// Retorna o sortText com bucket de uso para uso em listas de autocompletar:
        /// <list type="bullet">
        ///   <item>Usados (count &gt; 0): "1_{999999-count:D6}_{name}" — mais usados primeiro.</item>
        ///   <item>Não usados (count == 0): "5_{name}" — ordem alfabética pelo nome.</item>
        /// </list>
        /// </summary>
        public static string TableSortText(int usageCount, string name)
        {
            if (usageCount > 0)
            {
                int inverse = Math.Max(0, 999999 - usageCount);
                return "1_" + inverse.ToString("D6") + "_" + name;
            }
            return "5_" + name;
        }
    }
}
