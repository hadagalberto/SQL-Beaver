using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Completion;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;

namespace SqlBeaver.Navigation
{
    /// <summary>
    /// Diálogo de busca rápida de objetos do banco ativo.
    /// Filtro as-you-type sobre tabelas + objects do metadata cache.
    /// </summary>
    internal sealed class FindObjectDialog : Form
    {
        private const int MaxDisplayRows = 500;

        // ---------------------------------------------------------------
        // Result exposed after OK
        // ---------------------------------------------------------------
        internal sealed class SelectedResult
        {
            public string Schema { get; set; }
            public string Name { get; set; }
            public bool IsTable { get; set; }
            public DbObjectType? Type { get; set; }
        }

        internal SelectedResult Result { get; private set; }

        // ---------------------------------------------------------------
        // Controls
        // ---------------------------------------------------------------
        private readonly TextBox _searchBox;
        private readonly ComboBox _typeFilter;
        private readonly ListView _listView;

        // ---------------------------------------------------------------
        // Data
        // ---------------------------------------------------------------
        private readonly DbMetadata _metadata;
        private readonly string _server;
        private readonly string _database;
        private readonly List<FindItem> _allItems = new List<FindItem>();

        private sealed class FindItem
        {
            public string Schema;
            public string Name;
            public string TypeLabel;
            public bool IsTable;
            public DbObjectType? ObjectType;
        }

        // ---------------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------------
        public FindObjectDialog(DbMetadata metadata, string server, string database)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _server = server;
            _database = database;

            Text = "SQL Beaver — Localizar objeto";
            Width = 600;
            Height = 480;
            MinimumSize = new System.Drawing.Size(400, 300);
            StartPosition = FormStartPosition.CenterScreen;

            // Search box
            _searchBox = new TextBox { Dock = DockStyle.Top, Font = new System.Drawing.Font("Segoe UI", 10) };
            _searchBox.TextChanged += (s, e) => Refilter();

            // Type filter combo
            _typeFilter = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new System.Drawing.Font("Segoe UI", 9)
            };
            _typeFilter.Items.AddRange(new object[] { "Todos", "Tabelas", "Procedures", "Views", "Funções" });
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectedIndexChanged += (s, e) => Refilter();

