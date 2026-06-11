using System.Collections.Generic;
using SqlBeaver.Navigation;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ReferenceListFormatterTests
    {
        [Fact]
        public void Format_WithReferences_ContainsHeaderAndEntries()
        {
            var refs = new List<ReferenceListFormatter.ReferencedObject>
            {
                new ReferenceListFormatter.ReferencedObject("dbo",      "usp_GetCliente"),
                new ReferenceListFormatter.ReferencedObject("Relatorio", "vw_Resumo"),
            };

            string result = ReferenceListFormatter.Format("Clientes", "SRV01", "MyDB", refs);

            Assert.Contains("Referências a 'Clientes'", result);
            Assert.Contains("[SRV01].[MyDB]", result);
            Assert.Contains("2 objeto(s):", result);
            Assert.Contains("[dbo].[usp_GetCliente]", result);
            Assert.Contains("[Relatorio].[vw_Resumo]", result);
        }

        [Fact]
        public void Format_NoReferences_ShowsZeroObjects()
        {
            var refs = new List<ReferenceListFormatter.ReferencedObject>();

            string result = ReferenceListFormatter.Format("Orphan", "SRV", "DB", refs);

            Assert.Contains("0 objeto(s):", result);
            Assert.Contains("'Orphan'", result);
        }
    }
}
