using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class NameMatchJoinSuggesterTests
    {
        private static DbMetadata BuildMetadata(params MetadataAssembler.ColumnRow[] colRows)
        {
            var tables = new List<TableEntry>
            {
                new TableEntry("dbo", "Pedidos"),
                new TableEntry("dbo", "Clientes"),
                new TableEntry("dbo", "Itens"),
            };
            return MetadataAssembler.Assemble(
                tables,
                new List<string> { "dbo" },
                new List<MetadataAssembler.ColumnRow>(colRows),
                new List<MetadataAssembler.ForeignKeyColumnRow>());
        }

        [Fact]
        public void ExactIdMatch_Suggests()
        {
            var meta = BuildMetadata(
                new MetadataAssembler.ColumnRow("dbo", "Pedidos", "ClienteId", "int", false, false),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "ClienteId", "int", false, true));

            var scope = new List<TableRef> { new TableRef("dbo", "Pedidos", "p") };
            var result = NameMatchJoinSuggester.Suggest(scope, meta, new string[0]);
            Assert.Single(result);
            var s = result[0];
            Assert.Equal("Clientes", s.Table);
            Assert.Contains("ClienteId", s.OnClause);
        }

        [Fact]
        public void NonIdNonPkColumn_NotSuggested()
        {
            var meta = BuildMetadata(
                new MetadataAssembler.ColumnRow("dbo", "Pedidos", "Nome", "varchar(100)", true, false),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "Nome", "varchar(100)", true, false));

            var scope = new List<TableRef> { new TableRef("dbo", "Pedidos", "p") };
            var result = NameMatchJoinSuggester.Suggest(scope, meta, new string[0]);
            Assert.Empty(result);
        }

        [Fact]
        public void ScopeTable_NotSuggestedForItself()
        {
            var meta = BuildMetadata(
                new MetadataAssembler.ColumnRow("dbo", "Pedidos", "PedidoId", "int", false, true),
                new MetadataAssembler.ColumnRow("dbo", "Itens", "PedidoId", "int", false, false));

            // Pedidos is in scope; it should not suggest Pedidos as a JOIN target
            var scope = new List<TableRef>
            {
                new TableRef("dbo", "Pedidos", "p"),
                new TableRef("dbo", "Itens", "i"),
            };
            var result = NameMatchJoinSuggester.Suggest(scope, meta, new string[0]);
            // Both are in scope so nothing to suggest
            Assert.Empty(result);
        }

        [Fact]
        public void FkCoveredPair_Excluded()
        {
            var meta = BuildMetadata(
                new MetadataAssembler.ColumnRow("dbo", "Pedidos", "ClienteId", "int", false, false),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "ClienteId", "int", false, true));

            var scope = new List<TableRef> { new TableRef("dbo", "Pedidos", "p") };

            // Build FK pair key the same way as the suggester would
            string fkPairKey = "dbo.Clientes|dbo.Pedidos|ClienteId"; // canonical lowercase
            var result = NameMatchJoinSuggester.Suggest(scope, meta, new[] { fkPairKey });
            Assert.Empty(result);
        }

        [Fact]
        public void AliasGenerated()
        {
            var meta = BuildMetadata(
                new MetadataAssembler.ColumnRow("dbo", "Pedidos", "ClienteId", "int", false, false),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "ClienteId", "int", false, true));

            var scope = new List<TableRef> { new TableRef("dbo", "Pedidos", "p") };
            var result = NameMatchJoinSuggester.Suggest(scope, meta, new string[0]);
            Assert.Single(result);
            Assert.NotEmpty(result[0].Alias);
        }

        [Fact]
        public void NoMatchingColumns_ReturnsEmpty()
        {
            var meta = BuildMetadata(
                new MetadataAssembler.ColumnRow("dbo", "Pedidos", "NumeroId", "int", false, false),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "OutroId", "int", false, false));

            var scope = new List<TableRef> { new TableRef("dbo", "Pedidos", "p") };
            var result = NameMatchJoinSuggester.Suggest(scope, meta, new string[0]);
            Assert.Empty(result);
        }

        [Fact]
        public void InsertTextContainsQualifiedTableAndOnClause()
        {
            var meta = BuildMetadata(
                new MetadataAssembler.ColumnRow("dbo", "Pedidos", "ClienteId", "int", false, false),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "ClienteId", "int", false, true));

            var scope = new List<TableRef> { new TableRef("dbo", "Pedidos", "p") };
            var result = NameMatchJoinSuggester.Suggest(scope, meta, new string[0]);
            Assert.Single(result);
            Assert.Contains("dbo", result[0].InsertText);
            Assert.Contains("Clientes", result[0].InsertText);
            Assert.Contains("ON", result[0].InsertText);
        }
    }
}
