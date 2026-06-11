using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class TableScriptBuilderTests
    {
        [Fact]
        public void Build_BasicTable_ContainsCoreElements()
        {
            var columns = new List<ColumnEntry>
            {
                new ColumnEntry("IdPessoa", "uniqueidentifier", false, true),
                new ColumnEntry("Nome",     "varchar(250)",     true,  false),
            };

            string script = TableScriptBuilder.Build("Cadastro", "Pessoas", columns);

            // Regular identifiers: Bracket keeps them unbracketed
            Assert.Contains("CREATE TABLE Cadastro.Pessoas", script);
            Assert.Contains("IdPessoa uniqueidentifier NOT NULL,", script);
            Assert.Contains("Nome varchar(250) NULL,", script);
            Assert.Contains("CONSTRAINT PK_Pessoas PRIMARY KEY (IdPessoa)", script);
            Assert.Contains("-- Definição gerada pelo SQL Beaver", script);
        }

        [Fact]
        public void Build_NoPk_OmitsPkConstraint()
        {
            var columns = new List<ColumnEntry>
            {
                new ColumnEntry("IdLog",    "int",           false, false),
                new ColumnEntry("Mensagem", "nvarchar(max)", true,  false),
            };

            string script = TableScriptBuilder.Build("dbo", "LogTable", columns);

            Assert.DoesNotContain("PRIMARY KEY", script);
            Assert.Contains("IdLog int NOT NULL", script);
            Assert.Contains("Mensagem nvarchar(max) NULL", script);
        }

        [Fact]
        public void Build_CompositePk_AllPkColumnsInConstraint()
        {
            var columns = new List<ColumnEntry>
            {
                new ColumnEntry("IdPessoa", "uniqueidentifier", false, true),
                new ColumnEntry("IdTipo",   "int",              false, true),
                new ColumnEntry("Detalhe",  "varchar(100)",     true,  false),
            };

            string script = TableScriptBuilder.Build("Cadastro", "Pessoas", columns);

            Assert.Contains("PRIMARY KEY (IdPessoa, IdTipo)", script);
        }

        [Fact]
        public void Build_BracketNeedingNames_AreBracketed()
        {
            var columns = new List<ColumnEntry>
            {
                new ColumnEntry("Order",  "int",           false, true),
                new ColumnEntry("My Col", "nvarchar(50)",  true,  false),
            };

            string script = TableScriptBuilder.Build("My Schema", "Order Details", columns);

            // Names with spaces get bracketed
            Assert.Contains("[My Schema].[Order Details]", script);
            // "Order" has no special chars — not bracketed
            Assert.Contains("Order int NOT NULL", script);
            // "My Col" has a space — bracketed
            Assert.Contains("[My Col] nvarchar(50) NULL", script);
            Assert.Contains("PK_Order Details", script);
        }
    }
}
