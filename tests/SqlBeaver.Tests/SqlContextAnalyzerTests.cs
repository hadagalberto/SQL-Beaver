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
    }
}
