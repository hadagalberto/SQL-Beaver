using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class BracketTogglerTests
    {
        [Fact]
        public void Add_WrapsTableAndColumn()
        {
            string result = BracketToggler.AddBrackets("SELECT Nome FROM Pessoas");
            Assert.Equal("SELECT [Nome] FROM [Pessoas]", result);
        }

        [Fact]
        public void Add_DoesNotDoubleWrapAlreadyBracketed()
        {
            string result = BracketToggler.AddBrackets("SELECT [Nome] FROM [Pessoas]");
            Assert.Equal("SELECT [Nome] FROM [Pessoas]", result);
        }

        [Fact]
        public void Add_DoesNotWrapKeywords()
        {
            // FROM/SELECT are keywords (not Identifier tokens) so they stay unbracketed.
            string result = BracketToggler.AddBrackets("SELECT a FROM t");
            Assert.Equal("SELECT [a] FROM [t]", result);
        }

        [Fact]
        public void Remove_UnwrapsSimpleIdentifier()
        {
            string result = BracketToggler.RemoveBrackets("SELECT [Col] FROM [Tbl]");
            Assert.Equal("SELECT Col FROM Tbl", result);
        }

        [Fact]
        public void Remove_KeepsNameWithSpace()
        {
            string result = BracketToggler.RemoveBrackets("SELECT [Order Details] FROM [Tbl]");
            Assert.Equal("SELECT [Order Details] FROM Tbl", result);
        }

        [Fact]
        public void Remove_KeepsKeywordBracketed()
        {
            // [Select] is a reserved keyword — must stay bracketed.
            string result = BracketToggler.RemoveBrackets("SELECT [Select] FROM [Tbl]");
            Assert.Equal("SELECT [Select] FROM Tbl", result);
        }

        [Fact]
        public void Roundtrip_AddThenRemove()
        {
            string original = "SELECT a, b FROM t";
            string added = BracketToggler.AddBrackets(original);
            string removed = BracketToggler.RemoveBrackets(added);
            Assert.Equal(original, removed);
        }

        [Fact]
        public void StringsAndComments_Untouched()
        {
            string sql = "SELECT 'literal' AS x -- comentario com Pessoas\r\nFROM t";
            string added = BracketToggler.AddBrackets(sql);
            // 'literal' (string) and the comment are untouched; only x and t get wrapped.
            Assert.Equal("SELECT 'literal' AS [x] -- comentario com Pessoas\r\nFROM [t]", added);
        }
    }
}
