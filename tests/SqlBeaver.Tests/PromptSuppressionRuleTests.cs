using System.IO;
using SqlBeaver.Session;
using Xunit;

namespace SqlBeaver.Tests
{
    public class PromptSuppressionRuleTests
    {
        // ------------------------------------------------------------------
        // ShouldMarkSaved — tabela de verdade (parâmetro middle agora é
        // isScratchDocument em vez de fileExistsOnDisk)
        // ------------------------------------------------------------------

        [Fact]
        public void UntitledDirty_SnapshotWritten_Suppresses()
        {
            // Janela SQLQueryN (rascunho), snapshot verificado no disco
            Assert.True(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: false, isScratchDocument: true, snapshotWritten: true));
        }

        [Fact]
        public void UntitledDirty_SnapshotWriteFailed_DoesNotSuppress()
        {
            // Falha de escrita → preferimos o prompt a perder conteúdo
            Assert.False(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: false, isScratchDocument: true, snapshotWritten: false));
        }

        [Fact]
        public void RealFileDirty_DoesNotSuppress()
        {
            // Arquivo real no disco com alterações → prompt normal do SSMS
            Assert.False(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: false, isScratchDocument: false, snapshotWritten: true));
        }

        [Fact]
        public void AlreadySavedDocument_DoesNotSuppress()
        {
            // Documento já salvo → nada a suprimir
            Assert.False(PromptSuppressionRule.ShouldMarkSaved(
                documentSavedFlag: true, isScratchDocument: true, snapshotWritten: true));
        }

        // ------------------------------------------------------------------
        // IsScratchPath — 6 casos
        // ------------------------------------------------------------------

        private static readonly string FakeTempRoot =
            Path.Combine("C:", "Users", "x", "AppData", "Local", "Temp") + Path.DirectorySeparatorChar;

        [Theory]
        // null / empty → rascunho
        [InlineData(null,  false, true)]
        [InlineData("",    false, true)]
        [InlineData("   ", false, true)]
        public void IsScratchPath_NullOrEmpty_IsTrue(string fullName, bool fileExists, bool expected)
        {
            Assert.Equal(expected,
                PromptSuppressionRule.IsScratchPath(fullName, fileExists, FakeTempRoot));
        }

        [Fact]
        public void IsScratchPath_FileNotOnDisk_IsTrue()
        {
            // Arquivo não existe no disco → rascunho
            Assert.True(PromptSuppressionRule.IsScratchPath(
                @"E:\scripts\q.sql", fileExists: false, tempRoot: FakeTempRoot));
        }

        [Fact]
        public void IsScratchPath_FileUnderTemp_IsTrue()
        {
            // Arquivo existente DENTRO da pasta temp (SSMS query window) → rascunho
            string path = Path.Combine(FakeTempRoot, "0a0x1gem.sql");
            Assert.True(PromptSuppressionRule.IsScratchPath(
                path, fileExists: true, tempRoot: FakeTempRoot));
        }

        [Fact]
        public void IsScratchPath_TempRootWithoutTrailingSlash_StillTrue()
        {
            // tempRoot sem barra final → deve funcionar igual
            string tempWithoutSlash = FakeTempRoot.TrimEnd(Path.DirectorySeparatorChar);
            string path = Path.Combine(FakeTempRoot, "abc.sql");
            Assert.True(PromptSuppressionRule.IsScratchPath(
                path, fileExists: true, tempRoot: tempWithoutSlash));
        }

        [Fact]
        public void IsScratchPath_ExistingFileOutsideTemp_IsFalse()
        {
            // Arquivo real fora da pasta temp → não é rascunho
            Assert.False(PromptSuppressionRule.IsScratchPath(
                @"E:\scripts\q.sql", fileExists: true, tempRoot: FakeTempRoot));
        }

        [Fact]
        public void IsScratchPath_CaseInsensitiveTempMatch_IsTrue()
        {
            // Comparação deve ser case-insensitive (Windows)
            string upperPath = FakeTempRoot.ToUpperInvariant() + "QUERY.sql";
            Assert.True(PromptSuppressionRule.IsScratchPath(
                upperPath, fileExists: true, tempRoot: FakeTempRoot));
        }
    }
}
