using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Session
{
    /// <summary>
    /// Restauração de sessão estilo SQL Prompt com keep-clean CONTÍNUO: docs não
    /// salvos são mantidos não-sujos por persistência contínua (5s + troca de
    /// janela + shutdown); o prompt do shell nunca tem o que perguntar; arquivos
    /// reais do disco nunca são tocados. O diálogo "Save changes?" do SSMS enumera
    /// documentos sujos ANTES do DTEEvents.OnBeginShutdown — marcar Saved só no
    /// shutdown chega tarde, por isso cada passada persiste o texto das abas SQL em
    /// %LOCALAPPDATA%\SqlBeaver\lastsession\tab-NN.sql e marca doc.Saved=true
    /// APENAS em janelas não salvas sem arquivo real no disco, e somente depois de
    /// verificar que o conteúdo foi escrito (PromptSuppressionRule). No próximo
    /// startup as abas são reabertas automaticamente em novas janelas de query.
    /// </summary>
    internal static class SessionRestoreService
    {
        // Refs fortes: eventos COM do DTE são coletados pelo GC sem isso.
        private static DTEEvents _dteEvents;
        private static WindowEvents _windowEvents;
        private static DispatcherTimer _persistTimer;
        private static DTE2 _dte;

        // Última escrita VERIFICADA por caption (hash do conteúdo + arquivo de
        // destino): docs inalterados pulam a escrita na cadência de 5s.
        private static readonly Dictionary<string, VerifiedWrite> _verifiedByCaption =
            new Dictionary<string, VerifiedWrite>(StringComparer.OrdinalIgnoreCase);

        // Só persistir DEPOIS que a restauração do startup leu/limpou a sessão
        // anterior — senão a primeira passada do timer sobrescreveria o índice
        // antigo antes de ele ser reaberto.
        private static volatile bool _restorePassDone;

        // Durante o teardown do SSMS as janelas fecham uma a uma. Congelamos: os
        // eventos WindowClosing disparados no shutdown são IGNORADOS (não removem
        // captions do índice acumulado) → a restauração reabre TODAS as abas.
        private static volatile bool _shuttingDown;

        // Estado ACUMULADO em memória: o índice persistido espelha este conjunto.
        // Captions só saem dele por fechamento REAL de janela (não no teardown) ou
        // após uma restauração bem-sucedida.
        private static readonly SessionAccumulator _accumulator = new SessionAccumulator();

        private sealed class VerifiedWrite
        {
            public string ContentHash;
            public string TabPath;
        }

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
                _dte = dte;

                // Shutdown: congela o índice acumulado (ignora WindowClosing do
                // teardown) e faz uma passada final de captura enquanto os docs ainda
                // estão abertos cedo no teardown.
                _dteEvents = dte.Events.DTEEvents;
                _dteEvents.OnBeginShutdown += OnBeginShutdown;

                // Troca de janela = passada imediata (ref forte própria; não
                // compartilhar com o TabColorizer). WindowClosing = remoção explícita
                // de aba (fechamento REAL, fora do teardown).
                _windowEvents = dte.Events.WindowEvents;
                _windowEvents.WindowActivated += (gotFocus, lostFocus) => PersistOpenDocumentsSafe();
                _windowEvents.WindowClosing += OnWindowClosing;

                // Cadência contínua de 5s em ApplicationIdle.
                _persistTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _persistTimer.Tick += (s, e) => PersistOpenDocumentsSafe();
                _persistTimer.Start();

                Log.Info("SessionRestoreService: keep-clean contínuo ativo (5s + troca de janela + shutdown).");
            }
            catch (Exception ex)
            {
                Log.Error("SessionRestoreService.Initialize", ex);
            }
        }

        // ---------------------------------------------------------------
        // Persistência contínua — nunca propagar exceção (timer/eventos COM)
        // ---------------------------------------------------------------
        private static void PersistOpenDocumentsSafe()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = _dte ?? Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null) return;
                PersistOpenDocuments(dte);
            }
            catch (Exception ex)
            {
                Log.Error("SessionRestoreService.PersistOpenDocumentsSafe", ex);
            }
        }

        /// <summary>
        /// UPSERT ONLY. Persiste cada documento .sql aberto com texto não-vazio em
        /// lastsession\tab-NN.sql (NN estável por caption via SessionAccumulator) +
        /// faz upsert no conjunto acumulado. NÃO remove captions ausentes nesta
        /// passada e NÃO apaga tab-*.sql aqui — remoção é exclusiva do fechamento
        /// REAL de janela (OnWindowClosing). Assim o teardown do SSMS (docs fechando
        /// um a um) nunca encolhe o índice. Após cada escrita verificada (ou
        /// inalterada desde uma escrita verificada), aplica PromptSuppressionRule e
        /// marca doc.Saved=true.
        /// </summary>
        private static void PersistOpenDocuments(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // A restauração do startup ainda não leu a sessão anterior — não
            // sobrescrever o índice antes disso.
            if (!_restorePassDone) return;

            string dir = LastSessionDir;
            Directory.CreateDirectory(dir);

            // Conexão: somente para o documento ativo
            ActiveConnection activeConn = ConnectionService.GetActiveConnection();
            string activeDocFullName = null;
            try { activeDocFullName = dte.ActiveDocument?.FullName; } catch { }

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

                    string caption = NormalizeCaption(doc.Name);
                    string hash = ComputeShortHash(text);

                    // Nome de aba ESTÁVEL por caption (atribuído na 1ª vez e mantido).
                    // O acumulador é puro: devolve "tab-NN.sql"; juntamos com o dir.
                    bool isActive = string.Equals(fullName, activeDocFullName, StringComparison.OrdinalIgnoreCase);
                    string tabName = _accumulator.Upsert(
                        caption,
                        hash,
                        isActive ? activeConn?.Server : null,
                        isActive ? activeConn?.Database : null,
                        DateTime.Now.ToString("o"));
                    string tabPath = Path.Combine(dir, tabName);

                    // Escrita verificada — NUNCA suprimir o prompt sem o conteúdo
                    // comprovadamente no disco. Conteúdo + destino inalterados desde
                    // uma escrita verificada → pula a escrita (cadência barata de 5s).
                    bool snapshotWritten = false;
                    if (_verifiedByCaption.TryGetValue(caption, out VerifiedWrite prev)
                        && prev.ContentHash == hash
                        && string.Equals(prev.TabPath, tabPath, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(tabPath))
                    {
                        snapshotWritten = true; // inalterado desde escrita verificada
                    }
                    else
                    {
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
                            _verifiedByCaption[caption] = new VerifiedWrite
                            {
                                ContentHash = hash,
                                TabPath = tabPath
                            };
                        }
                        else
                        {
                            _verifiedByCaption.Remove(caption);
                        }
                    }

                    // Supressão do prompt: só janelas RASCUNHO (sem arquivo real
                    // no disco OU arquivo na pasta temp — o SSMS 22 cria um .sql
                    // temporário para cada query nova), e só com o snapshot verificado.
                    // Documento com arquivo real fora do temp e com alterações →
                    // prompt normal do SSMS.
                    bool savedFlag;
                    try { savedFlag = doc.Saved; }
                    catch { savedFlag = true; } // em dúvida, não mexer

                    bool fileExists = !string.IsNullOrWhiteSpace(fullName) && File.Exists(fullName);
                    bool isScratch = PromptSuppressionRule.IsScratchPath(fullName, fileExists, Path.GetTempPath());

                    if (PromptSuppressionRule.ShouldMarkSaved(savedFlag, isScratch, snapshotWritten))
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

            // Índice = espelho do conjunto ACUMULADO. Conjunto vazio → não sobrescrever
            // (guarda contra o teardown; preserva a sessão a ser restaurada).
            WriteIndexFromAccumulator(dir);
        }

        /// <summary>
        /// Escreve index.json (atômico) a partir do conjunto acumulado, ajustando o
        /// File de cada entrada para o caminho ABSOLUTO no diretório de sessão.
        /// Conjunto vazio → não escreve (ShouldWriteIndex).
        /// </summary>
        private static void WriteIndexFromAccumulator(string dir)
        {
            IReadOnlyList<SessionEntry> accEntries = _accumulator.Entries;
            if (!ShouldWriteIndex(accEntries.Count)) return;

            var entries = new List<SessionEntry>(accEntries.Count);
            foreach (SessionEntry e in accEntries)
            {
                entries.Add(new SessionEntry
                {
                    File        = Path.Combine(dir, e.File),
                    Caption     = e.Caption,
                    Server      = e.Server,
                    Database    = e.Database,
                    SavedAt     = e.SavedAt,
                    ContentHash = e.ContentHash
                });
            }

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
        }

        // ---------------------------------------------------------------
        // Shutdown: congela o índice e faz uma passada final de captura
        // ---------------------------------------------------------------
        private static void OnBeginShutdown()
        {
            // Setar PRIMEIRO: a partir daqui os WindowClosing do teardown são
            // ignorados (não encolhem o índice acumulado).
            _shuttingDown = true;
            PersistOpenDocumentsSafe();
        }

        // ---------------------------------------------------------------
        // Fechamento REAL de janela = remoção explícita do índice acumulado.
        // No teardown (_shuttingDown) é ignorado → todas as abas restauram.
        // ---------------------------------------------------------------
        private static void OnWindowClosing(EnvDTE.Window window)
        {
            try
            {
                if (_shuttingDown) return;
                if (!_restorePassDone) return;

                string docName = null;
                try { docName = window?.Document?.Name; } catch { return; }
                if (string.IsNullOrEmpty(docName)) return;
                if (!docName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) return;

                string caption = NormalizeCaption(docName);
                if (!_accumulator.Remove(caption)) return;

                _verifiedByCaption.Remove(caption);

                string dir = LastSessionDir;
                // Apaga o tab-*.sql da aba fechada (best-effort). Como a remoção já
                // saiu do acumulador, achamos o arquivo pela última escrita verificada
                // não é confiável aqui; recalculamos via Entries seria circular —
                // então varremos os tab-*.sql não mais referenciados.
                try
                {
                    var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (SessionEntry e in _accumulator.Entries)
                        referenced.Add(Path.Combine(dir, e.File));
                    foreach (string candidate in Directory.GetFiles(dir, "tab-*.sql"))
                        if (!referenced.Contains(candidate))
                            try { File.Delete(candidate); } catch { /* best-effort */ }
                }
                catch { /* best-effort */ }

                WriteIndexFromAccumulator(dir);
            }
            catch (Exception ex)
            {
                Log.Error("SessionRestoreService.OnWindowClosing", ex);
            }
        }

        /// <summary>
        /// Retorna true se o índice deve ser (re)escrito. Exposto como internal
        /// para testes unitários puros: um conjunto vazio não deve sobrescrever
        /// a sessão salva (guarda contra o teardown do SSMS).
        /// </summary>
        internal static bool ShouldWriteIndex(int entryCount) => entryCount > 0;

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

                // Conteúdo agora vive nas janelas restauradas (docs untitled novos):
                // a próxima passada de 5s os re-acumula — estado estável desejado.
                // Começo limpo: limpamos os tab-*.sql + index.json E o estado em
                // memória (acumulador + cache de escritas verificadas).
                CleanupLastSession();
                _accumulator.Clear();
                _verifiedByCaption.Clear();

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
            finally
            {
                // Libera a persistência contínua (mesmo se a restauração falhou,
                // a sessão anterior já teve sua chance de ser lida).
                _restorePassDone = true;
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

        /// <summary>SHA-256 truncado (16 hex) — mesmo formato do SessionSnapshotService.</summary>
        private static string ComputeShortHash(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
