using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SemicolonInserterTests
    {
        [Fact]
        public void TwoStatements_NoSemicolons_BothGet()
        {
            string sql = "SELECT * FROM A\r\nSELECT * FROM B";
            string result = SemicolonInserter.AddSemicolons(sql);
            Assert.Equal("SELECT * FROM A;\r\nSELECT * FROM B;", result);
        }

        [Fact]
        public void AlreadyTerminated_Unchanged()
        {
            string sql = "SELECT 1;\r\nSELECT 2;";
            Assert.Equal(sql, SemicolonInserter.AddSemicolons(sql));
        }

        [Fact]
        public void GoLines_Preserved_AndNotTerminated()
        {
            string sql = "SELECT 1\r\nGO\r\nSELECT 2\r\nGO";
            string result = SemicolonInserter.AddSemicolons(sql);
            Assert.Equal("SELECT 1;\r\nGO\r\nSELECT 2;\r\nGO", result);
        }

        [Fact]
        public void TrailingComment_SemicolonBeforeComment()
        {
            string sql = "SELECT 1 -- coment\r\n";
            string result = SemicolonInserter.AddSemicolons(sql);
            Assert.Equal("SELECT 1; -- coment\r\n", result);
        }

        [Fact]
        public void SingleStatement_Gets()
        {
            string sql = "SELECT 1";
            Assert.Equal("SELECT 1;", SemicolonInserter.AddSemicolons(sql));
        }

        [Fact]
        public void Empty_Unchanged()
        {
            Assert.Equal(string.Empty, SemicolonInserter.AddSemicolons(string.Empty));
            Assert.Equal("   ", SemicolonInserter.AddSemicolons("   "));
        }

        [Fact]
        public void MixedTerminationAndUnterminated()
        {
            string sql = "SELECT 1;\r\nSELECT 2\r\nSELECT 3;";
            string result = SemicolonInserter.AddSemicolons(sql);
            Assert.Equal("SELECT 1;\r\nSELECT 2;\r\nSELECT 3;", result);
        }
    }
}
