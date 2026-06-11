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

        // Refactor submenu
        private static CommandBarPopup _refactorPopup;
        private static CommandBarButton _expandWildcardButton;
        private static CommandBarButton _qualifyNamesButton;
        private static CommandBarButton _unqualifyNamesButton;
        private static CommandBarButton _renameButton;

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

                // Refactor submenu
                _refactorPopup = (CommandBarPopup)editorBar.Controls.Add(
                    MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, /*temporary:*/ true);
                _refactorPopup.Caption = "SQL Beaver: Refatorar";
                _refactorPopup.BeginGroup = true;

                _expandWildcardButton  = AddButton(_refactorPopup, "Expand wildcard (SELECT *)", OnExpandWildcard,  beginGroup: false);
                _qualifyNamesButton    = AddButton(_refactorPopup, "Qualify object names",        OnQualifyNames,    beginGroup: false);
                _unqualifyNamesButton  = AddButton(_refactorPopup, "Remove qualificação",         OnUnqualifyNames,  beginGroup: false);
                _renameButton          = AddButton(_refactorPopup, "Rename alias/variável…",      OnRename,          beginGroup: true);

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
    }
}
