using System.Collections.Generic;
using SqlBeaver.Analysis;
using SqlBeaver.Completion;
using SqlBeaver.Metadata;
using Xunit;

namespace SqlBeaver.Tests
{
    /// <summary>~8 testes do QuickInfoBuilder (puro, sem dependências do VS).</summary>
    public class QuickInfoBuilderTests
    {
        // -----------------------------------------------------------------------
        // Fixtures
        // -----------------------------------------------------------------------

        private static readonly List<TableEntry> Tables = new List<TableEntry>
        {
            new TableEntry("dbo", "Clientes"),
            new TableEntry("dbo", "Pedidos"),
        };

        private static readonly List<string> Schemas = new List<string> { "dbo" };

        private static DbMetadata BuildMetadata(
            List<MetadataAssembler.ColumnRow> columns = null,
            List<MetadataAssembler.ObjectRow> objects = null,
            List<MetadataAssembler.ParameterRow> parameters = null)
        {
            return MetadataAssembler.Assemble(
                Tables, Schemas,
                columns   ?? new List<MetadataAssembler.ColumnRow>(),
                new List<MetadataAssembler.ForeignKeyColumnRow>(),
                objects   ?? new List<MetadataAssembler.ObjectRow>(),
                parameters ?? new List<MetadataAssembler.ParameterRow>());
        }

