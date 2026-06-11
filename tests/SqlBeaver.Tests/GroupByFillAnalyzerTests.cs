using System.Collections.Generic;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class GroupByFillAnalyzerTests
    {
        private static IReadOnlyList<string> Analyze(string textBeforeCaret)
            => GroupByFillAnalyzer.NonAggregatedSelectColumns(textBeforeCaret, textBeforeCaret.Length);

        [Fact]
        public void SimpleTwoCols_WithAggregate_ReturnsTwoNonAgg()
        {
            string sql = "SELECT a, b, COUNT(*) FROM t GROUP BY ";
            var result = Analyze(sql);
            Assert.Equal(2, result.Count);
            Assert.Contains("a", result);
            Assert.Contains("b", result);
        }

        [Fact]
        public void AllAggregates_ReturnsEmpty()
        {
            string sql = "SELECT SUM(x), COUNT(*) FROM t GROUP BY ";
            var result = Analyze(sql);
            Assert.Empty(result);
        }

        [Fact]
        public void ColumnWithAlias_ReturnsExpression()
        {
            // "SELECT a AS x, b" → should return "a" and "b"
            string sql = "SELECT a AS x, b FROM t GROUP BY ";
            var result = Analyze(sql);
            Assert.Equal(2, result.Count);
            Assert.Contains("a", result);
            Assert.Contains("b", result);
        }

        [Fact]
        public void QualifiedColumn_Preserved()
        {
            string sql = "SELECT t.a, t.b, SUM(t.val) FROM t GROUP BY ";
            var result = Analyze(sql);
            Assert.Equal(2, result.Count);
            Assert.Contains("t.a", result);
            Assert.Contains("t.b", result);
        }

        [Fact]
        public void NonAggExpression_Kept()
        {
            // YEAR(d) is not an aggregate — should be kept
            string sql = "SELECT YEAR(d), SUM(x) FROM t GROUP BY ";
            var result = Analyze(sql);
            Assert.Single(result);
            Assert.Contains("YEAR(d)", result);
        }

        [Fact]
        public void EmptyText_ReturnsEmpty()
        {
            var result = GroupByFillAnalyzer.NonAggregatedSelectColumns("", 0);
            Assert.Empty(result);
        }

        [Fact]
        public void MultipleAggFunctions_AllStripped()
        {
            string sql = "SELECT a, b, AVG(x), MIN(y), MAX(z) FROM t GROUP BY ";
            var result = Analyze(sql);
            Assert.Equal(2, result.Count);
        }
    }
}
