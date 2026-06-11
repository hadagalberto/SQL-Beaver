using SqlBeaver.Usage;
using Xunit;

namespace SqlBeaver.Tests
{
    public class UsageRankerTests
    {
        // ── PairKey ───────────────────────────────────────────────────────────

        [Fact]
        public void PairKey_OrderIndependent()
        {
            string ab = UsageRanker.PairKey("dbo.Pessoas", "dbo.Titulos");
            string ba = UsageRanker.PairKey("dbo.Titulos", "dbo.Pessoas");
            Assert.Equal(ab, ba);
        }

        [Fact]
        public void PairKey_CaseInsensitiveOrdering()
        {
            // "dbo.a" < "dbo.B" in OrdinalIgnoreCase → "dbo.a" comes first
            string lower = UsageRanker.PairKey("dbo.a", "dbo.B");
            string upper = UsageRanker.PairKey("dbo.B", "dbo.a");
            Assert.Equal(lower, upper);
            Assert.StartsWith("dbo.a+", lower);
        }

        [Fact]
        public void PairKey_UsesPlus_Separator()
        {
            string key = UsageRanker.PairKey("s.A", "s.B");
            Assert.Contains("+", key);
        }

        // ── TableSortText ─────────────────────────────────────────────────────

        [Fact]
        public void TableSortText_UsedComesBeforeUnused()
        {
            string used = UsageRanker.TableSortText(1, "Alpha");
            string unused = UsageRanker.TableSortText(0, "Alpha");
            Assert.True(string.Compare(used, unused, System.StringComparison.Ordinal) < 0,
                "used sort text should sort before unused");
        }

        [Fact]
        public void TableSortText_HigherCountSortsBeforeLowerCount()
        {
            string count10 = UsageRanker.TableSortText(10, "Alpha");
            string count2 = UsageRanker.TableSortText(2, "Alpha");
            // count 10 → inverse 999989, count 2 → inverse 999997
            // "1_999989_..." < "1_999997_..." → count10 sorts first
            Assert.True(string.Compare(count10, count2, System.StringComparison.Ordinal) < 0,
                "count 10 should sort before count 2");
        }

        [Fact]
        public void TableSortText_UnusedAlphabetical()
        {
            string alpha = UsageRanker.TableSortText(0, "Alpha");
            string beta = UsageRanker.TableSortText(0, "Beta");
            Assert.True(string.Compare(alpha, beta, System.StringComparison.Ordinal) < 0,
                "Alpha should sort before Beta among unused");
        }

        [Fact]
        public void TableSortText_UnusedStartsWith5()
        {
            string s = UsageRanker.TableSortText(0, "Foo");
            Assert.StartsWith("5_", s);
        }
    }
}
