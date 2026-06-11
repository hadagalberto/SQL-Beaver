using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class BlockMatcherTests
    {
        // ---- helpers ----

        private static BlockMatch MatchAt(string text, int caretPos)
            => BlockMatcher.Match(text, caretPos);

        // ---- testes simples BEGIN/END ----

        [Fact]
        public void SimpleBeginEnd_CaretOnBegin_ReturnsMatch()
        {
            // "BEGIN\n  SELECT 1\nEND"
            //  01234 5  678901234 5 678 9
            // END starts at position 17
            string text = "BEGIN\n  SELECT 1\nEND";
            // caret no início de "BEGIN"
            var m = MatchAt(text, 0);
            Assert.NotNull(m);
            Assert.Equal(0, m.OpenStart);
            Assert.Equal(5, m.OpenLength);
            Assert.Equal(17, m.CloseStart);
            Assert.Equal(3, m.CloseLength);
        }

        [Fact]
        public void SimpleBeginEnd_CaretOnEnd_ReturnsMatch()
        {
            string text = "BEGIN\n  SELECT 1\nEND";
            // END starts at position 17
            var m = MatchAt(text, 17);
            Assert.NotNull(m);
            Assert.Equal(0, m.OpenStart);
            Assert.Equal(5, m.OpenLength);
            Assert.Equal(17, m.CloseStart);
            Assert.Equal(3, m.CloseLength);
        }

        // ---- nested ----

        [Fact]
        public void Nested_OuterBegin_MatchesOuterEnd()
        {
            //           0         1         2         3
            //           0123456789012345678901234567890123456789
            string text = "BEGIN BEGIN SELECT 1 END SELECT 2 END";
            // caret na posição 0 (outer BEGIN)
            var m = MatchAt(text, 0);
            Assert.NotNull(m);
            Assert.Equal(0, m.OpenStart);
            // O END mais externo está no final
            Assert.Equal(34, m.CloseStart);
        }

        [Fact]
        public void Nested_InnerBegin_MatchesInnerEnd()
        {
            string text = "BEGIN BEGIN SELECT 1 END SELECT 2 END";
            // caret na posição 6 (inner BEGIN)
            var m = MatchAt(text, 6);
            Assert.NotNull(m);
            Assert.Equal(6, m.OpenStart);
            Assert.Equal(21, m.CloseStart); // END após "SELECT 1 "
        }

        [Fact]
        public void Nested_OuterEnd_MatchesOuterBegin()
        {
            // "BEGIN BEGIN SELECT 1 END SELECT 2 END"
            //  01234 56789...
            // outer END is at position 34
            string text = "BEGIN BEGIN SELECT 1 END SELECT 2 END";
            int outerEnd = text.LastIndexOf("END");
            var m = MatchAt(text, outerEnd);
            Assert.NotNull(m);
            Assert.Equal(0, m.OpenStart);
            Assert.Equal(outerEnd, m.CloseStart);
        }

        // ---- BEGIN TRY / END TRY ----

        [Fact]
        public void BeginTry_CaretOnBegin_MatchesEndTry()
        {
            //           0         1         2         3
            //           01234567890123456789012345678901234
            string text = "BEGIN TRY\n  SELECT 1\nEND TRY";
            // caret no início de "BEGIN"
            var m = MatchAt(text, 0);
            Assert.NotNull(m);
            Assert.Equal(0, m.OpenStart);
            Assert.Equal(5, m.OpenLength);  // "BEGIN"
            // "END" começa na posição 21
            Assert.Equal(21, m.CloseStart);
            Assert.Equal(3, m.CloseLength); // "END"
        }

        // ---- sem par (unbalanced) ----

        [Fact]
        public void UnbalancedBegin_ReturnsNull()
        {
            string text = "BEGIN SELECT 1";
            var m = MatchAt(text, 0);
            Assert.Null(m);
        }

        [Fact]
        public void UnbalancedEnd_ReturnsNull()
        {
            string text = "SELECT 1 END";
            var m = MatchAt(text, 9);
            Assert.Null(m);
        }

        // ---- caret fora de keyword de bloco ----

        [Fact]
        public void CaretOnSelect_ReturnsNull()
        {
            string text = "BEGIN SELECT 1 END";
            // caret em "SELECT" (posição 6)
            var m = MatchAt(text, 6);
            Assert.Null(m);
        }

        // ---- keyword dentro de string deve ser ignorada ----

        [Fact]
        public void KeywordInsideString_Ignored()
        {
            //           0         1         2         3
            //           0123456789012345678901234567890123456789
            string text = "BEGIN SELECT 'END fake' END";
            // BEGIN na posição 0; o único END real está no final
            var m = MatchAt(text, 0);
            Assert.NotNull(m);
            Assert.Equal(0, m.OpenStart);
            Assert.Equal(24, m.CloseStart); // END real at position 24
        }

        // ---- caret em whitespace ----

        [Fact]
        public void CaretOnWhitespace_ReturnsNull()
        {
            string text = "BEGIN  END";
            // posição 6 é espaço
            var m = MatchAt(text, 6);
            Assert.Null(m);
        }
    }
}
