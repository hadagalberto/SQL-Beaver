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
    /// Salva snapshots de cada documento SQL aberto a cada 60 segundos.
    /// Deduplicação por hash SHA-256 (16 chars hex). Índice em index.json (últimos 50).
    /// </summary>
    internal static class SessionSnapshotService
    {
        private static DispatcherTimer _timer;
        private static readonly object _indexLock = new object();

        private static string SessionDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "sessions");

        private static string IndexPath => Path.Combine(SessionDir, "index.json");

        // ---------------------------------------------------------------
        // Initialize — deve ser chamado na UI thread do package
        // ---------------------------------------------------------------
        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(60)
                };
                _timer.Tick += (s, e) => SnapshotOpenDocuments();
                _timer.Start();
                Log.Info("SessionSnapshotService: timer de 60s iniciado.");
            }
            catch (Exception ex)
            {
                Log.Error("SessionSnapshotService.Initialize", ex);
            }
        }

        // ---------------------------------------------------------------
        // Snapshot — chamado na UI thread pelo timer
        // ---------------------------------------------------------------
        public static void SnapshotOpenDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null) return;

                // Captura dados do doc ativo para obter conexão (somente para o ativo)
                ActiveConnection activeConn = ConnectionService.GetActiveConnection();
                string activeDocFullName = null;
                try { activeDocFullName = dte.ActiveDocument?.FullName; } catch { }

                // Percorre todos os documentos (captura na UI thread)
                foreach (Document doc in dte.Documents)
                {
                    try
                    {
                        // Somente .sql (case-insensitive)
                        string fullName = doc.FullName;
                        if (!fullName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Tenta obter TextDocument
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

                        string caption = doc.Name;
                        string hash = ComputeShortHash(text);

                        // Conexão: somente para o documento ativo
                        string server = null;
                        string database = null;
                        bool isActive = string.Equals(fullName, activeDocFullName, StringComparison.OrdinalIgnoreCase);
                        if (isActive && activeConn != null)
                        {
                            server = activeConn.Server;
                            database = activeConn.Database;
                        }

                        // Captura concluída na UI thread; IO em background
                        string capturedText  = text;
                        string capturedHash  = hash;
                        string capturedCap   = caption;
                        string capturedSrv   = server;
                        string capturedDb    = database;
                        string savedAt       = DateTime.Now.ToString("o");

                        _ = Task.Run(() => PersistSnapshot(capturedText, capturedHash, capturedCap, capturedSrv, capturedDb, savedAt));
                    }
                    catch { /* per-document: nunca propagar */ }
                }
            }
            catch (Exception ex)
            {
                Log.Error("SessionSnapshotService.SnapshotOpenDocuments", ex);
            }
        }

        // ---------------------------------------------------------------
        // Persistência (background)
        // ---------------------------------------------------------------
        private static void PersistSnapshot(
            string text, string hash, string caption,
            string server, string database, string savedAt)
        {
            try
            {
                string dir = SessionDir;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Carrega índice atual
                IReadOnlyList<SessionEntry> entries = System.Array.Empty<SessionEntry>();
                lock (_indexLock)
                {
                    if (File.Exists(IndexPath))
                    {
                        try
                        {
                            string existing = File.ReadAllText(IndexPath, Encoding.UTF8);
                            entries = SessionIndex.Load(existing);
                        }
                        catch { }
                    }

                    // Verifica se já temos a mesma caption com o mesmo hash → skip
                    foreach (SessionEntry e in entries)
                    {
                        if (string.Equals(e.Caption, caption, StringComparison.OrdinalIgnoreCase)
                            && e.ContentHash == hash)
                        {
                            return; // nada a fazer
                        }
                    }

                    // Grava o arquivo de snapshot
                    string snapFile = $"snap-{hash}.sql";
                    string snapPath = Path.Combine(dir, snapFile);
                    File.WriteAllText(snapPath, text, Encoding.UTF8);

                    // Upsert no índice e salva
                    var entry = new SessionEntry
                    {
                        File        = snapFile,
                        Caption     = caption,
                        Server      = server,
                        Database    = database,
                        SavedAt     = savedAt,
                        ContentHash = hash
                    };

                    entries = SessionIndex.Upsert(entries, entry);
                    string json = SessionIndex.Serialize(entries);
                    File.WriteAllText(IndexPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SessionSnapshotService.PersistSnapshot ({caption})", ex);
            }
        }

        // ---------------------------------------------------------------
        // Show recovery dialog — UI thread
        // ---------------------------------------------------------------
        public static void ShowRecoveryDialog()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SessionRecoveryDialog.Show();
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private static string ComputeShortHash(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                // 16 hex chars = 8 bytes
                return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
