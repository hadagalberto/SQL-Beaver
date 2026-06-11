using System.Collections.Generic;
using System.Linq;
using SqlBeaver.Analysis;
using Xunit;

namespace SqlBeaver.Tests
{
    public class StatementScopeTests
    {
        private static IReadOnlyList<TableRef> Scope(string text, int? caret = null)
            => StatementScopeAnalyzer.GetTablesInScope(text, caret ?? text.Length);

        [Fact]
        public void SimpleFromWithAlias()
        {
            var refs = Scope("SELECT * FROM Cadastro.Pessoas p WHERE p.Id = 1");
            var r = Assert.Single(refs);
            Assert.Equal("Cadastro", r.Schema);
            Assert.Equal("Pessoas", r.Table);
            Assert.Equal("p", r.Alias);
        }

        [Fact]
        public void AliasWithAsKeyword()
        {
            var r = Assert.Single(Scope("SELECT * FROM Pessoas AS p"));
            Assert.Null(r.Schema);
            Assert.Equal("Pessoas", r.Table);
            Assert.Equal("p", r.Alias);
        }

        [Fact]
        public void NoAlias_KeywordIsNotCapturedAsAlias()
        {
            var r = Assert.Single(Scope("SELECT * FROM Pessoas WHERE Id = 1"));
            Assert.Equal("Pessoas", r.Table);
            Assert.Null(r.Alias);
        }

        [Fact]
        public void BracketedNames()
        {
            var r = Assert.Single(Scope("SELECT * FROM [Cadastro].[Minha Tabela] mt"));
            Assert.Equal("Cadastro", r.Schema);
            Assert.Equal("Minha Tabela", r.Table);
            Assert.Equal("mt", r.Alias);
        }

        [Fact]
        public void MultipleJoins()
        {
            var refs = Scope("SELECT * FROM A a INNER JOIN B b ON b.x = a.x LEFT JOIN C ON C.y = a.y");
            Assert.Equal(3, refs.Count);
            Assert.Equal("a", refs[0].Alias);
            Assert.Equal("b", refs[1].Alias);
            Assert.Equal("C", refs[2].Table);
            Assert.Null(refs[2].Alias); // "ON" não vira alias
        }

        [Fact]
        public void CommaSeparatedFromList()
        {
            var refs = Scope("SELECT * FROM Cadastro.A a, Financeiro.B b WHERE a.x = b.x");
            Assert.Equal(2, refs.Count);
            Assert.Equal("B", refs[1].Table);
            Assert.Equal("b", refs[1].Alias);
        }

        [Fact]
        public void CaretBeforeFrom_LooksForward()
        {
            string text = "SELECT  FROM Cadastro.Pessoas p";
            var refs = StatementScopeAnalyzer.GetTablesInScope(text, 7); // caret entre SELECT e FROM
            Assert.Single(refs);
            Assert.Equal("Pessoas", refs[0].Table);
        }

        [Fact]
        public void SecondStatement_OnlyItsTables()
        {
            string text = "SELECT * FROM A a; SELECT * FROM B b";
            var refs = Scope(text); // caret no fim = segundo statement
            var r = Assert.Single(refs);
            Assert.Equal("B", r.Table);
        }

        [Fact]
        public void GoSeparatesBatches()
        {
            string text = "SELECT * FROM A a\r\nGO\r\nSELECT * FROM B b";
            var r = Assert.Single(Scope(text));
            Assert.Equal("B", r.Table);
        }

        [Fact]
        public void Subquery_IsIgnored()
        {
            Assert.Empty(Scope("SELECT * FROM (SELECT * FROM X) t"));
        }

        [Fact]
        public void FromInsideCommentOrString_IsIgnored()
        {
            var refs = Scope("-- FROM Falsa f\r\nSELECT 'FROM OutraFalsa', * FROM Real r");
            var r = Assert.Single(refs);
            Assert.Equal("Real", r.Table);
        }

