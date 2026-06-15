using System;
using System.Collections.Generic;
using System.Text;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;

namespace SqlBeaver.Scripting
{
    /// <summary>
    /// Dado um conjunto de (TableRef, ColumnEntry) selecionados pelo usuário, gera
    /// a lista de colunas qualificadas e entre colchetes, separadas por vírgula.
    /// Quando o escopo tem mais de uma tabela, qualifica pelo alias (ou nome da tabela).
    /// </summary>
    public static class ColumnListBuilder
    {
        /// <param name="selected">Lista de (alias-ou-nome-da-tabela, coluna) na ordem da seleção.</param>
        /// <param name="multiTable">Quando true, qualifica com alias.</param>
        /// <param name="continuationIndent">
        /// Quando não-nulo/não-vazio, cada coluna fica na própria linha no estilo trailing-comma
        /// (<c>col1,\n{indent}col2,\n{indent}col3</c>), com as linhas de continuação prefixadas
        /// por <paramref name="continuationIndent"/> para alinhar sob a primeira coluna.
        /// Quando nulo/vazio, mantém o comportamento de linha única separada por <c>", "</c>.
        /// </param>
        public static string Build(
            IReadOnlyList<(string Qualifier, ColumnEntry Column)> selected,
            bool multiTable,
            string continuationIndent = null)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));

            bool multiLine = !string.IsNullOrEmpty(continuationIndent);

            var sb = new StringBuilder();
            for (int i = 0; i < selected.Count; i++)
            {
                if (i > 0)
                {
                    if (multiLine) sb.Append(",\n").Append(continuationIndent);
                    else sb.Append(", ");
                }
                var (qualifier, col) = selected[i];
                if (multiTable && !string.IsNullOrEmpty(qualifier))
                    sb.Append(SqlIdentifier.Bracket(qualifier)).Append('.').Append(SqlIdentifier.Bracket(col.Name));
                else
                    sb.Append(SqlIdentifier.Bracket(col.Name));
            }
            return sb.ToString();
        }
    }
}
