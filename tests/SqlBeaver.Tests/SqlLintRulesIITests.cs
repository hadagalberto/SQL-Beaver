using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlBeaver.Linting;
using SqlBeaver.Linting.Rules;
using Xunit;

namespace SqlBeaver.Tests
{
    /// <summary>
    /// TDD para as 15 regras de lint da Categoria 7 (Lint II) — uma positiva e uma
    /// negativa por regra — e para LintReportFormatter.
    /// </summary>
    public class SqlLintRulesIITests
    {
        private static TSqlFragment Parse(string sql)
        {
            var parser = new TSql160Parser(true);
            IList<ParseError> errors;
            using (var reader = new StringReader(sql))
            {
                TSqlFragment fragment = parser.Parse(reader, out errors);
                Assert.Empty(errors);
                return fragment;
            }
        }

        private static IReadOnlyList<LintDiagnostic> Run(ISqlLintRule rule, string sql)
            => rule.Inspect(Parse(sql)).ToList();

        // 1. deprecated-types
        [Fact]
        public void DeprecatedTypes_Fires_On_Text()
        {
            var diags = Run(new DeprecatedTypesRule(), "CREATE TABLE dbo.T (c TEXT)");
            Assert.Contains(diags, d => d.RuleId == "deprecated-types");
        }

        [Fact]
        public void DeprecatedTypes_Silent_On_VarcharMax()
        {
            var diags = Run(new DeprecatedTypesRule(), "CREATE TABLE dbo.T (c VARCHAR(MAX))");
            Assert.Empty(diags);
        }

        // 2. varchar-no-length
        [Fact]
        public void VarcharNoLength_Fires_When_No_Length()
        {
            var diags = Run(new VarcharNoLengthRule(), "DECLARE @x VARCHAR");
            Assert.Contains(diags, d => d.RuleId == "varchar-no-length");
        }

        [Fact]
        public void VarcharNoLength_Silent_When_Length_Present()
        {
            var diags = Run(new VarcharNoLengthRule(), "DECLARE @x VARCHAR(50)");
            Assert.Empty(diags);
        }

        // 3. null-comparison
        [Fact]
        public void NullComparison_Fires_On_EqualsNull()
        {
            var diags = Run(new NullComparisonRule(), "SELECT 1 WHERE a = NULL");
            Assert.Contains(diags, d => d.RuleId == "null-comparison");
        }

        [Fact]
        public void NullComparison_Silent_On_IsNull()
        {
            var diags = Run(new NullComparisonRule(), "SELECT 1 WHERE a IS NULL");
            Assert.Empty(diags);
        }

        // 4. order-by-ordinal
        [Fact]
        public void OrderByOrdinal_Fires_On_Position()
        {
            var diags = Run(new OrderByOrdinalRule(), "SELECT a, b FROM dbo.T ORDER BY 1, 2");
            Assert.Contains(diags, d => d.RuleId == "order-by-ordinal");
        }

        [Fact]
        public void OrderByOrdinal_Silent_On_ColumnNames()
        {
            var diags = Run(new OrderByOrdinalRule(), "SELECT a, b FROM dbo.T ORDER BY a, b");
            Assert.Empty(diags);
        }

        // 5. top-without-order-by
        [Fact]
        public void TopWithoutOrderBy_Fires()
        {
            var diags = Run(new TopWithoutOrderByRule(), "SELECT TOP 10 a FROM dbo.T");
            Assert.Contains(diags, d => d.RuleId == "top-without-order-by");
        }

        [Fact]
        public void TopWithoutOrderBy_Silent_With_OrderBy()
        {
            var diags = Run(new TopWithoutOrderByRule(), "SELECT TOP 10 a FROM dbo.T ORDER BY a");
            Assert.Empty(diags);
        }

        // 6. distinct-with-group-by
        [Fact]
        public void DistinctWithGroupBy_Fires()
        {
            var diags = Run(new DistinctWithGroupByRule(),
                "SELECT DISTINCT a FROM dbo.T GROUP BY a");
            Assert.Contains(diags, d => d.RuleId == "distinct-with-group-by");
        }

