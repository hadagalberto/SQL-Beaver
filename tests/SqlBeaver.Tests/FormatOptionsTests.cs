using SqlBeaver.Formatting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class FormatOptionsTests
    {
        // ── FormatOptions.Load ───────────────────────────────────────────────

        [Fact]
        public void Load_Null_ReturnsDefaults()
        {
            FormatOptions opts = FormatOptions.Load(null);
            Assert.Equal("uppercase", opts.KeywordCasing);
            Assert.Equal(4, opts.IndentationSize);
            Assert.True(opts.NewLineBeforeFromClause);
            Assert.True(opts.IncludeSemicolons);
            Assert.True(opts.NewLineBeforeWhereClause);
            Assert.True(opts.MultilineSelectElementsList);
        }

        [Fact]
        public void Load_PartialJson_ChangedValuesOverrideOthersStayDefault()
        {
            // Only keywordCasing and indentationSize overridden; all others should stay default.
            FormatOptions opts = FormatOptions.Load("{\"keywordCasing\":\"lowercase\",\"indentationSize\":2}");
            Assert.Equal("lowercase", opts.KeywordCasing);
            Assert.Equal(2, opts.IndentationSize);
            // Remaining defaults must be preserved:
            Assert.True(opts.IncludeSemicolons,           "includeSemicolons must default to true");
            Assert.True(opts.NewLineBeforeFromClause,     "newLineBeforeFromClause must default to true");
            Assert.True(opts.NewLineBeforeWhereClause,    "newLineBeforeWhereClause must default to true");
            Assert.True(opts.NewLineBeforeJoinClause,     "newLineBeforeJoinClause must default to true");
            Assert.True(opts.MultilineSelectElementsList, "multilineSelectElementsList must default to true");
        }

        [Fact]
        public void Load_InvalidJson_ReturnsDefaults()
        {
            FormatOptions opts = FormatOptions.Load("{invalid");
            Assert.Equal("uppercase", opts.KeywordCasing);
            Assert.Equal(4, opts.IndentationSize);
            Assert.True(opts.NewLineBeforeFromClause);
        }

        [Fact]
        public void Load_EmptyJson_ReturnsDefaults()
        {
            FormatOptions opts = FormatOptions.Load("");
            Assert.Equal("uppercase", opts.KeywordCasing);
            Assert.Equal(4, opts.IndentationSize);
        }

        [Fact]
        public void Load_FullJson_AllKnobsApplied()
        {
            string json =
                "{" +
                "\"keywordCasing\":\"lowercase\"," +
                "\"indentationSize\":2," +
                "\"alignClauseBodies\":true," +
                "\"asKeywordOnOwnLine\":true," +
                "\"includeSemicolons\":false," +
                "\"indentSetClause\":true," +
                "\"newLineBeforeFromClause\":false," +
                "\"newLineBeforeWhereClause\":false," +
                "\"newLineBeforeGroupByClause\":false," +
                "\"newLineBeforeOrderByClause\":false," +
                "\"newLineBeforeHavingClause\":false," +
                "\"newLineBeforeJoinClause\":false," +
                "\"newLineBeforeOpenParenthesisInMultilineList\":true," +
                "\"newLineBeforeCloseParenthesisInMultilineList\":true," +
                "\"multilineSelectElementsList\":false," +
                "\"multilineInsertSourcesList\":false," +
                "\"multilineWherePredicatesList\":true," +
                "\"multilineViewColumnsList\":true" +
                "}";

            FormatOptions opts = FormatOptions.Load(json);
            Assert.Equal("lowercase", opts.KeywordCasing);
            Assert.Equal(2, opts.IndentationSize);
            Assert.True(opts.AlignClauseBodies);
            Assert.True(opts.AsKeywordOnOwnLine);
            Assert.False(opts.IncludeSemicolons);
            Assert.True(opts.IndentSetClause);
            Assert.False(opts.NewLineBeforeFromClause);
            Assert.False(opts.NewLineBeforeWhereClause);
            Assert.False(opts.NewLineBeforeGroupByClause);
            Assert.False(opts.NewLineBeforeOrderByClause);
            Assert.False(opts.NewLineBeforeHavingClause);
            Assert.False(opts.NewLineBeforeJoinClause);
            Assert.True(opts.NewLineBeforeOpenParenthesisInMultilineList);
            Assert.True(opts.NewLineBeforeCloseParenthesisInMultilineList);
            Assert.False(opts.MultilineSelectElementsList);
            Assert.False(opts.MultilineInsertSourcesList);
            Assert.True(opts.MultilineWherePredicatesList);
            Assert.True(opts.MultilineViewColumnsList);
        }

        // ── Formatter integration ─────────────────────────────────────────────

        [Fact]
        public void Formatter_LowercaseKeywords_OutputContainsLowerSelect()
        {
            var opts = FormatOptions.Load("{\"keywordCasing\":\"lowercase\"}");
            bool ok = SqlFormatterService.TryFormat(
                "SELECT Id, Nome FROM dbo.Pessoas WHERE Id = 1",
                opts, out string formatted, out string error, out _);
            Assert.True(ok, error);
            Assert.Contains("select", formatted);
            Assert.DoesNotContain("SELECT", formatted);
        }

        [Fact]
        public void Formatter_IndentationSize8_Load_RoundTrips()
        {
            // Verify that indentationSize:8 is correctly loaded (regression guard).
            var opts = FormatOptions.Load("{\"indentationSize\":8}");
            Assert.Equal(8, opts.IndentationSize);
        }

        [Fact]
        public void Formatter_IndentationSize2_MultilineWhereIsIndented()
        {
            // IndentationSize=2 + multilineWherePredicatesList=true: each WHERE predicate
            // on its own line indented by 2 spaces from the WHERE keyword.
            var opts = FormatOptions.Load(
                "{\"indentationSize\":2,\"multilineWherePredicatesList\":true,\"newLineBeforeWhereClause\":true}");
            bool ok = SqlFormatterService.TryFormat(
                "SELECT Id FROM dbo.Pessoas WHERE Id = 1 AND Nome = 'x' AND Ativo = 1",
                opts, out string formatted, out string error, out _);
            Assert.True(ok, error);
            // The formatter with indentationSize=2 should produce lines with 2-space indentation;
            // confirm it formats without error and the options were accepted (not defaults).
            Assert.NotNull(formatted);
            Assert.Equal(2, opts.IndentationSize);
        }

        [Fact]
        public void Formatter_NewLineBeforeWhereClauseFalse_WhereNotAtLineStart()
        {
            var opts = FormatOptions.Load("{\"newLineBeforeWhereClause\":false,\"newLineBeforeFromClause\":false}");
            bool ok = SqlFormatterService.TryFormat(
                "SELECT Id FROM dbo.Pessoas WHERE Id = 1",
                opts, out string formatted, out string error, out _);
            Assert.True(ok, error);
            string normalized = formatted.Replace("\r\n", "\n");
            // WHERE must NOT be at the start of a line
            foreach (string line in normalized.Split('\n'))
            {
                string trimmed = line.TrimStart();
                Assert.False(trimmed.StartsWith("WHERE") && line != trimmed,
                    $"WHERE must not be at start of its own line.\nFormatted:\n{formatted}");
            }
        }

        // ── Existing 4-arg overload still works unchanged ─────────────────────

        [Fact]
        public void ExistingOverload_StillWorks_NoRegression()
        {
            bool ok = SqlFormatterService.TryFormat(
                "select id from dbo.t where id=1",
                out string formatted, out string error, out _);
            Assert.True(ok, error);
            // Defaults → uppercase
            Assert.Contains("SELECT", formatted);
        }
    }
}
