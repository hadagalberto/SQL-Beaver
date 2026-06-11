using System;
using SqlBeaver.Session;
using Xunit;

namespace SqlBeaver.Tests
{
    public class HistoryEntryFormatterTests
    {
        [Fact]
        public void Format_NullSql_ReturnsEmpty()
        {
            string result = HistoryEntryFormatter.Format(DateTime.Now, "srv", "db", null);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Format_EmptySql_ReturnsEmpty()
        {
            string result = HistoryEntryFormatter.Format(DateTime.Now, "srv", "db", "");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Format_ValidSql_HasExpectedStructure()
        {
            var ts = new DateTime(2024, 6, 11, 14, 32, 5);
            string result = HistoryEntryFormatter.Format(ts, "srv", "db", "SELECT 1");

            // Header line
            Assert.StartsWith("/* ===== 14:32:05  [srv].[db] ===== */\r\n", result);
            // SQL content
            Assert.Contains("SELECT 1", result);
            // Ends with two CRLF (blank line)
            Assert.EndsWith("SELECT 1\r\n\r\n", result);
        }

        [Fact]
        public void Format_NullServerAndDatabase_UsesQuestionMark()
        {
            var ts = new DateTime(2024, 6, 11, 9, 0, 0);
            string result = HistoryEntryFormatter.Format(ts, null, null, "SELECT 2");

            Assert.Contains("[?].[?]", result);
        }

        [Fact]
        public void Format_SqlWithTrailingNewlines_TrimsTrailingCrLf()
        {
            var ts = new DateTime(2024, 6, 11, 10, 0, 0);
            string result = HistoryEntryFormatter.Format(ts, "s", "d", "SELECT 3\r\n\r\n");

            // After trim, sql should not have trailing CRLF in the content part (only the appended ones)
            Assert.EndsWith("SELECT 3\r\n\r\n", result);
            // Should not have 4 consecutive CRLFs (trimmed + appended 2 = 2 total)
            Assert.DoesNotContain("SELECT 3\r\n\r\n\r\n", result);
        }
    }
}
