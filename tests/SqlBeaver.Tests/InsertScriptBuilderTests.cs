using System;
using System.Collections.Generic;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class InsertScriptBuilderTests
    {
        private static GridData Data(int rows = 2)
        {
            var cols = new List<GridColumn> { new GridColumn("Id", typeof(int)), new GridColumn("Nome", typeof(string)) };
            var data = new List<string[]>();
            for (int i = 1; i <= rows; i++) data.Add(new[] { i.ToString(), "Nome" + i });
            return new GridData(cols, data);
        }

        [Fact]
        public void BuildsInsert_WithBracketedColumns_AndTypedValues()
        {
            string script = InsertScriptBuilder.Build(Data(), "dbo.Pessoas");
            Assert.Contains("INSERT INTO dbo.Pessoas ([Id], [Nome])", script);
            Assert.Contains("(1, N'Nome1')", script);
            Assert.Contains("(2, N'Nome2')", script);
            Assert.EndsWith(";\r\n", script);
        }

        [Fact]
        public void NullCells_BecomeBareNull()
        {
            var cols = new List<GridColumn> { new GridColumn("A", typeof(string)) };
            var data = new GridData(cols, new List<string[]> { new[] { "NULL" } });
            Assert.Contains("(NULL)", InsertScriptBuilder.Build(data, "[T]"));
        }

        [Fact]
        public void Batches_AtOneThousandRows()
        {
            string script = InsertScriptBuilder.Build(Data(rows: 1001), "[T]");
            // limite do SQL Server: 1000 linhas por VALUES — segunda instrução INSERT
            int count = 0, idx = 0;
            while ((idx = script.IndexOf("INSERT INTO", idx, StringComparison.Ordinal)) >= 0) { count++; idx++; }
            Assert.Equal(2, count);
        }

        [Fact]
        public void EmptyRows_ReturnsEmptyString()
        {
            var data = new GridData(Data().Columns, new List<string[]>());
            Assert.Equal(string.Empty, InsertScriptBuilder.Build(data, "[T]"));
        }
    }
}
