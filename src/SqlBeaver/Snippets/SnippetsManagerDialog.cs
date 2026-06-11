using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Snippets
{
    /// <summary>
    /// Gerenciador visual de snippets (CRUD). Lista à esquerda (shortcut + título);
    /// campos à direita (shortcut / título / descrição / expansão multiline).
    /// Botões: Novo, Salvar, Excluir. Ao salvar, grava os snippets de USUÁRIO em
    /// %LOCALAPPDATA%\SqlBeaver\snippets.json e recarrega o catálogo em memória.
    /// Os built-ins não são gravados (apenas overrides/adições do usuário).
    /// </summary>
    internal sealed class SnippetsManagerDialog : Form
    {
        private readonly ListBox _listBox;
        private readonly TextBox _txtShortcut;
        private readonly TextBox _txtTitle;
        private readonly TextBox _txtDescription;
        private readonly TextBox _txtExpansion;
        private readonly Button _btnNew;
        private readonly Button _btnSave;
        private readonly Button _btnDelete;
        private readonly Button _btnClose;
        private readonly Label _lblStatus;

        // Set de shortcuts dos built-ins (case-insensitive) — não são gravados a menos
        // que o usuário os tenha sobrescrito (expansão diferente do default).
        private static readonly HashSet<string> BuiltinShortcuts =
            new HashSet<string>(SnippetCatalog.Defaults.Select(d => d.Shortcut), StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, SnippetDefinition> BuiltinByShortcut =
            SnippetCatalog.Defaults.ToDictionary(d => d.Shortcut, d => d, StringComparer.OrdinalIgnoreCase);

        // Lista de trabalho (em memória) de TODOS os snippets visíveis.
        private readonly List<SnippetDefinition> _items = new List<SnippetDefinition>();

        internal SnippetsManagerDialog()
        {
            Text = "SQL Beaver — Snippets";
            Width = 720;
            Height = 480;
            MinimumSize = new Size(620, 380);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            // ── Left: list ────────────────────────────────────────────────────
            _listBox = new ListBox
            {
                Font = new Font("Segoe UI", 9.75f),
                IntegralHeight = false,
            };
            _listBox.SetBounds(12, 12, 240, 380);
            _listBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _listBox.SelectedIndexChanged += OnSelectionChanged;

            // ── Right: fields ─────────────────────────────────────────────────
            var lblShortcut = MakeLabel("Atalho (shortcut):", 268, 12);
            _txtShortcut = MakeText(268, 32, 200, false);

            var lblTitle = MakeLabel("Título:", 268, 64);
            _txtTitle = MakeText(268, 84, 420, false);

            var lblDesc = MakeLabel("Descrição:", 268, 116);
            _txtDescription = MakeText(268, 136, 420, false);

            var lblExp = MakeLabel("Expansão (use $cursor$ ou ${1:x}$ / $0$):", 268, 168);
            _txtExpansion = MakeText(268, 188, 420, true);
            _txtExpansion.SetBounds(268, 188, 420, 150);
            _txtExpansion.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _txtExpansion.ScrollBars = ScrollBars.Vertical;
            _txtExpansion.AcceptsReturn = true;
            _txtExpansion.WordWrap = false;

            _lblStatus = new Label
            {
                Text = "",
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            };
            _lblStatus.SetBounds(268, 346, 420, 20);
            _lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // ── Buttons ───────────────────────────────────────────────────────
            _btnNew = MakeButton("Novo", 268, 372);
            _btnSave = MakeButton("Salvar", 360, 372);
            _btnDelete = MakeButton("Excluir", 452, 372);
            _btnClose = MakeButton("Fechar", 600, 372);
            foreach (Button b in new[] { _btnNew, _btnSave, _btnDelete, _btnClose })
                b.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnClose.SetBounds(596, 372, 96, 30);

            _btnNew.Click += OnNew;
            _btnSave.Click += OnSave;
            _btnDelete.Click += OnDelete;
            _btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
            {
                _listBox,
                lblShortcut, _txtShortcut,
                lblTitle, _txtTitle,
                lblDesc, _txtDescription,
                lblExp, _txtExpansion,
                _lblStatus,
                _btnNew, _btnSave, _btnDelete, _btnClose,
            });

            Load += (s, e) => LoadItems();
        }

        // ── Factory ───────────────────────────────────────────────────────────
        internal static void ShowManager(IWin32Window owner)
        {
            try
            {
                using (var dlg = new SnippetsManagerDialog())
                    dlg.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.Error("SnippetsManagerDialog", ex);
            }
        }

        // ── UI helpers ──────────────────────────────────────────────────────────
        private static Label MakeLabel(string text, int x, int y)
        {
            var lbl = new Label { Text = text, AutoSize = true };
            lbl.SetBounds(x, y, 300, 18);
            lbl.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            return lbl;
        }

        private TextBox MakeText(int x, int y, int w, bool multiline)
        {
            var tb = new TextBox
            {
                Multiline = multiline,
                Font = multiline ? new Font("Consolas", 9.75f) : new Font("Segoe UI", 9.75f),
            };
            tb.SetBounds(x, y, w, multiline ? 150 : 22);
            tb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            return tb;
        }

        private static Button MakeButton(string caption, int x, int y)
        {
            var b = new Button { Text = caption, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9f) };
            b.SetBounds(x, y, 88, 30);
            return b;
        }

        // ── Data ────────────────────────────────────────────────────────────────
        private void LoadItems()
        {
            _items.Clear();

            // Built-ins primeiro (cópias para edição segura).
            foreach (SnippetDefinition d in SnippetCatalog.Defaults)
                _items.Add(Clone(d));

            // Mescla snippets de usuário sobre os built-ins.
            try
            {
                string path = SnippetStore.UserSnippetsPath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    IReadOnlyDictionary<string, SnippetDefinition> merged = SnippetCatalog.Load(json);
                    foreach (KeyValuePair<string, SnippetDefinition> kv in merged)
                    {
                        int idx = _items.FindIndex(x =>
                            string.Equals(x.Shortcut, kv.Key, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0) _items[idx] = Clone(kv.Value);
                        else _items.Add(Clone(kv.Value));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("SnippetsManager: ler snippets.json", ex);
            }

            RefreshList(selectIndex: _items.Count > 0 ? 0 : -1);
        }

        private void RefreshList(int selectIndex)
        {
            _listBox.BeginUpdate();
            _listBox.Items.Clear();
            foreach (SnippetDefinition d in _items)
            {
                bool isBuiltin = BuiltinShortcuts.Contains(d.Shortcut ?? "");
                string tag = isBuiltin ? "" : "  *";
                _listBox.Items.Add($"{d.Shortcut}  —  {d.Title}{tag}");
            }
            _listBox.EndUpdate();

            if (selectIndex >= 0 && selectIndex < _listBox.Items.Count)
                _listBox.SelectedIndex = selectIndex;
            else
                ClearFields();
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            int idx = _listBox.SelectedIndex;
            if (idx < 0 || idx >= _items.Count) { ClearFields(); return; }
            SnippetDefinition d = _items[idx];
            _txtShortcut.Text = d.Shortcut ?? "";
            _txtTitle.Text = d.Title ?? "";
            _txtDescription.Text = d.Description ?? "";
            _txtExpansion.Text = d.Expansion ?? "";
        }

        private void ClearFields()
        {
            _txtShortcut.Text = "";
            _txtTitle.Text = "";
            _txtDescription.Text = "";
            _txtExpansion.Text = "";
        }

        // ── Buttons ───────────────────────────────────────────────────────────
        private void OnNew(object sender, EventArgs e)
        {
            _listBox.ClearSelected();
            ClearFields();
            _txtShortcut.Focus();
            SetStatus("Novo snippet — preencha os campos e clique em Salvar.");
        }

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                string shortcut = (_txtShortcut.Text ?? "").Trim();
                string expansion = _txtExpansion.Text ?? "";

                if (string.IsNullOrWhiteSpace(shortcut))
                {
                    Warn("O atalho (shortcut) é obrigatório.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(expansion))
                {
                    Warn("A expansão é obrigatória.");
                    return;
                }

                int selIdx = _listBox.SelectedIndex;
                bool editingExisting = selIdx >= 0 && selIdx < _items.Count;
                string editingShortcut = editingExisting ? _items[selIdx].Shortcut : null;

                // Unicidade: ignora a própria entrada em edição.
                var others = _items
                    .Where((d, i) => !(editingExisting && i == selIdx))
                    .Select(d => d.Shortcut);
                if (!SnippetShortcutValidator.IsValidUnique(shortcut, others))
                {
                    Warn($"Já existe um snippet com o atalho '{shortcut}'.");
                    return;
                }

                var def = new SnippetDefinition
                {
                    Shortcut = shortcut,
                    Title = (_txtTitle.Text ?? "").Trim(),
                    Description = (_txtDescription.Text ?? "").Trim(),
                    Expansion = expansion,
                };

                if (editingExisting)
                    _items[selIdx] = def;
                else
                    _items.Add(def);

                PersistAndReload();

                int newIdx = _items.FindIndex(x =>
                    string.Equals(x.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));
                RefreshList(newIdx);
                SetStatus("Snippet salvo e catálogo recarregado.");
            }
            catch (Exception ex)
            {
                Log.Error("SnippetsManager: Salvar", ex);
                Warn("Falha ao salvar: " + ex.Message);
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            int idx = _listBox.SelectedIndex;
            if (idx < 0 || idx >= _items.Count)
            {
                SetStatus("Selecione um snippet para excluir.");
                return;
            }

            SnippetDefinition d = _items[idx];
            bool isBuiltin = BuiltinShortcuts.Contains(d.Shortcut ?? "");

            var answer = MessageBox.Show(this,
                isBuiltin
                    ? $"'{d.Shortcut}' é um snippet embutido. Excluir aqui remove qualquer customização e volta ao padrão. Continuar?"
                    : $"Excluir o snippet '{d.Shortcut}'?",
                "SQL Beaver", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            try
            {
                _items.RemoveAt(idx);

                // Built-in removido da lista volta como padrão (não é gravado no arquivo).
                if (isBuiltin && BuiltinByShortcut.TryGetValue(d.Shortcut, out SnippetDefinition def))
                    _items.Add(Clone(def));

                PersistAndReload();
                RefreshList(_items.Count > 0 ? 0 : -1);
                SetStatus("Snippet excluído e catálogo recarregado.");
            }
            catch (Exception ex)
            {
                Log.Error("SnippetsManager: Excluir", ex);
                Warn("Falha ao excluir: " + ex.Message);
            }
        }

        // ── Persistência ────────────────────────────────────────────────────────
        private void PersistAndReload()
        {
            // Grava apenas snippets de USUÁRIO: adições (shortcut não built-in) ou
            // overrides (built-in com expansão/título/descrição diferentes do default).
            var userSnippets = new List<SnippetDefinition>();
            foreach (SnippetDefinition d in _items)
            {
                if (string.IsNullOrWhiteSpace(d.Shortcut) || string.IsNullOrWhiteSpace(d.Expansion))
                    continue;

                if (BuiltinByShortcut.TryGetValue(d.Shortcut, out SnippetDefinition builtin))
                {
                    if (IsSame(d, builtin))
                        continue; // built-in inalterado → não grava
                }
                userSnippets.Add(d);
            }

            string json = SnippetJsonWriter.Serialize(userSnippets);
            string path = SnippetStore.UserSnippetsPath;
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            try { File.Replace(tmp, path, null); }
            catch (FileNotFoundException) { File.Move(tmp, path); }

            SnippetStore.Reload();
        }

        private static bool IsSame(SnippetDefinition a, SnippetDefinition b)
        {
            return string.Equals(a.Expansion ?? "", b.Expansion ?? "", StringComparison.Ordinal)
                && string.Equals(a.Title ?? "", b.Title ?? "", StringComparison.Ordinal)
                && string.Equals(a.Description ?? "", b.Description ?? "", StringComparison.Ordinal);
        }

        private static SnippetDefinition Clone(SnippetDefinition d) => new SnippetDefinition
        {
            Shortcut = d.Shortcut,
            Title = d.Title,
            Description = d.Description,
            Expansion = d.Expansion,
        };

        private void SetStatus(string text) => _lblStatus.Text = text;

        private void Warn(string text)
        {
            _lblStatus.Text = text;
            MessageBox.Show(this, text, "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
