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
    [Guid(PackageGuidString)]
    public sealed class SqlBeaverPackage : ToolkitPackage
    {
        public const string PackageGuidString = "9B2E7C5A-4C63-4F1E-9D2A-8F5B3A7E6C41";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Log.Info("SQL Beaver inicializado.");
            Grid.GridCommandBarMenu.Initialize();
        }
    }
}
