using SqlBeaver.Formatting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlFormatterServiceTests
    {
        [Fact]
        public void Formats_LowercaseSelect_ToUppercaseKeywordsAndClausePerLine()
        {
            bool ok = SqlFormatterService.TryFormat(
                "select p.nome, p.id from cadastro.pessoas p where p.id = 1 order by p.nome",
                out string formatted, out string error);

            Assert.True(ok, error);
            Assert.Contains("SELECT", formatted);
            Assert.Contains("\nFROM", formatted.Replace("\r\n", "\n"));
            Assert.Contains("\nWHERE", formatted.Replace("\r\n", "\n"));
            Assert.Contains("\nORDER BY", formatted.Replace("\r\n", "\n"));
        }

        [Fact]
        public void SyntaxError_ReturnsFalse_WithLineInError()
        {
            bool ok = SqlFormatterService.TryFormat("SELECT * FROM WHERE", out _, out string error);
            Assert.False(ok);
            Assert.Contains("linha", error);
        }

        [Fact]
        public void PreservesStringLiterals()
        {
            Assert.True(SqlFormatterService.TryFormat(
                "select 'TeXto PreServado' as x", out string formatted, out _));
            Assert.Contains("'TeXto PreServado'", formatted);
        }

        [Fact]
        public void MultipleStatements_AllFormatted()
        {
            Assert.True(SqlFormatterService.TryFormat(
                "select 1; select 2;", out string formatted, out _));
            int count = 0, idx = 0;
            while ((idx = formatted.IndexOf("SELECT", idx, System.StringComparison.Ordinal)) >= 0) { count++; idx++; }
            Assert.Equal(2, count);
        }
    }
}
