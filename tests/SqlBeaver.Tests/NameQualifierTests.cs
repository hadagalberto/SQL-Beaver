using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class NameQualifierTests
    {
        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static DbMetadata MakeDb(params (string schema, string table)[] tables)
        {
            var tableList = new List<TableEntry>();
            var colMap = new Dictionary<string, IReadOnlyList<ColumnEntry>>(
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var (schema, table) in tables)
            {
                tableList.Add(new TableEntry(schema, table));
                colMap[DbMetadata.TableKey(schema, table)] =
                    new[] { new ColumnEntry("Id", "int", false, true) };
            }

            return new DbMetadata(
                new[] { "dbo", "sales" },
                tableList,
                colMap,
                new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>());
        }

        // ---------------------------------------------------------------
        // Qualify tests
        // ---------------------------------------------------------------

        [Fact]
        public void Qualify_SimpleFrom()
        {
            DbMetadata db = MakeDb(("dbo", "Orders"));
            string sql = "SELECT Id FROM Orders";
            IReadOnlyList<TextReplacement> edits = NameQualifier.Qualify(sql, db);
            string result = NameQualifier.Apply(sql, edits);
            Assert.Equal("SELECT Id FROM dbo.Orders", result);
        }

        [Fact]
        public void Qualify_WithJoin()
        {
            DbMetadata db = MakeDb(("dbo", "Orders"), ("dbo", "Items"));
            string sql = "SELECT * FROM Orders o JOIN Items i ON i.OrderId = o.Id";
            IReadOnlyList<TextReplacement> edits = NameQualifier.Qualify(sql, db);
            string result = NameQualifier.Apply(sql, edits);
            Assert.Contains("dbo.Orders", result);
            Assert.Contains("dbo.Items", result);
        }

        [Fact]
        public void Qualify_AlreadyQualified_Untouched()
        {
            DbMetadata db = MakeDb(("dbo", "Orders"));
            string sql = "SELECT Id FROM dbo.Orders";
            IReadOnlyList<TextReplacement> edits = NameQualifier.Qualify(sql, db);
            Assert.NotNull(edits);
            Assert.Empty(edits);
        }

        [Fact]
        public void Qualify_AmbiguousName_Untouched()
        {
            // Orders exists in both dbo and sales => ambiguous => no edit
            DbMetadata db = MakeDb(("dbo", "Orders"), ("sales", "Orders"));
            string sql = "SELECT Id FROM Orders";
            IReadOnlyList<TextReplacement> edits = NameQualifier.Qualify(sql, db);
            string result = NameQualifier.Apply(sql, edits);
            Assert.Equal("SELECT Id FROM Orders", result);
        }

        [Fact]
        public void Qualify_SyntaxError_ReturnsNull()
        {
            DbMetadata db = MakeDb(("dbo", "Orders"));
            IReadOnlyList<TextReplacement> edits = NameQualifier.Qualify("SELECT FROM WHERE", db);
            Assert.Null(edits);
        }

        // ---------------------------------------------------------------
        // Unqualify tests
        // ---------------------------------------------------------------

        [Fact]
        public void Unqualify_RemovesSchemaWhenUnique()
        {
            DbMetadata db = MakeDb(("dbo", "Orders"));
            string sql = "SELECT Id FROM dbo.Orders";
            IReadOnlyList<TextReplacement> edits = NameQualifier.Unqualify(sql, db);
            string result = NameQualifier.Apply(sql, edits);
            Assert.Equal("SELECT Id FROM Orders", result);
        }

        [Fact]
        public void Unqualify_AmbiguousName_Untouched()
        {
            DbMetadata db = MakeDb(("dbo", "Orders"), ("sales", "Orders"));
            string sql = "SELECT Id FROM dbo.Orders";
            IReadOnlyList<TextReplacement> edits = NameQualifier.Unqualify(sql, db);
            string result = NameQualifier.Apply(sql, edits);
            Assert.Equal("SELECT Id FROM dbo.Orders", result);
        }

        [Fact]
        public void Qualify_PreservesFormattingOutsideEdit()
        {
            DbMetadata db = MakeDb(("dbo", "Orders"));
            // Formatting (whitespace, newlines) around the table name is preserved
            string sql = "SELECT\r\n    Id\r\nFROM Orders\r\nWHERE Id = 1";
            IReadOnlyList<TextReplacement> edits = NameQualifier.Qualify(sql, db);
            string result = NameQualifier.Apply(sql, edits);
            // original whitespace preserved, only schema prepended
            Assert.Contains("FROM dbo.Orders", result);
            Assert.Contains("WHERE Id = 1", result);
            Assert.Contains("SELECT\r\n    Id", result);
        }
    }
}
