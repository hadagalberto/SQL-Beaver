using System.Collections.Generic;
using SqlBeaver.Session;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SnapshotSearchFilterTests
    {
        private static IReadOnlyList<SnapshotRow> Sample() => new List<SnapshotRow>
        {
            new SnapshotRow { Caption = "Consulta de clientes", ContentText = "SELECT * FROM Cliente" },
            new SnapshotRow { Caption = "Relatorio mensal",     ContentText = "SELECT SUM(Total) FROM Pedido" },
            new SnapshotRow { Caption = "Script de manutencao", ContentText = "UPDATE Estoque SET Qtd = 0" },
        };

        [Fact]
        public void Filter_CaptionMatch_ReturnsRow()
        {
            IReadOnlyList<SnapshotRow> result = SnapshotSearchFilter.Filter(Sample(), "Relatorio");
            Assert.Single(result);
            Assert.Equal("Relatorio mensal", result[0].Caption);
        }

        [Fact]
        public void Filter_ContentMatch_ReturnsRow()
        {
            IReadOnlyList<SnapshotRow> result = SnapshotSearchFilter.Filter(Sample(), "Pedido");
            Assert.Single(result);
            Assert.Equal("Relatorio mensal", result[0].Caption);
        }

        [Fact]
        public void Filter_CaseInsensitive_Matches()
        {
            IReadOnlyList<SnapshotRow> result = SnapshotSearchFilter.Filter(Sample(), "cLiEnTe");
            Assert.Single(result);
            Assert.Equal("Consulta de clientes", result[0].Caption);
        }

        [Fact]
        public void Filter_EmptyQuery_ReturnsAll()
        {
            IReadOnlyList<SnapshotRow> result = SnapshotSearchFilter.Filter(Sample(), "");
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void Filter_NullQuery_ReturnsAll()
        {
            IReadOnlyList<SnapshotRow> result = SnapshotSearchFilter.Filter(Sample(), null);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void Filter_NoMatch_ReturnsEmpty()
        {
            IReadOnlyList<SnapshotRow> result = SnapshotSearchFilter.Filter(Sample(), "inexistente");
            Assert.Empty(result);
        }

        [Fact]
        public void Filter_NullRows_ReturnsEmpty()
        {
            IReadOnlyList<SnapshotRow> result = SnapshotSearchFilter.Filter(null, "x");
            Assert.Empty(result);
        }
    }
}
