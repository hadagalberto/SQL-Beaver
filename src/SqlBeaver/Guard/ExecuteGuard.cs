using System;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Analysis;
using SqlBeaver.Diagnostics;

namespace SqlBeaver.Guard
{
    /// <summary>Confirma antes de executar DELETE/UPDATE sem WHERE. Intercepta o comando
    /// de Execute do SSMS via DTE.CommandEvents; se o comando não for encontrado, a
    /// feature fica desabilitada (log) sem afetar o resto da extensão.</summary>
    internal static class ExecuteGuard
    {
        // Refs fortes: eventos COM são coletados pelo GC sem isso.
        private static CommandEvents _executeEvents;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                if (dte == null) { Log.Info("ExecuteGuard: DTE indisponível."); return; }

                Command executeCommand = FindCommand(dte, "Query.Execute");
                if (executeCommand == null)
                {
                    Log.Info("ExecuteGuard: comando Query.Execute não encontrado — guard desabilitado.");
                    return;
                }

                _executeEvents = dte.Events.CommandEvents[executeCommand.Guid, executeCommand.ID];
                _executeEvents.BeforeExecute += OnBeforeExecute;
                Log.Info("ExecuteGuard ativo (Query.Execute interceptado).");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao inicializar o ExecuteGuard", ex);
            }
        }

        private static Command FindCommand(DTE2 dte, string name)
        {
            try
            {
                return dte.Commands.Item(name);
            }
            catch
            {
                return null;
            }
        }

        private static void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) return;

                // o SSMS executa a seleção quando há uma; senão o documento inteiro
                string sql = !doc.Selection.IsEmpty
                    ? doc.Selection.Text
                    : doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                var dangers = DangerousStatementDetector.Find(sql);
                if (dangers.Count == 0) return;

                var first = dangers[0];
                string message = dangers.Count == 1
                    ? $"{first.Keyword} sem WHERE na linha {first.Line}.\r\n\r\nExecutar mesmo assim?"
                    : $"{dangers.Count} statements sem WHERE (primeiro: {first.Keyword} na linha {first.Line}).\r\n\r\nExecutar mesmo assim?";

                var owner = new NativeWindow();
                owner.AssignHandle((System.IntPtr)(int)dte.MainWindow.HWnd);
                DialogResult choice;
                try
                {
                    choice = MessageBox.Show(
                        owner,
                        message, "SQL Beaver — atenção",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                }
                finally
                {
                    owner.ReleaseHandle();
                }

                if (choice != DialogResult.Yes)
                {
                    cancelDefault = true;
                    Log.Info($"Execução cancelada pelo guard: {first.Keyword} sem WHERE na linha {first.Line}.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("ExecuteGuard.OnBeforeExecute", ex);
                // nunca bloquear a execução por falha do guard
            }
        }
    }
}