        [Fact]
        public void ThreePartName_TakesLastTwoParts()
        {
            var r = Assert.Single(Scope("SELECT * FROM meudb.dbo.Tabela t"));
            Assert.Equal("dbo", r.Schema);
            Assert.Equal("Tabela", r.Table);
        }

        [Fact]
        public void JoinFollowedByJoinKeyword_NoAliasCaptured()
        {
            var refs = Scope("SELECT * FROM A INNER JOIN B INNER JOIN C ON 1=1 ON 1=1");
            Assert.Equal(3, refs.Count);
            Assert.All(refs, r => Assert.Null(r.Alias));
        }

        [Fact]
        public void EmptyText_ReturnsEmpty()
        {
            Assert.Empty(Scope(""));
        }

        [Fact]
        public void UpdateTarget_IsInScope()
        {
            var r = Assert.Single(Scope("UPDATE Cadastro.Pessoas SET Nome = 'x' WHERE Id = 1"));
            Assert.Equal("Cadastro", r.Schema);
            Assert.Equal("Pessoas", r.Table);
            Assert.Null(r.Alias); // SET é keyword, não vira alias
        }

        [Fact]
        public void UpdateWithFromForm_CapturesBoth()
        {
            var refs = Scope("UPDATE p SET p.Nome = 'x' FROM Cadastro.Pessoas p WHERE p.Id = 1");
            // "p" (alvo) não resolve para tabela conhecida; o FROM traz a tabela real
            Assert.Contains(refs, r => r.Table == "Pessoas" && r.Alias == "p");
        }

        // ---- divisão implícita de statements (sem ';'/GO) ----

        [Fact]
        public void TwoSelectsWithoutSemicolon_CaretInSecond_OnlySecondScope()
        {
            string text = "SELECT * FROM Cadastro.Pessoas p WHERE p.Nome = 'x'\r\n\r\nSELECT * FROM Cadastro.Pessoas ps WHERE ";
            var refs = Scope(text);
            var r = Assert.Single(refs);
            Assert.Equal("ps", r.Alias);
        }

        [Fact]
        public void TwoSelectsWithoutSemicolon_CaretInFirst_OnlyFirstScope()
        {
            string text = "SELECT * FROM A a WHERE \r\nSELECT * FROM B b";
            var refs = StatementScopeAnalyzer.GetTablesInScope(text, 24); // dentro do primeiro
            var r = Assert.Single(refs);
            Assert.Equal("a", r.Alias);
        }

        [Fact]
        public void UnionSelect_IsOneStatement()
        {
            var refs = Scope("SELECT x FROM A a UNION SELECT y FROM B b WHERE ");
            Assert.Equal(2, refs.Count);
        }

        [Fact]
        public void UnionAllSelect_IsOneStatement()
        {
            var refs = Scope("SELECT x FROM A a UNION ALL SELECT y FROM B b WHERE ");
            Assert.Equal(2, refs.Count);
        }

        [Fact]
        public void InsertSelect_IsOneStatement()
        {
            var refs = Scope("INSERT INTO Destino SELECT x FROM Origem o WHERE ");
            Assert.Equal(2, refs.Count); // Destino (INTO) + Origem
        }

        [Fact]
        public void CteWithSelect_IsOneStatement()
        {
            var refs = Scope("WITH cte AS (SELECT 1 AS um) SELECT * FROM Cadastro.Pessoas p WHERE ");
            var r = Assert.Single(refs);
            Assert.Equal("p", r.Alias);
        }

        [Fact]
        public void DeclareThenSelect_SplitsStatements()
        {
            var refs = Scope("DECLARE @x int\r\nSELECT * FROM Cadastro.Pessoas ps WHERE ");
            var r = Assert.Single(refs);
            Assert.Equal("ps", r.Alias);
        }

        [Fact]
        public void SelectThenUpdate_UpdateCapturesItsTarget()
        {
            var refs = Scope("SELECT * FROM A a\r\nUPDATE Cadastro.Pessoas SET Nome = 'x' WHERE ");
            var r = Assert.Single(refs);
            Assert.Equal("Pessoas", r.Table);
        }
    }
}
