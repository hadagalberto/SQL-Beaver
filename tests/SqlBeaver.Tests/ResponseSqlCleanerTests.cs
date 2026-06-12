using SqlBeaver.Ai;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ResponseSqlCleanerTests
    {
        [Fact]
        public void StripsSqlFence()
        {
            string cleaned = ResponseSqlCleaner.Clean("```sql\nSELECT 1\n```");
            Assert.Equal("SELECT 1", cleaned);
        }

        [Fact]
        public void StripsBareFence()
        {
            string cleaned = ResponseSqlCleaner.Clean("```\nSELECT 1\n```");
            Assert.Equal("SELECT 1", cleaned);
        }

        [Fact]
        public void NoFence_JustTrims()
        {
            string cleaned = ResponseSqlCleaner.Clean("  SELECT 1  ");
            Assert.Equal("SELECT 1", cleaned);
        }

        [Fact]
        public void NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal("", ResponseSqlCleaner.Clean(null));
            Assert.Equal("", ResponseSqlCleaner.Clean(""));
            Assert.Equal("", ResponseSqlCleaner.Clean("   "));
        }

        [Fact]
        public void StripsFence_WithUppercaseLangTag_AndMultilineBody()
        {
            string cleaned = ResponseSqlCleaner.Clean("```TSQL\nSELECT *\nFROM dbo.X\n```");
            Assert.Equal("SELECT *\nFROM dbo.X", cleaned);
        }

        [Fact]
        public void ExtractsFencedBlock_AmidProse()
        {
            string cleaned = ResponseSqlCleaner.Clean(
                "Claro! Aqui está a consulta:\n```sql\nSELECT Nome FROM dbo.Pessoas\n```\nEspero que ajude.");
            Assert.Equal("SELECT Nome FROM dbo.Pessoas", cleaned);
        }

        [Fact]
        public void NoFence_DropsLeadingProse_ToFirstSqlKeyword()
        {
            string cleaned = ResponseSqlCleaner.Clean(
                "Talvez a tabela Pagamentos sirva.\nSELECT Nome, Cpf\nFROM dbo.Pessoas;");
            Assert.Equal("SELECT Nome, Cpf\nFROM dbo.Pessoas;", cleaned);
        }

        [Fact]
        public void LooksLikeSql_TrueForSql_FalseForProse()
        {
            Assert.True(ResponseSqlCleaner.LooksLikeSql("WITH cte AS (SELECT 1) SELECT * FROM cte"));
            Assert.True(ResponseSqlCleaner.LooksLikeSql("linha de prosa\nUPDATE x SET y=1"));
            Assert.False(ResponseSqlCleaner.LooksLikeSql("Muitas vezes a tabela Debitos possui DataPagamento..."));
            Assert.False(ResponseSqlCleaner.LooksLikeSql(""));
        }
    }
}
