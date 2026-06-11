using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlIdentifierTests
    {
        [Theory]
        [InlineData("Pessoas", "Pessoas")]
        [InlineData("Minha Tabela", "[Minha Tabela]")]
        [InlineData("Estranho]Nome", "[Estranho]]Nome]")]
        [InlineData("Com-Hifen", "[Com-Hifen]")]
        public void BracketsOnlyWhenNeeded(string name, string expected)
        {
            Assert.Equal(expected, SqlIdentifier.Bracket(name));
        }
    }
}
