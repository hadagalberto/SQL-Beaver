using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ExecCallParserTests
    {
        [Fact]
        public void NamedArgs()
        {
            var call = ExecCallParser.Parse("EXEC GetPessoa @id = 5, @ativo = 1", 0);
            Assert.NotNull(call);
            Assert.Null(call.Schema);
            Assert.Equal("GetPessoa", call.Proc);
            Assert.Equal(2, call.Args.Count);
            Assert.Equal("@id", call.Args[0].Name);
            Assert.Equal("5", call.Args[0].ValueText);
            Assert.Equal("@ativo", call.Args[1].Name);
            Assert.Equal("1", call.Args[1].ValueText);
        }

        [Fact]
        public void PositionalArgs()
        {
            var call = ExecCallParser.Parse("EXEC GetPessoa 5, 'abc'", 0);
            Assert.NotNull(call);
            Assert.Equal(2, call.Args.Count);
            Assert.Null(call.Args[0].Name);
            Assert.Equal("5", call.Args[0].ValueText);
            Assert.Null(call.Args[1].Name);
            Assert.Equal("'abc'", call.Args[1].ValueText);
        }

        [Fact]
        public void NoArgs()
        {
            var call = ExecCallParser.Parse("EXEC dbo.Limpar", 5);
            Assert.NotNull(call);
            Assert.Equal("dbo", call.Schema);
            Assert.Equal("Limpar", call.Proc);
            Assert.Empty(call.Args);
        }

        [Fact]
        public void SchemaQualified()
        {
            var call = ExecCallParser.Parse("EXECUTE Sales.GetOrders @top = 10", 0);
            Assert.NotNull(call);
            Assert.Equal("Sales", call.Schema);
            Assert.Equal("GetOrders", call.Proc);
            Assert.Single(call.Args);
            Assert.Equal("@top", call.Args[0].Name);
        }

        [Fact]
        public void MixedAndStringForm()
        {
            // mixed named + positional
            var call = ExecCallParser.Parse("EXEC P @a = 1, 2", 0);
            Assert.NotNull(call);
            Assert.Equal("@a", call.Args[0].Name);
            Assert.Null(call.Args[1].Name);
            Assert.Equal("2", call.Args[1].ValueText);

            // EXEC('...') string form → not parseable for inlining
            Assert.Null(ExecCallParser.Parse("EXEC('SELECT 1')", 0));
        }
    }
}
