using System.Collections.Generic;

namespace SqlBeaver.Metadata
{
    /// <summary>FK com pares de colunas alinhados por índice (FK composta tem N pares).</summary>
    public sealed class ForeignKeyEntry
    {
        public string FromSchema { get; }
        public string FromTable { get; }
        public IReadOnlyList<string> FromColumns { get; }
        public string ToSchema { get; }
        public string ToTable { get; }
        public IReadOnlyList<string> ToColumns { get; }

        public ForeignKeyEntry(
            string fromSchema, string fromTable, IReadOnlyList<string> fromColumns,
            string toSchema, string toTable, IReadOnlyList<string> toColumns)
        {
            FromSchema = fromSchema;
            FromTable = fromTable;
            FromColumns = fromColumns;
            ToSchema = toSchema;
            ToTable = toTable;
            ToColumns = toColumns;
        }
    }
}
