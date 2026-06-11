using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EnvDTE;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Analysis
{
    /// <summary>
    /// "Summarize Script": lista a estrutura (Kind | Linha | Resumo) dos statements
    /// de topo do documento ativo. Duplo-clique / Enter / "Ir para" navega o documento
    /// ATIVO até a linha do statement (TextSelection.MoveToLineAndOffset).
    /// </summary>
    internal sealed class SummarizeScriptDialog : Form
    {
        private readonly ListView _listView;
        private readonly Button _btnGo;
        private readonly Button _btnClose;
        private readonly TextDocument _activeDoc;

        internal SummarizeScriptDialog(IReadOnlyList<OutlineItem> items, TextDocument activeDoc)
        {
            _activeDoc = activeDoc;

            Text = "SQL Beaver — Summarize Script";
            Width = 720;
            Height = 480;
            MinimumSize = new Size(520, 320);
            StartPosition = FormStartPosition.CenterScreen;

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                Font = new Font("Segoe UI", 9),
            };
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_listView, true);
            _listView.Columns.Add("Tipo", 150);
            _listView.Columns.Add("Linha", 60);
            _listView.Columns.Add("Resumo", 470);
            _listView.DoubleClick += (s, e) => NavigateSelected();
            _listView.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { NavigateSelected(); e.Handled = true; } };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            _btnGo = new Button { Text = "Ir para", FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9f) };
            _btnGo.SetBounds(520, 8, 90, 30);
            _btnGo.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnGo.Click += (s, e) => NavigateSelected();

            _btnClose = new Button { Text = "Fechar", FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9f) };
            _btnClose.SetBounds(616, 8, 90, 30);
            _btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnClose.Click += (s, e) => Close();

            bottom.Controls.Add(_btnGo);
            bottom.Controls.Add(_btnClose);

            Controls.Add(_listView);
            Controls.Add(bottom);

            CancelButton = _btnClose;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            Populate(items);
        }

        private void Populate(IReadOnlyList<OutlineItem> items)
        {
            _listView.BeginUpdate();
            foreach (OutlineItem item in items)
            {
                var lvi = new ListViewItem(item.Kind) { Tag = item };
                lvi.SubItems.Add(item.Line.ToString());
                lvi.SubItems.Add(item.Summary ?? "");
                _listView.Items.Add(lvi);
            }
            _listView.EndUpdate();
            if (_listView.Items.Count > 0)
                _listView.Items[0].Selected = true;
        }

        private void NavigateSelected()
        {
            if (_listView.SelectedItems.Count == 0) return;
            var item = _listView.SelectedItems[0].Tag as OutlineItem;
            if (item == null || _activeDoc == null) return;

            try
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                TextSelection sel = _activeDoc.Selection;
                sel.MoveToLineAndOffset(item.Line, 1, false);
                _activeDoc.DTE.ActiveWindow?.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("SummarizeScript: navegar", ex);
            }
        }

        // ---------------------------------------------------------------
        internal static void Show(IReadOnlyList<OutlineItem> items, TextDocument activeDoc, IWin32Window owner)
        {
            try
            {
                using (var dlg = new SummarizeScriptDialog(items, activeDoc))
                {
                    if (owner != null) dlg.ShowDialog(owner);
                    else dlg.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Log.Error("SummarizeScriptDialog", ex);
            }
        }
    }
}
