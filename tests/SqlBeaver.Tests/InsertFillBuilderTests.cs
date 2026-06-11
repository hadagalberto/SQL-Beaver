using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class InsertFillBuilderTests
    {
        private static ColumnEntry Col(string name) => new ColumnEntry(name, "int", false, false);

        [Fact]
        public void TwoColumns_ProducesExpectedText()
        {
            var cols = new List<ColumnEntry> { Col("Nome"), Col("Email") };
            string result = InsertFillBuilder.Build("[dbo].[Clientes]", cols);

            Assert.Contains("[dbo].[Clientes] (Nome, Email)", result);
            Assert.Contains("VALUES (", result);
            Assert.Contains("/* Nome */", result);
            Assert.Contains("/* Email */", result);
        }

        [Fact]
        public void Empty_ReturnsMinimalInsert()
        {
            var result = InsertFillBuilder.Build("[dbo].[Tabela]", new List<ColumnEntry>());
            Assert.Contains("[dbo].[Tabela] ()", result);
            Assert.Contains("VALUES (", result);
        }

        [Fact]
        public void NameNeedingBrackets_IsBracketed()
        {
            // "Order" has no special chars so SqlIdentifier.Bracket leaves it as-is;
            // "My Name" has a space so it gets bracketed.
            var cols = new List<ColumnEntry> { Col("Order"), Col("My Name") };
            string result = InsertFillBuilder.Build("Tabela", cols);
            Assert.Contains("Order", result);
            Assert.Contains("[My Name]", result);
        }

        [Fact]
        public void ThirtyOneCols_CapAt30AndShowsExtra()
        {
            var cols = new List<ColumnEntry>();
            for (int i = 1; i <= 31; i++)
                cols.Add(Col("Col" + i));
            string result = InsertFillBuilder.Build("T", cols);
            // The 31st column is not shown individually
            Assert.Contains("/* +1 colunas */", result);
            // Only 30 columns in the list individually + overflow marker
            Assert.DoesNotContain("Col31 */", result);
        }

        [Fact]
        public void ThirtyTwoCols_ShowsTwoPlusExtra()
        {
            var cols = new List<ColumnEntry>();
            for (int i = 1; i <= 32; i++)
                cols.Add(Col("Col" + i));
            string result = InsertFillBuilder.Build("T", cols);
            Assert.Contains("/* +2 colunas */", result);
        }

        [Fact]
        public void OrderPreserved()
        {
            var cols = new List<ColumnEntry> { Col("B"), Col("A"), Col("C") };
            string result = InsertFillBuilder.Build("T", cols);
            int posB = result.IndexOf("/* B */");
            int posA = result.IndexOf("/* A */");
            int posC = result.IndexOf("/* C */");
            Assert.True(posB < posA && posA < posC);
        }
    }
}
