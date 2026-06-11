using SqlBeaver.Session;
using Xunit;

namespace SqlBeaver.Tests
{
    public class PromptSuppressionRuleTests
    {
        [Fact]
        public void UntitledDirty_SnapshotWritten_Suppresses()
        {
            // Janela SQLQueryN não salva, sem arquivo real, snapshot verificado no disco
            Assert.True(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: false, fileExistsOnDisk: false, snapshotWritten: true));
        }

        [Fact]
        public void UntitledDirty_SnapshotWriteFailed_DoesNotSuppress()
        {
            // Falha de escrita → preferimos o prompt a perder conteúdo
            Assert.False(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: false, fileExistsOnDisk: false, snapshotWritten: false));
        }

        [Fact]
        public void RealFileDirty_DoesNotSuppress()
        {
            // Arquivo real no disco com alterações → prompt normal do SSMS
            Assert.False(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: false, fileExistsOnDisk: true, snapshotWritten: true));
        }

        [Fact]
        public void AlreadySavedDocument_DoesNotSuppress()
        {
            // Documento já salvo → nada a suprimir
            Assert.False(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: true, fileExistsOnDisk: false, snapshotWritten: true));
        }
    }
}
