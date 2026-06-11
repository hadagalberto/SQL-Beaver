using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class ProcBodyExtractorTests
    {
        [Fact]
        public void ParamsWithDefaults()
        {
            string script =
                "CREATE PROCEDURE dbo.P @id int, @nome varchar(50) = 'x'\r\nAS\r\nBEGIN\r\n  SELECT @id;\r\nEND";
            var body = ProcBodyExtractor.Extract(script);
            Assert.Equal(2, body.Parameters.Count);
            Assert.Equal("@id", body.Parameters[0].Name);
            Assert.Equal("int", body.Parameters[0].Type);
            Assert.Null(body.Parameters[0].DefaultOrNull);
            Assert.Equal("@nome", body.Parameters[1].Name);
            Assert.Equal("'x'", body.Parameters[1].DefaultOrNull);
        }

        [Fact]
        public void OutputParam()
        {
            string script =
                "CREATE PROCEDURE dbo.P @total int OUTPUT\r\nAS\r\n  SET @total = 1;";
            var body = ProcBodyExtractor.Extract(script);
            Assert.Single(body.Parameters);
            Assert.True(body.Parameters[0].IsOutput);
        }

        [Fact]
        public void NoParams()
        {
            string script = "CREATE PROCEDURE dbo.P AS SELECT 1;";
            var body = ProcBodyExtractor.Extract(script);
            Assert.Empty(body.Parameters);
        }

        [Fact]
        public void BodyExtracted()
        {
            string script =
                "CREATE PROCEDURE dbo.P @id int\r\nAS\r\nBEGIN\r\n  SELECT * FROM T WHERE Id = @id;\r\nEND";
            var body = ProcBodyExtractor.Extract(script);
            Assert.Contains("SELECT * FROM T WHERE Id = @id", body.BodyText);
            Assert.StartsWith("BEGIN", body.BodyText);
        }

        [Fact]
        public void ReturnWithValue_Flagged()
        {
            string script =
                "CREATE PROCEDURE dbo.P AS\r\nBEGIN\r\n  RETURN 42;\r\nEND";
            var body = ProcBodyExtractor.Extract(script);
            Assert.True(body.ContainsReturnWithValue);

            string noVal = "CREATE PROCEDURE dbo.P AS\r\nBEGIN\r\n  RETURN;\r\nEND";
            Assert.False(ProcBodyExtractor.Extract(noVal).ContainsReturnWithValue);
        }

        [Fact]
        public void Uncreatable_ReturnsEmpty()
        {
            var body = ProcBodyExtractor.Extract("-- definicao indisponivel (criptografada)");
            Assert.Empty(body.Parameters);
            Assert.Equal(string.Empty, body.BodyText);
        }
    }
}
