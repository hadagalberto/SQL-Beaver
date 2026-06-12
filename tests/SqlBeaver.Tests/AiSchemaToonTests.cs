using System.Collections.Generic;
using SqlBeaver.Ai;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AiSchemaToonTests
    {
        private static DbMetadata BuildMetadata()
        {
            var tables = new List<TableEntry>
            {
                new TableEntry("Cadastro", "Pessoas"),
                new TableEntry("Financeiro", "Debitos"),
            };
            var schemas = new List<string> { "Cadastro", "Financeiro" };
            var columns = new List<MetadataAssembler.ColumnRow>
            {
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "IdPessoa", "int", false, true),
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "Nome", "varchar(250)", true, false),
                new MetadataAssembler.ColumnRow("Financeiro", "Debitos", "Valor", "decimal(10,2)", false, false),
            };
            return MetadataAssembler.Assemble(tables, schemas, columns,
                new List<MetadataAssembler.ForeignKeyColumnRow>());
        }

        [Fact]
        public void EncodeFull_EmitsTabularToon_WithHeadersAndCounts()
        {
            string toon = AiSchemaToon.EncodeFull(BuildMetadata());

            Assert.Contains("tables[2]{schema,name}:", toon);
            Assert.Contains("  Cadastro,Pessoas", toon);
            Assert.Contains("  Financeiro,Debitos", toon);
            Assert.Contains("columns[3]{schema,table,name,type,pk}:", toon);
            Assert.Contains("  Cadastro,Pessoas,IdPessoa,int,1", toon);   // pk=1
            Assert.Contains("  Cadastro,Pessoas,Nome,varchar(250),0", toon);
        }

        [Fact]
        public void EncodeFull_QuotesTypeContainingComma()
        {
            string toon = AiSchemaToon.EncodeFull(BuildMetadata());
            // decimal(10,2) tem vírgula → precisa de aspas para não quebrar a linha tabular.
            Assert.Contains("Financeiro,Debitos,Valor,\"decimal(10,2)\",0", toon);
        }

        [Fact]
        public void EncodeFull_NullMetadata_ReturnsEmpty()
        {
            Assert.Equal("", AiSchemaToon.EncodeFull(null));
        }

        [Fact]
        public void EncodeSubset_OnlyScopeTables()
        {
            DbMetadata md = BuildMetadata();
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };

            string toon = AiSchemaToon.EncodeSubset(scope, md);

            Assert.Contains("tables[1]{schema,name}:", toon);
            Assert.Contains("  Cadastro,Pessoas", toon);
            Assert.DoesNotContain("Financeiro,Debitos", toon);
        }

        [Fact]
        public void Esc_QuotesAndEscapes()
        {
            Assert.Equal("int", AiSchemaToon.Esc("int"));                       // simples: sem aspas
            Assert.Equal("\"decimal(10,2)\"", AiSchemaToon.Esc("decimal(10,2)")); // vírgula → aspas
            Assert.Equal("\"\"", AiSchemaToon.Esc(""));                          // vazio → aspas vazias
            Assert.Equal("\"a\\\"b\"", AiSchemaToon.Esc("a\"b"));                // aspas internas escapadas
        }
    }
}
