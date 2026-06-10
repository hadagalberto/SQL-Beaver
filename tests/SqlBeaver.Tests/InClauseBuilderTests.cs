using System;
using System.Collections.Generic;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class InClauseBuilderTests
    {
        [Fact]
        public void Strings_QuotedAndDeduped_PreservingOrder()
        {
            var result = InClauseBuilder.Build(new List<string> { "a", "b", "a" }, typeof(string));
            Assert.Equal("(N'a', N'b')", result);
        }

        [Fact]
        public void Numbers_Unquoted()
        {
            var result = InClauseBuilder.Build(new List<string> { "1", "2" }, typeof(int));
            Assert.Equal("(1, 2)", result);
        }

        [Fact]
        public void NullDisplayValues_AreSkipped()
        {
            var result = InClauseBuilder.Build(new List<string> { "a", "NULL", "b" }, typeof(string));
            Assert.Equal("(N'a', N'b')", result);
        }

        [Fact]
        public void Empty_ReturnsEmptyParens()
        {
            Assert.Equal("()", InClauseBuilder.Build(new List<string>(), typeof(string)));
        }

        [Fact]
        public void ManyValues_WrapLines()
        {
            var values = new List<string>();
            for (int i = 1; i <= 25; i++) values.Add(i.ToString());
            string result = InClauseBuilder.Build(values, typeof(int));
            Assert.StartsWith("(1, ", result);
            Assert.Contains("\r\n", result); // quebra de linha a cada 10 valores
            Assert.EndsWith("25)", result);
        }
    }
}
