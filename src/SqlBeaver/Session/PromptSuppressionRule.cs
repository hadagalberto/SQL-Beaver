namespace SqlBeaver.Session
{
    /// <summary>
    /// Decisão pura de supressão do prompt salvar/descartar no shutdown.
    /// </summary>
    public static class PromptSuppressionRule
    {
        /// <summary>Só suprime o prompt de janelas não salvas SEM arquivo real no disco,
        /// e só quando o snapshot foi escrito com sucesso.</summary>
        public static bool ShouldMarkSaved(bool documentSavedFlag, bool fileExistsOnDisk, bool snapshotWritten)
            => !documentSavedFlag && !fileExistsOnDisk && snapshotWritten;
    }
}
