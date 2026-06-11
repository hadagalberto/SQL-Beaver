using SqlBeaver.Snippets;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SnippetShortcutValidatorTests
    {
        private static readonly string[] Existing = { "sel", "ins", "upd" };

        [Fact]
        public void IsValidUnique_Empty_Invalid()
        {
            Assert.False(SnippetShortcutValidator.IsValidUnique("", Existing));
            Assert.False(SnippetShortcutValidator.IsValidUnique("   ", Existing));
            Assert.False(SnippetShortcutValidator.IsValidUnique(null, Existing));
        }

        [Fact]
        public void IsValidUnique_DuplicateCaseInsensitive_Invalid()
        {
            Assert.False(SnippetShortcutValidator.IsValidUnique("SEL", Existing));
            Assert.False(SnippetShortcutValidator.IsValidUnique("ins", Existing));
        }

        [Fact]
        public void IsValidUnique_UniqueShortcut_Valid()
        {
            Assert.True(SnippetShortcutValidator.IsValidUnique("del", Existing));
        }

        [Fact]
        public void IsValidUnique_WhitespaceTrimmed_DetectsDuplicate()
        {
            Assert.False(SnippetShortcutValidator.IsValidUnique("  sel  ", Existing));
        }

        [Fact]
        public void IsValidUnique_NullExisting_TreatedAsEmpty()
        {
            Assert.True(SnippetShortcutValidator.IsValidUnique("any", null));
        }
    }
}
