using System.Linq;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class DangerousStatementDetectorTests
    {
        [Fact]
        public void DeleteWithoutWhere_IsFlagged_WithLine()
        {
            var result = DangerousStatementDetector.Find("SELECT 1;\r\nDELETE FROM Pessoas");
            var d = Assert.Single(result);
            Assert.Equal("DELETE", d.Keyword);
            Assert.Equal(2, d.Line);
        }

        [Fact]
        public void UpdateWithoutWhere_IsFlagged()
        {
            var d = Assert.Single(DangerousStatementDetector.Find("UPDATE Pessoas SET Nome = 'x'"));
            Assert.Equal("UPDATE", d.Keyword);
            Assert.Equal(1, d.Line);
        }

        [Theory]
        [InlineData("DELETE FROM Pessoas WHERE Id = 1")]
        [InlineData("UPDATE Pessoas SET Nome = 'x' WHERE Id = 1")]
        [InlineData("UPDATE p SET p.Nome = 'x' FROM Pessoas p WHERE p.Id = 1")]
        [InlineData("SELECT * FROM Pessoas")]
        [InlineData("-- DELETE FROM Pessoas")]
        [InlineData("SELECT 'DELETE FROM Pessoas'")]
        public void Safe_ReturnsEmpty(string sql)
        {
            Assert.Empty(DangerousStatementDetector.Find(sql));
        }

        [Fact]
        public void WhereOnlyInsideSubquery_IsStillFlagged()
        {
            // o WHERE está dentro do parêntese — o UPDATE de fora continua sem WHERE
            var result = DangerousStatementDetector.Find(
                "UPDATE Pessoas SET Nome = (SELECT TOP 1 Nome FROM Outra WHERE Id = 1)");
            Assert.Single(result);
        }

        [Fact]
        public void MultipleStatements_EachEvaluatedSeparately()
        {
            string sql = "DELETE FROM A;\r\nDELETE FROM B WHERE 1=1;\r\nUPDATE C SET x=1";
            var result = DangerousStatementDetector.Find(sql);
            Assert.Equal(2, result.Count);
            Assert.Equal(1, result[0].Line);
            Assert.Equal("UPDATE", result[1].Keyword);
            Assert.Equal(3, result[1].Line);
        }

        [Fact]
        public void GoSeparatesBatches()
        {
            var result = DangerousStatementDetector.Find("DELETE FROM A\r\nGO\r\nSELECT 1");
            Assert.Single(result);
        }

        [Fact]
        public void EmptyOrNull_ReturnsEmpty()
        {
            Assert.Empty(DangerousStatementDetector.Find(""));
            Assert.Empty(DangerousStatementDetector.Find(null));
        }

        [Theory]
        [InlineData("UPDATE STATISTICS dbo.Pessoas")]
        [InlineData("CREATE TRIGGER t ON dbo.X AFTER DELETE AS BEGIN SELECT 1 END")]
        [InlineData("CREATE TRIGGER t ON dbo.X FOR UPDATE AS BEGIN SELECT 1 END")]
        public void NonDmlForms_AreNotFlagged(string sql)
        {
            Assert.Empty(DangerousStatementDetector.Find(sql));
        }
    }
}
