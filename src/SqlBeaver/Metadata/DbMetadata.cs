using System;
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
        private static readonly IReadOnlyList<ObjectEntry> EmptyObjects =
            new ObjectEntry[0];

        public IReadOnlyList<string> Schemas { get; }
        public IReadOnlyList<TableEntry> Tables { get; }
        /// <summary>Chave: TableKey(schema, tabela). Comparador OrdinalIgnoreCase.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> ColumnsByTable { get; }
        /// <summary>FKs indexadas nas DUAS pontas (a mesma entrada aparece na chave From e na To).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> ForeignKeysByTable { get; }
        /// <summary>Objetos do banco (procedures, views, functions) excluindo tabelas e sys.</summary>
        public IReadOnlyList<ObjectEntry> Objects { get; }

        public DbMetadata(IReadOnlyList<string> schemas, IReadOnlyList<TableEntry> tables)
            : this(schemas, tables, EmptyColumns, EmptyForeignKeys)
        {
        }

        public DbMetadata(
            IReadOnlyList<string> schemas,
            IReadOnlyList<TableEntry> tables,
            IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> columnsByTable,
            IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> foreignKeysByTable)
            : this(schemas, tables, columnsByTable, foreignKeysByTable, EmptyObjects)
        {
        }

        public DbMetadata(
            IReadOnlyList<string> schemas,
            IReadOnlyList<TableEntry> tables,
            IReadOnlyDictionary<string, IReadOnlyList<ColumnEntry>> columnsByTable,
            IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyEntry>> foreignKeysByTable,
            IReadOnlyList<ObjectEntry> objects)
        {
            Schemas = schemas;
            Tables = tables;
            ColumnsByTable = columnsByTable;
            ForeignKeysByTable = foreignKeysByTable;
            Objects = objects ?? EmptyObjects;
        }

        public static string TableKey(string schema, string table) => schema + "." + table;

        /// <summary>Schema da tabela quando o nome é único entre os schemas; null se ambíguo/desconhecido.</summary>
        public string ResolveUniqueSchema(string tableName)
        {
            string schema = null;
            foreach (TableEntry table in Tables)
            {
                if (string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    if (schema != null)
                        return null;
                    schema = table.Schema;
                }
            }
            return schema;
        }
    }
}