        [Fact]
        public void DistinctWithGroupBy_Silent_Without_GroupBy()
        {
            var diags = Run(new DistinctWithGroupByRule(), "SELECT DISTINCT a FROM dbo.T");
            Assert.Empty(diags);
        }

        // 7. sp-prefix
        [Fact]
        public void SpPrefix_Fires()
        {
            var diags = Run(new SpPrefixRule(), "CREATE PROCEDURE sp_DoWork AS SELECT 1");
            Assert.Contains(diags, d => d.RuleId == "sp-prefix");
        }

        [Fact]
        public void SpPrefix_Silent_On_Normal_Name()
        {
            var diags = Run(new SpPrefixRule(), "CREATE PROCEDURE dbo.DoWork AS SELECT 1");
            Assert.Empty(diags);
        }

        // 8. nocount-missing
        [Fact]
        public void NocountMissing_Fires_When_First_Statement_Is_Not_SetNocount()
        {
            var diags = Run(new NocountMissingRule(), "CREATE PROCEDURE dbo.P AS SELECT 1");
            Assert.Contains(diags, d => d.RuleId == "nocount-missing");
        }

        [Fact]
        public void NocountMissing_Silent_When_SetNocountOn_First()
        {
            var diags = Run(new NocountMissingRule(),
                "CREATE PROCEDURE dbo.P AS SET NOCOUNT ON; SELECT 1");
            Assert.Empty(diags);
        }

        // 9. non-sargable
        [Fact]
        public void NonSargable_Fires_On_Function_Over_Column()
        {
            var diags = Run(new NonSargableRule(),
                "SELECT 1 FROM dbo.T WHERE UPPER(name) = 'X'");
            Assert.Contains(diags, d => d.RuleId == "non-sargable");
        }

        [Fact]
        public void NonSargable_Silent_On_Plain_Column()
        {
            var diags = Run(new NonSargableRule(),
                "SELECT 1 FROM dbo.T WHERE name = 'X'");
            Assert.Empty(diags);
        }

        // 10. like-leading-wildcard
        [Fact]
        public void LikeLeadingWildcard_Fires()
        {
            var diags = Run(new LikeLeadingWildcardRule(),
                "SELECT 1 FROM dbo.T WHERE name LIKE '%abc'");
            Assert.Contains(diags, d => d.RuleId == "like-leading-wildcard");
        }

        [Fact]
        public void LikeLeadingWildcard_Silent_On_Trailing()
        {
            var diags = Run(new LikeLeadingWildcardRule(),
                "SELECT 1 FROM dbo.T WHERE name LIKE 'abc%'");
            Assert.Empty(diags);
        }

        // 11. exec-string
        [Fact]
        public void ExecString_Fires_On_ExecOfString()
        {
            var diags = Run(new ExecStringRule(), "EXEC('SELECT 1')");
            Assert.Contains(diags, d => d.RuleId == "exec-string");
        }

        [Fact]
        public void ExecString_Silent_On_Proc_Call()
        {
            var diags = Run(new ExecStringRule(), "EXEC dbo.MyProc");
            Assert.Empty(diags);
        }

        // 12. goto
        [Fact]
        public void Goto_Fires()
        {
            var diags = Run(new GotoRule(), "GOTO done");
            Assert.Contains(diags, d => d.RuleId == "goto");
        }

        [Fact]
        public void Goto_Silent_Without_Goto()
        {
            var diags = Run(new GotoRule(), "SELECT 1");
            Assert.Empty(diags);
        }

        // 13. cursor
        [Fact]
        public void Cursor_Fires()
        {
            var diags = Run(new CursorRule(),
                "DECLARE c CURSOR FOR SELECT a FROM dbo.T");
            Assert.Contains(diags, d => d.RuleId == "cursor");
        }

        [Fact]
        public void Cursor_Silent_Without_Cursor()
        {
            var diags = Run(new CursorRule(), "SELECT a FROM dbo.T");
            Assert.Empty(diags);
        }

