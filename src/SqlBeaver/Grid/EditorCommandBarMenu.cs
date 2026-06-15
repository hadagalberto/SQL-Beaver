using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;
using SqlBeaver.Refactoring;

namespace SqlBeaver.Grid
{
    /// <summary>
    /// Registra os comandos do SQL Beaver no menu de contexto do editor SQL
    /// (CommandBar "SQL Files Editor Context"). Botões temporários, recriados a
    /// cada sessão do SSMS. Padrão do AxialSqlTools (Apache-2.0).
    /// </summary>
    internal static class EditorCommandBarMenu
    {
        private const string EditorContextBarName = "SQL Files Editor Context";

        // Referências fortes: handlers COM são coletados pelo GC sem isso.
        private static CommandBarButton _formatButton;
        private static CommandBarButton _refreshCacheButton;
        private static CommandBarButton _goToDefinitionButton;
        private static CommandBarButton _findObjectButton;
        private static CommandBarButton _findReferencesButton;
        private static CommandBarButton _queryHistoryButton;
        private static CommandBarButton _recoverSessionButton;
        private static CommandBarButton _environmentsButton;

        private static CommandBarButton _insertColumnsButton;
        private static CommandBarButton _summarizeScriptButton;
        private static CommandBarButton _manageSnippetsButton;

        // Format style submenu
        private static CommandBarPopup _formatStylePopup;
        private static CommandBarButton _manageStylesButton;
        // Dynamic style items — kept as a list so we can clear/rebuild
        private static readonly System.Collections.Generic.List<CommandBarButton> _styleButtons =
            new System.Collections.Generic.List<CommandBarButton>();

        // IA submenu
        private static CommandBarPopup _aiPopup;
        private static CommandBarButton _aiGenerateButton;
        private static CommandBarButton _aiExplainButton;
        private static CommandBarButton _aiOptimizeButton;
        private static CommandBarButton _aiSettingsButton;

        // Refactor submenu
        private static CommandBarPopup _refactorPopup;
        private static CommandBarButton _expandWildcardButton;
        private static CommandBarButton _qualifyNamesButton;
        private static CommandBarButton _unqualifyNamesButton;
        private static CommandBarButton _renameButton;
        private static CommandBarButton _inlineExecButton;
        private static CommandBarButton _encapsulateButton;
        private static CommandBarButton _insertSemicolonsButton;
        private static CommandBarButton _addBracketsButton;
        private static CommandBarButton _removeBracketsButton;
        private static CommandBarButton _applyCasingButton;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var bars = dte?.CommandBars as CommandBars;
                if (bars == null)
                {
                    Log.Info("CommandBars indisponível — menu do editor não registrado.");
                    return;
                }

                CommandBar editorBar = bars[EditorContextBarName];

                _formatButton = AddButton(editorBar, "SQL Beaver: Format Document", OnFormatDocument, beginGroup: true);
                _refreshCacheButton = AddButton(editorBar, "SQL Beaver: Refresh metadata cache", OnRefreshCache, beginGroup: false);
                _goToDefinitionButton = AddButton(editorBar, "SQL Beaver: Ir para definição", OnGoToDefinition, beginGroup: true);
                _findObjectButton = AddButton(editorBar, "SQL Beaver: Localizar objeto…", OnFindObject, beginGroup: false);
                _findReferencesButton = AddButton(editorBar, "SQL Beaver: Localizar referências", OnFindReferences, beginGroup: false);
                _queryHistoryButton   = AddButton(editorBar, "SQL Beaver: Histórico de consultas", OnQueryHistory,   beginGroup: true);
                _recoverSessionButton = AddButton(editorBar, "SQL Beaver: Recuperar consultas…",   OnRecoverSession, beginGroup: false);
                _environmentsButton   = AddButton(editorBar, "SQL Beaver: Ambientes (cores)…",     OnEnvironments,   beginGroup: false);
                _insertColumnsButton  = AddButton(editorBar, "SQL Beaver: Inserir/Selecionar colunas (substitui *)…", OnInsertColumns,  beginGroup: true);
                _summarizeScriptButton= AddButton(editorBar, "SQL Beaver: Summarize Script…",       OnSummarizeScript, beginGroup: false);
                _manageSnippetsButton = AddButton(editorBar, "SQL Beaver: Snippets…",               OnManageSnippets, beginGroup: false);

