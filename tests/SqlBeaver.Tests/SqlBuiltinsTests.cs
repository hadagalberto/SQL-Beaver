using System.Linq;
using SqlBeaver.Completion;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlBuiltinsTests
    {
        [Fact]
        public void Functions_NonEmpty_ContainsExpectedEntries()
        {
            Assert.NotEmpty(SqlBuiltins.Functions);
            var names = SqlBuiltins.Functions.Select(f => f.Name).ToList();
            Assert.Contains("GETDATE", names);
            Assert.Contains("ROW_NUMBER", names);
            Assert.Contains("ISNULL", names);
            Assert.Contains("COALESCE", names);
            Assert.Contains("STRING_AGG", names);
            Assert.Contains("TRY_CONVERT", names);
        }

        [Fact]
        public void SystemViews_NonEmpty_ContainsExpectedEntries()
        {
            Assert.NotEmpty(SqlBuiltins.SystemViews);
            Assert.Contains("sys.objects", SqlBuiltins.SystemViews);
            Assert.Contains("sys.tables", SqlBuiltins.SystemViews);
            Assert.Contains("sys.dm_exec_requests", SqlBuiltins.SystemViews);
        }

        [Fact]
        public void Functions_NoDuplicateNames()
        {
            var names = SqlBuiltins.Functions.Select(f => f.Name).ToList();
            var distinct = names.Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(distinct.Count, names.Count);
        }
    }
}
