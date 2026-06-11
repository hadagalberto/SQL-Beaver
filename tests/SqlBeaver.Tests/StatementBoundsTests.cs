using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class StatementBoundsTests
    {
        private static StatementBounds Bounds(string text, int? caret = null)
            => StatementScopeAnalyzer.GetStatementBoundsAt(text, caret ?? text.Length);

        private static string Span(string text, StatementBounds b)
            => b.Length == 0 ? string.Empty : text.Substring(b.Start, b.Length);

        // 1. Single statement — whole text (trimmed)
        [Fact]
        public void SingleStatement_ReturnsTrimmedWhole()
        {
            string text = "  SELECT * FROM Pessoas  ";
            var b = Bounds(text);
            Assert.Equal("SELECT * FROM Pessoas", Span(text, b));
        }

        // 2. Caret in first of two SELECTs without ';' → only first
        [Fact]
        public void TwoSelectsWithoutSemicolon_CaretInFirst_ReturnsFirst()
        {
            string text = "SELECT * FROM A\r\nSELECT * FROM B";
            // caret at position 5, inside "SELECT * FROM A"
            var b = Bounds(text, 5);
            Assert.Equal("SELECT * FROM A", Span(text, b));
        }

        // 3. Caret in second of two SELECTs without ';' → only second
        [Fact]
        public void TwoSelectsWithoutSemicolon_CaretInSecond_ReturnsSecond()
        {
            string text = "SELECT * FROM A\r\nSELECT * FROM B";
            // caret at the end (inside second statement)
            var b = Bounds(text);
            Assert.Equal("SELECT * FROM B", Span(text, b));
        }

        // 4. Semicolon-separated: caret in first → first only
        [Fact]
        public void SemicolonSeparated_CaretInFirst_ReturnsFirst()
        {
            string text = "SELECT 1; SELECT 2";
            var b = Bounds(text, 4); // inside "SELECT 1"
            Assert.Equal("SELECT 1", Span(text, b));
        }

        // 5. Semicolon-separated: caret in second → second only
        [Fact]
        public void SemicolonSeparated_CaretInSecond_ReturnsSecond()
        {
            string text = "SELECT 1; SELECT 2";
            var b = Bounds(text); // caret at end
            Assert.Equal("SELECT 2", Span(text, b));
        }

        // 6. GO-separated: caret in first batch → first only
        [Fact]
        public void GoSeparated_CaretInFirst_ReturnsFirst()
        {
            string text = "SELECT * FROM A\r\nGO\r\nSELECT * FROM B";
            var b = Bounds(text, 5); // inside first SELECT
            Assert.Equal("SELECT * FROM A", Span(text, b));
        }

        // 7. GO-separated: caret in second batch → second only
        [Fact]
        public void GoSeparated_CaretInSecond_ReturnsSecond()
        {
            string text = "SELECT * FROM A\r\nGO\r\nSELECT * FROM B";
            var b = Bounds(text); // caret at end
            Assert.Equal("SELECT * FROM B", Span(text, b));
        }

        // 8. UNION is ONE statement → bounds cover whole UNION query
        [Fact]
        public void UnionIsOneStatement_BoundsCoverBoth()
        {
            string text = "SELECT x FROM A UNION ALL SELECT y FROM B";
            // caret in the second SELECT part
            var b = Bounds(text);
            Assert.Equal("SELECT x FROM A UNION ALL SELECT y FROM B", Span(text, b));
        }

        // 9. Caret inside trailing comment of a statement stays with it
        [Fact]
        public void TrailingComment_StaysWithStatement()
        {
            string text = "SELECT 1 -- my comment\r\nSELECT 2";
            // caret inside the comment "-- my comment" (position 15)
            var b = Bounds(text, 15);
            // The comment is part of the first statement; trimming cuts trailing whitespace
            // but the comment text is non-whitespace so it stays
            Assert.Contains("SELECT 1", Span(text, b));
            Assert.DoesNotContain("SELECT 2", Span(text, b));
        }

        // 10. Empty text → zero-length bounds at caret
        [Fact]
        public void EmptyText_ReturnsZeroLength()
        {
            var b = Bounds("", 0);
            Assert.Equal(0, b.Length);
        }

        // 11. Whitespace-only text → zero-length (all trimmed)
        [Fact]
        public void WhitespaceOnly_ReturnsZeroLength()
        {
            var b = Bounds("   \r\n   ");
            Assert.Equal(0, b.Length);
        }

        // 12. INSERT…SELECT is one statement
        [Fact]
        public void InsertSelect_IsOneStatement()
        {
            string text = "INSERT INTO T SELECT * FROM S";
            var b = Bounds(text);
            Assert.Equal("INSERT INTO T SELECT * FROM S", Span(text, b));
        }
    }
}
