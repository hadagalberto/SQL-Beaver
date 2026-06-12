using System.Collections.Generic;
using SqlBeaver.Ai;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AiSchemaContextTests
    {
        private static DbMetadata BuildMetadata()
        {
            var tables = new List<TableEntry>
            {
                new TableEntry("Cadastro", "Pessoas"),
                new TableEntry("Financeiro", "Titulos"),
            };
            var schemas = new List<string> { "Cadastro", "Financeiro" };
            var columns = new List<MetadataAssembler.ColumnRow>
            {
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "IdPessoa", "int", false, true),
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "Nome", "varchar(250)", true, false),
                new MetadataAssembler.ColumnRow("Financeiro", "Titulos", "IdTitulo", "int", false, true),
            };
            return MetadataAssembler.Assemble(tables, schemas, columns,
                new List<MetadataAssembler.ForeignKeyColumnRow>());
        }

        [Fact]
        public void Scope_RendersColumnsAndPkMarker()
        {
            DbMetadata md = BuildMetadata();
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", "p") };

            string text = AiSchemaContext.Render(scope, md, AiSchemaScope.Scope);

            Assert.Equal("Tabela: Cadastro.Pessoas (IdPessoa int PK, Nome varchar(250))", text);
        }

        [Fact]
        public void None_ReturnsEmpty()
        {
            DbMetadata md = BuildMetadata();
            var scope = new List<TableRef> { new TableRef("Cadastro", "Pessoas", null) };
            Assert.Equal("", AiSchemaContext.Render(scope, md, AiSchemaScope.None));
        }

        [Fact]
        public void All_CapsAtSixty()
        {
            var tables = new List<TableEntry>();
            var schemas = new List<string> { "dbo" };
            for (int i = 0; i < 70; i++)
                tables.Add(new TableEntry("dbo", "T" + i));
            DbMetadata md = MetadataAssembler.Assemble(tables, schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>());

            string text = AiSchemaContext.Render(null, md, AiSchemaScope.All);
            string[] lines = text.Split('\n');

            Assert.Equal(61, lines.Length); // 60 tabelas + linha de truncamento
            Assert.Contains("(+10 tabelas)", text);
        }

        [Fact]
        public void Scope_TableMissingFromCache_NameOnly()
        {
            var tables = new List<TableEntry> { new TableEntry("dbo", "SemColunas") };
            var schemas = new List<string> { "dbo" };
            DbMetadata md = MetadataAssembler.Assemble(tables, schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>());

            var scope = new List<TableRef> { new TableRef("dbo", "SemColunas", null) };
            Assert.Equal("Tabela: dbo.SemColunas", AiSchemaContext.Render(scope, md, AiSchemaScope.Scope));
        }

        [Fact]
        public void Scope_MultipleTables_JoinedByNewline()
        {
            DbMetadata md = BuildMetadata();
            var scope = new List<TableRef>
            {
                new TableRef("Cadastro", "Pessoas", null),
                new TableRef("Financeiro", "Titulos", null),
            };

            string text = AiSchemaContext.Render(scope, md, AiSchemaScope.Scope);
            string[] lines = text.Split('\n');

            Assert.Equal(2, lines.Length);
            Assert.StartsWith("Tabela: Cadastro.Pessoas", lines[0]);
            Assert.StartsWith("Tabela: Financeiro.Titulos", lines[1]);
        }

        [Fact]
        public void Scope_UnqualifiedTable_ResolvedViaUniqueSchema()
        {
            DbMetadata md = BuildMetadata();
            var scope = new List<TableRef> { new TableRef(null, "Pessoas", "p") };

            string text = AiSchemaContext.Render(scope, md, AiSchemaScope.Scope);
            Assert.StartsWith("Tabela: Cadastro.Pessoas", text);
        }
    }
}