                // Format style submenu
                _formatStylePopup = (CommandBarPopup)editorBar.Controls.Add(
                    MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
                _formatStylePopup.Caption    = "SQL Beaver: Estilo de formatação";
                _formatStylePopup.BeginGroup = true;

                // "Gerenciar estilos…" is always present at the top of the popup
                _manageStylesButton = AddButton(_formatStylePopup, "Gerenciar estilos…", OnManageFormatStyles, beginGroup: false);

                // Populate dynamic style items now (and rebuild every time popup opens)
                RebuildStyleItems(_formatStylePopup);

                // IA submenu — alternativa confiável ao atalho Ctrl+K,Ctrl+A (atalhos podem
                // não disparar no SSMS; o menu de contexto é confiável).
                _aiPopup = (CommandBarPopup)editorBar.Controls.Add(
                    MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
                _aiPopup.Caption    = "SQL Beaver: IA";
                _aiPopup.BeginGroup = true;

                _aiGenerateButton = AddButton(_aiPopup, "Gerar SQL do comentário", OnAiGenerate, beginGroup: false);
                _aiExplainButton  = AddButton(_aiPopup, "Explicar SQL",            OnAiExplain,  beginGroup: false);
                _aiOptimizeButton = AddButton(_aiPopup, "Otimizar SQL",            OnAiOptimize, beginGroup: false);
                _aiSettingsButton = AddButton(_aiPopup, "IA (configuração)…",      OnAiSettings, beginGroup: true);

                // Refactor submenu
                _refactorPopup = (CommandBarPopup)editorBar.Controls.Add(
                    MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
                _refactorPopup.Caption = "SQL Beaver: Refatorar";
                _refactorPopup.BeginGroup = true;

                _expandWildcardButton  = AddButton(_refactorPopup, "Expand wildcard (SELECT *)", OnExpandWildcard,  beginGroup: false);
                _qualifyNamesButton    = AddButton(_refactorPopup, "Qualify object names",        OnQualifyNames,    beginGroup: false);
                _unqualifyNamesButton  = AddButton(_refactorPopup, "Remove qualificação",         OnUnqualifyNames,  beginGroup: false);
                _renameButton          = AddButton(_refactorPopup, "Rename alias/variável…",      OnRename,          beginGroup: true);
                _inlineExecButton      = AddButton(_refactorPopup, "Inline EXEC",                 OnInlineExec,      beginGroup: true);
                _encapsulateButton     = AddButton(_refactorPopup, "Encapsular como procedure…",  OnEncapsulate,     beginGroup: false);
                _insertSemicolonsButton= AddButton(_refactorPopup, "Inserir ponto-e-vírgula",     OnInsertSemicolons, beginGroup: true);
                _addBracketsButton     = AddButton(_refactorPopup, "Adicionar colchetes",         OnAddBrackets,     beginGroup: false);
                _removeBracketsButton  = AddButton(_refactorPopup, "Remover colchetes",           OnRemoveBrackets,  beginGroup: false);
                _applyCasingButton     = AddButton(_refactorPopup, "Aplicar casing do banco",     OnApplyCasing,     beginGroup: false);

                Log.Info("Comandos registrados no menu de contexto do editor SQL.");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao registrar o menu do editor (\"" + EditorContextBarName + "\")", ex);
            }
        }

        private static CommandBarButton AddButton(
            CommandBar bar,
            string caption,
            _CommandBarButtonEvents_ClickEventHandler onClick,
            bool beginGroup)
        {
            var button = (CommandBarButton)bar.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
            button.Caption    = caption;
            button.BeginGroup = beginGroup;
            button.Click     += onClick;
            return button;
        }

        private static CommandBarButton AddButton(
            CommandBarPopup popup,
            string caption,
            _CommandBarButtonEvents_ClickEventHandler onClick,
            bool beginGroup)
        {
            var button = (CommandBarButton)popup.Controls.Add(
                MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
            button.Caption    = caption;
            button.BeginGroup = beginGroup;
            button.Click     += onClick;
            return button;
        }

        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }

        private static void OnFormatDocument(CommandBarButton ctrl, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commands.EditorCommands.FormatDocument();
        }

        private static void OnRefreshCache(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ActiveConnection connection = ConnectionService.GetActiveConnection();
                MetadataCache cache = Completion.SqlBeaverCompletionSourceProvider.Cache;

                if (connection == null)
                {
                    cache.InvalidateAll();
                    ShowStatus("cache de metadata limpo (todas as conexões).");
                    Log.Info("Refresh cache: sem conexão ativa — cache inteiro limpo.");
                    return;
                }

                cache.Invalidate(connection.Server, connection.Database);
                // dispara a recarga já, para o próximo popup vir quente
                cache.TryGet(connection.Server, connection.Database,
                    new SqlBeaver.Metadata.MetadataRequest { ConnectionString = connection.ConnectionString, AccessToken = connection.AccessToken, ProviderConnectionType = connection.ProviderConnectionType });
                ShowStatus($"cache de [{connection.Database}] atualizando em background.");
                Log.Info($"Refresh cache: [{connection.Server}].[{connection.Database}] invalidado e recarregando.");
            }
            catch (Exception ex)
            {
                Log.Error("Refresh metadata cache", ex);
                ShowStatus("falha no refresh do cache — veja Output > SQL Beaver");
            }
        }

