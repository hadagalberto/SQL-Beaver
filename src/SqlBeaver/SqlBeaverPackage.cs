using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;

namespace SqlBeaver
{
    /// <summary>
    /// Pacote mínimo: autoload em background só para registrar no Output pane
    /// que a extensão carregou — essencial para diagnosticar problemas de instalação.
    /// O completion em si é MEF e carrega com o editor, independente deste pacote.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidString)]
    public sealed class SqlBeaverPackage : ToolkitPackage
    {
        public const string PackageGuidString = "9B2E7C5A-4C63-4F1E-9D2A-8F5B3A7E6C41";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Log.Info("SQL Beaver inicializado.");
            Grid.GridCommandBarMenu.Initialize();
            Grid.EditorCommandBarMenu.Initialize();
            Guard.ExecuteGuard.Initialize();
            Session.SessionSnapshotService.Initialize();
            Environments.TabColorizer.Initialize();
            Session.SessionRestoreService.Initialize();

            if (await GetServiceAsync(typeof(System.ComponentModel.Design.IMenuCommandService))
                is System.ComponentModel.Design.IMenuCommandService menuService)
            {
                AddCommand(menuService, Commands.SqlBeaverCommandIds.FormatDocument, () => Commands.EditorCommands.FormatDocument());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.FindObject, () => Commands.EditorCommands.FindObject());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.GoToDefinition, () => Commands.EditorCommands.GoToDefinition());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.FindReferences, () => Commands.EditorCommands.FindReferences());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.QueryHistory,   () => Commands.EditorCommands.QueryHistory());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.RecoverSession, () => Commands.EditorCommands.RecoverSession());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.Environments,        () => Commands.EditorCommands.Environments());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.RunCurrentStatement, () => Commands.EditorCommands.RunCurrentStatement());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.InsertColumns,       () => Commands.EditorCommands.InsertColumns());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.AnalyzeScript,        () => Commands.EditorCommands.AnalyzeScript());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.InvalidObjects,       () => Commands.EditorCommands.InvalidObjects());
                AddCommand(menuService, Commands.SqlBeaverCommandIds.ManageFormatStyles,  () => Commands.EditorCommands.ManageFormatStyles());
                Log.Info("Comandos nomeados registrados (menu Tools > SQL Beaver, toolbar e atalhos).");
            }
            else
            {
                Log.Info("IMenuCommandService indisponível — comandos nomeados/atalhos desabilitados.");
            }

            // Restauração da última sessão: fire-and-forget, nunca atrasa/quebra o startup
            // (o delay de 2s e o try/catch ficam dentro do serviço).
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(
                () => Session.SessionRestoreService.RestorePreviousSessionAsync());
        }

        private static void AddCommand(System.ComponentModel.Design.IMenuCommandService service, int id, Action handler)
        {
            var commandId = new System.ComponentModel.Design.CommandID(Commands.SqlBeaverCommandIds.CommandSetGuid, id);
            service.AddCommand(new Microsoft.VisualStudio.Shell.OleMenuCommand((s, e) =>
            {
                try { handler(); }
                catch (Exception ex) { Diagnostics.Log.Error("Comando SQL Beaver", ex); }
            }, commandId));
        }
    }
}
