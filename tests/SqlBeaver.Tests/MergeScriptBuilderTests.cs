using System.Collections.Generic;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class MergeScriptBuilderTests
    {
        private static GridData Data2Rows()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("Id", typeof(int)),
                new GridColumn("Nome", typeof(string)),
                new GridColumn("Ativo", typeof(bool))
            };
            var rows = new List<string[]>
            {
                new[] { "1", "Alice", "1" },
                new[] { "2", "NULL", "0" }
            };
            return new GridData(cols, rows);
        }

        [Fact]
        public void BuildsMerge_BasicStructure()
        {
            string script = MergeScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", new[] { "Id" });
            Assert.Contains("MERGE dbo.Pessoas AS alvo", script);
            Assert.Contains("USING (VALUES", script);
            Assert.Contains("AS origem", script);
            Assert.Contains("ON alvo.[Id] = origem.[Id]", script);
            Assert.Contains("WHEN MATCHED THEN UPDATE SET", script);
            Assert.Contains("WHEN NOT MATCHED THEN INSERT", script);
            Assert.EndsWith(";\r\n", script);
        }

        [Fact]
        public void PkColumns_InOn_NonPkColumns_InUpdate()
        {
            string script = MergeScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", new[] { "Id" });
            Assert.Contains("alvo.[Nome] = origem.[Nome]", script);
            Assert.Contains("alvo.[Ativo] = origem.[Ativo]", script);
            // PK should NOT be in SET
            Assert.DoesNotContain("alvo.[Id] = origem.[Id], alvo.[Nome]", script);
        }

        [Fact]
        public void EmptyPkColumns_EmitsPlaceholderAndComment()
        {
            string script = MergeScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", new string[0]);
            Assert.Contains("ATENÇÃO", script);
            Assert.Contains("1 = 0", script);
        }

        [Fact]
        public void NullPkColumns_EmitsPlaceholder()
        {
            string script = MergeScriptBuilder.Build(Data2Rows(), "dbo.Pessoas", null);
            Assert.Contains("1 = 0", script);
        }

        [Fact]
        public void Truncates_AtOneThousandRows()
        {
            var cols = new List<GridColumn> { new GridColumn("Id", typeof(int)) };
            var rows = new List<string[]>();
            for (int i = 0; i < 1001; i++) rows.Add(new[] { i.ToString() });

            string script = MergeScriptBuilder.Build(new GridData(cols, rows), "T", new[] { "Id" });
            Assert.Contains("truncado", script);
            // Count the value rows: should be exactly 1000
            int count = 0;
            int idx = 0;
            // Values lines start with 4 spaces + "("
            while ((idx = script.IndexOf("    (", idx, System.StringComparison.Ordinal)) >= 0)
            { count++; idx++; }
            Assert.Equal(1000, count);
        }

        [Fact]
        public void ValueTyping_NullAndNumbers()
        {
            var cols = new List<GridColumn>
            {
                new GridColumn("Id", typeof(int)),
                new GridColumn("Nome", typeof(string))
            };
            var rows = new List<string[]> { new[] { "42", "NULL" } };
            string script = MergeScriptBuilder.Build(new GridData(cols, rows), "T", new[] { "Id" });
            Assert.Contains("(42, NULL)", script);
        }
    }
}
