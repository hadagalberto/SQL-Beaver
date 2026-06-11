using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class TextPositionTests
    {
        [Fact]
        public void Offset0_ReturnsLine1Col1()
        {
            TextPosition.FromOffset("SELECT 1", 0, out int line, out int col);
            Assert.Equal(1, line);
            Assert.Equal(1, col);
        }

        [Fact]
        public void SingleLine_MidOffset()
        {
            // "SELECT" — offset 3 → line 1, col 4
            TextPosition.FromOffset("SELECT 1", 3, out int line, out int col);
            Assert.Equal(1, line);
            Assert.Equal(4, col);
        }

        [Fact]
        public void AfterOneCRLF_ReturnsLine2Col1()
        {
            // "A\r\nB" — offset 3 (just after \n) → line 2, col 1
            string text = "A\r\nB";
            TextPosition.FromOffset(text, 3, out int line, out int col);
            Assert.Equal(2, line);
            Assert.Equal(1, col);
        }

        [Fact]
        public void MultiLineCRLF_MiddleOfLine3()
        {
            // "A\r\nBB\r\nCCC" — offset for 'C' at index 7 → line 3, col 1
            // breakdown: A=0, \r=1, \n=2, B=3, B=4, \r=5, \n=6, C=7, C=8, C=9
            string text = "A\r\nBB\r\nCCC";
            // offset 9 → third C → line 3, col 3
            TextPosition.FromOffset(text, 9, out int line, out int col);
            Assert.Equal(3, line);
            Assert.Equal(3, col);
        }
    }
}
