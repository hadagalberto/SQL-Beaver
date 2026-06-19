using SqlBeaver.Editing;
using Xunit;

namespace SqlBeaver.Tests
{
    public class AutoCloseQuoteTests
    {
        [Fact]
        public void EmptyEnd_InsertsPair()
        {
            Assert.Equal(QuoteAction.InsertPair, AutoCloseQuote.Decide("WHERE x = ", 10));
        }

        [Fact]
        public void NextCharIsQuote_SkipsOver()
        {
            // par recém-inserido: "x = '|'" → digitar ' pula a de fechamento
            Assert.Equal(QuoteAction.SkipOver, AutoCloseQuote.Decide("WHERE x = ''", 11));
        }

        [Fact]
        public void InsideOpenString_DoesNotPair()
        {
            // dentro de string aberta: a aspa fecha a string, não pareia
            Assert.Equal(QuoteAction.None, AutoCloseQuote.Decide("WHERE x = 'abc", 14));
        }

        [Fact]
        public void InsideComment_DoesNotPair()
        {
            Assert.Equal(QuoteAction.None, AutoCloseQuote.Decide("-- nota ", 8));
        }

        [Fact]
        public void NextCharIsLetter_DoesNotPair()
        {
            // colado num identificador/valor → não pareia
            Assert.Equal(QuoteAction.None, AutoCloseQuote.Decide("xabc", 1));
        }

        [Fact]
        public void NextCharIsCloser_InsertsPair()
        {
            // "IN (|)" → digitar ' pareia
            Assert.Equal(QuoteAction.InsertPair, AutoCloseQuote.Decide("IN ()", 4));
        }

        [Fact]
        public void OutOfRange_None()
        {
            Assert.Equal(QuoteAction.None, AutoCloseQuote.Decide(null, 0));
            Assert.Equal(QuoteAction.None, AutoCloseQuote.Decide("ab", 5));
        }
    }
}
