using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Metadata;
using Xunit;

namespace SqlBeaver.Tests
{
    public class MetadataAssemblerTests
    {
        private static readonly List<TableEntry> Tables = new List<TableEntry>
        {
            new TableEntry("Cadastro", "Pessoas"),
            new TableEntry("Financeiro", "Titulos"),
        };
        private static readonly List<string> Schemas = new List<string> { "Cadastro", "Financeiro" };

        [Fact]
        public void Columns_AreGroupedByTableKey_CaseInsensitive()
        {
            var columns = new List<MetadataAssembler.ColumnRow>
            {
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "IdPessoa", "uniqueidentifier", false, true),
                new MetadataAssembler.ColumnRow("Cadastro", "Pessoas", "Nome", "varchar(250)", true, false),
                new MetadataAssembler.ColumnRow("Financeiro", "Titulos", "IdTitulo", "int", false, true),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                columns, new List<MetadataAssembler.ForeignKeyColumnRow>());

            IReadOnlyList<ColumnEntry> pessoas = md.ColumnsByTable["cadastro.pessoas"]; // case-insensitive
            Assert.Equal(2, pessoas.Count);
            Assert.Equal("IdPessoa", pessoas[0].Name);
            Assert.True(pessoas[0].IsPrimaryKey);
            Assert.False(pessoas[0].IsNullable);
            Assert.Equal("varchar(250)", pessoas[1].SqlType);
            Assert.Single(md.ColumnsByTable[DbMetadata.TableKey("Financeiro", "Titulos")]);
        }

        [Fact]
        public void CompositeFk_BecomesSingleEntry_WithAlignedColumnPairs()
        {
            var fkRows = new List<MetadataAssembler.ForeignKeyColumnRow>
            {
                new MetadataAssembler.ForeignKeyColumnRow(7, "Financeiro", "Titulos", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa"),
                new MetadataAssembler.ForeignKeyColumnRow(7, "Financeiro", "Titulos", "IdTipo", "Cadastro", "Pessoas", "IdTipo"),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), fkRows);

            ForeignKeyEntry fk = md.ForeignKeysByTable["financeiro.titulos"].Single();
            Assert.Equal(new[] { "IdPessoa", "IdTipo" }, fk.FromColumns);
            Assert.Equal(new[] { "IdPessoa", "IdTipo" }, fk.ToColumns);
        }

        [Fact]
        public void Fk_IsIndexedOnBothEnds()
        {
            var fkRows = new List<MetadataAssembler.ForeignKeyColumnRow>
            {
                new MetadataAssembler.ForeignKeyColumnRow(1, "Financeiro", "Titulos", "IdPessoa", "Cadastro", "Pessoas", "IdPessoa"),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), fkRows);

            Assert.Same(
                md.ForeignKeysByTable["Financeiro.Titulos"].Single(),
                md.ForeignKeysByTable["Cadastro.Pessoas"].Single());
        }

        [Fact]
        public void SelfReferencingFk_IsIndexedOnce()
        {
            var fkRows = new List<MetadataAssembler.ForeignKeyColumnRow>
            {
                new MetadataAssembler.ForeignKeyColumnRow(2, "Cadastro", "Pessoas", "IdPessoaPai", "Cadastro", "Pessoas", "IdPessoa"),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), fkRows);

            Assert.Single(md.ForeignKeysByTable["Cadastro.Pessoas"]);
        }

        [Fact]
        public void TablesAndSchemas_PassThrough()
        {
            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(), new List<MetadataAssembler.ForeignKeyColumnRow>());
            Assert.Equal(2, md.Tables.Count);
            Assert.Equal(2, md.Schemas.Count);
        }
    }
}
