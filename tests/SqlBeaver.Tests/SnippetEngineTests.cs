using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Snippets;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SnippetEngineTests
    {
        private static readonly IReadOnlyDictionary<string, SnippetDefinition> Catalog =
            SnippetCatalog.Load(null);

        // ── Testes legados (mantidos intactos) ────────────────────────────────

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

        // ── Novos testes de placeholders ──────────────────────────────────────

        [Fact]
        public void SingleDollar1_EmptyPlaceholder_OffsetAndLengthZero()
        {
            // "$1$texto" → ReplacementText="texto", placeholder order=1 offset=0 length=0
            SnippetEngine.ParseExpansion("$1$texto", out string text, out var placeholders, out int caret);
            Assert.Equal("texto", text);
            Assert.Single(placeholders);
            Assert.Equal(0, placeholders[0].Offset);
            Assert.Equal(0, placeholders[0].Length);
            Assert.Equal(1, placeholders[0].Order);
            Assert.Equal(0, caret); // caret = offset do placeholder 1
        }

        [Fact]
        public void TwoOrderedPlaceholders_TwoSpans()
        {
            // "A$1$B$2$C"
            SnippetEngine.ParseExpansion("A$1$B$2$C", out string text, out var placeholders, out int caret);
            Assert.Equal("ABC", text);
            Assert.Equal(2, placeholders.Count);
            // placeholder 1 em offset 1 (depois do 'A')
            Assert.Equal(1, placeholders[0].Order);
            Assert.Equal(1, placeholders[0].Offset);
            Assert.Equal(0, placeholders[0].Length);
            // placeholder 2 em offset 2 (depois do 'B')
            Assert.Equal(2, placeholders[1].Order);
            Assert.Equal(2, placeholders[1].Offset);
            Assert.Equal(0, placeholders[1].Length);
            Assert.Equal(1, caret); // caret aponta pro placeholder 1
        }

        [Fact]
        public void DefaultTextPlaceholder_LengthMatchesDefault()
        {
            // "${1:col}$" → placeholder order=1, default="col", length=3
            SnippetEngine.ParseExpansion("${1:col}$", out string text, out var placeholders, out int caret);
            Assert.Equal("col", text);
            Assert.Single(placeholders);
            Assert.Equal(1, placeholders[0].Order);
            Assert.Equal(0, placeholders[0].Offset);
            Assert.Equal(3, placeholders[0].Length);
            Assert.Equal(0, caret);
        }

        [Fact]
        public void DollarZero_FinalCaretPosition()
        {
            // "abc$0$" → caret at offset 3
            SnippetEngine.ParseExpansion("abc$0$", out string text, out var placeholders, out int caret);
            Assert.Equal("abc", text);
            Assert.Single(placeholders);
            Assert.Equal(0, placeholders[0].Order);
            Assert.Equal(3, placeholders[0].Offset);
            Assert.Equal(3, caret); // $0$ = caret final
        }

        [Fact]
        public void Mixed_NumberedAndFinalCaret_CaretAtLowest()
        {
            // "$1$foo$0$" → caret em 0 (placeholder 1)
            SnippetEngine.ParseExpansion("$1$foo$0$", out string text, out var placeholders, out int caret);
            Assert.Equal("foo", text);
            Assert.Equal(2, placeholders.Count);
            Assert.Equal(0, caret); // caret = placeholder 1 (lowest non-zero)
        }

        [Fact]
        public void CursorMarker_TreatedAsOrder0()
        {
            // Catálogo: ssf usa $cursor$, que é synonym de $0$
            // Testa via TryExpand com snippet custom que tem só $cursor$
            var catalog = SnippetCatalog.Load(
                @"{""snippets"":[{""shortcut"":""aa"",""title"":""t"",""expansion"":""X$cursor$Y"",""description"":""d""}]}");
            Assert.True(SnippetEngine.TryExpand("aa", catalog, out SnippetExpansion e));
            Assert.Equal("XY", e.ReplacementText);
            Assert.Equal(1, e.CaretOffset); // entre X e Y
            // placeholder de ordem 0 registrado
            Assert.Single(e.Placeholders);
            Assert.Equal(0, e.Placeholders[0].Order);
        }

        [Fact]
        public void LegacySnippetsWithOnlyCursor_Unchanged()
        {
            // wh = "WHERE $cursor$" → deve continuar funcionando
            bool ok = SnippetEngine.TryExpand("wh", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal("WHERE ", e.ReplacementText);
            Assert.Equal(6, e.CaretOffset);
        }

        [Fact]
        public void NoMarkers_EmptyPlaceholderList_CaretAtEnd()
        {
            SnippetEngine.ParseExpansion("NOMARKERS", out string text, out var placeholders, out int caret);
            Assert.Equal("NOMARKERS", text);
            Assert.Empty(placeholders);
            Assert.Equal(9, caret);
        }

        [Fact]
        public void SameOrderTwice_MirrorFields_TwoSpans()
        {
            // "${1:a}$ foo ${1:a}$" → dois spans de ordem 1
            SnippetEngine.ParseExpansion("${1:a}$ foo ${1:a}$", out string text, out var placeholders, out int caret);
            Assert.Equal("a foo a", text);
            var order1 = placeholders.Where(p => p.Order == 1).ToList();
            Assert.Equal(2, order1.Count);
            Assert.Equal(0, order1[0].Offset); // primeira ocorrência em offset 0
            Assert.Equal(6, order1[1].Offset); // segunda em offset 6 ("a foo " = 6 chars)
        }

        [Fact]
        public void IitSnippet_HasThreePlaceholders()
        {
            // iit agora usa ${1:tabela}$, ${2:colunas}$, ${3:valores}$, $0$
            bool ok = SnippetEngine.TryExpand("iit", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            // deve ter 4 spans (ordens 1, 2, 3 e 0)
            Assert.Equal(4, e.Placeholders.Count);
            var orders = e.Placeholders.Select(p => p.Order).ToList();
            Assert.Contains(1, orders);
            Assert.Contains(2, orders);
            Assert.Contains(3, orders);
            Assert.Contains(0, orders);
            // caret aponta pro placeholder 1 (primeiro campo editável)
            var p1 = e.Placeholders.First(p => p.Order == 1);
            Assert.Equal(p1.Offset, e.CaretOffset);
        }

        [Fact]
        public void JnSnippet_HasOnePlaceholderAndFinalCaret()
        {
            // jn = "INNER JOIN ${1:tabela}$ ON $0$"
            bool ok = SnippetEngine.TryExpand("jn", Catalog, out SnippetExpansion e);
            Assert.True(ok);
            Assert.Equal("INNER JOIN tabela ON ", e.ReplacementText);
            Assert.Equal(2, e.Placeholders.Count); // ordem 1 + ordem 0
            var p1 = e.Placeholders.First(p => p.Order == 1);
            Assert.Equal(6, p1.Length); // "tabela".Length == 6
            Assert.Equal(p1.Offset, e.CaretOffset);
        }
    }
}
