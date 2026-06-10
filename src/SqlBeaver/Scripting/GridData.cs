using System;
using System.Collections.Generic;

namespace SqlBeaver.Scripting
{
    public sealed class GridColumn
    {
        public string Name { get; }
        /// <summary>Tipo CLR da coluna; null quando o schema não pôde ser lido.</summary>
        public Type ClrType { get; }

        public GridColumn(string name, Type clrType)
        {
            Name = name;
            ClrType = clrType;
        }
    }

    /// <summary>Snapshot puro da grid de resultados: células como strings de exibição ("NULL" = SQL NULL).</summary>
    public sealed class GridData
    {
        public IReadOnlyList<GridColumn> Columns { get; }
        public IReadOnlyList<string[]> Rows { get; }

        public GridData(IReadOnlyList<GridColumn> columns, IReadOnlyList<string[]> rows)
        {
            Columns = columns;
            Rows = rows;
        }
    }
}
