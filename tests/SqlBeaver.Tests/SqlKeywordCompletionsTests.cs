using System;
using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Completion;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlKeywordCompletionsTests
    {
        [Fact]
        public void Keywords_IsNonEmpty_AndContainsCommonOnes()
        {
            IReadOnlyList<string> keywords = SqlKeywordCompletions.Keywords;
            Assert.NotEmpty(keywords);
            Assert.Contains("SELECT", keywords);
            Assert.Contains("FROM", keywords);
            Assert.Contains("INNER JOIN", keywords);
        }

        [Fact]
        public void Keywords_AreAllUppercase()
        {
            foreach (string keyword in SqlKeywordCompletions.Keywords)
                Assert.Equal(keyword.ToUpperInvariant(), keyword);
        }

        [Fact]
        public void Keywords_HasNoDuplicates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string keyword in SqlKeywordCompletions.Keywords)
                Assert.True(seen.Add(keyword), $"keyword duplicada: {keyword}");
        }
    }
}
