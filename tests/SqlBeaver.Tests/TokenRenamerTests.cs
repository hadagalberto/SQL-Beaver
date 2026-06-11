using System.Collections.Generic;
using SqlBeaver.Refactoring;
using Xunit;

namespace SqlBeaver.Tests
{
    public class TokenRenamerTests
    {
        // Helper: apply edits (descending order expected) to text
        private static string Apply(string text, IReadOnlyList<TextReplacement> edits)
        {
            var sb = new System.Text.StringBuilder(text);
            foreach (TextReplacement e in edits)
            {
                sb.Remove(e.Start, e.Length);
                sb.Insert(e.Start, e.NewText);
            }
            return sb.ToString();
        }

        [Fact]
        public void RenamesAlias_MultipleOccurrences()
        {
            string text = "SELECT p.Id, p.Name FROM Persons p WHERE p.Id = 1";
            IReadOnlyList<TextReplacement> edits = TokenRenamer.Rename(text, 0, text.Length, "p", "person");
            string result = Apply(text, edits);
            Assert.Equal("SELECT person.Id, person.Name FROM Persons person WHERE person.Id = 1", result);
        }

        [Fact]
        public void DoesNotRename_InsideString()
        {
            string text = "SELECT 'p is a letter' FROM T p WHERE p.Id = 1";
            IReadOnlyList<TextReplacement> edits = TokenRenamer.Rename(text, 0, text.Length, "p", "x");
            string result = Apply(text, edits);
            Assert.Contains("'p is a letter'", result);
            Assert.DoesNotContain("'x is a letter'", result);
        }

        [Fact]
        public void DoesNotRename_InsideComment()
        {
            string text = "-- p alias\r\nSELECT p.Id FROM T p";
            IReadOnlyList<TextReplacement> edits = TokenRenamer.Rename(text, 0, text.Length, "p", "x");
            string result = Apply(text, edits);
            Assert.Contains("-- p alias", result);
            Assert.Contains("SELECT x.Id FROM T x", result);
        }

        [Fact]
        public void WordBoundary_ShortAliasDoesNotHitLongerToken()
        {
            // 'p' should NOT match inside 'pe.col' or 'Products'
            string text = "SELECT p.Id, pe.col FROM T p JOIN T2 pe";
            IReadOnlyList<TextReplacement> edits = TokenRenamer.Rename(text, 0, text.Length, "p", "x");
            string result = Apply(text, edits);
            Assert.Contains("SELECT x.Id", result);
            Assert.Contains("pe.col", result);   // pe unchanged
            Assert.Contains("JOIN T2 pe", result); // pe unchanged
        }

        [Fact]
        public void RenamesVariable_IncludingAtSign()
        {
            string text = "DECLARE @userId INT; SET @userId = 1; SELECT * FROM T WHERE Id = @userId";
            IReadOnlyList<TextReplacement> edits = TokenRenamer.Rename(text, 0, text.Length, "@userId", "@id");
            string result = Apply(text, edits);
            Assert.Equal("DECLARE @id INT; SET @id = 1; SELECT * FROM T WHERE Id = @id", result);
        }

        [Fact]
        public void RangeRespected_OccurrenceOutsideRangeUntouched()
        {
            string text = "SELECT p.A FROM T p; SELECT p.B FROM T p";
            int secondStmtStart = text.IndexOf("; SELECT") + 2;
            IReadOnlyList<TextReplacement> edits = TokenRenamer.Rename(text, secondStmtStart, text.Length, "p", "q");
            string result = Apply(text, edits);
            // first statement unchanged
            Assert.Contains("SELECT p.A FROM T p;", result);
            // second statement renamed
            Assert.Contains("SELECT q.B FROM T q", result);
        }

        // ---------------------------------------------------------------
        // StatementBounds tests
        // ---------------------------------------------------------------

        [Fact]
        public void StatementBounds_SimpleNoDelimiters()
        {
            string text = "SELECT 1";
            (int start, int end) = TokenRenamer.StatementBounds(text, 0);
            Assert.Equal(0, start);
            Assert.Equal(text.Length, end);
        }

        [Fact]
        public void StatementBounds_BetweenSemicolons()
        {
            string text = "SELECT 1; SELECT 2; SELECT 3";
            // caret in second statement
            int caret = text.IndexOf("SELECT 2") + 2;
            (int start, int end) = TokenRenamer.StatementBounds(text, caret);
            string stmt = text.Substring(start, end - start).Trim();
            Assert.Contains("SELECT 2", stmt);
            Assert.DoesNotContain("SELECT 1", stmt);
            Assert.DoesNotContain("SELECT 3", stmt);
        }

        [Fact]
        public void StatementBounds_GoBatch()
        {
            string text = "SELECT 1\r\nGO\r\nSELECT 2";
            int caret = text.IndexOf("SELECT 2") + 2;
            (int start, int end) = TokenRenamer.StatementBounds(text, caret);
            string stmt = text.Substring(start, end - start).Trim();
            Assert.Contains("SELECT 2", stmt);
            Assert.DoesNotContain("SELECT 1", stmt);
        }
    }
}
