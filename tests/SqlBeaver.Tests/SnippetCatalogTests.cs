using SqlBeaver.Snippets;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SnippetCatalogTests
    {
        [Fact]
        public void Defaults_ContainCoreShortcuts()
        {
            var catalog = SnippetCatalog.Load(null);
            Assert.True(catalog.ContainsKey("ssf"));
            Assert.True(catalog.ContainsKey("st100"));
            Assert.True(catalog.ContainsKey("cte"));
            Assert.Equal("SELECT * FROM $cursor$", catalog["ssf"].Expansion);
        }

        [Fact]
        public void Lookup_IsCaseInsensitive()
        {
            var catalog = SnippetCatalog.Load(null);
            Assert.True(catalog.ContainsKey("SSF"));
        }

        [Fact]
        public void UserJson_OverridesDefaultsByShortcut_AndAddsNew()
        {
            string json = @"{""snippets"":[
                {""shortcut"":""ssf"",""title"":""Meu SSF"",""expansion"":""SELECT TOP 5 * FROM $cursor$"",""description"":""custom""},
                {""shortcut"":""xx"",""title"":""Novo"",""expansion"":""EXEC xx $cursor$"",""description"":""novo""}]}";

            var catalog = SnippetCatalog.Load(json);
            Assert.Equal("SELECT TOP 5 * FROM $cursor$", catalog["ssf"].Expansion);
            Assert.Equal("EXEC xx $cursor$", catalog["xx"].Expansion);
            Assert.True(catalog.ContainsKey("st100")); // defaults preservados
        }

        [Fact]
        public void InvalidJson_FallsBackToDefaults()
        {
            var catalog = SnippetCatalog.Load("{not json");
            Assert.True(catalog.ContainsKey("ssf"));
            Assert.Equal("SELECT * FROM $cursor$", catalog["ssf"].Expansion);
        }

        [Fact]
        public void UserEntryWithoutShortcutOrExpansion_IsIgnored()
        {
            string json = @"{""snippets"":[{""title"":""quebrado""}]}";
            var catalog = SnippetCatalog.Load(json);
            Assert.True(catalog.ContainsKey("ssf"));
        }
    }
}
