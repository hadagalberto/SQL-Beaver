using System.Collections.Generic;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class UpdateScriptBuilderTests
    {
        private static GridData Data2Rows()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("Id", typeof(int)),
                new GridColumn("Nome", typeof(string)),
                new GridColumn("Ativo", typeof(bool))
            };
            var rows = new List<string[]>
            {
                new[] { "1", "Alice", "1" },
                new[] { "2", "NULL", "0" }
            };
            return new GridData(cols, rows);
        }

        [Fact]
        public void BuildsUpdate_WithPk_InWhere_AndNonPk_InSet()
        {
            string script = UpdateScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", new[] { "Id" });
            // Two UPDATEs
            Assert.Contains("UPDATE dbo.Pessoas SET", script);
            // PK in WHERE
            Assert.Contains("WHERE [Id] = 1", script);
            Assert.Contains("WHERE [Id] = 2", script);
            // Non-PK in SET
            Assert.Contains("[Nome] = N'Alice'", script);
            Assert.Contains("[Ativo] = 1", script);
            // NULLs
            Assert.Contains("[Nome] = NULL", script);
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
            string script = UpdateScriptBuilder.Build(
                new GridData(cols, rows), "dbo.Itens", new[] { "PedidoId", "ItemId" });

            Assert.Contains("WHERE [PedidoId] = 10 AND [ItemId] = 20", script);
            Assert.Contains("[Qty] = 5", script);
        }

        [Fact]
        public void EmptyPkColumns_EmitsPlaceholderAndComment()
        {
            string script = UpdateScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", new string[0]);
            Assert.Contains("ATENÇÃO", script);
            Assert.Contains("1 = 0", script);
        }

        [Fact]
        public void NullPkColumns_EmitsPlaceholder()
        {
            string script = UpdateScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", null);
            Assert.Contains("1 = 0", script);
        }

        [Fact]
        public void EmptyRows_ReturnsEmpty()
        {
            var data = new GridData(Data2Rows().Columns, new List<string[]>());
            Assert.Equal(string.Empty, UpdateScriptBuilder.Build(data, "[T]", new[] { "Id" }));
        }

        [Fact]
        public void BracketNamesWithSpecialChars()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("My Id", typeof(int)),
                new GridColumn("My Value", typeof(string))
            };
            var rows = new List<string[]> { new[] { "1", "x" } };
            string script = UpdateScriptBuilder.Build(
                new GridData(cols, rows), "[T]", new[] { "My Id" });
            Assert.Contains("[My Id] = 1", script);
            Assert.Contains("[My Value] = N'x'", script);
        }
    }
}
