using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;
using SqlBeaver.Environments;
using SqlBeaver.Navigation;
using SqlBeaver.Session;

namespace SqlBeaver.Commands
{
    /// <summary>
    /// Handlers canônicos dos comandos do editor SQL. Chamados tanto pelos botões
    /// de CommandBar (menu de contexto) quanto pelos comandos nomeados VSCT
    /// (menu Tools > SQL Beaver, toolbar e atalhos de teclado).
    /// Cada método é autocontido: assert de UI thread, try/catch, Log e status bar.
    /// </summary>
    internal static class EditorCommands
    {
        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }

        public static void FormatDocument()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) { ShowStatus("Format: nenhum documento ativo."); return; }

                bool hasSelection = !doc.Selection.IsEmpty;
                string original = hasSelection
                    ? doc.Selection.Text
                    : doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);

                if (string.IsNullOrWhiteSpace(original)) { ShowStatus("Format: nada para formatar."); return; }

                if (!Formatting.SqlFormatterService.TryFormat(original, out string formatted, out string error, out bool containsComments))
                {
                    ShowStatus("não formatado: " + error);
                    Log.Info("Format Document abortado: " + error);
                    return;
                }

                if (containsComments)
                {
                    var owner = new System.Windows.Forms.NativeWindow();
                    owner.AssignHandle((IntPtr)(int)dte.MainWindow.HWnd);
                    System.Windows.Forms.DialogResult keep;
                    try
                    {
                        keep = System.Windows.Forms.MessageBox.Show(
                            owner,
                            "O script contém comentários e a formatação vai REMOVÊ-LOS.\r\n\r\nFormatar mesmo assim?",
                            "SQL Beaver — Format Document",
                            System.Windows.Forms.MessageBoxButtons.YesNo,
                            System.Windows.Forms.MessageBoxIcon.Warning,
                            System.Windows.Forms.MessageBoxDefaultButton.Button2);
                    }
                    finally
                    {
                        owner.ReleaseHandle();
                    }
                    if (keep != System.Windows.Forms.DialogResult.Yes)
                    {
                        ShowStatus("formatação cancelada (comentários seriam removidos).");
                        return;
                    }
                }

                dte.UndoContext.Open("SQL Beaver Format Document");
                try
                {
                    if (hasSelection)
                    {
                        doc.Selection.Insert(formatted,
                            (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    }
                    else
                    {
                        EditPoint start = doc.StartPoint.CreateEditPoint();
                        start.ReplaceText(doc.EndPoint, formatted,
                            (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);
                    }
                }
                finally
                {
                    dte.UndoContext.Close();
                }

                ShowStatus("documento formatado.");
                Log.Info("Format Document aplicado" + (hasSelection ? " (seleção)." : " (documento inteiro)."));
            }
            catch (Exception ex)
            {
                Log.Error("Format Document", ex);
                ShowStatus("falha no Format Document — veja Output > SQL Beaver");
            }
        }

        public static void GoToDefinition()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                DefinitionService.GoToDefinition();
            }
            catch (Exception ex)
            {
                Log.Error("Ir para definição", ex);
                ShowStatus("falha em Ir para definição — veja Output > SQL Beaver");
            }
        }

        public static void FindObject()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                FindObjectDialog.Show();
            }
            catch (Exception ex)
            {
                Log.Error("Localizar objeto", ex);
                ShowStatus("falha em Localizar objeto — veja Output > SQL Beaver");
            }
        }

        public static void FindReferences()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                DefinitionService.FindReferences();
            }
            catch (Exception ex)
            {
                Log.Error("Localizar referências", ex);
                ShowStatus("falha em Localizar referências — veja Output > SQL Beaver");
            }
        }

        public static void QueryHistory()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                QueryHistoryService.OpenTodayFile();
            }
            catch (Exception ex)
            {
                Log.Error("Histórico de consultas", ex);
                ShowStatus("falha em Histórico de consultas — veja Output > SQL Beaver");
            }
        }

        public static void RecoverSession()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                SessionSnapshotService.ShowRecoveryDialog();
            }
            catch (Exception ex)
            {
                Log.Error("Recuperar consultas", ex);
                ShowStatus("falha em Recuperar consultas — veja Output > SQL Beaver");
            }
        }

        public static void Environments()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                EnvironmentsDialog.Show();
            }
            catch (Exception ex)
            {
                Log.Error("Ambientes (cores)", ex);
                ShowStatus("falha ao abrir editor de ambientes — veja Output > SQL Beaver");
            }
        }
    }
}
