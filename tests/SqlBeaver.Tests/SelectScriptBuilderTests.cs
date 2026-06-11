using System.Collections.Generic;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SelectScriptBuilderTests
    {
        private static GridData Data()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("Id", typeof(int)),
                new GridColumn("Nome", typeof(string))
            };
            return new GridData(cols, new List<string[]>());
        }

        [Fact]
        public void BuildsSelect_WithBracketedColumns_And_TableName()
        {
            string script = SelectScriptBuilder.Build(Data(), "dbo.Pessoas");
            Assert.Contains("SELECT [Id], [Nome]", script);
            Assert.Contains("FROM dbo.Pessoas", script);
            Assert.EndsWith(";", script);
        }

        [Fact]
        public void BracketNamesWithSpaces()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("My Col", typeof(string)),
                new GridColumn("Other", typeof(int))
            };
            string script = SelectScriptBuilder.Build(new GridData(cols, new List<string[]>()), "[dbo].[T]");
            Assert.Contains("[My Col]", script);
            Assert.Contains("[Other]", script);
        }

        [Fact]
        public void NoRows_StillProducesValidScript()
        {
            string script = SelectScriptBuilder.Build(Data(), "[dbo].[T]");
            Assert.NotEmpty(script);
            Assert.Contains("SELECT", script);
            Assert.Contains("FROM", script);
        }

        [Fact]
        public void SingleColumn_NeedsBrackets()
        {
            var cols = new List<GridColumn> { new GridColumn("Order Id", typeof(int)) };
            string script = SelectScriptBuilder.Build(new GridData(cols, new List<string[]>()), "T");
            Assert.Contains("[Order Id]", script);
        }
    }
}
