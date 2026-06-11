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
            Assert.Equal(SqlContextKind.ColumnContext, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
        }

        [Fact]
        public void ClosedString_DoesNotBlock()
        {
            var ctx = Analyze("SELECT 'ok', Ped");
            Assert.Equal(SqlContextKind.ColumnContext, ctx.Kind);
            Assert.Equal("Ped", ctx.Partial);
        }

        // ---- identificador livre ----

        [Fact]
        public void AfterSelect_IsColumnContext()
        {
            var ctx = Analyze("SELECT Ped");
            Assert.Equal(SqlContextKind.ColumnContext, ctx.Kind);
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

        // ---- dot ----

        [Fact]
        public void AfterDot_EmptyPartial()
        {
            var ctx = Analyze("SELECT * FROM dbo.");
            Assert.Equal(SqlContextKind.AfterDot, ctx.Kind);
            Assert.Equal("dbo", ctx.DotPrefix);
            Assert.Equal("", ctx.Partial);
        }

        [Fact]
        public void AfterDot_WithPartial()
        {
            var ctx = Analyze("SELECT * FROM dbo.Ped");
            Assert.Equal(SqlContextKind.AfterDot, ctx.Kind);
            Assert.Equal("dbo", ctx.DotPrefix);
            Assert.Equal("Ped", ctx.Partial);
            Assert.Equal(18, ctx.PartialStart);
        }

        [Fact]
        public void AfterBracketedDot_ExtractsPrefix()
        {
            var ctx = Analyze("SELECT * FROM [dbo].");
            Assert.Equal(SqlContextKind.AfterDot, ctx.Kind);
            Assert.Equal("dbo", ctx.DotPrefix);
        }

        [Fact]
        public void LoneDot_ReturnsNone()
        {
            Assert.Equal(SqlContextKind.None, Analyze(".").Kind);
        }

        // ---- digitação livre não dispara enquanto se digita uma keyword ----

        [Theory]
        [InlineData("sele")]      // prefixo de SELECT
        [InlineData("SELE")]
        [InlineData("sel")]
        [InlineData("upd")]       // prefixo de UPDATE
        [InlineData("WHERE x = 1 ord")] // prefixo de ORDER
        [InlineData("end")]       // keyword exata
        public void FreeTyping_KeywordPrefix_ReturnsNone(string text)
        {
            Assert.Equal(SqlContextKind.None, Analyze(text).Kind);
        }

        [Fact]
        public void FreeTyping_DivergesFromKeywords_SuggestsAgain()
        {
            var ctx = Analyze("seleco"); // não é mais prefixo de SELECT
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("seleco", ctx.Partial);
        }

        [Fact]
        public void AfterFrom_KeywordLikePartial_StillSuggests()
        {
            // o guard vale só para digitação livre; pós-FROM continua sugerindo
            var ctx = Analyze("SELECT * FROM sel");
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal("sel", ctx.Partial);
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

        // ---- v2: contexto de colunas ----

        [Theory]
        [InlineData("SELECT ")]
        [InlineData("SELECT No")]
        [InlineData("WHERE ")]
        [InlineData("WHERE Nome")]
        [InlineData("ORDER BY ")]
        [InlineData("GROUP BY Da")]
        [InlineData("HAVING ")]
        [InlineData("UPDATE T SET ")]
        [InlineData("ON p")]
        [InlineData("WHERE a = 1 AND ")]
        [InlineData("WHERE a = 1 OR Va")]
        [InlineData("SELECT a, b, No")]   // vírgula nível 0
        public void ColumnTriggers_ReturnColumnContext(string text)
        {
            Assert.Equal(SqlContextKind.ColumnContext, Analyze(text).Kind);
        }

        [Fact]
        public void CommaInsideParens_IsNotColumnContext()
        {
            // vírgula dentro de IN (...): não é a lista do SELECT
            var ctx = Analyze("WHERE x IN (a, Pe");
            Assert.Equal(SqlContextKind.FreeIdentifier, ctx.Kind);
            Assert.Equal("Pe", ctx.Partial);
        }

        // ---- v2: JOIN separado de FROM ----

        [Theory]
        [InlineData("SELECT * FROM a INNER JOIN ")]
        [InlineData("SELECT * FROM a LEFT JOIN Pe")]
        [InlineData("join ")]
        public void AfterJoin_ReturnsAfterJoinKind(string text)
        {
            var ctx = Analyze(text);
            Assert.Equal(SqlContextKind.AfterJoin, ctx.Kind);
            Assert.Equal("JOIN", ctx.TriggerKeyword);
        }

        [Theory]
        [InlineData("SELECT * FROM ", "FROM")]
        [InlineData("INSERT INTO ", "INTO")]
        [InlineData("UPDATE ", "UPDATE")]
        public void AfterFromJoin_CarriesTriggerKeyword(string text, string keyword)
        {
            var ctx = Analyze(text);
            Assert.Equal(SqlContextKind.AfterFromJoin, ctx.Kind);
            Assert.Equal(keyword, ctx.TriggerKeyword);
        }
    }
}
