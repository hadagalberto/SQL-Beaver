using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class KeywordCaseFixerTests
    {
        private static (bool ok, int start, int len, string repl) Fix(string text)
        {
            bool ok = KeywordCaseFixer.TryGetReplacement(text, out int start, out int len, out string repl);
            return (ok, start, len, repl);
        }

        [Theory]
        [InlineData("select ", 0, 6, "SELECT")]
        [InlineData("Select ", 0, 6, "SELECT")]
        [InlineData("sElEcT ", 0, 6, "SELECT")]
        [InlineData("select * from ", 9, 4, "FROM")]
        [InlineData("select(", 0, 6, "SELECT")]
        [InlineData("inner join ", 6, 4, "JOIN")] // INNER já foi corrigido na tecla anterior
        [InlineData("go\r\n", 0, 2, "GO")]        // Enter como separador (CRLF)
        public void Keyword_LowerOrMixed_IsReplaced(string text, int start, int len, string repl)
        {
            var result = Fix(text);
            Assert.True(result.ok);
            Assert.Equal(start, result.start);
            Assert.Equal(len, result.len);
            Assert.Equal(repl, result.repl);
        }

        [Theory]
        [InlineData("SELECT ")]      // já maiúscula: nada a fazer
        [InlineData("myselect ")]    // não é a keyword inteira
        [InlineData("selecting ")]
        [InlineData("dbo.select ")]  // parte de nome qualificado
        [InlineData("[select] ")]    // identificador bracketed
        [InlineData("\"select\" ")]  // identificador entre aspas
        [InlineData("@select ")]     // variável
        [InlineData("#select ")]     // temp table
        [InlineData("-- select ")]   // comentário de linha
        [InlineData("/* select ")]   // comentário de bloco
        [InlineData("'select ")]     // dentro de string
        [InlineData(" ")]            // sem palavra
        [InlineData("x ")]           // identificador qualquer
        public void NotApplicable_ReturnsFalse(string text)
        {
            Assert.False(Fix(text).ok);
        }
    }
}
