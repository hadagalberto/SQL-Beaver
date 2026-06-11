using System;
using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Session;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SessionIndexTests
    {
        // ── Load ──────────────────────────────────────────────────────────────

        [Fact]
        public void Load_Null_ReturnsEmpty()
        {
            IReadOnlyList<SessionEntry> result = SessionIndex.Load(null);
            Assert.Empty(result);
        }

        [Fact]
        public void Load_InvalidJson_ReturnsEmpty()
        {
            IReadOnlyList<SessionEntry> result = SessionIndex.Load("{not json at all");
            Assert.Empty(result);
        }

        [Fact]
        public void Load_ValidJson_ReturnsEntries()
        {
            string json = "{\"entries\":[{\"file\":\"snap-abc.sql\",\"caption\":\"query1\",\"server\":\"s\",\"database\":\"d\",\"savedAt\":\"2024-06-11T10:00:00\",\"contentHash\":\"aabbccdd\"}]}";
            IReadOnlyList<SessionEntry> result = SessionIndex.Load(json);
            Assert.Single(result);
            Assert.Equal("snap-abc.sql", result[0].File);
            Assert.Equal("query1", result[0].Caption);
        }

        // ── Serialize round-trip ──────────────────────────────────────────────

        [Fact]
        public void Serialize_LoadRoundTrip_PreservesData()
        {
            var entries = new List<SessionEntry>
            {
                new SessionEntry { File = "snap-1.sql", Caption = "A", Server = "s1", Database = "d1", SavedAt = "2024-06-11T10:00:00.0000000", ContentHash = "hash1" },
                new SessionEntry { File = "snap-2.sql", Caption = "B", Server = "s2", Database = "d2", SavedAt = "2024-06-11T09:00:00.0000000", ContentHash = "hash2" }
            };

            string json = SessionIndex.Serialize(entries);
            IReadOnlyList<SessionEntry> loaded = SessionIndex.Load(json);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("snap-1.sql", loaded[0].File);
            Assert.Equal("A", loaded[0].Caption);
            Assert.Equal("hash1", loaded[0].ContentHash);
        }

        // ── Upsert ───────────────────────────────────────────────────────────

        [Fact]
        public void Upsert_NewCaption_Appends()
        {
            var existing = new List<SessionEntry>
            {
                new SessionEntry { File = "f1.sql", Caption = "A", SavedAt = "2024-06-10T10:00:00", ContentHash = "h1" }
            };
            var newEntry = new SessionEntry { File = "f2.sql", Caption = "B", SavedAt = "2024-06-11T10:00:00", ContentHash = "h2" };

            IReadOnlyList<SessionEntry> result = SessionIndex.Upsert(existing, newEntry);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, e => e.Caption == "B");
        }

        [Fact]
        public void Upsert_SameCaptionSameHash_KeepsOriginal()
        {
            var original = new SessionEntry { File = "f1.sql", Caption = "A", SavedAt = "2024-06-10T08:00:00", ContentHash = "samehash" };
            var existing = new List<SessionEntry> { original };

            var duplicateEntry = new SessionEntry { File = "f1-new.sql", Caption = "A", SavedAt = "2024-06-11T10:00:00", ContentHash = "samehash" };

            IReadOnlyList<SessionEntry> result = SessionIndex.Upsert(existing, duplicateEntry);

            Assert.Single(result);
            // File must remain the original (not updated)
            Assert.Equal("f1.sql", result[0].File);
            Assert.Equal("2024-06-10T08:00:00", result[0].SavedAt);
        }

        [Fact]
        public void Upsert_SameCaptionDifferentHash_Replaces()
        {
            var existing = new List<SessionEntry>
            {
                new SessionEntry { File = "f1.sql", Caption = "A", SavedAt = "2024-06-10T08:00:00", ContentHash = "oldhash" }
            };
            var updated = new SessionEntry { File = "f1-new.sql", Caption = "A", SavedAt = "2024-06-11T10:00:00", ContentHash = "newhash" };

            IReadOnlyList<SessionEntry> result = SessionIndex.Upsert(existing, updated);

            Assert.Single(result);
            Assert.Equal("f1-new.sql", result[0].File);
            Assert.Equal("newhash", result[0].ContentHash);
        }

        [Fact]
        public void Upsert_CapAt50_RemovesOldestWhenOver50()
        {
            // Create 50 existing entries with distinct captions and timestamps
            var existing = new List<SessionEntry>();
            for (int i = 0; i < 50; i++)
            {
                existing.Add(new SessionEntry
                {
                    File = $"f{i}.sql",
                    Caption = $"cap{i:D3}",
                    SavedAt = $"2024-06-11T{i:D2}:00:00",
                    ContentHash = $"hash{i}"
                });
            }

            // Add a new (51st) entry with a future timestamp
            var newEntry = new SessionEntry
            {
                File = "fnew.sql",
                Caption = "capNew",
                SavedAt = "2024-06-12T00:00:00",
                ContentHash = "hashnew"
            };

            IReadOnlyList<SessionEntry> result = SessionIndex.Upsert(existing, newEntry);

            Assert.Equal(50, result.Count);
            // The newest entry must be present
            Assert.Contains(result, e => e.Caption == "capNew");
        }
    }
}
