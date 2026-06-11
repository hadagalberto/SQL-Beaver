using SqlBeaver.Formatting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class FormatStyleResolverTests
    {
        // ── ResolveActive ─────────────────────────────────────────────────────────

        [Fact]
        public void ResolveActive_ExactMatch_ReturnsActive()
        {
            var styles = new[] { "Padrao", "Compacto", "Verbose" };
            Assert.Equal("Compacto", FormatStyleResolver.ResolveActive(styles, "Compacto"));
        }

        [Fact]
        public void ResolveActive_CaseInsensitiveMatch_ReturnsStoredCasing()
        {
            var styles = new[] { "Padrao", "Compacto" };
            Assert.Equal("Compacto", FormatStyleResolver.ResolveActive(styles, "compacto"));
        }

        [Fact]
        public void ResolveActive_ActiveMissing_ReturnsFirst()
        {
            var styles = new[] { "Padrao", "Compacto" };
            Assert.Equal("Padrao", FormatStyleResolver.ResolveActive(styles, "NaoExiste"));
        }

        [Fact]
        public void ResolveActive_EmptyList_ReturnsNull()
        {
            Assert.Null(FormatStyleResolver.ResolveActive(new string[0], "Padrao"));
        }

        [Fact]
        public void ResolveActive_NullList_ReturnsNull()
        {
            Assert.Null(FormatStyleResolver.ResolveActive(null, "Padrao"));
        }

        [Fact]
        public void ResolveActive_NullRequested_ReturnsFirst()
        {
            var styles = new[] { "Alpha", "Beta" };
            Assert.Equal("Alpha", FormatStyleResolver.ResolveActive(styles, null));
        }

        // ── ToFileName ────────────────────────────────────────────────────────────

        [Fact]
        public void ToFileName_CleanName_AppendsJsonExtension()
        {
            Assert.Equal("Padrao.json", FormatStyleResolver.ToFileName("Padrao"));
        }

        [Fact]
        public void ToFileName_InvalidChars_Sanitized()
        {
            string result = FormatStyleResolver.ToFileName("My/Style:Name?");
            Assert.NotNull(result);
            Assert.EndsWith(".json", result);
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            string nameOnly = result.Substring(0, result.Length - 5);
            foreach (char c in invalid)
                Assert.DoesNotContain(c.ToString(), nameOnly);
        }

        [Fact]
        public void ToFileName_Null_ReturnsNull()
        {
            Assert.Null(FormatStyleResolver.ToFileName(null));
        }

        [Fact]
        public void ToFileName_Empty_ReturnsNull()
        {
            Assert.Null(FormatStyleResolver.ToFileName(""));
        }

        // ── FromFileName ──────────────────────────────────────────────────────────

        [Fact]
        public void FromFileName_StripsDotJson()
        {
            Assert.Equal("Padrao", FormatStyleResolver.FromFileName("Padrao.json"));
        }

        [Fact]
        public void FromFileName_CaseInsensitiveExtension()
        {
            Assert.Equal("Compacto", FormatStyleResolver.FromFileName("Compacto.JSON"));
        }

        [Fact]
        public void FromFileName_NoExtension_ReturnsAsIs()
        {
            Assert.Equal("Padrao", FormatStyleResolver.FromFileName("Padrao"));
        }

        [Fact]
        public void FromFileName_Null_ReturnsNull()
        {
            Assert.Null(FormatStyleResolver.FromFileName(null));
        }
    }
}
