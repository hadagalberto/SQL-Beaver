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
    }
}
