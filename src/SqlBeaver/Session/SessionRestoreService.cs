using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Session
{
    /// <summary>
    /// Restauração de sessão estilo SQL Prompt: no fechamento do SSMS
    /// (DTEEvents.OnBeginShutdown) persiste o texto de todas as abas SQL abertas em
    /// %LOCALAPPDATA%\SqlBeaver\lastsession\tab-NN.sql e suprime o prompt
    /// salvar/descartar APENAS de janelas não salvas sem arquivo real no disco —
    /// e somente depois de verificar que o conteúdo foi escrito. No próximo startup
    /// as abas são reabertas automaticamente em novas janelas de query.
    /// </summary>
    internal static class SessionRestoreService
    {
        // Ref forte: eventos COM do DTE são coletados pelo GC sem isso.
        private static DTEEvents _dteEvents;

        private static string LastSessionDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "lastsession");

        private static string IndexPath => Path.Combine(LastSessionDir, "index.json");

        // ---------------------------------------------------------------
        // Initialize — UI thread do package
        // ---------------------------------------------------------------
        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null)
                {
                    Log.Info("SessionRestoreService: DTE indisponível — restauração de sessão desabilitada.");
                    return;
                }

                _dteEvents = dte.Events.DTEEvents;
                _dteEvents.OnBeginShutdown += SaveSessionOnShutdown;
                Log.Info("SessionRestoreService: hook de OnBeginShutdown registrado.");
            }
            catch (Exception ex)
            {
                Log.Error("SessionRestoreService.Initialize", ex);
            }
        }

        // ---------------------------------------------------------------
        // Shutdown — salvar TODAS as abas SQL (síncrono: o processo está morrendo)
        // ---------------------------------------------------------------
        private static void SaveSessionOnShutdown()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null) return;

                string dir = LastSessionDir;

                // Limpa restos de sessões anteriores antes de gravar a atual
                try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
                catch { /* best-effort */ }
                Directory.CreateDirectory(dir);

                // Conexão: somente para o documento ativo
                ActiveConnection activeConn = ConnectionService.GetActiveConnection();
                string activeDocFullName = null;
                try { activeDocFullName = dte.ActiveDocument?.FullName; } catch { }

                var entries = new List<SessionEntry>();
                int n = 0;

                foreach (Document doc in dte.Documents)
                {
                    try
                    {
                        string fullName = doc.FullName;
                        if (!fullName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var textDoc = doc.Object("TextDocument") as TextDocument;
                        if (textDoc == null) continue;

                        string text;
                        try
                        {
                            text = textDoc.StartPoint.CreateEditPoint()
                                          .GetText(textDoc.EndPoint);
                        }
                        catch { continue; }

                        if (string.IsNullOrEmpty(text)) continue;

                        n++;
                        string tabPath = Path.Combine(dir, $"tab-{n:00}.sql");

                        // Escrita SÍNCRONA e verificada — NUNCA suprimir o prompt
                        // sem o conteúdo comprovadamente no disco.
                        bool snapshotWritten = false;
                        try
                        {
                            File.WriteAllText(tabPath, text, Encoding.UTF8);
                            var info = new FileInfo(tabPath);
                            snapshotWritten = info.Exists
                                && info.Length >= Encoding.UTF8.GetByteCount(text);
                        }
                        catch (Exception writeEx)
                        {
                            Log.Error($"SessionRestoreService: falha ao gravar {tabPath} — prompt do SSMS preservado", writeEx);
                        }

                        if (snapshotWritten)
                        {
                            bool isActive = string.Equals(fullName, activeDocFullName, StringComparison.OrdinalIgnoreCase);
                            entries.Add(new SessionEntry
                            {
                                File     = tabPath,
                                Caption  = NormalizeCaption(doc.Name),
                                Server   = isActive ? activeConn?.Server : null,
                                Database = isActive ? activeConn?.Database : null,
                                SavedAt  = DateTime.Now.ToString("o")
                            });
                        }

                        // Supressão do prompt: só janelas não-salvas SEM arquivo real
                        // no disco (SQLQueryN), e só com o snapshot verificado.
                        // Documento com arquivo real e alterações → prompt normal do SSMS.
                        bool savedFlag;
                        try { savedFlag = doc.Saved; }
                        catch { savedFlag = true; } // em dúvida, não mexer

                        bool fileExistsOnDisk = File.Exists(fullName);

                        if (PromptSuppressionRule.ShouldMarkSaved(savedFlag, fileExistsOnDisk, snapshotWritten))
                        {
                            try { doc.Saved = true; }
                            catch (Exception markEx)
                            {
                                Log.Error($"SessionRestoreService: falha ao marcar Saved ({doc.Name})", markEx);
                            }
                        }
                    }
                    catch { /* per-document: nunca propagar */ }
                }

                // Índice por último, atômico (tmp + Replace)
                if (entries.Count > 0)
                {
                    string json = SessionIndex.Serialize(entries);
                    string tmp  = IndexPath + ".tmp";
                    File.WriteAllText(tmp, json, Encoding.UTF8);
                    try
                    {
                        File.Replace(tmp, IndexPath, null);
                    }
                    catch (FileNotFoundException)
                    {
                        File.Move(tmp, IndexPath);
                    }
                    Log.Info($"SessionRestoreService: {entries.Count} aba(s) persistida(s) para a próxima sessão.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("SessionRestoreService.SaveSessionOnShutdown", ex);
            }
        }

        // ---------------------------------------------------------------
        // Startup — reabrir as abas da última sessão
        // ---------------------------------------------------------------
        public static async Task RestorePreviousSessionAsync()
        {
            try
            {
                // Deixa o shell assentar antes de abrir janelas
                await Task.Delay(2000).ConfigureAwait(false);

                string indexPath = IndexPath;
                if (!File.Exists(indexPath)) return;

                IReadOnlyList<SessionEntry> entries =
                    SessionIndex.Load(File.ReadAllText(indexPath, Encoding.UTF8));
                if (entries.Count == 0)
                {
                    CleanupLastSession();
                    return;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                int restored = 0;
                foreach (SessionEntry entry in entries)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(entry.File) || !File.Exists(entry.File))
                            continue;

                        string content = File.ReadAllText(entry.File, Encoding.UTF8);
                        if (string.IsNullOrEmpty(content)) continue;

                        Navigation.DefinitionService.OpenNewQueryWindow(content);
                        restored++;
                    }
                    catch (Exception entryEx)
                    {
                        Log.Error($"SessionRestoreService: falha ao restaurar '{entry.Caption}'", entryEx);
                    }
                }

                // Conteúdo agora vive nas janelas (e nos snapshots de 60s) — apaga tudo
                CleanupLastSession();

                if (restored > 0)
                {
                    Log.Info($"{restored} aba(s) restaurada(s) da última sessão.");
                    await Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync(
                        $"SQL Beaver: {restored} aba(s) restaurada(s) da última sessão.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("SessionRestoreService.RestorePreviousSessionAsync", ex);
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private static void CleanupLastSession()
        {
            try
            {
                if (Directory.Exists(LastSessionDir))
                    Directory.Delete(LastSessionDir, recursive: true);
            }
            catch (Exception ex)
            {
                Log.Error("SessionRestoreService.CleanupLastSession", ex);
            }
        }

        /// <summary>Remove o '*' de sujeira do fim da caption, se presente.</summary>
        private static string NormalizeCaption(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return caption;
            return caption.TrimEnd().TrimEnd('*').TrimEnd();
        }
    }
}
