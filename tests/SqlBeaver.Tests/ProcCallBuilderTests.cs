using System.Collections.Generic;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ProcCallBuilderTests
    {
        [Fact]
        public void NoParameters_ReturnsQualifiedNameOnly()
        {
            string result = ProcCallBuilder.BuildExecInsertText("dbo", "sp_Foo", new ParameterEntry[0]);
            Assert.Equal("dbo.sp_Foo", result);
        }

        [Fact]
        public void NullParameters_ReturnsQualifiedNameOnly()
        {
            string result = ProcCallBuilder.BuildExecInsertText("dbo", "sp_Foo", null);
            Assert.Equal("dbo.sp_Foo", result);
        }

        [Fact]
        public void SingleInputParameter_BuiltCorrectly()
        {
            var parameters = new List<ParameterEntry>
            {
                new ParameterEntry("@Id", "int", false, 1),
            };
            string result = ProcCallBuilder.BuildExecInsertText("dbo", "sp_GetPessoa", parameters);
            Assert.Equal("dbo.sp_GetPessoa @Id = ", result);
        }

        [Fact]
        public void MultipleParameters_SeparatedByCommaSpace()
        {
            var parameters = new List<ParameterEntry>
            {
                new ParameterEntry("@Id",   "int",          false, 1),
                new ParameterEntry("@Nome", "varchar(100)", false, 2),
            };
            string result = ProcCallBuilder.BuildExecInsertText("dbo", "sp_Insert", parameters);
            Assert.Equal("dbo.sp_Insert @Id = , @Nome = ", result);
        }

        [Fact]
        public void OutputParameter_HasOutputSuffix()
        {
            var parameters = new List<ParameterEntry>
            {
                new ParameterEntry("@Id",     "int",          false, 1),
                new ParameterEntry("@Result", "int",          true,  2),
            };
            string result = ProcCallBuilder.BuildExecInsertText("dbo", "sp_Calc", parameters);
            Assert.Equal("dbo.sp_Calc @Id = , @Result =  OUTPUT", result);
        }

        [Fact]
        public void SchemaAndProcNeedingBrackets_AreBracketed()
        {
            var parameters = new List<ParameterEntry>
            {
                new ParameterEntry("@Val", "int", false, 1),
            };
            string result = ProcCallBuilder.BuildExecInsertText("My Schema", "sp-Proc", parameters);
            Assert.Equal("[My Schema].[sp-Proc] @Val = ", result);
        }

        [Fact]
        public void RegularSchemaAndProc_NotBracketed()
        {
            string result = ProcCallBuilder.BuildExecInsertText("Cadastro", "sp_Foo", new ParameterEntry[0]);
            Assert.Equal("Cadastro.sp_Foo", result);
        }
    }
}
