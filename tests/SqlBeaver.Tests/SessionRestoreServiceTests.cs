using SqlBeaver.Session;
using Xunit;

namespace SqlBeaver.Tests
{
    public class SessionRestoreServiceTests
    {
        // ── ShouldWriteIndex ──────────────────────────────────────────────────
        // Guarda pura: garante que o índice NÃO é sobrescrito com conjunto vazio
        // durante o teardown do SSMS (quando todos os docs fecham antes da última
        // passada de persistência).

        [Fact]
        public void ShouldWriteIndex_ZeroEntries_ReturnsFalse()
        {
            Assert.False(SessionRestoreService.ShouldWriteIndex(0));
        }

        [Fact]
        public void ShouldWriteIndex_OneOrMoreEntries_ReturnsTrue()
        {
            Assert.True(SessionRestoreService.ShouldWriteIndex(1));
            Assert.True(SessionRestoreService.ShouldWriteIndex(5));
        }
    }
}
