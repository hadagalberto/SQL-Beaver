using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;
using SqlBeaver.Navigation;

namespace SqlBeaver.Session
{
    /// <summary>
    /// Diálogo de recuperação de sessão: lista os snapshots do index.json,
    /// ordenados do mais recente para o mais antigo.
    /// Duplo-clique / Enter → abre o snapshot em nova janela de query.
    /// </summary>
    internal sealed class SessionRecoveryDialog : Form
    {
        private readonly ListView _listView;
        private readonly IReadOnlyList<SessionEntry> _entries;

        private static string SessionDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "sessions");

        private static string IndexPath => Path.Combine(SessionDir, "index.json");

        // ---------------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------------
        public SessionRecoveryDialog(IReadOnlyList<SessionEntry> entries)
        {
            _entries = entries ?? throw new ArgumentNullException(nameof(entries));

            Text = "SQL Beaver — Recuperar consultas";
            Width = 640;
            Height = 420;
            MinimumSize = new System.Drawing.Size(400, 280);
            StartPosition = FormStartPosition.CenterScreen;

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                Font = new System.Drawing.Font("Segoe UI", 9)
            };

            // Double-buffer to reduce flicker
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_listView, true);

            _listView.Columns.Add("Caption", 240);
            _listView.Columns.Add("Servidor · DB", 200);
            _listView.Columns.Add("Quando", 160);

            _listView.KeyDown += OnListKeyDown;
            _listView.DoubleClick += (s, e) => AcceptSelected();

            Controls.Add(_listView);

            KeyPreview = true;
            KeyDown += OnFormKeyDown;

            PopulateList();
            if (_listView.Items.Count > 0)
                _listView.Items[0].Selected = true;
            _listView.Focus();
        }

        // ---------------------------------------------------------------
        // Populate
        // ---------------------------------------------------------------
        private void PopulateList()
        {
            _listView.BeginUpdate();
            foreach (SessionEntry entry in _entries)
            {
                string serverDb = string.IsNullOrEmpty(entry.Server) && string.IsNullOrEmpty(entry.Database)
                    ? ""
                    : $"{entry.Server ?? "?"} · {entry.Database ?? "?"}";

                string when = entry.SavedAt ?? "";
                // Try to parse and reformat nicely
                if (DateTime.TryParse(entry.SavedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
                    when = dt.ToString("yyyy-MM-dd HH:mm:ss");

                var lvi = new ListViewItem(entry.Caption ?? "") { Tag = entry };
                lvi.SubItems.Add(serverDb);
                lvi.SubItems.Add(when);
                _listView.Items.Add(lvi);
            }
            _listView.EndUpdate();
        }

        // ---------------------------------------------------------------
        // Selection
        // ---------------------------------------------------------------
        private void AcceptSelected()
        {
            if (_listView.SelectedItems.Count == 0) return;
            var entry = _listView.SelectedItems[0].Tag as SessionEntry;
            if (entry == null) return;

            try
            {
                string snapPath = Path.Combine(SessionDir, entry.File ?? "");
                if (!File.Exists(snapPath))
                {
                    MessageBox.Show(
                        this,
                        $"Arquivo de snapshot não encontrado:\r\n{snapPath}",
                        "SQL Beaver",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string content = File.ReadAllText(snapPath, System.Text.Encoding.UTF8);
                Close();

                // Switch to UI thread for OpenNewQueryWindow
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    DefinitionService.OpenNewQueryWindow(content);
                });
            }
            catch (Exception ex)
            {
                Log.Error("SessionRecoveryDialog.AcceptSelected", ex);
            }
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { AcceptSelected(); e.Handled = true; }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        }

        // ---------------------------------------------------------------
        // Static entry point
        // ---------------------------------------------------------------
        internal static new void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string indexPath = IndexPath;
                IReadOnlyList<SessionEntry> entries = Array.Empty<SessionEntry>();

                if (File.Exists(indexPath))
                {
                    string json = File.ReadAllText(indexPath, System.Text.Encoding.UTF8);
                    entries = SessionIndex.Load(json);
                }

                if (entries.Count == 0)
                {
                    _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync(
                        "SQL Beaver: nenhum snapshot de sessão encontrado.");
                    return;
                }

                var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE))
                    as EnvDTE80.DTE2;

                NativeWindow owner = null;
                if (dte != null)
                {
                    owner = new NativeWindow();
                    owner.AssignHandle((IntPtr)(int)dte.MainWindow.HWnd);
                }

                using (var dlg = new SessionRecoveryDialog(entries))
                {
                    try
                    {
                        if (owner != null) dlg.ShowDialog(owner);
                        else dlg.ShowDialog();
                    }
                    finally
                    {
                        owner?.ReleaseHandle();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("SessionRecoveryDialog.Show", ex);
                _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync(
                    "SQL Beaver: falha ao abrir recuperação de sessão — veja Output > SQL Beaver");
            }
        }
    }
}
