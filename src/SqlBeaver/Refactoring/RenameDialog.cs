using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Simple input dialog to collect a new identifier name.
    /// </summary>
    internal sealed class RenameDialog : Form
    {
        private static readonly Regex ValidIdentifier =
            new Regex(@"^@?[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private readonly TextBox _textBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly Label _label;

        public string NewName => _textBox.Text.Trim();

        public RenameDialog(string currentName)
        {
            Text = "SQL Beaver — Renomear";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Width = 360;
            Height = 130;
            ShowInTaskbar = false;

            _label = new Label
            {
                Text = "Novo nome:",
                Left = 12, Top = 12,
                Width = 80, Height = 20,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _textBox = new TextBox
            {
                Left = 96, Top = 10,
                Width = 240, Height = 22,
                Text = currentName
            };
            _textBox.SelectAll();
            _textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TryAccept(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
            };

            _okButton = new Button
            {
                Text = "OK",
                Left = 175, Top = 56,
                Width = 75, Height = 26,
                DialogResult = DialogResult.None
            };
            _okButton.Click += (s, e) => TryAccept();

            _cancelButton = new Button
            {
                Text = "Cancelar",
                Left = 260, Top = 56,
                Width = 80, Height = 26,
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { _label, _textBox, _okButton, _cancelButton });
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            ActiveControl = _textBox;
        }

        private void TryAccept()
        {
            string name = _textBox.Text.Trim();
            if (!ValidIdentifier.IsMatch(name))
            {
                MessageBox.Show(
                    "Nome inválido. Use letras, dígitos e _ (pode começar com @ para variável).",
                    "SQL Beaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
