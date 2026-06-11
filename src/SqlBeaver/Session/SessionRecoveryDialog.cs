using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;
using SqlBeaver.Navigation;

namespace SqlBeaver.Session
{
    /// <summary>
    /// Diálogo de recuperação de sessão v2: busca as-you-type (caption + conteúdo),
    /// lista (caption/servidor·db/quando) e preview read-only monoespaçado do snapshot.
    /// Duplo-clique / Enter / "Abrir" → abre o snapshot em nova janela de query.
    /// O conteúdo de cada snapshot é lido sob demanda e cacheado em memória.
    /// </summary>
    internal sealed class SessionRecoveryDialog : Form
    {
        private readonly TextBox _searchBox;
        private readonly ListView _listView;
        private readonly TextBox _preview;
        private readonly Button _btnOpen;
        private readonly Button _btnClose;

        private readonly IReadOnlyList<SessionEntry> _entries;
        // Cache do texto de cada snapshot por nome de arquivo.
        private readonly Dictionary<string, string> _contentCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Linhas (com conteúdo já carregado) usadas para filtrar.
        private List<RowEntry> _rows;

        private static string SessionDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SqlBeaver", "sessions");

        private static string IndexPath => Path.Combine(SessionDir, "index.json");

        private sealed class RowEntry
        {
            public SessionEntry Entry;
            public SnapshotRow Row; // Caption/Server/Database/When/ContentText
        }

        // ---------------------------------------------------------------
        public SessionRecoveryDialog(IReadOnlyList<SessionEntry> entries)
        {
            _entries = entries ?? throw new ArgumentNullException(nameof(entries));

            Text = "SQL Beaver — Recuperar consultas";
            Width = 860;
            Height = 540;
            MinimumSize = new Size(560, 360);
            StartPosition = FormStartPosition.CenterScreen;

            // ── Search box (top) ──────────────────────────────────────────────
            var lblSearch = new Label
            {
                Text = "Buscar (título ou conteúdo):",
                AutoSize = true,
            };
            lblSearch.SetBounds(12, 12, 220, 18);
            lblSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            _searchBox = new TextBox { Font = new Font("Segoe UI", 9.75f) };
            _searchBox.SetBounds(12, 32, 824, 24);
            _searchBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _searchBox.TextChanged += (s, e) => ApplyFilter();

            // ── List (left) ───────────────────────────────────────────────────
            _listView = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Font = new Font("Segoe UI", 9),
            };
            _listView.SetBounds(12, 66, 430, 392);
            _listView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_listView, true);
            _listView.Columns.Add("Título", 170);
            _listView.Columns.Add("Servidor · DB", 150);
            _listView.Columns.Add("Quando", 100);
            _listView.SelectedIndexChanged += (s, e) => UpdatePreview();
            _listView.DoubleClick += (s, e) => AcceptSelected();
            _listView.KeyDown += OnListKeyDown;

            // ── Preview (right) ────────────────────────────────────────────────
            _preview = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.75f),
                BackColor = SystemColors.Window,
            };
            _preview.SetBounds(450, 66, 386, 392);
            _preview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // ── Buttons (bottom) ───────────────────────────────────────────────
            _btnOpen = new Button { Text = "Abrir", FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9f) };
            _btnOpen.SetBounds(644, 470, 90, 30);
            _btnOpen.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnOpen.Click += (s, e) => AcceptSelected();

            _btnClose = new Button { Text = "Fechar", FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9f) };
            _btnClose.SetBounds(744, 470, 90, 30);
            _btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { lblSearch, _searchBox, _listView, _preview, _btnOpen, _btnClose });

            AcceptButton = _btnOpen;
            CancelButton = _btnClose;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

            BuildRows();
            ApplyFilter();
            _searchBox.Focus();
        }

        // ---------------------------------------------------------------
        // Build rows (load each snapshot's content lazily/once, cache it)
        // ---------------------------------------------------------------
        private void BuildRows()
        {
            _rows = new List<RowEntry>();
            foreach (SessionEntry entry in _entries)
            {
                string content = LoadContent(entry.File);
                string serverDb = string.IsNullOrEmpty(entry.Server) && string.IsNullOrEmpty(entry.Database)
                    ? ""
                    : $"{entry.Server ?? "?"} · {entry.Database ?? "?"}";
                string when = entry.SavedAt ?? "";
                if (DateTime.TryParse(entry.SavedAt, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
                    when = dt.ToString("yyyy-MM-dd HH:mm:ss");

                _rows.Add(new RowEntry
                {
                    Entry = entry,
                    Row = new SnapshotRow
                    {
                        Caption = entry.Caption ?? "",
                        Server = entry.Server,
                        Database = entry.Database,
                        When = when,
                        ContentText = content ?? "",
                    }
                });
            }
        }

        private string LoadContent(string file)
        {
            if (string.IsNullOrEmpty(file)) return "";
            if (_contentCache.TryGetValue(file, out string cached)) return cached;

            string content = "";
            try
            {
                string path = Path.Combine(SessionDir, file);
                if (File.Exists(path))
                    content = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error("SessionRecoveryDialog.LoadContent (" + file + ")", ex);
            }
            _contentCache[file] = content;
            return content;
        }

        // ---------------------------------------------------------------
        // Filter (uses the pure SnapshotSearchFilter)
        // ---------------------------------------------------------------
        private void ApplyFilter()
        {
            string query = _searchBox.Text;

            var allRows = new List<SnapshotRow>(_rows.Count);
            foreach (RowEntry re in _rows) allRows.Add(re.Row);

            IReadOnlyList<SnapshotRow> filtered = SnapshotSearchFilter.Filter(allRows, query);
            var matchedSet = new HashSet<SnapshotRow>(filtered);

            _listView.BeginUpdate();
            _listView.Items.Clear();
            foreach (RowEntry re in _rows)
            {
                if (!matchedSet.Contains(re.Row)) continue;
                string serverDb = string.IsNullOrEmpty(re.Row.Server) && string.IsNullOrEmpty(re.Row.Database)
                    ? ""
                    : $"{re.Row.Server ?? "?"} · {re.Row.Database ?? "?"}";
                var lvi = new ListViewItem(re.Row.Caption) { Tag = re };
                lvi.SubItems.Add(serverDb);
                lvi.SubItems.Add(re.Row.When);
                _listView.Items.Add(lvi);
            }
            _listView.EndUpdate();

            if (_listView.Items.Count > 0)
                _listView.Items[0].Selected = true;
            else
                _preview.Text = "";
        }

        private void UpdatePreview()
        {
            if (_listView.SelectedItems.Count == 0) { _preview.Text = ""; return; }
            var re = _listView.SelectedItems[0].Tag as RowEntry;
            _preview.Text = re?.Row?.ContentText ?? "";
            _preview.SelectionStart = 0;
            _preview.SelectionLength = 0;
        }

        // ---------------------------------------------------------------
        private void AcceptSelected()
        {
            if (_listView.SelectedItems.Count == 0) return;
            var re = _listView.SelectedItems[0].Tag as RowEntry;
            if (re == null) return;

            try
            {
                string content = LoadContent(re.Entry.File);
                if (string.IsNullOrEmpty(content))
                {
                    MessageBox.Show(this,
                        "Não foi possível ler o conteúdo do snapshot.",
                        "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Close();
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

        // ---------------------------------------------------------------
        // Static entry point (unchanged contract)
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
