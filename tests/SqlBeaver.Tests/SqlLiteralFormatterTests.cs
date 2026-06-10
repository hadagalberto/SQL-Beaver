using System;
using SqlBeaver.Scripting;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SqlLiteralFormatterTests
    {
        [Fact]
        public void NullDisplayString_BecomesBareNull()
        {
            Assert.Equal("NULL", SqlLiteralFormatter.Format("NULL", typeof(string)));
            Assert.Equal("NULL", SqlLiteralFormatter.Format("NULL", typeof(int)));
            Assert.Equal("NULL", SqlLiteralFormatter.Format("NULL", null));
        }

        [Theory]
        [InlineData("42", typeof(int), "42")]
        [InlineData("-7", typeof(long), "-7")]
        [InlineData("3.14", typeof(decimal), "3.14")]
        [InlineData("3,14", typeof(decimal), "3.14")] // grid pode exibir na cultura pt-BR
        [InlineData("0", typeof(byte), "0")]
        public void Numerics_AreUnquoted_InvariantCulture(string display, Type type, string expected)
        {
            Assert.Equal(expected, SqlLiteralFormatter.Format(display, type));
        }

        [Theory]
        // pt-BR thousand separator + comma decimal → unquoted invariant
        [InlineData("1.234,56", typeof(decimal), "1234.56")]
        // scientific notation from float/real columns → normalized via double round-trip "R"
        // On .NET 4.8 double.Parse("1E-08").ToString("R", Invariant) == "0.00000001"
        [InlineData("1E-08", typeof(double), "0.00000001")]
        // string column with comma-containing value must NEVER be treated as numeric
        [InlineData("3,14", typeof(string), "N'3,14'")]
        public void Numerics_EdgeCases(string display, Type type, string expected)
        {
            Assert.Equal(expected, SqlLiteralFormatter.Format(display, type));
        }

        [Fact]
        public void Numeric_ThatDoesNotParse_FallsBackToQuotedString()
        {
            Assert.Equal("N'abc'", SqlLiteralFormatter.Format("abc", typeof(int)));
        }

        [Theory]
        [InlineData("True", "1")]
        [InlineData("False", "0")]
        [InlineData("1", "1")]
        [InlineData("0", "0")]
        public void Booleans_BecomeBit(string display, string expected)
        {
            Assert.Equal(expected, SqlLiteralFormatter.Format(display, typeof(bool)));
        }

        [Fact]
        public void Guid_IsQuoted()
        {
            Assert.Equal("'8f2c1a90-0000-0000-0000-000000000001'",
                SqlLiteralFormatter.Format("8f2c1a90-0000-0000-0000-000000000001", typeof(Guid)));
        }

        [Fact]
        public void DateTime_IsNQuoted()
        {
            Assert.Equal("N'2026-06-10 12:00:00'",
                SqlLiteralFormatter.Format("2026-06-10 12:00:00", typeof(DateTime)));
        }

        [Fact]
        public void String_IsNQuoted_WithEscapedApostrophes()
        {
            Assert.Equal("N'it''s'", SqlLiteralFormatter.Format("it's", typeof(string)));
        }

        [Fact]
        public void UnknownType_IsTreatedAsString()
        {
            Assert.Equal("N'x'", SqlLiteralFormatter.Format("x", null));
        }

        [Fact]
        public void Binary_HexDisplay_StaysRaw()
        {
            Assert.Equal("0x1A2B", SqlLiteralFormatter.Format("0x1A2B", typeof(byte[])));
        }
    }
}
