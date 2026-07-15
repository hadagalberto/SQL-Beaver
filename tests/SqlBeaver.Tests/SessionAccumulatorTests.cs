using System.Linq;
using SqlBeaver.Session;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SessionAccumulatorTests
    {
        private static SessionEntry Upsert(SessionAccumulator acc, string caption, string hash,
            string server = null, string database = null, string savedAt = "2024-06-11T10:00:00")
        {
            string tabPath = acc.Upsert(caption, hash, server, database, savedAt);
            return acc.Entries.First(e => e.Caption == caption && e.File == tabPath);
        }

        // ── Upsert new ────────────────────────────────────────────────────────

        [Fact]
        public void Upsert_NewCaption_AppendsAndAssignsTabName()
        {
            var acc = new SessionAccumulator();
            string tabPath = acc.Upsert("query1", "h1", null, null, "2024-06-11T10:00:00");

            Assert.Equal("tab-01.sql", tabPath);
            Assert.Single(acc.Entries);
            Assert.Equal("query1", acc.Entries[0].Caption);
            Assert.Equal("tab-01.sql", acc.Entries[0].File);
        }

        [Fact]
        public void Upsert_SameCaption_UpdatesInPlaceSameTabNamePreservesOrder()
        {
            var acc = new SessionAccumulator();
            acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00");
            acc.Upsert("B", "h2", null, null, "2024-06-11T10:00:01");

            string tabPath = acc.Upsert("A", "h1-new", "srv", "db", "2024-06-11T11:00:00");

            Assert.Equal("tab-01.sql", tabPath); // same stable tab name
            Assert.Equal(2, acc.Entries.Count);
            // order preserved (A first, B second)
            Assert.Equal("A", acc.Entries[0].Caption);
            Assert.Equal("B", acc.Entries[1].Caption);
            // content updated in place
            Assert.Equal("h1-new", acc.Entries[0].ContentHash);
            Assert.Equal("srv", acc.Entries[0].Server);
            Assert.Equal("db", acc.Entries[0].Database);
        }

        [Fact]
        public void Upsert_TwoCaptions_GetDistinctTabNames()
        {
            var acc = new SessionAccumulator();
            string t1 = acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00");
            string t2 = acc.Upsert("B", "h2", null, null, "2024-06-11T10:00:01");

            Assert.Equal("tab-01.sql", t1);
            Assert.Equal("tab-02.sql", t2);
            Assert.NotEqual(t1, t2);
        }

        // ── Remove ────────────────────────────────────────────────────────────

        [Fact]
        public void Remove_ExistingCaption_DropsItReturnsTrue()
        {
            var acc = new SessionAccumulator();
            acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00");
            acc.Upsert("B", "h2", null, null, "2024-06-11T10:00:01");

            bool removed = acc.Remove("A");

            Assert.True(removed);
            Assert.Single(acc.Entries);
            Assert.Equal("B", acc.Entries[0].Caption);
        }

        [Fact]
        public void Remove_UnknownCaption_ReturnsFalse()
        {
            var acc = new SessionAccumulator();
            acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00");

            Assert.False(acc.Remove("ghost"));
            Assert.Single(acc.Entries);
        }

        [Fact]
        public void Remove_FreesTabNumberForReuseByNextNewCaption()
        {
            var acc = new SessionAccumulator();
            acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00"); // tab-01
            acc.Upsert("B", "h2", null, null, "2024-06-11T10:00:01"); // tab-02
            acc.Remove("A"); // frees tab-01

            string tNew = acc.Upsert("C", "h3", null, null, "2024-06-11T10:00:02");

            Assert.Equal("tab-01.sql", tNew); // smallest unused reused
        }

        // ── Order / Clear / content ────────────────────────────────────────────

        [Fact]
        public void Entries_OrderPreservedAcrossUpserts()
        {
            var acc = new SessionAccumulator();
            acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00");
            acc.Upsert("B", "h2", null, null, "2024-06-11T10:00:01");
            acc.Upsert("C", "h3", null, null, "2024-06-11T10:00:02");
            // re-upsert middle one
            acc.Upsert("B", "h2b", null, null, "2024-06-11T11:00:00");

            Assert.Equal(new[] { "A", "B", "C" }, acc.Entries.Select(e => e.Caption).ToArray());
        }

        [Fact]
        public void Clear_EmptiesEntries()
        {
            var acc = new SessionAccumulator();
            acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00");
            acc.Upsert("B", "h2", null, null, "2024-06-11T10:00:01");

            acc.Clear();

            Assert.Empty(acc.Entries);
            // after clear, tab numbering restarts from 01
            Assert.Equal("tab-01.sql", acc.Upsert("C", "h3", null, null, "2024-06-11T10:00:02"));
        }

        [Fact]
        public void Entries_ReflectContent()
        {
            var acc = new SessionAccumulator();
            SessionEntry e = Upsert(acc, "A", "hashA", "myserver", "mydb", "2024-06-11T10:00:00");

            Assert.Equal("A", e.Caption);
            Assert.Equal("hashA", e.ContentHash);
            Assert.Equal("myserver", e.Server);
            Assert.Equal("mydb", e.Database);
            Assert.Equal("2024-06-11T10:00:00", e.SavedAt);
            Assert.Equal("tab-01.sql", e.File);
        }

        [Fact]
        public void Upsert_CarriesOriginalPath_AndUpdatesIt()
        {
            var acc = new SessionAccumulator();
            acc.Upsert("A", "h1", null, null, "2024-06-11T10:00:00", @"C:\scripts\a.sql");
            Assert.Equal(@"C:\scripts\a.sql", acc.Entries[0].OriginalPath);

            // upsert do mesmo caption sem caminho (virou rascunho) atualiza para null
            acc.Upsert("A", "h2", null, null, "2024-06-11T10:05:00", null);
            Assert.Null(acc.Entries[0].OriginalPath);
        }
    }
}
