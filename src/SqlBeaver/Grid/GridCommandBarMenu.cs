using System;
using System.Collections.Generic;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Diagnostics;
using SqlBeaver.Scripting;

namespace SqlBeaver.Grid
{
    /// <summary>
    /// Registra os comandos do SQL Beaver no menu de contexto da grid de resultados
    /// (CommandBar "SQL Results Grid Tab Context"). Botões temporários, recriados a
    /// cada sessão do SSMS. Padrão do AxialSqlTools (Apache-2.0).
    /// </summary>
    internal static class GridCommandBarMenu
    {
        private const string GridContextBarName = "SQL Results Grid Tab Context";

        // Referências fortes: os handlers COM são coletados pelo GC sem isso.
        private static CommandBarButton _scriptAsInsertButton;
        private static CommandBarButton _copyAsInButton;
        private static CommandBarButton _openInExcelButton;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var bars = dte?.CommandBars as CommandBars;
                if (bars == null)
                {
                    Log.Info("CommandBars indisponível — menu da grid não registrado.");
                    return;
                }

                CommandBar gridBar = bars[GridContextBarName];

                _scriptAsInsertButton = AddButton(gridBar, "Script as INSERT", OnScriptAsInsert, beginGroup: true);
                _copyAsInButton       = AddButton(gridBar, "Copy as IN clause", OnCopyAsInClause, beginGroup: false);
                _openInExcelButton    = AddButton(gridBar, "Open in Excel", OnOpenInExcel, beginGroup: false);

                Log.Info("Comandos registrados no menu da grid de resultados.");
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao registrar o menu da grid (\"" + GridContextBarName + "\")", ex);
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

        private static void OnScriptAsInsert(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null) { Log.Info("Script as INSERT: nenhuma grid em foco."); return; }

                GridData data = ResultsGridAccess.ReadAll(grid, out bool truncated);
                if (data == null || data.Rows.Count == 0) { Log.Info("Script as INSERT: grid vazia."); return; }

                string tableName = TableNameHeuristic.TryExtract(GetActiveQueryText()) ?? "[NomeDaTabela]";
                string script    = InsertScriptBuilder.Build(data, tableName);
                if (truncated)
                    script += "-- ATENÇÃO: resultado truncado em " + ResultsGridAccess.MaxRows + " linhas\r\n";

                Clipboard.SetText(script);
                Log.Info($"Script as INSERT: {data.Rows.Count} linha(s) copiada(s) para o clipboard (tabela: {tableName}).");
            }
            catch (Exception ex)
            {
                Log.Error("Script as INSERT", ex);
            }
        }

        private static void OnCopyAsInClause(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null) { Log.Info("Copy as IN clause: nenhuma grid em foco."); return; }

                Tuple<List<string>, Type> selection =
                    ResultsGridAccess.ReadSelectedColumnValues(grid);
                if (selection == null || selection.Item1.Count == 0)
                {
                    Log.Info("Copy as IN clause: selecione ao menos uma célula.");
                    return;
                }

                string inClause = InClauseBuilder.Build(selection.Item1, selection.Item2);
                Clipboard.SetText(inClause);
                Log.Info($"Copy as IN clause: {selection.Item1.Count} valor(es) copiado(s).");
            }
            catch (Exception ex)
            {
                Log.Error("Copy as IN clause", ex);
            }
        }

        private static void OnOpenInExcel(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null) { Log.Info("Open in Excel: nenhuma grid em foco."); return; }

                GridData data = ResultsGridAccess.ReadAll(grid, out bool truncated);
                if (data == null) { Log.Info("Open in Excel: falha ao ler a grid."); return; }

                string path = ExcelExporter.ExportToTempFile(data);
                if (truncated)
                    Log.Info($"Open in Excel: exportação truncada em {ResultsGridAccess.MaxRows} linhas.");

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
                Log.Info($"Open in Excel: {data.Rows.Count} linha(s) exportada(s) para {path}.");
            }
            catch (Exception ex)
            {
                Log.Error("Open in Excel", ex);
            }
        }

        private static string GetActiveQueryText()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
                var doc = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (doc == null) return null;
                return doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
            }
            catch
            {
                return null;
            }
        }
    }
}
