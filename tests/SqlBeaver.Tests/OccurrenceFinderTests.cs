using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class OccurrenceFinderTests
    {
        // ---- IdentifierAt ----

        [Fact]
        public void IdentifierAt_MidWord_ReturnsWord()
        {
            // "SELECT pessoa" — caret no meio de "pessoa"
            string text = "SELECT pessoa FROM t";
            // 'e' em "pessoa" está na posição 8
            Assert.Equal("pessoa", OccurrenceFinder.IdentifierAt(text, 9));
        }

        [Fact]
        public void IdentifierAt_AtStart_ReturnsWord()
        {
            string text = "pessoa FROM t";
            Assert.Equal("pessoa", OccurrenceFinder.IdentifierAt(text, 0));
        }

        [Fact]
        public void IdentifierAt_EndExclusive_ReturnsWord()
        {
            // Caret imediatamente após "pessoa" (posição 6)
            string text = "pessoa FROM t";
            Assert.Equal("pessoa", OccurrenceFinder.IdentifierAt(text, 6));
        }

        [Fact]
        public void IdentifierAt_OnWhitespace_ReturnsNull()
        {
            string text = "SELECT  FROM";
            // posição 7 é espaço
            Assert.Null(OccurrenceFinder.IdentifierAt(text, 7));
        }

        [Fact]
        public void IdentifierAt_InsideString_ReturnsNull()
        {
            // 'pessoa' dentro de uma string SQL
            string text = "SELECT 'pessoa' FROM t";
            // posição 9 está dentro da string
            Assert.Null(OccurrenceFinder.IdentifierAt(text, 9));
        }

        [Fact]
        public void IdentifierAt_AtVariable_ReturnsWithAt()
        {
            string text = "DECLARE @param INT";
            // posição 9 está em "@param"
            Assert.Equal("@param", OccurrenceFinder.IdentifierAt(text, 9));
        }

        // ---- FindAll ----

        [Fact]
        public void FindAll_ThreeOccurrences()
        {
            string text = "p + p * p";
            var results = OccurrenceFinder.FindAll(text, "p");
            // name.Length < 2 → vazio (anti-ruído)
            Assert.Empty(results);
        }

        [Fact]
        public void FindAll_TwoLetterName_FindsAll()
        {
            string text = "id + id * id";
            var results = OccurrenceFinder.FindAll(text, "id");
            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void FindAll_CaseInsensitive()
        {
            string text = "SELECT Nome, NOME, nome FROM t";
            var results = OccurrenceFinder.FindAll(text, "nome");
            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void FindAll_NoSubstringMatch()
        {
            // "pe" não deve casar dentro de "pessoa"
            string text = "SELECT pe, pessoa FROM t";
            var results = OccurrenceFinder.FindAll(text, "pe");
            var only = Assert.Single(results);
            Assert.Equal(7, only.Start); // posição de "pe" isolado
        }

        [Fact]
        public void FindAll_SkipsInsideString()
        {
            string text = "SELECT nome FROM t WHERE nome = 'nome fora'";
            var results = OccurrenceFinder.FindAll(text, "nome");
            // "nome" em posição 7 e em posição 24 — dentro da string não conta
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void FindAll_SkipsInsideLineComment()
        {
            string text = "SELECT nome -- nome comentado\nFROM t";
            var results = OccurrenceFinder.FindAll(text, "nome");
            var only = Assert.Single(results);
            Assert.Equal(7, only.Start);
        }

        [Fact]
        public void FindAll_SkipsInsideBlockComment()
        {
            string text = "SELECT nome /* nome oculto */ FROM t";
            var results = OccurrenceFinder.FindAll(text, "nome");
            Assert.Single(results);
        }

        [Fact]
        public void FindAll_NameLengthOne_ReturnsEmpty()
        {
            // Anti-ruído: nomes de 1 char geram false positives demais
            string text = "SELECT a, b, c FROM t WHERE a = b";
            var results = OccurrenceFinder.FindAll(text, "a");
            Assert.Empty(results);
        }

        [Fact]
        public void FindAll_AtVariable_IncludesAtSign()
        {
            string text = "SET @id = 1; SELECT @id";
            var results = OccurrenceFinder.FindAll(text, "@id");
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void FindAll_CorrectStartAndLength()
        {
            string text = "FROM nome WHERE nome > 0";
            var results = OccurrenceFinder.FindAll(text, "nome");
            Assert.Equal(2, results.Count);
            Assert.Equal(5,  results[0].Start);
            Assert.Equal(4,  results[0].Length);
            Assert.Equal(16, results[1].Start);
            Assert.Equal(4,  results[1].Length);
        }
    }
}
