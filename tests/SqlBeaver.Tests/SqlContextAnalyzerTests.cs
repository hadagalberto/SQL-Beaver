using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlContextAnalyzerTests
    {
        private static SqlContext Analyze(string textBeforeCaret)
            => SqlContextAnalyzer.Analyze(textBeforeCaret, textBeforeCaret.Length);

        // ---- comentários e strings: nunca sugerir ----

        [Theory]
        [InlineData("-- FROM ")]
        [InlineData("SELECT 1 -- comentário FROM ")]
        [InlineData("/* FROM ")]
        [InlineData("/* a /* aninhado */ ainda dentro FROM ")] // T-SQL aninha /* */
        [InlineData("SELECT 'FROM ")]
        [InlineData("SELECT 'it''s FROM ")] // '' escapa a aspa; ainda dentro da string
        [InlineData("SELECT \"FROM ")]      // identificador entre aspas duplas
        [InlineData("SELECT * FROM [Ped")]  // dentro de colchetes: fora do escopo do v1
        public void InsideCommentOrString_ReturnsNone(string text)
        {
            Assert.Equal(SqlContextKind.None, Analyze(text).Kind);
        }

        [Fact]
        public void ClosedBlockComment_DoesNotBlock()
        {
            var ctx = Analyze("/* comentário */ SELECT Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
        }

        [Fact]
        public void ClosedString_DoesNotBlock()
        {
            var ctx = Analyze("SELECT 'ok', Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
        }

        // ---- identificador livre ----

        [Fact]
        public void FreeIdentifier_ReturnsPartialAndStart()
        {
            var ctx = Analyze("SELECT Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
            Assert.Equal(7, ctx.PartialStart);
        }

        [Fact]
        public void EmptyPartialWithoutKeyword_ReturnsNone()
        {
            Assert.Equal(SqlContextKind.None, Analyze("SELECT * ").Kind);
        }

        [Theory]
        [InlineData("SELECT @var")]  // variável
        [InlineData("FROM #tmp")]    // tabela temporária
        public void VariablesAndTempTables_ReturnNone(string text)
        {
            Assert.Equal(SqlContextKind.None, Analyze(text).Kind);
        }

        [Fact]
        public void EmptyText_ReturnsNone()
        {
            Assert.Equal(SqlContextKind.None, Analyze("").Kind);
        }

        // ---- FROM / JOIN / INTO / UPDATE ----

        [Theory]
        [InlineData("SELECT * FROM ")]
        [InlineData("select * from ")] // case-insensitive
        [InlineData("SELECT * FROM\n    ")] // quebra de linha como separador
        [InlineData("SELECT * FROM a INNER JOIN ")]
        [InlineData("SELECT * FROM a LEFT JOIN ")]
        [InlineData("INSERT INTO ")]
        [InlineData("UPDATE ")]
        [InlineData("DELETE FROM ")]
        public void AfterTableKeyword_EmptyPartial_ReturnsAfterFromJoin(string text)
        {
            var ctx = Analyze(text);
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal("", ctx.Partial);
            Assert.Equal(text.Length, ctx.PartialStart);
        }

        [Fact]
        public void AfterFrom_WithPartial_ReturnsPartial()
        {
            var ctx = Analyze("SELECT * FROM Ped");
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
            Assert.Equal(14, ctx.PartialStart);
        }

        [Fact]
        public void WordEndingInFrom_IsNotKeyword()
        {
            // "PERFROM" não é a keyword FROM
            var ctx = Analyze("SELECT * PERFROM Ped");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
        }

        [Fact]
        public void CaretInMiddleOfText_AnalyzesOnlyPrefix()
        {
            // caret logo após "Pe" (posição 7); o resto da linha é ignorado
            var ctx = SqlContextAnalyzer.Analyze("FROM Ped ORDER", 7);
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal("Pe", ctx.Partial);
            Assert.Equal(5, ctx.PartialStart);
        }

        // ---- schema-dot ----

        [Fact]
        public void AfterSchemaDot_EmptyPartial()
        {
            var ctx = Analyze("SELECT * FROM dbo.");
            Assert.Equal(SqlContextKind.AfterSchemaDot, ctx.Kind);
            Assert.Equal("dbo", ctx.SchemaPrefix);
            Assert.Equal("", ctx.Partial);
        }

        [Fact]
        public void AfterSchemaDot_WithPartial()
        {
            var ctx = Analyze("SELECT * FROM dbo.Ped");
            Assert.Equal(SqlContextKind.AfterSchemaDot, ctx.Kind);
            Assert.Equal("dbo", ctx.SchemaPrefix);
            Assert.Equal("Ped", ctx.Partial);
            Assert.Equal(18, ctx.PartialStart);
        }

        [Fact]
        public void AfterBracketedSchemaDot_ExtractsSchema()
        {
            var ctx = Analyze("SELECT * FROM [dbo].");
            Assert.Equal(SqlContextKind.AfterSchemaDot, ctx.Kind);
            Assert.Equal("dbo", ctx.SchemaPrefix);
        }

        [Fact]
        public void LoneDot_ReturnsNone()
        {
            Assert.Equal(SqlContextKind.None, Analyze(".").Kind);
        }

        // ---- keywords bloqueadas ----

        [Theory]
        [InlineData("EXEC ")]
        [InlineData("EXEC sp")]
        [InlineData("EXECUTE my")]
        [InlineData("USE ")]
        [InlineData("USE ma")]
        [InlineData("DECLARE ")]
        [InlineData("SELECT 1 AS ali")]
        [InlineData("CREATE PROC my")]
        public void AfterBlockedKeyword_ReturnsNone(string text)
        {
            Assert.Equal(SqlContextKind.None, Analyze(text).Kind);
        }
    }
}
