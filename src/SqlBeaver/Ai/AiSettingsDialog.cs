using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Ai
{
    /// <summary>
    /// Diálogo de configuração da IA: provedor, modelo, chave de API (DPAPI),
    /// contexto de schema e teste de conexão. Nenhuma exceção escapa.
    /// </summary>
    internal sealed class AiSettingsDialog : Form
    {
        private readonly ComboBox _cboProvider;
        private readonly TextBox  _txtModel;
        private readonly TextBox  _txtKey;
        private readonly Label    _lblKeyHint;
        private readonly ComboBox _cboScope;
        private readonly CheckBox _chkAutoGen;
        private readonly Button   _btnTest;
        private readonly Label    _lblStatus;
        private readonly Button   _btnSave;
        private readonly Button   _btnCancel;

        private string _lastProviderDefaultModel;
        private readonly bool _hadKey;

        private sealed class ProviderItem
        {
            public string Id;
            public string Display;
            public string DefaultModel;
            public override string ToString() => Display;
        }

        private sealed class ScopeItem
        {
            public string Value;
            public string Display;
            public override string ToString() => Display;
        }

        private AiSettingsDialog()
        {
            Text            = "SQL Beaver — IA (configuração)";
            Width           = 560;
            Height          = 466;
            MinimumSize     = new Size(520, 436);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;

            var font = new Font("Segoe UI", 9f);

            AiConfig cfg = AiConfigStore.Load();
            _hadKey = !string.IsNullOrEmpty(cfg.KeyProtected);

            // ── Provedor ──────────────────────────────────────────────────────
            var lblProvider = new Label { Text = "Provedor:", Font = font, AutoSize = true };
            lblProvider.SetBounds(16, 18, 120, 18);

            _cboProvider = new ComboBox
            {
                Font          = font,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _cboProvider.SetBounds(150, 14, 380, 24);
            foreach (IAiProvider p in AiProviders.All)
            {
                _cboProvider.Items.Add(new ProviderItem
                {
                    Id           = p.Id,
                    Display      = p.DisplayName,
                    DefaultModel = p.DefaultModel,
                });
            }

            // ── Modelo ────────────────────────────────────────────────────────
            var lblModel = new Label { Text = "Modelo:", Font = font, AutoSize = true };
            lblModel.SetBounds(16, 54, 120, 18);

            _txtModel = new TextBox { Font = font };
            _txtModel.SetBounds(150, 50, 380, 24);

            // ── Chave de API ──────────────────────────────────────────────────
            var lblKey = new Label { Text = "Chave de API:", Font = font, AutoSize = true };
            lblKey.SetBounds(16, 90, 120, 18);

            _txtKey = new TextBox { Font = font, UseSystemPasswordChar = true };
            _txtKey.SetBounds(150, 86, 380, 24);

            _lblKeyHint = new Label
            {
                Font      = new Font("Segoe UI", 8f, FontStyle.Italic),
                ForeColor = SystemColors.GrayText,
                AutoSize  = false,
                Text      = "",
            };
            _lblKeyHint.SetBounds(150, 112, 380, 18);

            // ── Contexto de schema ────────────────────────────────────────────
            var lblScope = new Label { Text = "Contexto de schema:", Font = font, AutoSize = true };
            lblScope.SetBounds(16, 140, 130, 18);

            _cboScope = new ComboBox
            {
                Font          = font,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _cboScope.SetBounds(150, 136, 380, 24);
            _cboScope.Items.Add(new ScopeItem { Value = "scope", Display = "Tabelas no escopo" });
            _cboScope.Items.Add(new ScopeItem { Value = "none",  Display = "Nenhum" });
            _cboScope.Items.Add(new ScopeItem { Value = "all",   Display = "Banco todo" });

            // ── Gerar com IA ao pressionar Enter ──────────────────────────────
            _chkAutoGen = new CheckBox
            {
                Font     = font,
                AutoSize = true,
                Text     = "Gerar com IA ao pressionar Enter num comentário",
            };
            _chkAutoGen.SetBounds(150, 168, 380, 20);

            // ── Privacidade ───────────────────────────────────────────────────
            var lblPrivacy = new Label
            {
                Font      = new Font("Segoe UI", 8.25f),
                ForeColor = SystemColors.GrayText,
                AutoSize  = false,
                Text      = "Seu SQL e o schema selecionado são enviados ao provedor escolhido. " +
                            "A chave é guardada criptografada (DPAPI) nesta máquina, só o seu " +
                            "usuário Windows a lê.",
            };
            lblPrivacy.SetBounds(16, 200, 514, 50);

            // ── Testar conexão ────────────────────────────────────────────────
            _btnTest = new Button { Text = "Testar conexão", Font = font, FlatStyle = FlatStyle.System };
            _btnTest.SetBounds(16, 256, 140, 30);

            _lblStatus = new Label
            {
                Font     = new Font("Segoe UI", 8.5f),
                AutoSize = false,
                Text     = "",
            };
            _lblStatus.SetBounds(166, 256, 364, 56);

            // ── Salvar / Cancelar ─────────────────────────────────────────────
            _btnSave = new Button { Text = "Salvar", Font = font, FlatStyle = FlatStyle.System,
                                    DialogResult = DialogResult.None };
            _btnSave.SetBounds(354, 348, 80, 30);
            _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            _btnCancel = new Button { Text = "Cancelar", Font = font, FlatStyle = FlatStyle.System,
                                      DialogResult = DialogResult.Cancel };
            _btnCancel.SetBounds(444, 348, 80, 30);
            _btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            CancelButton = _btnCancel;

            Controls.AddRange(new Control[]
            {
                lblProvider, _cboProvider,
                lblModel, _txtModel,
                lblKey, _txtKey, _lblKeyHint,
                lblScope, _cboScope,
                _chkAutoGen,
                lblPrivacy,
                _btnTest, _lblStatus,
                _btnSave, _btnCancel,
            });

            // ── Events ────────────────────────────────────────────────────────
            _cboProvider.SelectedIndexChanged += OnProviderChanged;
            _btnTest.Click  += OnTestConnection;
            _btnSave.Click  += OnSave;

            Load += (s, e) => InitFromConfig(cfg);
        }

        // ── Public factory ────────────────────────────────────────────────────

        internal static void ShowSettings(IWin32Window owner)
        {
            try
            {
                using (var dlg = new AiSettingsDialog())
                    dlg.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.Error("AiSettingsDialog", ex);
            }
        }

        // ── Init ──────────────────────────────────────────────────────────────

        private void InitFromConfig(AiConfig cfg)
        {
            try
            {
                string providerId = AiConfigResolver.NormalizeProvider(cfg.Provider);
                int provIdx = 0;
                for (int i = 0; i < _cboProvider.Items.Count; i++)
                {
                    if (((ProviderItem)_cboProvider.Items[i]).Id == providerId) { provIdx = i; break; }
                }
                _cboProvider.SelectedIndex = provIdx;
                _lastProviderDefaultModel = ((ProviderItem)_cboProvider.SelectedItem).DefaultModel;

                // Modelo: usa o da config, senão o default do provedor.
                _txtModel.Text = string.IsNullOrEmpty(cfg.Model)
                    ? _lastProviderDefaultModel
                    : cfg.Model;

                // Chave: deixa em branco; mostra dica se já existe uma salva.
                _txtKey.Text = "";
                _lblKeyHint.Text = _hadKey
                    ? "(uma chave já está salva — deixe em branco para mantê-la)"
                    : "";

                // Contexto de schema.
                string scopeValue = string.IsNullOrEmpty(cfg.SchemaScope) ? "scope" : cfg.SchemaScope;
                int scopeIdx = 0;
                for (int i = 0; i < _cboScope.Items.Count; i++)
                {
                    if (string.Equals(((ScopeItem)_cboScope.Items[i]).Value, scopeValue,
                            StringComparison.OrdinalIgnoreCase))
                    { scopeIdx = i; break; }
                }
                _cboScope.SelectedIndex = scopeIdx;

                // Gerar ao Enter: ligado por padrão (null/ausente → marcado).
                _chkAutoGen.Checked = AiConfigResolver.AutoGenerateOnEnter(cfg);
            }
            catch (Exception ex)
            {
                Log.Error("AiSettingsDialog: InitFromConfig", ex);
                _lblStatus.Text = "Falha ao carregar configuração atual.";
            }
        }

        private void OnProviderChanged(object sender, EventArgs e)
        {
            try
            {
                var item = _cboProvider.SelectedItem as ProviderItem;
                if (item == null) return;
                _lastProviderDefaultModel = item.DefaultModel;
                // Simplicidade: sempre sobrescreve o modelo com o default do provedor ao trocar.
                _txtModel.Text = item.DefaultModel;
            }
            catch (Exception ex)
            {
                Log.Error("AiSettingsDialog: OnProviderChanged", ex);
            }
        }

        // ── Testar conexão ────────────────────────────────────────────────────

        private async void OnTestConnection(object sender, EventArgs e)
        {
            try
            {
                var provItem = _cboProvider.SelectedItem as ProviderItem;
                if (provItem == null) { _lblStatus.Text = "Selecione um provedor."; return; }

                IAiProvider provider = AiProviders.ById(provItem.Id);
                string model = string.IsNullOrWhiteSpace(_txtModel.Text)
                    ? provider.DefaultModel
                    : _txtModel.Text.Trim();

                string typed = _txtKey.Text;
                string apiKey = string.IsNullOrEmpty(typed) ? AiConfigStore.GetApiKey() : typed;
                if (string.IsNullOrEmpty(apiKey))
                {
                    _lblStatus.Text = "Informe uma chave de API para testar.";
                    return;
                }

                _btnTest.Enabled = false;
                _lblStatus.ForeColor = SystemColors.GrayText;
                _lblStatus.Text = "Testando…";

                var req = new AiRequest { System = "Responda apenas: OK", User = "ping", MaxTokens = 16 };

                AiResult result = await RunTestAsync(provider, req, model, apiKey);

                if (result == null)
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "Falha inesperada no teste.";
                }
                else if (result.Ok)
                {
                    string preview = result.Text ?? "";
                    if (preview.Length > 40) preview = preview.Substring(0, 40);
                    _lblStatus.ForeColor = Color.ForestGreen;
                    _lblStatus.Text = "Conexão OK. " + preview;
                }
                else
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = result.Error ?? "Falha desconhecida.";
                }
            }
            catch (Exception ex)
            {
                Log.Error("AiSettingsDialog: TestConnection", ex);
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "Falha ao testar — veja Output > SQL Beaver.";
            }
            finally
            {
                _btnTest.Enabled = true;
            }
        }

        private static async Task<AiResult> RunTestAsync(IAiProvider provider, AiRequest req, string model, string apiKey)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    return await Task.Run(() => provider.CompleteAsync(req, model, apiKey, cts.Token), cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return AiResult.Fail("tempo esgotado ao consultar a IA.");
            }
            catch (Exception ex)
            {
                Log.Error("AiSettingsDialog: RunTestAsync", ex);
                return AiResult.Fail("falha ao consultar a IA.");
            }
        }

        // ── Salvar ────────────────────────────────────────────────────────────

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                var provItem = _cboProvider.SelectedItem as ProviderItem;
                var scopeItem = _cboScope.SelectedItem as ScopeItem;

                string providerId = provItem != null ? provItem.Id : "anthropic";
                string model = _txtModel.Text == null ? "" : _txtModel.Text.Trim();
                string scopeValue = scopeItem != null ? scopeItem.Value : "scope";

                string typedKey = _txtKey.Text;
                string keyToSave = string.IsNullOrEmpty(typedKey) ? null : typedKey;

                // Preserva o blob existente quando o usuário não digitou nova chave.
                AiConfig existing = AiConfigStore.Load();
                var cfg = new AiConfig
                {
                    Provider            = providerId,
                    Model               = model,
                    SchemaScope         = scopeValue,
                    KeyProtected        = existing.KeyProtected,
                    AutoGenerateOnEnter = _chkAutoGen.Checked ? "true" : "false",
                };

                AiConfigStore.Save(cfg, keyToSave);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Log.Error("AiSettingsDialog: Save", ex);
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "Falha ao salvar — veja Output > SQL Beaver.";
            }
        }
    }
}
