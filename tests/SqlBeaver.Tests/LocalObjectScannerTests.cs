using System.Linq;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class LocalObjectScannerTests
    {
        private static System.Collections.Generic.IReadOnlyList<LocalTableDef> Scan(string text, int? caret = null)
            => LocalObjectScanner.Scan(text, caret ?? text.Length);

        // ---- CREATE TABLE #temp ----

        [Fact]
        public void CreateTempTable_TwoColumns_WithTypes()
        {
            const string sql = "CREATE TABLE #Orders (OrderId INT, Total DECIMAL(10,2))";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("#Orders", def.Name);
            Assert.Equal(LocalTableKind.Temp, def.Kind);
            Assert.Equal(2, def.Columns.Count);
            Assert.Equal("OrderId", def.Columns[0].Name);
            Assert.Equal("INT", def.Columns[0].SqlType);
            Assert.Equal("Total", def.Columns[1].Name);
            Assert.Equal("DECIMAL(10,2)", def.Columns[1].SqlType);
        }

        [Fact]
        public void CreateTempTable_DecimalType_Preserved()
        {
            const string sql = "CREATE TABLE #T (Price DECIMAL(18,4), Name NVARCHAR(100))";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("DECIMAL(18,4)", def.Columns[0].SqlType);
            Assert.Equal("NVARCHAR(100)", def.Columns[1].SqlType);
        }

        [Fact]
        public void CreateTempTable_ColumnWithNotNull_TypeStripped()
        {
            // Type is only the type token; NOT NULL, PRIMARY KEY etc. are ignored
            const string sql = "CREATE TABLE #T (Id INT NOT NULL, Name VARCHAR(50) NULL)";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("INT", def.Columns[0].SqlType);
            Assert.Equal("VARCHAR(50)", def.Columns[1].SqlType);
        }

        // ---- DECLARE @t TABLE ----

        [Fact]
        public void DeclareTableVar_TwoColumns()
        {
            const string sql = "DECLARE @t TABLE (Id INT, Name NVARCHAR(200))";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("@t", def.Name);
            Assert.Equal(LocalTableKind.TableVar, def.Kind);
            Assert.Equal(2, def.Columns.Count);
            Assert.Equal("Id", def.Columns[0].Name);
            Assert.Equal("INT", def.Columns[0].SqlType);
        }

        // ---- SELECT ... INTO #x ----

        [Fact]
        public void SelectInto_EmptyColumns()
        {
            const string sql = "SELECT a, b INTO #Result FROM dbo.Source";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("#Result", def.Name);
            Assert.Equal(LocalTableKind.Temp, def.Kind);
            Assert.Empty(def.Columns);
        }

        // ---- WITH cte (explicit columns) ----

        [Fact]
        public void WithCte_ExplicitColumnList()
        {
            const string sql = "WITH MyCte (Col1, Col2) AS (SELECT a, b FROM T)";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("MyCte", def.Name);
            Assert.Equal(LocalTableKind.Cte, def.Kind);
            Assert.Equal(2, def.Columns.Count);
            Assert.Equal("Col1", def.Columns[0].Name);
            Assert.Equal("Col2", def.Columns[1].Name);
            // types are empty for explicit CTE columns
            Assert.Equal("", def.Columns[0].SqlType);
        }

        // ---- WITH cte AS (SELECT ...) — heuristic ----

        [Fact]
        public void WithCte_SelectWithAliasesAndDotted()
        {
            // "a" → bare ident; "t.b AS c" → alias c; "x+1" → skip (no alias); "t.d AS d2" → d2
            const string sql = "WITH Q AS (SELECT a, t.b AS c, x+1, t.d AS d2 FROM T t)";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("Q", def.Name);
            var names = def.Columns.Select(col => col.Name).ToList();
            Assert.Contains("a", names);
            Assert.Contains("c", names);
            Assert.Contains("d2", names);
            // "x+1" without alias should NOT appear
            Assert.DoesNotContain("1", names);
        }

        // ---- Multiple CTEs ----

        [Fact]
        public void WithTwoCtes_BothReturned()
        {
            const string sql = "WITH A AS (SELECT x FROM T), B (c1, c2) AS (SELECT 1, 2)";
            var defs = Scan(sql);
            Assert.Equal(2, defs.Count);
            Assert.Equal("A", defs[0].Name);
            Assert.Equal("B", defs[1].Name);
            Assert.Equal(2, defs[1].Columns.Count);
        }

        // ---- Comments and strings ignored ----

        [Fact]
        public void CommentsAndStrings_Ignored()
        {
            const string sql = @"-- CREATE TABLE #fake (x INT)
/* DECLARE @t TABLE (y INT) */
CREATE TABLE #real (Id INT)
SELECT 'CREATE TABLE #fake2 (z INT)'";
            var defs = Scan(sql);
            var def = Assert.Single(defs);
            Assert.Equal("#real", def.Name);
        }

        // ---- Caret outside batch (after GO) does not see prior batch ----

        [Fact]
        public void CaretAfterGo_DoesNotSeePriorBatch()
        {
            const string sql = "CREATE TABLE #T (Id INT)\nGO\nSELECT 1";
            // caret is in the second batch
            int caret = sql.Length; // after "SELECT 1"
            var defs = Scan(sql, caret);
            Assert.Empty(defs);
        }

        // ---- Name casing preserved ----

        [Fact]
        public void NameCasing_Preserved()
        {
            const string sql = "CREATE TABLE #MyTable (X INT)";
            var defs = Scan(sql);
            Assert.Equal("#MyTable", defs[0].Name);
        }

        // ---- Unknown/invalid — not returned ----

        [Fact]
        public void EmptyText_ReturnsEmpty()
        {
            Assert.Empty(Scan(""));
        }

        [Fact]
        public void NoLocalObjects_ReturnsEmpty()
        {
            Assert.Empty(Scan("SELECT * FROM dbo.Orders"));
        }
    }
}
