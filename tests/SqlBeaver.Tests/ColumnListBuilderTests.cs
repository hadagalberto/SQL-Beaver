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

        [Fact]
        public void NullIndent_SingleLine_BackCompat()
        {
            var selected = new List<(string, ColumnEntry)>
            {
                ("t", Col("A")),
                ("t", Col("B")),
                ("t", Col("C")),
            };
            string result = ColumnListBuilder.Build(selected, multiTable: false, continuationIndent: null);
            Assert.Equal("A, B, C", result);
        }

        [Fact]
        public void Indent_MultiLine_EachColumnOnOwnLine()
        {
            var selected = new List<(string, ColumnEntry)>
            {
                ("t", Col("A")),
                ("t", Col("B")),
                ("t", Col("C")),
            };
            string indent = new string(' ', 7);
            string result = ColumnListBuilder.Build(selected, multiTable: false, continuationIndent: indent);
            Assert.Equal("A,\n" + indent + "B,\n" + indent + "C", result);
        }

        [Fact]
        public void Indent_MultiTable_QualifiesEachColumn()
        {
            var selected = new List<(string, ColumnEntry)>
            {
                ("p", Col("Nome")),
                ("e", Col("Email")),
            };
            string indent = new string(' ', 4);
            string result = ColumnListBuilder.Build(selected, multiTable: true, continuationIndent: indent);
            Assert.Equal("p.Nome,\n" + indent + "e.Email", result);
        }
    }
}
