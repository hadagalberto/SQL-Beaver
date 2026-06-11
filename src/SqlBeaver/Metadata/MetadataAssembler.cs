using System;
using System.Collections.Generic;

namespace SqlBeaver.Metadata
{
    /// <summary>Monta o DbMetadata indexado a partir das linhas cruas dos catálogos. Puro.</summary>
    public static class MetadataAssembler
    {
        public sealed class ColumnRow
        {
            public string Schema { get; }
            public string Table { get; }
            public string Column { get; }
            public string SqlType { get; }
            public bool IsNullable { get; }
            public bool IsPrimaryKey { get; }

            public ColumnRow(string schema, string table, string column, string sqlType, bool isNullable, bool isPrimaryKey)
            {
                Schema = schema; Table = table; Column = column;
                SqlType = sqlType; IsNullable = isNullable; IsPrimaryKey = isPrimaryKey;
            }
        }

        public sealed class ForeignKeyColumnRow
        {
            public int ForeignKeyId { get; }
            public string FromSchema { get; }
            public string FromTable { get; }
            public string FromColumn { get; }
            public string ToSchema { get; }
            public string ToTable { get; }
            public string ToColumn { get; }

            public ForeignKeyColumnRow(int foreignKeyId,
                string fromSchema, string fromTable, string fromColumn,
                string toSchema, string toTable, string toColumn)
            {
                ForeignKeyId = foreignKeyId;
                FromSchema = fromSchema; FromTable = fromTable; FromColumn = fromColumn;
                ToSchema = toSchema; ToTable = toTable; ToColumn = toColumn;
            }
        }

        public static DbMetadata Assemble(
            IReadOnlyList<TableEntry> tables,
            IReadOnlyList<string> schemas,
            IReadOnlyList<ColumnRow> columnRows,
            IReadOnlyList<ForeignKeyColumnRow> foreignKeyRows)
        {
            var columnsByTable = new Dictionary<string, IReadOnlyList<ColumnEntry>>(StringComparer.OrdinalIgnoreCase);
            var columnBuckets = new Dictionary<string, List<ColumnEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (ColumnRow row in columnRows)
            {
                string key = DbMetadata.TableKey(row.Schema, row.Table);
                if (!columnBuckets.TryGetValue(key, out List<ColumnEntry> bucket))
                {
                    bucket = new List<ColumnEntry>();
                    columnBuckets[key] = bucket;
                    columnsByTable[key] = bucket;
                }
                bucket.Add(new ColumnEntry(row.Column, row.SqlType, row.IsNullable, row.IsPrimaryKey));
            }

            // agrupa pares de colunas por FK (linhas vêm ordenadas por FK + posição)
            var fkGroups = new Dictionary<int, ForeignKeyBuilder>();
            var fkOrder = new List<int>();
            foreach (ForeignKeyColumnRow row in foreignKeyRows)
            {
                if (!fkGroups.TryGetValue(row.ForeignKeyId, out ForeignKeyBuilder builder))
                {
                    builder = new ForeignKeyBuilder(row);
                    fkGroups[row.ForeignKeyId] = builder;
                    fkOrder.Add(row.ForeignKeyId);
                }
                builder.FromColumns.Add(row.FromColumn);
                builder.ToColumns.Add(row.ToColumn);
            }

            var foreignKeysByTable = new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>(StringComparer.OrdinalIgnoreCase);
            var fkBuckets = new Dictionary<string, List<ForeignKeyEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (int id in fkOrder)
            {
                ForeignKeyEntry entry = fkGroups[id].Build();
                AddFk(fkBuckets, foreignKeysByTable, DbMetadata.TableKey(entry.FromSchema, entry.FromTable), entry);
                string toKey = DbMetadata.TableKey(entry.ToSchema, entry.ToTable);
                if (!string.Equals(toKey, DbMetadata.TableKey(entry.FromSchema, entry.FromTable), StringComparison.OrdinalIgnoreCase))
                    AddFk(fkBuckets, foreignKeysByTable, toKey, entry); // auto-referência indexa uma vez só
            }

            return new DbMetadata(schemas, tables, columnsByTable, foreignKeysByTable);
        }

        private static void AddFk(
            Dictionary<string, List<ForeignKeyEntry>> buckets,
            Dictionary<string, IReadOnlyList<ForeignKeyEntry>> result,
            string key, ForeignKeyEntry entry)
        {
            if (!buckets.TryGetValue(key, out List<ForeignKeyEntry> bucket))
            {
                bucket = new List<ForeignKeyEntry>();
                buckets[key] = bucket;
                result[key] = bucket;
            }
            bucket.Add(entry);
        }

        private sealed class ForeignKeyBuilder
        {
            public string FromSchema, FromTable, ToSchema, ToTable;
            public List<string> FromColumns = new List<string>();
            public List<string> ToColumns = new List<string>();

            public ForeignKeyBuilder(ForeignKeyColumnRow first)
            {
                FromSchema = first.FromSchema; FromTable = first.FromTable;
                ToSchema = first.ToSchema; ToTable = first.ToTable;
            }

            public ForeignKeyEntry Build()
                => new ForeignKeyEntry(FromSchema, FromTable, FromColumns, ToSchema, ToTable, ToColumns);
        }
    }
}
