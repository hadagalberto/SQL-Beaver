using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Metadata;
using SqlBeaver.Usage;
using Xunit;

namespace SqlBeaver.Tests
{
    public class UsedTablesExtractorTests
    {
        // ── Metadata helpers ──────────────────────────────────────────────────

        private static DbMetadata SimpleMetadata(params (string schema, string table)[] tables)
        {
            var tableEntries = new List<TableEntry>();
            foreach (var (schema, table) in tables)
                tableEntries.Add(new TableEntry(schema, table));

            var schemas = new HashSet<string>();
            foreach (var e in tableEntries)
                schemas.Add(e.Schema);

            return MetadataAssembler.Assemble(
                tableEntries,
                new List<string>(schemas),
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>());
        }

        private static DbMetadata Db2Tables =>
            SimpleMetadata(("dbo", "Pessoas"), ("dbo", "Titulos"));

        // ── Basic FROM ────────────────────────────────────────────────────────

        [Fact]
        public void SingleFrom_CountsTable()
        {
            var r = UsedTablesExtractor.Extract("SELECT * FROM dbo.Pessoas", Db2Tables);
            Assert.Single(r.TableKeys);
            Assert.Equal("dbo.Pessoas", r.TableKeys[0], ignoreCase: true);
        }

        [Fact]
        public void UnresolvedUnqualified_IsIgnored()
        {
            // "Unknown" is not in metadata
            var r = UsedTablesExtractor.Extract("SELECT * FROM Unknown", Db2Tables);
            Assert.Empty(r.TableKeys);
        }

        [Fact]
        public void ResolvedUnqualified_ViaMetadata()
        {
            // "Pessoas" is unique in metadata → resolves to dbo.Pessoas
            var r = UsedTablesExtractor.Extract("SELECT * FROM Pessoas", Db2Tables);
            Assert.Single(r.TableKeys);
            Assert.Equal("dbo.Pessoas", r.TableKeys[0], ignoreCase: true);
        }

        // ── JOIN pairs ────────────────────────────────────────────────────────

        [Fact]
        public void TwoTableJoin_YieldsOnePair_OrderIndependent()
        {
            var r = UsedTablesExtractor.Extract(
                "SELECT * FROM dbo.Pessoas p JOIN dbo.Titulos t ON t.IdPessoa = p.IdPessoa",
                Db2Tables);

            Assert.Equal(2, r.TableKeys.Count);
            Assert.Single(r.JoinPairKeys);

            // Same pair regardless of scan order
            string expected = UsageRanker.PairKey("dbo.Pessoas", "dbo.Titulos");
            Assert.Equal(expected, r.JoinPairKeys[0], ignoreCase: true);
        }

        [Fact]
        public void ThreeTables_YieldsThreePairs()
        {
            var meta = SimpleMetadata(("dbo", "A"), ("dbo", "B"), ("dbo", "C"));
            var r = UsedTablesExtractor.Extract(
                "SELECT * FROM dbo.A JOIN dbo.B ON 1=1 JOIN dbo.C ON 1=1",
                meta);

            Assert.Equal(3, r.TableKeys.Count);
            Assert.Equal(3, r.JoinPairKeys.Count);
        }

        // ── Dedup across statements ───────────────────────────────────────────

        [Fact]
        public void TablesRepeatedAcrossStatements_CountedOnce()
        {
            string sql = "SELECT * FROM dbo.Pessoas; SELECT * FROM dbo.Pessoas;";
            var r = UsedTablesExtractor.Extract(sql, Db2Tables);
            Assert.Single(r.TableKeys);
        }

        [Fact]
        public void PairsRepeatedAcrossStatements_CountedOnce()
        {
            string sql =
                "SELECT * FROM dbo.Pessoas JOIN dbo.Titulos ON 1=1;" +
                "SELECT * FROM dbo.Titulos JOIN dbo.Pessoas ON 1=1;";
            var r = UsedTablesExtractor.Extract(sql, Db2Tables);
            Assert.Single(r.JoinPairKeys);
        }

        // ── Comments and strings ──────────────────────────────────────────────

        [Fact]
        public void CommentsAndStrings_Ignored()
        {
            // "FROM dbo.Titulos" inside a comment and string must not be extracted
            string sql =
                "-- FROM dbo.Titulos\n" +
                "SELECT 'FROM dbo.Titulos' FROM dbo.Pessoas";
            var r = UsedTablesExtractor.Extract(sql, Db2Tables);
            Assert.Single(r.TableKeys);
            Assert.Equal("dbo.Pessoas", r.TableKeys[0], ignoreCase: true);
        }

        // ── GO batch separator ────────────────────────────────────────────────

        [Fact]
        public void GoBatchSeparator_SplitsStatements()
        {
            string sql = "SELECT * FROM dbo.Pessoas\nGO\nSELECT * FROM dbo.Titulos\nGO";
            var r = UsedTablesExtractor.Extract(sql, Db2Tables);
            Assert.Equal(2, r.TableKeys.Count);
            // Two single-table statements → no pairs
            Assert.Empty(r.JoinPairKeys);
        }

        // ── 7+ tables → no pairs ──────────────────────────────────────────────

        [Fact]
        public void SevenOrMoreTables_NoPairsGenerated_ButTablesStillCounted()
        {
            var meta = SimpleMetadata(
                ("dbo", "T1"), ("dbo", "T2"), ("dbo", "T3"), ("dbo", "T4"),
                ("dbo", "T5"), ("dbo", "T6"), ("dbo", "T7"));

            string sql = "SELECT * FROM dbo.T1 " +
                         "JOIN dbo.T2 ON 1=1 JOIN dbo.T3 ON 1=1 JOIN dbo.T4 ON 1=1 " +
                         "JOIN dbo.T5 ON 1=1 JOIN dbo.T6 ON 1=1 JOIN dbo.T7 ON 1=1";

            var r = UsedTablesExtractor.Extract(sql, meta);
            Assert.Equal(7, r.TableKeys.Count);
            Assert.Empty(r.JoinPairKeys);
        }

        // ── Null / empty ──────────────────────────────────────────────────────

        [Fact]
        public void NullSql_ReturnsEmptyResult()
        {
            var r = UsedTablesExtractor.Extract(null, Db2Tables);
            Assert.Empty(r.TableKeys);
            Assert.Empty(r.JoinPairKeys);
        }

        [Fact]
        public void EmptySql_ReturnsEmptyResult()
        {
            var r = UsedTablesExtractor.Extract("", Db2Tables);
            Assert.Empty(r.TableKeys);
            Assert.Empty(r.JoinPairKeys);
        }
    }
}
