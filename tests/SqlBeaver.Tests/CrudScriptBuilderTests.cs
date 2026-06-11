using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class CrudScriptBuilderTests
    {
        private static List<ColumnEntry> Columns(bool withPk = true)
        {
            return new List<ColumnEntry>
            {
                new ColumnEntry("Id",   "int",          false, withPk),
                new ColumnEntry("Nome", "varchar(100)", true,  false),
                new ColumnEntry("Ativo","bit",          false, false)
            };
        }

        [Fact]
        public void AllFourSections_Present()
        {
            string script = CrudScriptBuilder.Build("dbo", "Pessoas", Columns());
            Assert.Contains("-- SELECT", script);
            Assert.Contains("-- INSERT", script);
            Assert.Contains("-- UPDATE", script);
            Assert.Contains("-- DELETE", script);
        }

        [Fact]
        public void PkColumns_InWhere_Of_Select_Update_Delete()
        {
            string script = CrudScriptBuilder.Build("dbo", "Pessoas", Columns());
            // SELECT WHERE
            Assert.Contains("WHERE [Id] = @Id", script);
            // UPDATE WHERE
            // UPDATE SET should not have Id (it's PK)
            Assert.DoesNotContain("SET [Id]", script);
            // DELETE WHERE
            Assert.Contains("DELETE FROM [dbo].[Pessoas]", script);
        }

        [Fact]
        public void NoPk_EmitsPlaceholderAndComment()
        {
            string script = CrudScriptBuilder.Build("dbo", "Pessoas", Columns(withPk: false));
            Assert.Contains("ATENÇÃO", script);
            Assert.Contains("1 = 0", script);
        }

        [Fact]
        public void BracketNames_InScript()
        {
            var cols = new List<ColumnEntry>
            {
                new ColumnEntry("My Id",    "int",          false, true),
                new ColumnEntry("My Value", "varchar(100)", true,  false)
            };
            string script = CrudScriptBuilder.Build("dbo", "My Table", cols);
            Assert.Contains("[My Table]", script);
            Assert.Contains("[My Id]", script);
            Assert.Contains("[My Value]", script);
            // Parameter names are plain (no brackets)
            Assert.Contains("@My Id", script);
        }

        [Fact]
        public void InsertContainsAllColumns()
        {
            string script = CrudScriptBuilder.Build("dbo", "Pessoas", Columns());
            Assert.Contains("INSERT INTO [dbo].[Pessoas] ([Id], [Nome], [Ativo])", script);
            Assert.Contains("VALUES (@Id, @Nome, @Ativo)", script);
        }
    }
}
