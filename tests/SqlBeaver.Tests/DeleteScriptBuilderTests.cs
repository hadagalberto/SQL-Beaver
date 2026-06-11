using System.Collections.Generic;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class DeleteScriptBuilderTests
    {
        private static GridData Data2Rows()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("Id", typeof(int)),
                new GridColumn("Nome", typeof(string))
            };
            var rows = new List<string[]>
            {
                new[] { "1", "Alice" },
                new[] { "2", "Bob" }
            };
            return new GridData(cols, rows);
        }

        [Fact]
        public void BuildsDelete_OnePerRow_WithPkInWhere()
        {
            string script = DeleteScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", new[] { "Id" });
            Assert.Contains("DELETE FROM dbo.Pessoas WHERE [Id] = 1;", script);
            Assert.Contains("DELETE FROM dbo.Pessoas WHERE [Id] = 2;", script);
        }

        [Fact]
        public void MultiplePk_AllInWhere()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("PedidoId", typeof(int)),
                new GridColumn("ItemId", typeof(int)),
                new GridColumn("Qty", typeof(int))
            };
            var rows = new List<string[]> { new[] { "10", "20", "5" } };
            string script = DeleteScriptBuilder.Build(
                new GridData(cols, rows), "dbo.Itens", new[] { "PedidoId", "ItemId" });

            Assert.Contains("WHERE [PedidoId] = 10 AND [ItemId] = 20", script);
        }

        [Fact]
        public void EmptyPkColumns_EmitsPlaceholderAndComment()
        {
            string script = DeleteScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", new string[0]);
            Assert.Contains("ATENÇÃO", script);
            Assert.Contains("1 = 0", script);
        }

        [Fact]
        public void NullPkColumns_EmitsPlaceholder()
        {
            string script = DeleteScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", null);
            Assert.Contains("1 = 0", script);
        }

        [Fact]
        public void EmptyRows_ReturnsEmpty()
        {
            var data = new GridData(Data2Rows().Columns, new List<string[]>());
            Assert.Equal(string.Empty, DeleteScriptBuilder.Build(data, "[T]", new[] { "Id" }));
        }

        [Fact]
        public void BracketNamesWithSpecialChars()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("My Id", typeof(int)),
                new GridColumn("Nome", typeof(string))
            };
            var rows = new List<string[]> { new[] { "1", "X" } };
            string script = DeleteScriptBuilder.Build(
                new GridData(cols, rows), "[T]", new[] { "My Id" });
            Assert.Contains("[My Id] = 1", script);
        }
    }
}
