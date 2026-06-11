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

        // --- ObjectEntry / Objects tests ---

        [Fact]
        public void Objects_AreMappedWithCorrectTypes()
        {
            var objectRows = new List<MetadataAssembler.ObjectRow>
            {
                new MetadataAssembler.ObjectRow("dbo", "usp_GetPessoa", "P"),
                new MetadataAssembler.ObjectRow("dbo", "vw_Relatorio",  "V"),
                new MetadataAssembler.ObjectRow("dbo", "fn_Calc",       "FN"),
                new MetadataAssembler.ObjectRow("dbo", "tvf_Lista",     "IF"),
                new MetadataAssembler.ObjectRow("dbo", "tvf_Lista2",    "TF"),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                objectRows);

            Assert.Equal(5, md.Objects.Count);
            Assert.Equal(DbObjectType.Procedure,      md.Objects[0].Type);
            Assert.Equal(DbObjectType.View,           md.Objects[1].Type);
            Assert.Equal(DbObjectType.ScalarFunction, md.Objects[2].Type);
            Assert.Equal(DbObjectType.TableFunction,  md.Objects[3].Type);
            Assert.Equal(DbObjectType.TableFunction,  md.Objects[4].Type);
            Assert.Equal("dbo",         md.Objects[0].Schema);
            Assert.Equal("usp_GetPessoa", md.Objects[0].Name);
        }

        [Fact]
        public void Objects_UnknownTypeCode_IsSkipped()
        {
            var objectRows = new List<MetadataAssembler.ObjectRow>
            {
                new MetadataAssembler.ObjectRow("dbo", "usp_Known",   "P"),
                new MetadataAssembler.ObjectRow("dbo", "obj_Unknown", "X"),  // deve ser ignorado
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                objectRows);

            Assert.Single(md.Objects);
            Assert.Equal("usp_Known", md.Objects[0].Name);
        }

        [Fact]
        public void CompatCtors_Objects_IsEmpty()
        {
            // ctor 2-param
            var md2 = new DbMetadata(Schemas, Tables);
            Assert.Empty(md2.Objects);

            // ctor 4-param (via 4-arg Assemble)
            DbMetadata md4 = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>());
            Assert.Empty(md4.Objects);
        }

        // --- ParameterEntry / ParametersByObject tests ---

        [Fact]
        public void Parameters_AreGroupedByObjectKey()
        {
            var paramRows = new List<MetadataAssembler.ParameterRow>
            {
                new MetadataAssembler.ParameterRow("dbo", "sp_Foo", "@Id",   "int",          false, 1),
                new MetadataAssembler.ParameterRow("dbo", "sp_Foo", "@Nome", "varchar(100)", false, 2),
                new MetadataAssembler.ParameterRow("dbo", "sp_Bar", "@Val",  "int",          false, 1),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                new List<MetadataAssembler.ObjectRow>(),
                paramRows);

            Assert.True(md.ParametersByObject.ContainsKey("dbo.sp_Foo"));
            Assert.Equal(2, md.ParametersByObject["dbo.sp_Foo"].Count);
            Assert.Equal("@Id",   md.ParametersByObject["dbo.sp_Foo"][0].Name);
            Assert.Equal("@Nome", md.ParametersByObject["dbo.sp_Foo"][1].Name);
            Assert.Single(md.ParametersByObject["dbo.sp_Bar"]);
        }

        [Fact]
        public void Parameters_OrderedByOrdinal()
        {
            // rows chegam fora de ordem
            var paramRows = new List<MetadataAssembler.ParameterRow>
            {
                new MetadataAssembler.ParameterRow("dbo", "sp_Foo", "@C", "int", false, 3),
                new MetadataAssembler.ParameterRow("dbo", "sp_Foo", "@A", "int", false, 1),
                new MetadataAssembler.ParameterRow("dbo", "sp_Foo", "@B", "int", false, 2),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                new List<MetadataAssembler.ObjectRow>(),
                paramRows);

            IReadOnlyList<ParameterEntry> ps = md.ParametersByObject["dbo.sp_Foo"];
            Assert.Equal("@A", ps[0].Name);
            Assert.Equal("@B", ps[1].Name);
            Assert.Equal("@C", ps[2].Name);
        }

        [Fact]
        public void Parameters_OutputFlag_Preserved()
        {
            var paramRows = new List<MetadataAssembler.ParameterRow>
            {
                new MetadataAssembler.ParameterRow("dbo", "sp_Foo", "@In",  "int", false, 1),
                new MetadataAssembler.ParameterRow("dbo", "sp_Foo", "@Out", "int", true,  2),
            };

            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                new List<MetadataAssembler.ObjectRow>(),
                paramRows);

            IReadOnlyList<ParameterEntry> ps = md.ParametersByObject["dbo.sp_Foo"];
            Assert.False(ps[0].IsOutput);
            Assert.True(ps[1].IsOutput);
        }

        [Fact]
        public void Parameters_Empty_ParametersByObjectIsEmpty()
        {
            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                new List<MetadataAssembler.ObjectRow>(),
                new List<MetadataAssembler.ParameterRow>());

            Assert.Empty(md.ParametersByObject);
        }

        [Fact]
        public void CompatAssemble_5Args_ParametersByObjectIsEmpty()
        {
            DbMetadata md = MetadataAssembler.Assemble(Tables, Schemas,
                new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                new List<MetadataAssembler.ObjectRow>());

            Assert.Empty(md.ParametersByObject);
        }

        [Fact]
        public void CompatCtors_ParametersByObjectIsEmpty()
        {
            var md2 = new DbMetadata(Schemas, Tables);
            Assert.Empty(md2.ParametersByObject);

            var md5 = new DbMetadata(Schemas, Tables,
                new Dictionary<string, IReadOnlyList<ColumnEntry>>(),
                new Dictionary<string, IReadOnlyList<ForeignKeyEntry>>(),
                new ObjectEntry[0]);
            Assert.Empty(md5.ParametersByObject);
        }
    }
}
