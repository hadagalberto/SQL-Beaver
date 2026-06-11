using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class WildcardExpanderTests
    {
        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static DbMetadata MakeDb(params (string schema, string table, string[] cols)[] tables)
        {
            var tableList = new List<TableEntry>();
            var colMap = new Dictionary<string, IReadOnlyList<ColumnEntry>>(
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var (schema, table, cols) in tables)
            {
                tableList.Add(new TableEntry(schema, table));
                var entries = new List<ColumnEntry>();
                foreach (string col in cols)
                    entries.Add(new ColumnEntry(col, "int", true, false));
                colMap[DbMetadata.TableKey(schema, table)] = entries;
            }

            return new DbMetadata(
                new[] { "dbo" },
                tableList,
                colMap,
                new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>());
        }

        // ---------------------------------------------------------------
        // Tests
        // ---------------------------------------------------------------

        [Fact]
        public void SingleTable_BareNames()
        {
            DbMetadata db = MakeDb(("dbo", "Orders", new[] { "Id", "Name", "Total" }));
            string sql = "SELECT * FROM dbo.Orders";
            int caret = sql.IndexOf('*');

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.NotNull(r);
            Assert.Equal(caret, r.Start);
            Assert.Equal(1, r.Length);
            Assert.Equal("Id, Name, Total", r.NewText);
        }

        [Fact]
        public void TwoTables_QualifiedWithAliasOrTableName()
        {
            DbMetadata db = MakeDb(
                ("dbo", "Orders", new[] { "Id", "Total" }),
                ("dbo", "Items",  new[] { "ItemId", "Qty" }));
            string sql = "SELECT * FROM dbo.Orders o JOIN dbo.Items i ON o.Id = i.ItemId";
            int caret = sql.IndexOf('*');

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.NotNull(r);
            // multi-table: should qualify each column with alias
            Assert.Contains("o.", r.NewText);
            Assert.Contains("i.", r.NewText);
            Assert.Contains("o.Id", r.NewText);
            Assert.Contains("i.ItemId", r.NewText);
        }

        [Fact]
        public void AliasWildcard_OnlyAliasColumns_SpanIncludesAlias()
        {
            DbMetadata db = MakeDb(
                ("dbo", "Products", new[] { "ProductId", "Price" }),
                ("dbo", "Orders",   new[] { "OrderId", "Total" }));
            string sql = "SELECT p.* FROM dbo.Products p JOIN dbo.Orders o ON o.OrderId = p.ProductId";
            int dotPos = sql.IndexOf("p.*");
            int caret = dotPos + 2; // caret on *

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.NotNull(r);
            // span should start at 'p' (alias start)
            Assert.Equal(sql.IndexOf("p.*"), r.Start);
            Assert.Equal(3, r.Length); // "p.*"
            Assert.Equal("ProductId, Price", r.NewText);
        }

        [Fact]
        public void StarInWhere_ReturnsNull()
        {
            DbMetadata db = MakeDb(("dbo", "Orders", new[] { "Id" }));
            // * after WHERE is not a SELECT wildcard
            string sql = "SELECT Id FROM dbo.Orders WHERE Id * 2 = 4";
            int caret = sql.IndexOf("Id *") + 3; // caret on *

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.Null(r);
        }

        [Fact]
        public void InsideString_ReturnsNull()
        {
            DbMetadata db = MakeDb(("dbo", "Orders", new[] { "Id" }));
            string sql = "SELECT '* test' FROM dbo.Orders";
            int caret = sql.IndexOf('*');

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.Null(r);
        }

        [Fact]
        public void InsideComment_ReturnsNull()
        {
            DbMetadata db = MakeDb(("dbo", "Orders", new[] { "Id" }));
            string sql = "-- SELECT * FROM t\r\nSELECT 1 FROM dbo.Orders";
            int caret = sql.IndexOf('*');

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.Null(r);
        }

        [Fact]
        public void UnknownTable_ReturnsNull()
        {
            DbMetadata db = MakeDb(("dbo", "KnownTable", new[] { "Id" }));
            string sql = "SELECT * FROM dbo.UnknownTable";
            int caret = sql.IndexOf('*');

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.Null(r);
        }

        [Fact]
        public void CaretImmediatelyAfterStar_Works()
        {
            DbMetadata db = MakeDb(("dbo", "Orders", new[] { "Id", "Name" }));
            string sql = "SELECT * FROM dbo.Orders";
            int starPos = sql.IndexOf('*');
            int caret = starPos + 1; // caret immediately after *

            TextReplacement r = WildcardExpander.TryExpand(sql, caret, db);

            Assert.NotNull(r);
            Assert.Equal("Id, Name", r.NewText);
        }
    }
}