        // 14. float-for-money
        [Fact]
        public void FloatForMoney_Fires_On_Float_Money_Column()
        {
            var diags = Run(new FloatForMoneyRule(),
                "CREATE TABLE dbo.T (preco FLOAT)");
            Assert.Contains(diags, d => d.RuleId == "float-for-money");
        }

        [Fact]
        public void FloatForMoney_Silent_On_Decimal()
        {
            var diags = Run(new FloatForMoneyRule(),
                "CREATE TABLE dbo.T (preco DECIMAL(10,2))");
            Assert.Empty(diags);
        }

        [Fact]
        public void FloatForMoney_Silent_On_NonMoney_Float()
        {
            var diags = Run(new FloatForMoneyRule(),
                "CREATE TABLE dbo.T (latitude FLOAT)");
            Assert.Empty(diags);
        }

        // 15. waitfor-delay
        [Fact]
        public void WaitforDelay_Fires()
        {
            var diags = Run(new WaitForDelayRule(), "WAITFOR DELAY '00:00:05'");
            Assert.Contains(diags, d => d.RuleId == "waitfor-delay");
        }

        [Fact]
        public void WaitforDelay_Silent_On_WaitforTime()
        {
            var diags = Run(new WaitForDelayRule(), "WAITFOR TIME '23:00:00'");
            Assert.Empty(diags);
        }

        // ── LintRuleSet wiring ────────────────────────────────────────────────
        [Fact]
        public void LintRuleSet_Includes_All_New_Rules()
        {
            var ruleSet = LintRuleSet.CreateDefault();
            var fragment = Parse("SELECT TOP 5 a FROM dbo.T WHERE UPPER(name) = 'X'");
            var diags = ruleSet.Inspect(fragment, new string[0]);
            Assert.Contains(diags, d => d.RuleId == "top-without-order-by");
            Assert.Contains(diags, d => d.RuleId == "non-sargable");
        }

        // ── LintReportFormatter ───────────────────────────────────────────────
        [Fact]
        public void Report_Empty_Produces_NoWarnings_Header()
        {
            string report = LintReportFormatter.Format(new List<LintDiagnostic>());
            Assert.Contains("nenhum aviso", report);
        }

        [Fact]
        public void Report_Groups_By_RuleId_With_Counts()
        {
            var diags = new List<LintDiagnostic>
            {
                new LintDiagnostic("select-star", "Evite SELECT *", 1, 8, 1),
                new LintDiagnostic("select-star", "Evite SELECT *", 8, 8, 1),
                new LintDiagnostic("missing-schema", "Qualifique a tabela", 1, 15, 1),
            };
            string report = LintReportFormatter.Format(diags);

            Assert.Contains("[select-star]", report);
            Assert.Contains("[missing-schema]", report);
            // 3 warnings in 2 rules
            Assert.Contains("3", report);
            Assert.Contains("2", report);
            Assert.Contains("linha 1", report);
            Assert.Contains("linha 8", report);
        }

        [Fact]
        public void Report_Orders_Rules_By_Id()
        {
            var diags = new List<LintDiagnostic>
            {
                new LintDiagnostic("zzz-rule", "z", 1, 1, 1),
                new LintDiagnostic("aaa-rule", "a", 1, 1, 1),
            };
            string report = LintReportFormatter.Format(diags);
            int idxA = report.IndexOf("[aaa-rule]");
            int idxZ = report.IndexOf("[zzz-rule]");
            Assert.True(idxA >= 0 && idxZ >= 0 && idxA < idxZ);
        }

        [Fact]
        public void Report_Counts_Per_Rule()
        {
            var diags = new List<LintDiagnostic>
            {
                new LintDiagnostic("r1", "m", 1, 1, 1),
                new LintDiagnostic("r1", "m", 2, 1, 1),
            };
            string report = LintReportFormatter.Format(diags);
            Assert.Contains("ocorrência", report);
        }
    }
}
