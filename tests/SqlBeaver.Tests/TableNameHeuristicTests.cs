using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class TableNameHeuristicTests
    {
        [Theory]
        [InlineData("SELECT * FROM dbo.Pessoas WHERE Id = 1", "dbo.Pessoas")]
        [InlineData("select id from [Cadastro].[Pessoas] p join X on 1=1", "[Cadastro].[Pessoas]")]
        [InlineData("SELECT * FROM Pessoas", "Pessoas")]
        [InlineData("SELECT 1", null)]
        [InlineData("", null)]
        [InlineData("-- FROM Comentario\r\nSELECT * FROM Real", "Real")]
        public void ExtractsFirstFromTarget(string query, string expected)
        {
            Assert.Equal(expected, TableNameHeuristic.TryExtract(query));
        }

        [Fact]
        public void NullQuery_ReturnsNull()
        {
            Assert.Null(TableNameHeuristic.TryExtract(null));
        }

        [Fact]
        public void LineComment_WithBlockCommentInside_DoesNotEatNextLine()
        {
            // "-- nota /*" must NOT start a block comment that eats "SELECT * FROM Real"
            Assert.Equal("Real", TableNameHeuristic.TryExtract("-- nota /*\r\nSELECT * FROM Real"));
        }

        [Fact]
        public void SubqueryInnerFrom_ReturnsInnerTableName()
        {
            // Characterizes current behavior: the regex finds the first FROM,
            // which is the outer one; the inner table dbo.X is not returned.
            // If behavior changes this test will catch it.
            Assert.Equal("dbo.X", TableNameHeuristic.TryExtract("SELECT * FROM (SELECT a FROM dbo.X) t"));
        }
    }
}
