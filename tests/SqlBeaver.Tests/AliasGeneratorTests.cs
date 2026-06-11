using System;
using System.Collections.Generic;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AliasGeneratorTests
    {
        private static readonly HashSet<string> None = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [Theory]
        [InlineData("Pessoas", "p")]
        [InlineData("PessoasFisicas", "pf")]
        [InlineData("AcessoTermoAceiteLgpd", "atal")]
        [InlineData("titulos", "t")]   // sem maiúsculas: primeira letra
        [InlineData("CONFIG", "c")]    // tudo maiúsculo: primeira letra
        public void GeneratesPascalCaseInitials(string table, string expected)
        {
            Assert.Equal(expected, AliasGenerator.Generate(table, None));
        }

        [Fact]
        public void Collision_AppendsNumber()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "p" };
            Assert.Equal("p2", AliasGenerator.Generate("Pessoas", used));
        }

        [Fact]
        public void Collision_CaseInsensitive_KeepsIncrementing()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P", "p2" };
            Assert.Equal("p3", AliasGenerator.Generate("Pessoas", used));
        }

        [Fact]
        public void KeywordAlias_IsAvoided()
        {
            // "OrdensNovas" → "on" é keyword → on2
            Assert.Equal("on2", AliasGenerator.Generate("OrdensNovas", None));
        }

        [Fact]
        public void NameStartingWithNonLetter_UsesFirstLetter()
        {
            Assert.Equal("t", AliasGenerator.Generate("_Temp123", None));
        }
    }
}
