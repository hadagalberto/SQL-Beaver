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

        [Fact]
        public void ShouldClearSavedSession_UserClosedAllTabs_ReturnsTrue()
        {
            // fechamento REAL (fora do teardown) que zerou o conjunto → descartar a sessão
            Assert.True(SessionRestoreService.ShouldClearSavedSession(0, shuttingDown: false));
        }

        [Fact]
        public void ShouldClearSavedSession_DuringShutdown_ReturnsFalse()
        {
            // no teardown as abas fecham uma a uma — NÃO é "usuário fechou tudo"
            Assert.False(SessionRestoreService.ShouldClearSavedSession(0, shuttingDown: true));
        }

        [Fact]
        public void ShouldClearSavedSession_StillHasTabs_ReturnsFalse()
        {
            Assert.False(SessionRestoreService.ShouldClearSavedSession(2, shuttingDown: false));
            Assert.False(SessionRestoreService.ShouldClearSavedSession(2, shuttingDown: true));
        }
    }
}
