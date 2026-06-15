using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SqlBeaver.Analysis;
using SqlBeaver.Metadata;
using SqlBeaver.Scripting;

namespace SqlBeaver.Completion
{
    /// <summary>
    /// Diálogo WinForms para seleção de colunas (Inserir colunas…).
    /// Exibe as tabelas do escopo com checkboxes por coluna e um filtro de substring.
    /// OK insere a lista qualificada no caret (uma edição/um undo).
    /// </summary>
    internal sealed class InsertColumnsDialog : Form
    {
        private readonly TextBox _filterBox;
        private readonly CheckedListBox _columnList;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly Button _selectAllButton;
        private readonly Button _clearAllButton;

        // Each entry: (qualifier, column)
        private readonly List<(string Qualifier, ColumnEntry Column, string DisplayLabel)> _allItems
            = new List<(string, ColumnEntry, string)>();

        public string ResultText { get; private set; }

        // v1: indentação de continuação (alinha as colunas inseridas sob o caret). Quando
        // nulo/vazio, mantém o comportamento de linha única.
        private readonly string _continuationIndent;

        public InsertColumnsDialog(
            IReadOnlyList<TableRef> scope,
            DbMetadata metadata,
            string continuationIndent = null)
        {
            _continuationIndent = continuationIndent;

            Text = "SQL Beaver — Inserir colunas…";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Width = 460;
            Height = 480;
            ShowInTaskbar = false;
            MinimumSize = new Size(360, 340);

            bool multiTable = scope.Count > 1;

            // Build flat list of (qualifier, column, displayLabel)
            foreach (TableRef tr in scope)
            {
                string qualifier = tr.Alias ?? tr.Table;
                string tableKey = metadata.ResolveUniqueSchema(tr.Table) is string schema
                    ? DbMetadata.TableKey(schema, tr.Table)
                    : tr.Schema != null ? DbMetadata.TableKey(tr.Schema, tr.Table) : null;
                if (tableKey == null && tr.Schema != null)
                    tableKey = DbMetadata.TableKey(tr.Schema, tr.Table);
                if (tableKey == null) continue;

                if (!metadata.ColumnsByTable.TryGetValue(tableKey, out IReadOnlyList<ColumnEntry> cols))
                    continue;

                foreach (ColumnEntry col in cols)
                {
                    string label = multiTable
                        ? qualifier + "." + col.Name + "  (" + col.SqlType + ")"
                        : col.Name + "  (" + col.SqlType + ")";
                    _allItems.Add((qualifier, col, label));
                }
            }

            // --- Filter label + box ---
            var filterLabel = new Label
            {
                Text = "Filtrar:",
                Left = 8, Top = 10, Width = 44, Height = 20,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _filterBox = new TextBox
            {
                Left = 56, Top = 8, Width = 380, Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _filterBox.TextChanged += (s, e) => RefreshList();

            // --- Column list ---
            _columnList = new CheckedListBox
            {
                Left = 8, Top = 36,
                Width = 428, Height = 340,
                CheckOnClick = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            // --- Select all / Clear all ---
            _selectAllButton = new Button
            {
                Text = "Todos",
                Left = 8, Top = 384, Width = 68, Height = 26,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            _selectAllButton.Click += (s, e) =>
            {
                for (int i = 0; i < _columnList.Items.Count; i++)
                    _columnList.SetItemChecked(i, true);
            };

            _clearAllButton = new Button
            {
                Text = "Nenhum",
                Left = 80, Top = 384, Width = 68, Height = 26,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            _clearAllButton.Click += (s, e) =>
            {
                for (int i = 0; i < _columnList.Items.Count; i++)
                    _columnList.SetItemChecked(i, false);
            };

            // --- OK / Cancel ---
            _cancelButton = new Button
            {
                Text = "Cancelar",
                Left = 348, Top = 384, Width = 88, Height = 26,
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            _okButton = new Button
            {
                Text = "OK",
                Left = 256, Top = 384, Width = 88, Height = 26,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _okButton.Click += OnOk;

            Controls.AddRange(new Control[]
            {
                filterLabel, _filterBox,
                _columnList,
                _selectAllButton, _clearAllButton,
                _okButton, _cancelButton
            });

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            RefreshList();
        }

        // Indices into _allItems for the currently displayed rows
        private readonly List<int> _visibleIndices = new List<int>();
        // tracks which _allItems indices are checked
        private readonly HashSet<int> _checkedIndices = new HashSet<int>();

        private void RefreshList()
        {
            // Save current check state back to _checkedIndices
            for (int vi = 0; vi < _visibleIndices.Count; vi++)
            {
                int ai = _visibleIndices[vi];
                if (_columnList.GetItemChecked(vi))
                    _checkedIndices.Add(ai);
                else
                    _checkedIndices.Remove(ai);
            }

            string filter = _filterBox.Text.Trim();
            _visibleIndices.Clear();
            _columnList.BeginUpdate();
            _columnList.Items.Clear();

            for (int i = 0; i < _allItems.Count; i++)
            {
                var (_, _, label) = _allItems[i];
                if (string.IsNullOrEmpty(filter) ||
                    label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _visibleIndices.Add(i);
                    _columnList.Items.Add(label, _checkedIndices.Contains(i));
                }
            }

            _columnList.EndUpdate();
        }

        private void OnOk(object sender, EventArgs e)
        {
            // Collect checked items (in display order = _allItems order)
            // First flush current visible state
            for (int vi = 0; vi < _visibleIndices.Count; vi++)
            {
                int ai = _visibleIndices[vi];
                if (_columnList.GetItemChecked(vi))
                    _checkedIndices.Add(ai);
                else
                    _checkedIndices.Remove(ai);
            }

            var selected = new List<(string Qualifier, ColumnEntry Column)>();
            // Preserve insertion order from _allItems
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (_checkedIndices.Contains(i))
                    selected.Add((_allItems[i].Qualifier, _allItems[i].Column));
            }

            if (selected.Count == 0)
            {
                MessageBox.Show("Selecione pelo menos uma coluna.", "SQL Beaver",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool multiTable = false;
            var seenQualifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (q, _) in selected)
                seenQualifiers.Add(q);
            multiTable = seenQualifiers.Count > 1;

            ResultText = ColumnListBuilder.Build(selected, multiTable, _continuationIndent);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