            // List view
            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                Font = new System.Drawing.Font("Segoe UI", 9)
            };
            // Double-buffer via reflection to reduce flicker
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_listView, true);

            _listView.Columns.Add("Nome", 220);
            _listView.Columns.Add("Schema", 120);
            _listView.Columns.Add("Tipo", 120);
            _listView.KeyDown += OnListKeyDown;
            _listView.DoubleClick += OnListDoubleClick;

            // Layout
            var panel = new Panel { Dock = DockStyle.Top, Height = 60 };
            _typeFilter.Height = 25;
            _typeFilter.Width = Width - 20;
            _searchBox.Height = 25;
            _searchBox.Width = Width - 20;
            Controls.Add(_listView);
            Controls.Add(_typeFilter);
            Controls.Add(_searchBox);

            KeyPreview = true;
            KeyDown += OnFormKeyDown;

            BuildAllItems();
            Refilter();
            _searchBox.Focus();
        }

        // ---------------------------------------------------------------
        // Data
        // ---------------------------------------------------------------
        private void BuildAllItems()
        {
            // Tables
            foreach (TableEntry t in _metadata.Tables)
                _allItems.Add(new FindItem { Schema = t.Schema, Name = t.Name, TypeLabel = "Tabela", IsTable = true });

            // Objects (procedures, views, functions)
            foreach (ObjectEntry obj in _metadata.Objects)
            {
                string label;
                switch (obj.Type)
                {
                    case DbObjectType.Procedure:      label = "Procedure";   break;
                    case DbObjectType.View:           label = "View";        break;
                    case DbObjectType.ScalarFunction: label = "Função";      break;
                    case DbObjectType.TableFunction:  label = "Função";      break;
                    default:                          label = obj.Type.ToString(); break;
                }
                _allItems.Add(new FindItem
                {
                    Schema = obj.Schema, Name = obj.Name,
                    TypeLabel = label, IsTable = false, ObjectType = obj.Type
                });
            }
        }

        private void Refilter()
        {
            string filter = _searchBox.Text.Trim();
            string typeFilter = _typeFilter.SelectedItem as string ?? "Todos";

            // Sem texto de filtro: os mais usados primeiro (tabelas com execuções registradas),
            // depois ordem alfabética. Com filtro, mantém a ordem original da lista.
            IEnumerable<FindItem> source = _allItems;
            if (string.IsNullOrEmpty(filter))
            {
                source = _allItems
                    .OrderByDescending(it => it.IsTable
                        ? Usage.UsageStore.GetTableCount(_server, _database, DbMetadata.TableKey(it.Schema, it.Name))
                        : 0)
                    .ThenBy(it => it.Name, StringComparer.OrdinalIgnoreCase);
            }

            _listView.BeginUpdate();
            _listView.Items.Clear();
            int count = 0;
            foreach (FindItem item in source)
            {
                if (count >= MaxDisplayRows) break;

                // Type filter
                if (typeFilter == "Tabelas" && !item.IsTable) continue;
                if (typeFilter == "Procedures" && item.TypeLabel != "Procedure") continue;
                if (typeFilter == "Views" && item.TypeLabel != "View") continue;
                if (typeFilter == "Funções" && item.TypeLabel != "Função") continue;

                // Text filter
                if (!string.IsNullOrEmpty(filter) &&
                    item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.Schema.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var lvi = new ListViewItem(item.Name) { Tag = item };
                lvi.SubItems.Add(item.Schema);
                lvi.SubItems.Add(item.TypeLabel);
                _listView.Items.Add(lvi);
                count++;
            }
            _listView.EndUpdate();
        }

        // ---------------------------------------------------------------
        // Selection
        // ---------------------------------------------------------------
        private void AcceptSelected()
        {
            if (_listView.SelectedItems.Count == 0) return;
            var item = _listView.SelectedItems[0].Tag as FindItem;
            if (item == null) return;
            Result = new SelectedResult
            {
                Schema = item.Schema,
                Name = item.Name,
                IsTable = item.IsTable,
                Type = item.ObjectType
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnListDoubleClick(object sender, EventArgs e) => AcceptSelected();

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { AcceptSelected(); e.Handled = true; }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            if (e.KeyCode == Keys.Enter && _listView.Focused == false) AcceptSelected();
            // Arrow keys in search box → move list selection
            if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && _searchBox.Focused)
            {
                _listView.Focus();
                if (_listView.Items.Count > 0 && _listView.SelectedItems.Count == 0)
                    _listView.Items[0].Selected = true;
            }
        }

        // ---------------------------------------------------------------
        // Static entry point
        // ---------------------------------------------------------------
        internal static new void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                ActiveConnection conn = ConnectionService.GetActiveConnection();
                if (conn == null) { ShowStatus("Localizar objeto: sem conexão ativa."); return; }

                DbMetadata metadata = SqlBeaverCompletionSourceProvider.Cache.TryGet(
                    conn.Server, conn.Database,
                    new MetadataRequest
                    {
                        ConnectionString = conn.ConnectionString,
                        AccessToken = conn.AccessToken,
                        ProviderConnectionType = conn.ProviderConnectionType
                    });

                if (metadata == null)
                {
                    ShowStatus("Localizar objeto: cache ainda carregando — tente novamente em instantes.");
                    return;
                }

                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as DTE2;
                NativeWindow owner = null;
                if (dte != null)
                {
                    owner = new NativeWindow();
                    owner.AssignHandle((IntPtr)(int)dte.MainWindow.HWnd);
                }

                using (var dlg = new FindObjectDialog(metadata, conn.Server, conn.Database))
                {
                    DialogResult result;
                    try
                    {
                        result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                    }
                    finally
                    {
                        owner?.ReleaseHandle();
                    }

                    if (result != DialogResult.OK || dlg.Result == null) return;

                    SelectedResult sel = dlg.Result;
                    DefinitionService.GoTo(sel.Schema, sel.Name);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Localizar objeto", ex);
                ShowStatus("Localizar objeto: falha — veja Output > SQL Beaver");
            }
        }

        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }
    }
}
