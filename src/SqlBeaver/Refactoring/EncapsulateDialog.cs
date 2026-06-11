using System.Drawing;
using System.Windows.Forms;

namespace SqlBeaver.Refactoring
{
    /// <summary>
    /// Collects the schema and procedure name for the "Encapsulate as procedure" refactor.
    /// </summary>
    internal sealed class EncapsulateDialog : Form
    {
        private readonly TextBox _schemaBox;
        private readonly TextBox _nameBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public string SchemaName => _schemaBox.Text.Trim();
        public string ProcName => _nameBox.Text.Trim();

        public EncapsulateDialog()
        {
            Text = "SQL Beaver — Encapsular como procedure";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Width = 380;
            Height = 160;
            ShowInTaskbar = false;

            var schemaLabel = new Label
            {
                Text = "Schema:", Left = 12, Top = 14, Width = 80, Height = 20,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _schemaBox = new TextBox { Left = 96, Top = 12, Width = 250, Height = 22, Text = "dbo" };

            var nameLabel = new Label
            {
                Text = "Nome da proc:", Left = 12, Top = 44, Width = 80, Height = 20,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _nameBox = new TextBox { Left = 96, Top = 42, Width = 250, Height = 22 };
            _nameBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
            };

            _okButton = new Button
            {
                Text = "OK", Left = 185, Top = 84, Width = 75, Height = 26,
                DialogResult = DialogResult.None
            };
            _okButton.Click += (s, e) => TryAccept();

            _cancelButton = new Button
            {
                Text = "Cancelar", Left = 270, Top = 84, Width = 80, Height = 26,
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { schemaLabel, _schemaBox, nameLabel, _nameBox, _okButton, _cancelButton });
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            ActiveControl = _nameBox;
        }

        private void TryAccept()
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show("Informe o nome da procedure.", "SQL Beaver",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
