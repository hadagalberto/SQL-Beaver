using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ObjectCasingFixerTests
    {
        private static DbMetadata Meta()
        {
            var schemas = new[] { "dbo", "Sales" };
            var tables = new[]
            {
                new TableEntry("dbo", "Pessoas"),
                new TableEntry("Sales", "Orders"),
            };
            var cols = new Dictionary<string, IReadOnlyList<ColumnEntry>>
            {
                [DbMetadata.TableKey("dbo", "Pessoas")] = new[]
                {
                    new ColumnEntry("Nome", "varchar(50)", true, false),
                    new ColumnEntry("Idade", "int", true, false),
                },
            };
            var objects = new[]
            {
                new ObjectEntry("dbo", "GetPessoas", DbObjectType.Procedure),
            };
            return new DbMetadata(schemas, tables, cols,
                new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>(), objects);
        }

        [Fact]
        public void LowercaseTable_FixedToCanonical()
        {
            string result = ObjectCasingFixer.Fix("SELECT * FROM pessoas", Meta());
            Assert.Equal("SELECT * FROM Pessoas", result);
        }

        [Fact]
        public void Column_Cased()
        {
            string result = ObjectCasingFixer.Fix("SELECT nome FROM Pessoas", Meta());
            Assert.Equal("SELECT Nome FROM Pessoas", result);
        }

        [Fact]
        public void Schema_Cased()
        {
            string result = ObjectCasingFixer.Fix("SELECT * FROM sales.Orders", Meta());
            Assert.Equal("SELECT * FROM Sales.Orders", result);
        }

        [Fact]
        public void AlreadyCorrect_Unchanged()
        {
            string sql = "SELECT Nome FROM Pessoas";
            Assert.Equal(sql, ObjectCasingFixer.Fix(sql, Meta()));
        }

        [Fact]
        public void UnknownIdentifier_Untouched()
        {
            string sql = "SELECT foo FROM bar";
            Assert.Equal(sql, ObjectCasingFixer.Fix(sql, Meta()));
        }

        [Fact]
        public void Ambiguous_Skipped()
        {
            // Two tables differing only by case → ambiguous, skipped.
            var tables = new[]
            {
                new TableEntry("dbo", "Item"),
                new TableEntry("dbo", "ITEM"),
            };
            var meta = new DbMetadata(new[] { "dbo" }, tables);
            string sql = "SELECT * FROM item";
            Assert.Equal(sql, ObjectCasingFixer.Fix(sql, meta));
        }

        [Fact]
        public void InsideStringOrComment_Untouched()
        {
            string sql = "SELECT 'pessoas' AS x -- pessoas\r\nFROM Pessoas";
            string result = ObjectCasingFixer.Fix(sql, Meta());
            // string and comment 'pessoas' stay lowercase; only the real table ref is correct already.
            Assert.Equal(sql, result);
        }
    }
}
