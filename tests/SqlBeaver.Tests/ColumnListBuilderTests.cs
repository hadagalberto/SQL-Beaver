using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ColumnListBuilderTests
    {
        private static ColumnEntry Col(string name) => new ColumnEntry(name, "int", false, false);

        [Fact]
        public void SingleTable_NoQualifier()
        {
            var selected = new List<(string, ColumnEntry)>
            {
                ("p", Col("Nome")),
                ("p", Col("Email")),
            };
            string result = ColumnListBuilder.Build(selected, multiTable: false);
            Assert.Equal("Nome, Email", result);
        }

        [Fact]
        public void MultiTable_WithQualifier()
        {
            var selected = new List<(string, ColumnEntry)>
            {
                ("p", Col("Nome")),
                ("e", Col("Email")),
            };
            string result = ColumnListBuilder.Build(selected, multiTable: true);
            Assert.Equal("p.Nome, e.Email", result);
        }

        [Fact]
        public void ColumnNeedingBrackets_IsBracketed()
        {
            // "My Col" has a space → SqlIdentifier.Bracket produces [My Col]
            var selected = new List<(string, ColumnEntry)>
            {
                ("t", Col("My Col")),
            };
            string result = ColumnListBuilder.Build(selected, multiTable: true);
            Assert.Equal("t.[My Col]", result);
        }

        [Fact]
        public void OrderPreserved()
        {
            var selected = new List<(string, ColumnEntry)>
            {
                ("t", Col("C")),
                ("t", Col("A")),
                ("t", Col("B")),
            };
            string result = ColumnListBuilder.Build(selected, multiTable: false);
            Assert.Equal("C, A, B", result);
        }
    }
}
