using SqlBeaver.Ai;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AiPromptBuilderTests
    {
        [Fact]
        public void Generate_System_ForbidsCodeFences()
        {
            AiPrompt p = AiPromptBuilder.BuildGenerateFromComment("-- listar pessoas", "");
            Assert.Contains("```sql", p.System);     // a instrução cita as cercas que devem ser evitadas
            Assert.Contains("SOMENTE SQL", p.System);
        }

        [Fact]
        public void Generate_StripsLineCommentLeader()
        {
            AiPrompt p = AiPromptBuilder.BuildGenerateFromComment("-- listar pessoas ativas", "");
            Assert.Equal("listar pessoas ativas", p.User);
            Assert.DoesNotContain("--", p.User);
        }

        [Fact]
        public void Generate_StripsBlockCommentLeader()
        {
            AiPrompt p = AiPromptBuilder.BuildGenerateFromComment("/* contar titulos vencidos */", "");
            Assert.Equal("contar titulos vencidos", p.User);
        }

        [Fact]
        public void Generate_MultiLineComment_StripsEachLeader()
        {
            AiPrompt p = AiPromptBuilder.BuildGenerateFromComment("-- linha 1\r\n-- linha 2", "");
            Assert.Equal("linha 1\nlinha 2", p.User);
        }

        [Fact]
        public void Generate_EmptySchemaContext_OmittedCleanly()
        {
            AiPrompt p = AiPromptBuilder.BuildGenerateFromComment("-- x", "   ");
            Assert.Equal("x", p.User);
            Assert.DoesNotContain("Schema", p.User);
        }

        [Fact]
        public void Generate_IncludesSchemaBlockWhenPresent()
        {
            AiPrompt p = AiPromptBuilder.BuildGenerateFromComment(
                "-- x", "Tabela: dbo.Pessoas (Id int PK)");
            Assert.Contains("Tabela: dbo.Pessoas", p.User);
            Assert.Contains("Schema", p.User);
        }

        [Fact]
        public void Explain_IncludesSqlAndIsPtBr()
        {
            AiPrompt p = AiPromptBuilder.BuildExplain("SELECT * FROM Pessoas", "");
            Assert.Contains("SELECT * FROM Pessoas", p.User);
            Assert.Contains("PT-BR", p.System);
        }

        [Fact]
        public void Optimize_IncludesSqlAndAsksPerformance()
        {
            AiPrompt p = AiPromptBuilder.BuildOptimize("SELECT * FROM Pessoas", "Tabela: dbo.Pessoas (Id int PK)");
            Assert.Contains("SELECT * FROM Pessoas", p.User);
            Assert.Contains("Tabela: dbo.Pessoas", p.User);
            Assert.Contains("desempenho", p.System);
        }
    }
}
