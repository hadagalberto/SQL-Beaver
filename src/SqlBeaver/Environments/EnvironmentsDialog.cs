using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Environments
{
    // =========================================================================
    // EnvironmentsDialog — lista e edita as regras de ambiente
    // =========================================================================

    /// <summary>
    /// Diálogo visual para gerenciar as regras de ambiente do SQL Beaver.
    /// Carrega de EnvironmentStore, permite adicionar/editar/remover/reordenar,
    /// e salva com recarga ao vivo via EnvironmentStore.Save + TabColorizer.RefreshAfterRulesChanged.
    /// </summary>
    internal sealed class EnvironmentsDialog : Form
    {
        // ── Controles ─────────────────────────────────────────────────────────
        private readonly ListView   _listView;
        private readonly Button     _btnAdd;
        private readonly Button     _btnEdit;
        private readonly Button     _btnRemove;
        private readonly Button     _btnUp;
        private readonly Button     _btnDown;
        private readonly Button     _btnSave;
        private readonly Button     _btnCancel;

        // Cópia local mutável (não edita o live EnvironmentStore até Salvar)
        private readonly List<EnvironmentRule> _rules;

        // ── Construtor ────────────────────────────────────────────────────────
        public EnvironmentsDialog(IReadOnlyList<EnvironmentRule> currentRules)
        {
            // Clona as regras para não editar os objetos vivos
            _rules = new List<EnvironmentRule>();
            foreach (EnvironmentRule r in currentRules)
                _rules.Add(CloneRule(r));

            Text            = "SQL Beaver — Ambientes (cores)";
            Width           = 760;
            Height          = 440;
            MinimumSize     = new Size(600, 320);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            // ── ListView ──────────────────────────────────────────────────────
            _listView = new ListView
            {
                View           = View.Details,
                FullRowSelect  = true,
                MultiSelect    = false,
                UseCompatibleStateImageBehavior = false,
                Font           = new Font("Segoe UI", 9f),
                Dock           = DockStyle.Fill,
            };
            // Reduz flickering
            typeof(Control)
                .GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_listView, true);

            _listView.Columns.Add("Ambiente",         140);
            _listView.Columns.Add("Cor",               70);
            _listView.Columns.Add("Servidores",        170);
            _listView.Columns.Add("Bancos",            160);
            _listView.Columns.Add("Confirmar execute",  95);

            _listView.DoubleClick          += (s, e) => EditSelected();
            _listView.SelectedIndexChanged += (s, e) => UpdateButtons();
            _listView.KeyDown              += OnListKeyDown;

            // ── Painel de botões laterais ──────────────────────────────────────
            _btnAdd    = MakeButton("Adicionar",  OnAdd);
            _btnEdit   = MakeButton("Editar",     OnEdit);
            _btnRemove = MakeButton("Remover",    OnRemove);
            _btnUp     = MakeButton("Subir",      OnUp);
            _btnDown   = MakeButton("Descer",     OnDown);

            var sidePanel = new Panel { Width = 96, Dock = DockStyle.Right, Padding = new Padding(4) };
            int y = 8;
            foreach (Button b in new[] { _btnAdd, _btnEdit, _btnRemove, _btnUp, _btnDown })
            {
                b.Location = new Point(4, y);
                b.Width    = 84;
                b.Height   = 26;
                sidePanel.Controls.Add(b);
                y += 32;
            }

            // ── Painel de botões inferiores ────────────────────────────────────
            _btnSave   = new Button { Text = "Salvar",   Width = 80, Height = 26, DialogResult = DialogResult.None };
            _btnCancel = new Button { Text = "Cancelar", Width = 80, Height = 26, DialogResult = DialogResult.Cancel };
            _btnSave.Click += OnSave;

            var bottomPanel = new Panel { Height = 40, Dock = DockStyle.Bottom };
            _btnSave.Location   = new Point(bottomPanel.Width - 172, 7);
            _btnCancel.Location = new Point(bottomPanel.Width - 88,  7);
            _btnSave.Anchor     = AnchorStyles.Right | AnchorStyles.Bottom;
            _btnCancel.Anchor   = AnchorStyles.Right | AnchorStyles.Bottom;
            bottomPanel.Controls.Add(_btnSave);
            bottomPanel.Controls.Add(_btnCancel);

            CancelButton = _btnCancel;

            Controls.Add(_listView);
            Controls.Add(sidePanel);
            Controls.Add(bottomPanel);

            RefreshList();
            UpdateButtons();
        }

        // ── Helpers de layout ─────────────────────────────────────────────────
        private static Button MakeButton(string text, EventHandler handler)
        {
            var b = new Button { Text = text };
            b.Click += handler;
            return b;
        }

        // ── ListView ──────────────────────────────────────────────────────────
        private void RefreshList()
        {
            _listView.BeginUpdate();
            _listView.Items.Clear();
            foreach (EnvironmentRule rule in _rules)
            {
                var item = new ListViewItem(rule.Name ?? "")
                {
                    UseItemStyleForSubItems = false,
                };
                // "Cor" subitem: swatch de fundo + texto hex
                var colorSubItem = new ListViewItem.ListViewSubItem
                {
                    Text      = rule.Color ?? "",
                    BackColor = ParseColorSafe(rule.Color),
                    ForeColor = PickForeColor(ParseColorSafe(rule.Color)),
                };
                item.SubItems.Add(colorSubItem);
                item.SubItems.Add(JoinGlobs(rule.Servers));
                item.SubItems.Add(JoinGlobs(rule.Databases));
                item.SubItems.Add(rule.ConfirmExecute ? "Sim" : "Não");
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();
        }

        private void UpdateButtons()
        {
            bool hasSelection = _listView.SelectedIndices.Count > 0;
            int  idx          = hasSelection ? _listView.SelectedIndices[0] : -1;
            _btnEdit.Enabled   = hasSelection;
            _btnRemove.Enabled = hasSelection;
            _btnUp.Enabled     = hasSelection && idx > 0;
            _btnDown.Enabled   = hasSelection && idx < _rules.Count - 1;
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && _btnRemove.Enabled) OnRemove(sender, e);
            if (e.KeyCode == Keys.Enter  && _btnEdit.Enabled)   EditSelected();
        }

        // ── CRUD handlers ─────────────────────────────────────────────────────
        private void OnAdd(object sender, EventArgs e)
        {
            using (var dlg = new EnvironmentRuleDialog(null))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _rules.Add(dlg.Rule);
                RefreshList();
                SelectIndex(_rules.Count - 1);
            }
        }

        private void OnEdit(object sender, EventArgs e) => EditSelected();

        private void EditSelected()
        {
            if (_listView.SelectedIndices.Count == 0) return;
            int idx = _listView.SelectedIndices[0];
            using (var dlg = new EnvironmentRuleDialog(_rules[idx]))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _rules[idx] = dlg.Rule;
                RefreshList();
                SelectIndex(idx);
            }
        }

        private void OnRemove(object sender, EventArgs e)
        {
            if (_listView.SelectedIndices.Count == 0) return;
            int idx = _listView.SelectedIndices[0];
            _rules.RemoveAt(idx);
            RefreshList();
            if (_rules.Count > 0)
                SelectIndex(Math.Min(idx, _rules.Count - 1));
            UpdateButtons();
        }

        private void OnUp(object sender, EventArgs e)
        {
            if (_listView.SelectedIndices.Count == 0) return;
            int idx = _listView.SelectedIndices[0];
            if (idx <= 0) return;
            Swap(idx, idx - 1);
            RefreshList();
            SelectIndex(idx - 1);
        }

        private void OnDown(object sender, EventArgs e)
        {
            if (_listView.SelectedIndices.Count == 0) return;
            int idx = _listView.SelectedIndices[0];
            if (idx >= _rules.Count - 1) return;
            Swap(idx, idx + 1);
            RefreshList();
            SelectIndex(idx + 1);
        }

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                EnvironmentStore.Save(_rules);
                TabColorizer.RefreshAfterRulesChanged();
                _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync(
                    "SQL Beaver: ambientes salvos e aplicados.");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Log.Error("EnvironmentsDialog.Save", ex);
                MessageBox.Show(this,
                    "Falha ao salvar environments.json:\r\n" + ex.Message,
                    "SQL Beaver — Ambientes",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void Swap(int a, int b)
        {
            EnvironmentRule tmp = _rules[a];
            _rules[a] = _rules[b];
            _rules[b] = tmp;
        }

        private void SelectIndex(int idx)
        {
            if (idx >= 0 && idx < _listView.Items.Count)
            {
                _listView.Items[idx].Selected = true;
                _listView.Items[idx].EnsureVisible();
            }
        }

        private static string JoinGlobs(string[] globs)
        {
            if (globs == null || globs.Length == 0) return "*";
            return string.Join(", ", globs);
        }

        private static Color ParseColorSafe(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return SystemColors.Window;
            try { return ColorTranslator.FromHtml(hex); }
            catch { return SystemColors.Window; }
        }

        /// <summary>Escolhe preto ou branco dependendo do brilho do fundo.</summary>
        private static Color PickForeColor(Color bg)
        {
            double luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            return luminance > 0.5 ? Color.Black : Color.White;
        }

        private static EnvironmentRule CloneRule(EnvironmentRule src) => new EnvironmentRule
        {
            Name           = src.Name,
            Color          = src.Color,
            Servers        = src.Servers  != null ? (string[])src.Servers.Clone()   : null,
            Databases      = src.Databases != null ? (string[])src.Databases.Clone() : null,
            ConfirmExecute = src.ConfirmExecute,
        };

        // ── Ponto de entrada estático ─────────────────────────────────────────
        internal static new void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                IReadOnlyList<EnvironmentRule> current = EnvironmentStore.Rules;

                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as DTE2;
                NativeWindow owner = null;
                if (dte != null)
                {
                    owner = new NativeWindow();
                    owner.AssignHandle((IntPtr)(int)dte.MainWindow.HWnd);
                }

                try
                {
                    using (var dlg = new EnvironmentsDialog(current))
                    {
                        if (owner != null)
                            dlg.ShowDialog(owner);
                        else
                            dlg.ShowDialog();
                    }
                }
                finally
                {
                    owner?.ReleaseHandle();
                }
            }
            catch (Exception ex)
            {
                Log.Error("EnvironmentsDialog.Show", ex);
                _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync(
                    "SQL Beaver: falha ao abrir editor de ambientes — veja Output > SQL Beaver");
            }
        }
    }

    // =========================================================================
    // EnvironmentRuleDialog — adiciona / edita uma única regra
    // =========================================================================

    /// <summary>
    /// Diálogo de adição/edição de uma regra de ambiente.
    /// Expõe a regra resultante em <see cref="Rule"/> após DialogResult.OK.
    /// </summary>
    internal sealed class EnvironmentRuleDialog : Form
    {
        // ── Controles ─────────────────────────────────────────────────────────
        private readonly TextBox _txtName;
        private readonly Panel   _colorSwatch;
        private readonly Label   _lblHex;
        private readonly Button  _btnColor;
        private readonly TextBox _txtServers;
        private readonly TextBox _txtDatabases;
        private readonly CheckBox _chkConfirm;
        private readonly Button  _btnOk;
        private readonly Button  _btnCancel;

        private string _currentHex;

        // ── Resultado ─────────────────────────────────────────────────────────
        public EnvironmentRule Rule { get; private set; }

        // ── Construtor ────────────────────────────────────────────────────────
        public EnvironmentRuleDialog(EnvironmentRule existing)
        {
            Text            = existing == null
                              ? "SQL Beaver — Novo ambiente"
                              : "SQL Beaver — Editar ambiente";
            Width           = 420;
            Height          = 300;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;

            _currentHex = existing?.Color ?? "#0078D4";

            int lw = 130; // largura do label
            int fx = 140; // x do controle
            int fw = 240; // largura do controle
            int y  = 14;
            int dy = 34;

            // Nome
            Controls.Add(MakeLabel("Nome:", lw, y));
            _txtName = new TextBox { Left = fx, Top = y, Width = fw, Text = existing?.Name ?? "" };
            Controls.Add(_txtName);
            y += dy;

            // Cor
            Controls.Add(MakeLabel("Cor:", lw, y));
            _colorSwatch = new Panel
            {
                Left      = fx,
                Top       = y,
                Width     = 28,
                Height    = 20,
                BackColor = ParseColorSafe(_currentHex),
                BorderStyle = BorderStyle.FixedSingle,
            };
            Controls.Add(_colorSwatch);
            _lblHex = new Label
            {
                Left      = fx + 34,
                Top       = y + 2,
                Width     = 80,
                Text      = _currentHex,
                Font      = new Font("Consolas", 9f),
            };
            Controls.Add(_lblHex);
            _btnColor = new Button
            {
                Left   = fx + 120,
                Top    = y - 1,
                Width  = 60,
                Height = 23,
                Text   = "Cor…",
            };
            _btnColor.Click += OnPickColor;
            Controls.Add(_btnColor);
            y += dy;

            // Servidores
            Controls.Add(MakeLabel("Servidores (globs,\r\nvírgula):", lw, y));
            _txtServers = new TextBox
            {
                Left      = fx,
                Top       = y,
                Width     = fw,
                Text      = JoinGlobs(existing?.Servers),
                Multiline = false,
            };
            Controls.Add(_txtServers);
            y += dy;

            // Bancos
            Controls.Add(MakeLabel("Bancos (globs,\r\nvírgula):", lw, y));
            _txtDatabases = new TextBox
            {
                Left  = fx,
                Top   = y,
                Width = fw,
                Text  = JoinGlobs(existing?.Databases),
            };
            Controls.Add(_txtDatabases);
            y += dy;

            // Confirmar execute
            _chkConfirm = new CheckBox
            {
                Left    = fx,
                Top     = y,
                Width   = fw + lw,
                Text    = "Confirmar todo Execute neste ambiente",
                Checked = existing?.ConfirmExecute ?? false,
            };
            Controls.Add(_chkConfirm);
            y += dy;

            // Botões OK / Cancelar
            _btnOk     = new Button { Text = "OK",      Width = 72, Height = 26, Left = fx,      Top = y, DialogResult = DialogResult.None };
            _btnCancel = new Button { Text = "Cancelar", Width = 72, Height = 26, Left = fx + 80, Top = y, DialogResult = DialogResult.Cancel };
            _btnOk.Click += OnOk;
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Height = y + 68;
        }

        // ── Handlers ──────────────────────────────────────────────────────────
        private void OnPickColor(object sender, EventArgs e)
        {
            using (var dlg = new ColorDialog
            {
                Color        = ParseColorSafe(_currentHex),
                FullOpen     = true,
                AnyColor     = true,
                SolidColorOnly = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _currentHex        = ColorToHex(dlg.Color);
                _lblHex.Text       = _currentHex;
                _colorSwatch.BackColor = dlg.Color;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            string name = _txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "O nome do ambiente não pode ser vazio.",
                    "SQL Beaver — Ambiente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtName.Focus();
                return;
            }

            Rule = new EnvironmentRule
            {
                Name           = name,
                Color          = _currentHex,
                Servers        = SplitGlobs(_txtServers.Text),
                Databases      = SplitGlobs(_txtDatabases.Text),
                ConfirmExecute = _chkConfirm.Checked,
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static Label MakeLabel(string text, int width, int top)
            => new Label { Text = text, Left = 8, Top = top + 2, Width = width, AutoSize = false };

        private static string JoinGlobs(string[] globs)
        {
            if (globs == null || globs.Length == 0) return "*";
            return string.Join(", ", globs);
        }

        private static string[] SplitGlobs(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new[] { "*" };
            var parts = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (!string.IsNullOrEmpty(t))
                    result.Add(t);
            }
            return result.Count > 0 ? result.ToArray() : new[] { "*" };
        }

        private static Color ParseColorSafe(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.CornflowerBlue;
            try { return ColorTranslator.FromHtml(hex); }
            catch { return Color.CornflowerBlue; }
        }

        private static string ColorToHex(Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
