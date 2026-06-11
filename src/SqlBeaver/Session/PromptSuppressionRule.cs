using System;
using System.IO;

namespace SqlBeaver.Session
{
    /// <summary>Decide quando suprimir o prompt de salvar: somente janelas RASCUNHO
    /// (sem arquivo real OU com arquivo dentro da pasta temp — o SSMS 22 cria um .sql
    /// temporário para cada query nova) e somente após snapshot verificado em disco.</summary>
    public static class PromptSuppressionRule
    {
        public static bool ShouldMarkSaved(bool documentSavedFlag, bool isScratchDocument, bool snapshotWritten)
            => !documentSavedFlag && isScratchDocument && snapshotWritten;

        /// <summary>Rascunho = caminho vazio, arquivo inexistente, ou residente na pasta temp.</summary>
        public static bool IsScratchPath(string fullName, bool fileExists, string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return true;
            if (!fileExists)
                return true;
            if (string.IsNullOrWhiteSpace(tempRoot))
                return false;

            string normalizedTemp = tempRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullName.StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
