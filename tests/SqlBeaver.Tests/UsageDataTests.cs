using System.Collections.Generic;
using SqlBeaver.Usage;
using Xunit;

namespace SqlBeaver.Tests
{
    public class UsageDataTests
    {
        private const string Db = "server1|MyDb";

        // ── Record / GetTableCount ─────────────────────────────────────────────

        [Fact]
        public void Record_AndGet_Roundtrip()
        {
            var data = UsageData.Load(null);
            data.Record(Db, new[] { "dbo.Pessoas" }, new string[0]);
            Assert.Equal(1, data.GetTableCount(Db, "dbo.Pessoas"));
        }

        [Fact]
        public void GetTableCount_Unknown_ReturnsZero()
        {
            var data = UsageData.Load(null);
            Assert.Equal(0, data.GetTableCount(Db, "dbo.NoSuchTable"));
        }

        [Fact]
        public void Record_Accumulates()
        {
            var data = UsageData.Load(null);
            data.Record(Db, new[] { "dbo.T" }, new string[0]);
            data.Record(Db, new[] { "dbo.T" }, new string[0]);
            data.Record(Db, new[] { "dbo.T" }, new string[0]);
            Assert.Equal(3, data.GetTableCount(Db, "dbo.T"));
        }

        [Fact]
        public void Record_CaseInsensitiveKey()
        {
            var data = UsageData.Load(null);
            data.Record(Db, new[] { "DBO.Pessoas" }, new string[0]);
            Assert.Equal(1, data.GetTableCount(Db, "dbo.pessoas"));
        }

        // ── Serialize / Load roundtrip ─────────────────────────────────────────

        [Fact]
        public void Serialize_Load_PreservesTableAndJoinCounts()
        {
            var data = UsageData.Load(null);
            data.Record(Db, new[] { "dbo.A", "dbo.B" }, new[] { "dbo.A+dbo.B" });
            data.Record(Db, new[] { "dbo.A" }, new string[0]);

            string json = data.Serialize();
            var loaded = UsageData.Load(json);

            Assert.Equal(2, loaded.GetTableCount(Db, "dbo.A"));
            Assert.Equal(1, loaded.GetTableCount(Db, "dbo.B"));
            Assert.Equal(1, loaded.GetJoinCount(Db, "dbo.A+dbo.B"));
        }

        [Fact]
        public void Load_InvalidJson_ReturnsEmpty()
        {
            var data = UsageData.Load("{not valid json!!!");
            Assert.Equal(0, data.GetTableCount(Db, "dbo.T"));
        }
    }
}
