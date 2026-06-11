using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Gera o texto de inserção para "INSERT completo": lista de colunas + VALUES alinhado,
    /// com hint de coluna como comentário. Estratégia (b): texto plano, sem navegação de
    /// placeholder — seguro, sem tocar no pipeline de completion.
    /// Cap de 30 colunas; excesso vira "/* +N colunas */".
    /// </summary>
    public static class InsertFillBuilder
    {
        public const int MaxColumns = 30;

        /// <param name="tableDisplay">Texto qualificado da tabela, ex.: "[dbo].[Pedidos]".</param>
        /// <param name="columns">Lista de colunas na ordem do catálogo.</param>
        public static string Build(string tableDisplay, IReadOnlyList<ColumnEntry> columns)
        {
            if (tableDisplay == null) throw new ArgumentNullException(nameof(tableDisplay));
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            int shown = Math.Min(columns.Count, MaxColumns);
            int extra = columns.Count - shown;

            var colList = new StringBuilder();
            var valList = new StringBuilder();

            for (int i = 0; i < shown; i++)
            {
                string bracketedName = SqlIdentifier.Bracket(columns[i].Name);
                if (i > 0)
                {
                    colList.Append(", ");
                    valList.Append(",\r\n    ");
                }
                colList.Append(bracketedName);
                valList.Append("/* ").Append(columns[i].Name).Append(" */ ");
            }

            if (extra > 0)
            {
                if (shown > 0)
                {
                    colList.Append(", ");
                    valList.Append(",\r\n    ");
                }
                colList.Append("/* +").Append(extra).Append(" colunas */");
                valList.Append("/* +").Append(extra).Append(" colunas */");
            }

            var sb = new StringBuilder();
            sb.Append(tableDisplay).Append(" (").Append(colList).Append(")");
            sb.Append("\r\nVALUES (\r\n    ").Append(valList).Append("\r\n)");
            return sb.ToString();
        }
    }
}
