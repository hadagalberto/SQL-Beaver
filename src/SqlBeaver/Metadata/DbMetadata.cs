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
        public IReadOnlyList<string> Schemas { get; }
        public IReadOnlyList<TableEntry> Tables { get; }

        public DbMetadata(IReadOnlyList<string> schemas, IReadOnlyList<TableEntry> tables)
        {
            Schemas = schemas;
            Tables = tables;
        }
    }
}
