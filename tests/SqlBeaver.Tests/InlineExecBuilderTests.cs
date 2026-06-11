using System.Collections.Generic;
using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class InlineExecBuilderTests
    {
        private static ProcBody Proc(string body, bool ret, params ProcParameter[] ps)
            => new ProcBody(new List<ProcParameter>(ps), body, ret);

        [Fact]
        public void NamedArgs_MappedByName()
        {
            var call = new ExecCall(null, "P", new[]
            {
                new ExecArg("@id", "5"),
                new ExecArg("@nome", "'abc'"),
            }, 0, 0);
            var proc = Proc("SELECT @id, @nome;", false,
                new ProcParameter("@id", "int", null, false),
                new ProcParameter("@nome", "varchar(50)", null, false));

            string result = InlineExecBuilder.Build(call, proc);
            Assert.Contains("-- inline de P", result);
            Assert.Contains("DECLARE @id int = 5;", result);
            Assert.Contains("DECLARE @nome varchar(50) = 'abc';", result);
            Assert.Contains("SELECT @id, @nome;", result);
        }

        [Fact]
        public void PositionalArgs_MappedByOrder()
        {
            var call = new ExecCall("dbo", "P", new[]
            {
                new ExecArg(null, "1"),
                new ExecArg(null, "2"),
            }, 0, 0);
            var proc = Proc("SELECT 1;", false,
                new ProcParameter("@a", "int", null, false),
                new ProcParameter("@b", "int", null, false));

            string result = InlineExecBuilder.Build(call, proc);
            Assert.Contains("-- inline de dbo.P", result);
            Assert.Contains("DECLARE @a int = 1;", result);
            Assert.Contains("DECLARE @b int = 2;", result);
        }

        [Fact]
        public void DefaultFallback_WhenNoArg()
        {
            var call = new ExecCall(null, "P", new ExecArg[0], 0, 0);
            var proc = Proc("SELECT 1;", false,
                new ProcParameter("@x", "int", "42", false));

            string result = InlineExecBuilder.Build(call, proc);
            Assert.Contains("DECLARE @x int = 42;", result);
        }

        [Fact]
        public void ReturnWarning_Prepended()
        {
            var call = new ExecCall(null, "P", new ExecArg[0], 0, 0);
            var proc = Proc("RETURN 1;", true);
            string result = InlineExecBuilder.Build(call, proc);
            Assert.Contains("-- aviso: a proc tinha RETURN", result);
        }

        [Fact]
        public void NoParams_OnlyHeaderAndBody()
        {
            var call = new ExecCall(null, "P", new ExecArg[0], 0, 0);
            var proc = Proc("SELECT 1;", false);
            string result = InlineExecBuilder.Build(call, proc);
            Assert.Contains("-- inline de P", result);
            Assert.Contains("SELECT 1;", result);
            Assert.DoesNotContain("DECLARE", result);
        }
    }
}
