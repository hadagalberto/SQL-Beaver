using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ProcEncapsulatorTests
    {
        [Fact]
        public void OneUndeclaredVar_BecomesParam()
        {
            string sel = "SELECT * FROM T WHERE Id = @id";
            string result = ProcEncapsulator.Build(sel, "dbo", "P");
            Assert.Contains("CREATE PROCEDURE dbo.P (@id sql_variant /* ajuste o tipo */)", result);
            Assert.Contains("AS\r\nBEGIN\r\n", result);
            Assert.Contains(sel, result);
            Assert.EndsWith("END", result);
        }

        [Fact]
        public void DeclaredAbove_TypeInferred()
        {
            string full = "DECLARE @id int = 5;\r\nSELECT * FROM T WHERE Id = @id";
            int selStart = full.IndexOf("SELECT");
            string result = ProcEncapsulator.Build(full, selStart, full.Length - selStart, "dbo", "P");
            Assert.Contains("(@id int)", result);
        }

        [Fact]
        public void MultipleVars_AllParams()
        {
            string full = "DECLARE @a int;\r\nDECLARE @b varchar(50);\r\nSELECT @a, @b, @c";
            int selStart = full.IndexOf("SELECT");
            string result = ProcEncapsulator.Build(full, selStart, full.Length - selStart, "dbo", "P");
            Assert.Contains("@a int", result);
            Assert.Contains("@b varchar(50)", result);
            Assert.Contains("@c sql_variant /* ajuste o tipo */", result);
        }

        [Fact]
        public void NoVars_NoParams()
        {
            string sel = "SELECT * FROM T";
            string result = ProcEncapsulator.Build(sel, "dbo", "P");
            Assert.Contains("CREATE PROCEDURE dbo.P\r\nAS", result);
            Assert.DoesNotContain("(", result.Substring(0, result.IndexOf("AS")));
        }

        [Fact]
        public void NameWithSpace_Bracketed()
        {
            string sel = "SELECT 1";
            string result = ProcEncapsulator.Build(sel, "dbo", "My Proc");
            Assert.Contains("CREATE PROCEDURE dbo.[My Proc]", result);
        }

        [Fact]
        public void VarInsideString_Ignored()
        {
            string sel = "SELECT '@naoparam' AS x, @real";
            string result = ProcEncapsulator.Build(sel, "dbo", "P");
            // Only the parameter list (before AS) matters: @real is a param, @naoparam is not.
            string header = result.Substring(0, result.IndexOf("AS"));
            Assert.Contains("@real", header);
            Assert.DoesNotContain("@naoparam", header);
        }

        [Fact]
        public void DeclaredInsideSelection_NotParam()
        {
            string sel = "DECLARE @tmp int = 1;\r\nSELECT @tmp";
            string result = ProcEncapsulator.Build(sel, "dbo", "P");
            // @tmp is declared inside the selection → not a parameter
            Assert.Contains("CREATE PROCEDURE dbo.P\r\nAS", result);
        }
    }
}