        private static readonly List<MetadataAssembler.ColumnRow> ClientesColumns =
            new List<MetadataAssembler.ColumnRow>
            {
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "IdCliente", "int",         false, true),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "Nome",      "varchar(250)", true,  false),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "Email",     "varchar(150)", true,  false),
            };

        private static readonly List<TableRef> EmptyScope  = new List<TableRef>();
        private static readonly List<LocalTableDef> NoLocals = new List<LocalTableDef>();

        // -----------------------------------------------------------------------
        // 1. Alias → tabela + colunas
        // -----------------------------------------------------------------------

        [Fact]
        public void Alias_ResolvesToTableAndColumns()
        {
            DbMetadata md = BuildMetadata(ClientesColumns);
            var scope = new List<TableRef> { new TableRef("dbo", "Clientes", "c") };

            string result = QuickInfoBuilder.Build("c", null, scope, md, NoLocals);

            Assert.NotNull(result);
            Assert.Contains("c →", result);
            Assert.Contains("Clientes", result);
            Assert.Contains("IdCliente", result);
            Assert.Contains("[PK]", result);
        }

        // -----------------------------------------------------------------------
        // 2. Alias → tabela local (colunas do LocalTableDef, não do cache)
        // -----------------------------------------------------------------------

        [Fact]
        public void Alias_ResolvesToLocalColumns()
        {
            DbMetadata md = BuildMetadata(); // sem colunas no cache
            var scope = new List<TableRef> { new TableRef(null, "#tmp", "t") };
            var localCols = new List<ColumnEntry>
            {
                new ColumnEntry("Id",   "int",         false, true),
                new ColumnEntry("Nome", "varchar(100)", true,  false),
            };
            var locals = new List<LocalTableDef>
            {
                new LocalTableDef("#tmp", LocalTableKind.Temp, localCols),
            };

            string result = QuickInfoBuilder.Build("t", null, scope, md, locals);

            Assert.NotNull(result);
            Assert.Contains("Id", result);
            Assert.Contains("Nome", result);
        }

        // -----------------------------------------------------------------------
        // 3. Coluna em tabela única do escopo
        // -----------------------------------------------------------------------

        [Fact]
        public void Column_InSingleScopeTable_ShowsTableAndType()
        {
            DbMetadata md = BuildMetadata(ClientesColumns);
            var scope = new List<TableRef> { new TableRef("dbo", "Clientes", "c") };

            string result = QuickInfoBuilder.Build("Nome", null, scope, md, NoLocals);

            Assert.NotNull(result);
            Assert.Contains("Nome", result);
            Assert.Contains("varchar(250)", result);
            Assert.Contains("NULL", result);
        }

        // -----------------------------------------------------------------------
        // 4. Coluna ambígua em dois tables do escopo → lista as duas
        // -----------------------------------------------------------------------

        [Fact]
        public void Column_AmbiguousAcrossScope_ListsBoth()
        {
            var columns = new List<MetadataAssembler.ColumnRow>
            {
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "IdCliente", "int",  false, true),
                new MetadataAssembler.ColumnRow("dbo", "Clientes", "DataCriacao","date", true, false),
                new MetadataAssembler.ColumnRow("dbo", "Pedidos",  "IdPedido",  "int",  false, true),
                new MetadataAssembler.ColumnRow("dbo", "Pedidos",  "DataCriacao","date", true, false),
            };
            DbMetadata md = BuildMetadata(columns);
            var scope = new List<TableRef>
            {
                new TableRef("dbo", "Clientes", "c"),
                new TableRef("dbo", "Pedidos",  "p"),
            };

            string result = QuickInfoBuilder.Build("DataCriacao", null, scope, md, NoLocals);

            Assert.NotNull(result);
            // Both occurrences must appear
            int first  = result.IndexOf("DataCriacao");
            int second = result.IndexOf("DataCriacao", first + 1);
            Assert.True(second > first, "Expected two occurrences of DataCriacao");
        }

        // -----------------------------------------------------------------------
        // 5. Tabela por nome (resolução sem escopo)
        // -----------------------------------------------------------------------

        [Fact]
        public void Table_ByName_ShowsSchemaAndColumns()
        {
            DbMetadata md = BuildMetadata(ClientesColumns);

            string result = QuickInfoBuilder.Build("Clientes", null, EmptyScope, md, NoLocals);

            Assert.NotNull(result);
            Assert.Contains("dbo.Clientes", result);
            Assert.Contains("IdCliente", result);
        }

        // -----------------------------------------------------------------------
        // 6. Procedure com parâmetros (OUTPUT marcado)
        // -----------------------------------------------------------------------

        [Fact]
        public void Procedure_ShowsSignatureAndOutputParam()
        {
            var objects = new List<MetadataAssembler.ObjectRow>
            {
                new MetadataAssembler.ObjectRow("dbo", "sp_BuscarCliente", "P"),
            };
            var parameters = new List<MetadataAssembler.ParameterRow>
            {
                new MetadataAssembler.ParameterRow("dbo", "sp_BuscarCliente", "@IdCliente", "int",         false, 1),
                new MetadataAssembler.ParameterRow("dbo", "sp_BuscarCliente", "@Nome",      "varchar(250)", true,  2),
            };
            DbMetadata md = BuildMetadata(null, objects, parameters);

            string result = QuickInfoBuilder.Build("sp_BuscarCliente", null, EmptyScope, md, NoLocals);

            Assert.NotNull(result);
            Assert.Contains("sp_BuscarCliente", result);
            Assert.Contains("procedure", result);
            Assert.Contains("@IdCliente", result);
            Assert.Contains("@Nome", result);
            Assert.Contains("[OUTPUT]", result);
        }

        // -----------------------------------------------------------------------
        // 7. Mais de 20 colunas → mostra "+N coluna(s)"
        // -----------------------------------------------------------------------

        [Fact]
        public void MoreThan20Columns_ShowsOverflowLine()
        {
            var columns = new List<MetadataAssembler.ColumnRow>();
            for (int i = 1; i <= 25; i++)
                columns.Add(new MetadataAssembler.ColumnRow("dbo", "Clientes", "Col" + i, "int", true, false));

            DbMetadata md = BuildMetadata(columns);

            string result = QuickInfoBuilder.Build("Clientes", null, EmptyScope, md, NoLocals);

            Assert.NotNull(result);
            Assert.Contains("+5 coluna(s)", result);
            // Col21 must NOT appear as its own line (it's in the overflow)
            Assert.DoesNotContain("Col21", result);
        }

        // -----------------------------------------------------------------------
        // 8. Identificador desconhecido → null
        // -----------------------------------------------------------------------

        [Fact]
        public void Unknown_ReturnsNull()
        {
            DbMetadata md = BuildMetadata();

            string result = QuickInfoBuilder.Build("xyzQualquerCoisa", null, EmptyScope, md, NoLocals);

            Assert.Null(result);
        }
    }
}
