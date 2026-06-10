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
    }
}
