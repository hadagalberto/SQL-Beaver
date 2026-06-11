using System.Collections.Generic;

namespace SqlBeaver.Metadata
{
    public sealed class TableEntry
    {
        public string Schema { get; }
        public string Name { get; }

        public TableEntry(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }
    }

    public sealed class DbMetadata
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> EmptyColumns =
            new Dictionary<string, IReadOnlyList<ColumnEntry>>();
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> EmptyForeignKeys =
            new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>();

        public IReadOnlyList<string> Schemas { get; }
        public IReadOnlyList<TableEntry> Tables { get; }
        /// <summary>Chave: TableKey(schema, tabela). Comparador OrdinalIgnoreCase.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> ColumnsByTable { get; }
        /// <summary>FKs indexadas nas DUAS pontas (a mesma entrada aparece na chave From e na To).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> ForeignKeysByTable { get; }

        public DbMetadata(IReadOnlyList<string> schemas, IReadOnlyList<TableEntry> tables)
            : this(schemas, tables, EmptyColumns, EmptyForeignKeys)
        {
        }

        public DbMetadata(
            IReadOnlyList<string> schemas,
            IReadOnlyList<TableEntry> tables,
            IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> columnsByTable,
            IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> foreignKeysByTable)
        {
            Schemas = schemas;
            Tables = tables;
            ColumnsByTable = columnsByTable;
            ForeignKeysByTable = foreignKeysByTable;
        }

        public static string TableKey(string schema, string table) => schema + "." + table;
    }
}
