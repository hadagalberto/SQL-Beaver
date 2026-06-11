using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ScriptOutlineBuilderTests
    {
        [Fact]
        public void Build_TwoStatements_TwoItemsWithKindsAndLines()
        {
            string sql = "SELECT 1;\r\nUPDATE T SET X = 1;";
            IReadOnlyList<OutlineItem> items = ScriptOutlineBuilder.Build(sql);

            Assert.Equal(2, items.Count);
            Assert.Equal("SELECT", items[0].Kind);
            Assert.Equal(1, items[0].Line);
            Assert.Equal("UPDATE", items[1].Kind);
            Assert.Equal(2, items[1].Line);
        }

        [Fact]
        public void Build_CreateProcedure_Detected()
        {
            string sql = "CREATE PROCEDURE dbo.MyProc AS BEGIN SELECT 1 END";
            IReadOnlyList<OutlineItem> items = ScriptOutlineBuilder.Build(sql);

            Assert.Single(items);
            Assert.Equal("CREATE PROCEDURE", items[0].Kind);
        }

        [Fact]
        public void Build_LongStatement_SummaryTruncatedTo80()
        {
            string cols = string.Join(", ", System.Linq.Enumerable.Range(1, 40).Select(i => "Col" + i));
            string sql = "SELECT " + cols + " FROM BigTable";
            IReadOnlyList<OutlineItem> items = ScriptOutlineBuilder.Build(sql);

            Assert.Single(items);
            Assert.True(items[0].Summary.Length <= 80);
        }

        [Fact]
        public void Build_LinesAreOneBased()
        {
            string sql = "DECLARE @x INT;";
            IReadOnlyList<OutlineItem> items = ScriptOutlineBuilder.Build(sql);

            Assert.Single(items);
            Assert.Equal(1, items[0].Line);
            Assert.Equal("DECLARE", items[0].Kind);
        }

        [Fact]
        public void Build_Empty_ReturnsEmpty()
        {
            Assert.Empty(ScriptOutlineBuilder.Build(""));
            Assert.Empty(ScriptOutlineBuilder.Build("   "));
            Assert.Empty(ScriptOutlineBuilder.Build(null));
        }

        [Fact]
        public void Build_ParseError_HandledGracefully()
        {
            string sql = "SELECT FROM WHERE ((( garbage";
            IReadOnlyList<OutlineItem> items = ScriptOutlineBuilder.Build(sql);

            // Não lança e retorna ao menos um item descrevendo o problema.
            Assert.NotEmpty(items);
            Assert.Equal("PARSE ERROR", items[0].Kind);
        }

        [Fact]
        public void Build_SummaryWhitespaceCollapsed()
        {
            string sql = "SELECT     A,\r\n   B\r\nFROM   T;";
            IReadOnlyList<OutlineItem> items = ScriptOutlineBuilder.Build(sql);

            Assert.Single(items);
            Assert.DoesNotContain("  ", items[0].Summary);
            Assert.DoesNotContain("\r", items[0].Summary);
            Assert.DoesNotContain("\n", items[0].Summary);
        }
    }
}