        private static void OnGoToDefinition(CommandBarButton ctrl, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commands.EditorCommands.GoToDefinition();
        }

        private static void OnFindObject(CommandBarButton ctrl, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commands.EditorCommands.FindObject();
        }

        private static void OnFindReferences(CommandBarButton ctrl, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commands.EditorCommands.FindReferences();
        }

        // ---------------------------------------------------------------
        // Refactor submenu handlers
        // ---------------------------------------------------------------

        private static void OnExpandWildcard(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.ExpandWildcard();
            }
            catch (Exception ex)
            {
                Log.Error("Expand wildcard", ex);
                ShowStatus("falha em Expand * — veja Output > SQL Beaver");
            }
        }

        private static void OnQualifyNames(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.QualifyNames();
            }
            catch (Exception ex)
            {
                Log.Error("Qualify names", ex);
                ShowStatus("falha em Qualify — veja Output > SQL Beaver");
            }
        }

        private static void OnUnqualifyNames(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.UnqualifyNames();
            }
            catch (Exception ex)
            {
                Log.Error("Unqualify names", ex);
                ShowStatus("falha em Unqualify — veja Output > SQL Beaver");
            }
        }

        private static void OnRename(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.RenameAliasOrVariable();
            }
            catch (Exception ex)
            {
                Log.Error("Rename alias/variable", ex);
                ShowStatus("falha em Rename — veja Output > SQL Beaver");
            }
        }

        private static void OnInlineExec(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.InlineExec();
            }
            catch (Exception ex)
            {
                Log.Error("Inline EXEC", ex);
                ShowStatus("falha em Inline EXEC — veja Output > SQL Beaver");
            }
        }

        private static void OnEncapsulate(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.EncapsulateAsProcedure();
            }
            catch (Exception ex)
            {
                Log.Error("Encapsular como proc", ex);
                ShowStatus("falha em Encapsular — veja Output > SQL Beaver");
            }
        }

        private static void OnInsertSemicolons(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.InsertSemicolons();
            }
            catch (Exception ex)
            {
                Log.Error("Inserir ponto-e-vírgula", ex);
                ShowStatus("falha em Inserir ponto-e-vírgula — veja Output > SQL Beaver");
            }
        }

        private static void OnAddBrackets(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.AddBrackets();
            }
            catch (Exception ex)
            {
                Log.Error("Adicionar colchetes", ex);
                ShowStatus("falha em Adicionar colchetes — veja Output > SQL Beaver");
            }
        }

        private static void OnRemoveBrackets(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.RemoveBrackets();
            }
            catch (Exception ex)
            {
                Log.Error("Remover colchetes", ex);
                ShowStatus("falha em Remover colchetes — veja Output > SQL Beaver");
            }
        }

        private static void OnApplyCasing(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefactoringCommands.ApplyCasing();
            }
            catch (Exception ex)
            {
                Log.Error("Aplicar casing", ex);
                ShowStatus("falha em Aplicar casing — veja Output > SQL Beaver");
            }
        }

        private static void OnQueryHistory(CommandBarButton ctrl, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commands.EditorCommands.QueryHistory();
        }

        private static void OnRecoverSession(CommandBarButton ctrl, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commands.EditorCommands.RecoverSession();
        }

        private static void OnEnvironments(CommandBarButton ctrl, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Commands.EditorCommands.Environments();
        }

        private static void OnInsertColumns(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.InsertColumns();
            }
            catch (Exception ex)
            {
                Log.Error("Inserir colunas", ex);
                ShowStatus("falha em Inserir colunas — veja Output > SQL Beaver");
            }
        }

        // ---------------------------------------------------------------
        // IA submenu handlers
        // ---------------------------------------------------------------

        private static void OnAiGenerate(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.AiGenerateFromComment();
            }
            catch (Exception ex)
            {
                Log.Error("IA: gerar SQL de comentário", ex);
                ShowStatus("falha em IA: gerar SQL — veja Output > SQL Beaver");
            }
        }

        private static void OnAiExplain(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.AiExplain();
            }
            catch (Exception ex)
            {
                Log.Error("IA: explicar SQL", ex);
                ShowStatus("falha em IA: explicar SQL — veja Output > SQL Beaver");
            }
        }

        private static void OnAiOptimize(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.AiOptimize();
            }
            catch (Exception ex)
            {
                Log.Error("IA: otimizar SQL", ex);
                ShowStatus("falha em IA: otimizar SQL — veja Output > SQL Beaver");
            }
        }

        private static void OnAiSettings(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.AiSettings();
            }
            catch (Exception ex)
            {
                Log.Error("IA (configuração)", ex);
                ShowStatus("falha ao abrir configuração de IA — veja Output > SQL Beaver");
            }
        }

        private static void OnSummarizeScript(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.SummarizeScript();
            }
            catch (Exception ex)
            {
                Log.Error("Summarize Script", ex);
                ShowStatus("falha em Summarize Script — veja Output > SQL Beaver");
            }
        }

        private static void OnManageSnippets(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.ManageSnippets();
            }
            catch (Exception ex)
            {
                Log.Error("Snippets", ex);
                ShowStatus("falha ao abrir gerenciador de snippets — veja Output > SQL Beaver");
            }
        }

        // ---------------------------------------------------------------
        // Format style submenu
        // ---------------------------------------------------------------

        private static void OnManageFormatStyles(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Commands.EditorCommands.ManageFormatStyles();
            }
            catch (Exception ex)
            {
                Log.Error("Gerenciar estilos de formatação", ex);
                ShowStatus("falha ao abrir gerenciador de estilos — veja Output > SQL Beaver");
            }
        }

        /// <summary>
        /// Reconstrói os itens dinâmicos do popup de estilos.
        /// Remove os itens antigos (preserva o botão fixo "Gerenciar estilos…") e
        /// adiciona um botão por estilo disponível, marcando o ativo com "● ".
        /// </summary>
        private static void RebuildStyleItems(CommandBarPopup popup)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Remove previously created dynamic buttons (they are temporary COM controls)
            foreach (CommandBarButton old in _styleButtons)
            {
                try { old.Delete(); } catch { /* ignore COM errors on stale items */ }
            }
            _styleButtons.Clear();

            try
            {
                System.Collections.Generic.IReadOnlyList<string> styles =
                    Formatting.FormatStyleStore.ListStyles();
                string active = Formatting.FormatStyleStore.ActiveStyleName;

                if (styles.Count == 0)
                    return;

                foreach (string styleName in styles)
                {
                    string name = styleName; // capture for closure
                    string caption = string.Equals(name, active, StringComparison.OrdinalIgnoreCase)
                        ? "● " + name
                        : "    " + name;

                    var btn = (CommandBarButton)popup.Controls.Add(
                        MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
                    btn.Caption    = caption;
                    btn.BeginGroup = (_styleButtons.Count == 0); // separator before the first style item

                    // Use a local lambda — CommandBar buttons require a field ref for click handler,
                    // but we store each button anyway to keep the COM reference alive.
                    btn.Click += (CommandBarButton b, ref bool cancel) =>
                    {
                        try
                        {
                            ThreadHelper.ThrowIfNotOnUIThread();
                            Commands.EditorCommands.SetFormatStyle(name);
                            // Rebuild so the active marker updates immediately
                            RebuildStyleItems(popup);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Trocar estilo de formatação", ex);
                        }
                    };

                    _styleButtons.Add(btn);
                }
            }
            catch (Exception ex)
            {
                Log.Error("RebuildStyleItems", ex);
            }
        }
    }
}
