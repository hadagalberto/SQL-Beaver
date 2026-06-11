using SqlBeaver.Snippets;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SnippetEngineTests
    {
        private static readonly System.Collections.Generic.IReadOnlyDictionary<string, SnippetDefinition> Catalog =
            SnippetCatalog.Load(null);

        [Fact]
        public void ExpandsShortcut_BeforeCaret()
        {
            bool ok = SnippetEngine.TryExpand("ssf", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal(0, e.WordStart);
            Assert.Equal(3, e.WordLength);
            Assert.Equal("SELECT * FROM ", e.ReplacementText);     // $cursor$ removido
            Assert.Equal("SELECT * FROM ".Length, e.CaretOffset);  // caret no marcador
        }

        [Fact]
        public void CursorMarker_InMiddle_PlacesCaret()
        {
            bool ok = SnippetEngine.TryExpand("wh", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal("WHERE ", e.ReplacementText);
            Assert.Equal(6, e.CaretOffset);
        }

        [Fact]
        public void ShortcutAfterOtherText_UsesWordSpan()
        {
            bool ok = SnippetEngine.TryExpand("SELECT 1; ssf", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal(10, e.WordStart);
            Assert.Equal(3, e.WordLength);
        }

        [Theory]
        [InlineData("xyzo")]            // não é shortcut
        [InlineData("")]                // vazio
        [InlineData("-- ssf")]          // comentário
        [InlineData("'ssf")]            // string
        [InlineData("dbo.ssf")]         // qualificado
        [InlineData("@ssf")]            // variável
        public void NotApplicable_ReturnsFalse(string text)
        {
            Assert.False(SnippetEngine.TryExpand(text, Catalog, out _));
        }

        [Fact]
        public void ExpansionWithoutMarker_CaretAtEnd()
        {
            var catalog = SnippetCatalog.Load(
                @"{""snippets"":[{""shortcut"":""zz"",""title"":""t"",""expansion"":""ABC"",""description"":""d""}]}");
            Assert.True(SnippetEngine.TryExpand("zz", catalog, out SnippetExpansion e));
            Assert.Equal("ABC", e.ReplacementText);
            Assert.Equal(3, e.CaretOffset);
        }
    }
}
