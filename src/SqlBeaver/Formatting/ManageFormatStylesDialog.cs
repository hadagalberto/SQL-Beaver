using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Formatting
{
    /// <summary>
    /// Diálogo para gerenciar estilos de formatação nomeados.
    /// Permite: Novo, Duplicar, Renomear, Excluir, Importar (.json) e Exportar.
    /// A edição dos knobs em si é feita diretamente no arquivo .json;
    /// este diálogo gerencia os arquivos (nomes, copiar, apagar).
    /// </summary>
    internal sealed class ManageFormatStylesDialog : Form
    {
        private readonly ListBox  _listBox;
        private readonly Button   _btnNew;
        private readonly Button   _btnDuplicate;
        private readonly Button   _btnRename;
        private readonly Button   _btnDelete;
        private readonly Button   _btnImport;
        private readonly Button   _btnExport;
        private readonly Button   _btnSetActive;
        private readonly Button   _btnClose;
        private readonly Label    _lblActive;

        internal ManageFormatStylesDialog()
        {
            Text            = "SQL Beaver — Gerenciar estilos de formatação";
            Width           = 520;
            Height          = 400;
            MinimumSize     = new Size(420, 300);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            // ── Left panel: list ─────────────────────────────────────────────
            _listBox = new ListBox
            {
                SelectionMode = SelectionMode.One,
                Font          = new Font("Segoe UI", 9.75f),
                IntegralHeight = false,
            };
            _listBox.SetBounds(12, 40, 280, 300);
            _listBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;

            _lblActive = new Label
            {
                Text      = "",
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = SystemColors.GrayText,
                AutoSize  = false,
            };
            _lblActive.SetBounds(12, 12, 280, 22);
            _lblActive.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            // ── Right panel: buttons ─────────────────────────────────────────
            _btnSetActive  = MakeButton("Ativar",     0);
            _btnNew        = MakeButton("Novo",       1);
            _btnDuplicate  = MakeButton("Duplicar",   2);
            _btnRename     = MakeButton("Renomear",   3);
            _btnDelete     = MakeButton("Excluir",    4);
            _btnImport     = MakeButton("Importar…",  6);
            _btnExport     = MakeButton("Exportar…",  7);
            _btnClose      = MakeButton("Fechar",     9);

            // Layout buttons on the right side
            int bx = 308;
            Button[] btns = { _btnSetActive, _btnNew, _btnDuplicate, _btnRename, _btnDelete,
                               _btnImport, _btnExport, _btnClose };
            for (int i = 0; i < btns.Length; i++)
            {
                int by = 40 + i * 36;
                if (i == 5) by += 10; // gap before Import
                if (i == 7) by += 10; // gap before Close
                btns[i].SetBounds(bx, by, 170, 30);
                btns[i].Anchor = AnchorStyles.Top | AnchorStyles.Right;
            }

            Controls.AddRange(new Control[] {
                _lblActive, _listBox,
                _btnSetActive, _btnNew, _btnDuplicate, _btnRename, _btnDelete,
                _btnImport, _btnExport, _btnClose });

            // ── Events ───────────────────────────────────────────────────────
            _btnSetActive.Click  += OnSetActive;
            _btnNew.Click        += OnNew;
            _btnDuplicate.Click  += OnDuplicate;
            _btnRename.Click     += OnRename;
            _btnDelete.Click     += OnDelete;
            _btnImport.Click     += OnImport;
            _btnExport.Click     += OnExport;
            _btnClose.Click      += (s, e) => Close();

            Load += (s, e) => RefreshList();
        }

        // ── Public factory ────────────────────────────────────────────────────

        internal static void ShowManager(IWin32Window owner)
        {
            try
            {
                using (var dlg = new ManageFormatStylesDialog())
                    dlg.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.Error("ManageFormatStylesDialog", ex);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Button MakeButton(string caption, int _)
        {
            return new Button
            {
                Text      = caption,
                Font      = new Font("Segoe UI", 9f),
                FlatStyle = FlatStyle.System,
            };
        }

        private void RefreshList()
        {
            string sel = _listBox.SelectedItem as string;
            _listBox.Items.Clear();
            IReadOnlyList<string> styles = FormatStyleStore.ListStyles();
            string active = FormatStyleStore.ActiveStyleName;
            foreach (string s in styles)
                _listBox.Items.Add(s);

            _lblActive.Text = string.IsNullOrEmpty(active)
                ? "Nenhum estilo ativo"
                : $"Ativo: {active}";

            // Re-select previously selected item if it still exists
            if (sel != null)
            {
                int idx = _listBox.Items.IndexOf(sel);
                if (idx >= 0) _listBox.SelectedIndex = idx;
            }

            if (_listBox.SelectedIndex < 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
        }

        private string SelectedStyle => _listBox.SelectedItem as string;

        private bool RequireSelection()
        {
            if (SelectedStyle != null) return true;
            MessageBox.Show(this, "Selecione um estilo na lista.", "SQL Beaver",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void OnSetActive(object sender, EventArgs e)
        {
            if (!RequireSelection()) return;
            try
            {
                FormatStyleStore.SetActive(SelectedStyle);
                RefreshList();
            }
            catch (Exception ex)
            {
                Log.Error("ManageFormatStyles: SetActive", ex);
                MessageBox.Show(this, "Falha ao ativar estilo: " + ex.Message,
                    "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnNew(object sender, EventArgs e)
        {
            string name = PromptName("Novo estilo", "Nome:", "");
            if (name == null) return;
            try
            {
                FormatStyleStore.Save(name, FormatOptions.CreateDefault());
                RefreshList();
                SelectStyle(name);
            }
            catch (Exception ex)
            {
                Log.Error("ManageFormatStyles: New", ex);
                MessageBox.Show(this, "Falha ao criar estilo: " + ex.Message,
                    "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnDuplicate(object sender, EventArgs e)
        {
            if (!RequireSelection()) return;
            string src  = SelectedStyle;
            string name = PromptName("Duplicar estilo", "Nome do novo estilo:", src + " (2)");
            if (name == null) return;
            try
            {
                FormatStyleStore.Duplicate(src, name);
                RefreshList();
                SelectStyle(name);
            }
            catch (Exception ex)
            {
                Log.Error("ManageFormatStyles: Duplicate", ex);
                MessageBox.Show(this, "Falha ao duplicar estilo: " + ex.Message,
                    "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRename(object sender, EventArgs e)
        {
            if (!RequireSelection()) return;
            string old  = SelectedStyle;
            string name = PromptName("Renomear estilo", "Novo nome:", old);
            if (name == null || name == old) return;
            try
            {
                FormatStyleStore.Rename(old, name);
                RefreshList();
                SelectStyle(name);
            }
            catch (Exception ex)
            {
                Log.Error("ManageFormatStyles: Rename", ex);
                MessageBox.Show(this, "Falha ao renomear estilo: " + ex.Message,
                    "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            if (!RequireSelection()) return;
            string name = SelectedStyle;
            var answer = MessageBox.Show(this,
                $"Excluir o estilo '{name}'?",
                "SQL Beaver", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            try
            {
                FormatStyleStore.Delete(name);
                RefreshList();
            }
            catch (Exception ex)
            {
                Log.Error("ManageFormatStyles: Delete", ex);
                MessageBox.Show(this, "Falha ao excluir estilo: " + ex.Message,
                    "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnImport(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title  = "Importar estilo de formatação",
                Filter = "JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string src  = dlg.FileName;
                string name = Path.GetFileNameWithoutExtension(src);
                try
                {
                    // Validate it loads as FormatOptions before importing
                    string json  = File.ReadAllText(src, System.Text.Encoding.UTF8);
                    FormatOptions opts = FormatOptions.Load(json);
                    FormatStyleStore.Save(name, opts);
                    RefreshList();
                    SelectStyle(name);
                }
                catch (Exception ex)
                {
                    Log.Error("ManageFormatStyles: Import", ex);
                    MessageBox.Show(this, "Falha ao importar: " + ex.Message,
                        "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExport(object sender, EventArgs e)
        {
            if (!RequireSelection()) return;
            string name = SelectedStyle;
            using (var dlg = new SaveFileDialog
            {
                Title    = "Exportar estilo de formatação",
                Filter   = "JSON (*.json)|*.json",
                FileName = name + ".json",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string src = FormatStyleStore.GetStylePath(name);
                    File.Copy(src, dlg.FileName, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Error("ManageFormatStyles: Export", ex);
                    MessageBox.Show(this, "Falha ao exportar: " + ex.Message,
                        "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ── Name prompt helper ────────────────────────────────────────────────

        private string PromptName(string title, string label, string defaultValue)
        {
            using (var dlg = new NamePromptDialog(title, label, defaultValue))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    return dlg.EnteredName;
                return null;
            }
        }

        private void SelectStyle(string name)
        {
            int idx = _listBox.Items.IndexOf(name);
            if (idx >= 0) _listBox.SelectedIndex = idx;
        }
    }

    // =========================================================================
    // NamePromptDialog — input box simples
    // =========================================================================

    internal sealed class NamePromptDialog : Form
    {
        private readonly TextBox _textBox;

        public string EnteredName => _textBox.Text.Trim();

        internal NamePromptDialog(string title, string label, string defaultValue)
        {
            Text            = "SQL Beaver — " + title;
            Width           = 360;
            Height          = 140;
            MinimumSize     = new Size(300, 130);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = false;
            MinimizeBox     = false;

            var lbl = new Label { Text = label, AutoSize = true };
            lbl.SetBounds(12, 14, 320, 18);

            _textBox = new TextBox { Text = defaultValue };
            _textBox.SetBounds(12, 36, 320, 22);
            _textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
            btnOk.SetBounds(168, 70, 80, 26);
            btnOk.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
            btnCancel.SetBounds(256, 70, 80, 26);
            btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lbl, _textBox, btnOk, btnCancel });
        }
    }
}
