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

        private static void ShowStatus(string message)
        {
            _ = Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync("SQL Beaver: " + message);
        }

        private static void OnScriptAsInsert(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null)
                {
                    Log.Info("Script as INSERT: nenhuma grid em foco.");
                    ShowStatus("nenhuma grid em foco");
                    return;
                }

                ResultsGridAccess.GridSelection sel = ResultsGridAccess.ReadSelection(grid);
                if (sel != null)
                    Log.Info("Script as INSERT: blocos de seleção: " + string.Join(" | ", sel.BlockDump));

                GridData data;
                string modeMsg;
                if (ResultsGridAccess.IsRealSelection(sel))
                {
                    data = ResultsGridAccess.ReadRows(grid, sel.RowIndexes);
                    modeMsg = $"{sel.RowIndexes.Count} linha(s) selecionada(s)";
                }
                else
                {
                    data = ResultsGridAccess.ReadAll(grid, out bool truncated);
                    int rowCount = data?.Rows.Count ?? 0;
                    modeMsg = $"grid inteira ({rowCount} linha(s))";
                    if (truncated)
                        modeMsg += $" — truncada em {ResultsGridAccess.MaxRows}";
                }

                if (data == null || data.Rows.Count == 0)
                {
                    Log.Info("Script as INSERT: grid vazia.");
                    ShowStatus("grid vazia");
                    return;
                }

                string tableName = TableNameHeuristic.TryExtract(GetActiveQueryText()) ?? "[NomeDaTabela]";
                string script    = InsertScriptBuilder.Build(data, tableName);
                if (ResultsGridAccess.IsRealSelection(sel) && sel.RowIndexes.Count >= ResultsGridAccess.MaxRows)
                    script += "-- ATENÇÃO: resultado truncado em " + ResultsGridAccess.MaxRows + " linhas\r\n";

                try
                {
                    Clipboard.SetText(script);
                }
                catch (Exception clipEx)
                {
                    Log.Error("Script as INSERT: clipboard inacessível", clipEx);
                    ShowStatus("não foi possível acessar o clipboard (em uso por outro app)");
                    return;
                }

                string msg = $"{data.Rows.Count} linha(s) copiada(s) ({modeMsg}, tabela: {tableName})";
                Log.Info("Script as INSERT: " + msg + ".");
                ShowStatus(msg);
            }
            catch (Exception ex)
            {
                Log.Error("Script as INSERT", ex);
                ShowStatus("falha em Script as INSERT — veja Output > SQL Beaver");
            }
        }

        private static void OnCopyAsInClause(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null)
                {
                    Log.Info("Copy as IN clause: nenhuma grid em foco.");
                    ShowStatus("nenhuma grid em foco");
                    return;
                }

                Tuple<List<string>, Type> result =
                    ResultsGridAccess.ReadSelectedColumnValues(grid, out ResultsGridAccess.GridSelection selection);

                if (selection != null)
                    Log.Info("Copy as IN clause: blocos de seleção: " + string.Join(" | ", selection.BlockDump));

                if (result == null || result.Item1.Count == 0)
                {
                    Log.Info("Copy as IN clause: selecione ao menos uma célula.");
                    ShowStatus("selecione ao menos uma célula");
                    return;
                }

                string inClause = InClauseBuilder.Build(result.Item1, result.Item2);

                if (inClause == "()")
                {
                    Log.Info("Copy as IN clause: seleção só contém NULL — nada copiado.");
                    ShowStatus("seleção só contém NULL — nada copiado");
                    return;
                }

                try
                {
                    Clipboard.SetText(inClause);
                }
                catch (Exception clipEx)
                {
                    Log.Error("Copy as IN clause: clipboard inacessível", clipEx);
                    ShowStatus("não foi possível acessar o clipboard (em uso por outro app)");
                    return;
                }

                string msg = $"{result.Item1.Count} valor(es) copiado(s)";
                Log.Info("Copy as IN clause: " + msg + ".");
                ShowStatus(msg);
            }
            catch (Exception ex)
            {
                Log.Error("Copy as IN clause", ex);
                ShowStatus("falha em Copy as IN clause — veja Output > SQL Beaver");
            }
        }

        private static void OnOpenInExcel(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null)
                {
                    Log.Info("Open in Excel: nenhuma grid em foco.");
                    ShowStatus("nenhuma grid em foco");
                    return;
                }

                ResultsGridAccess.GridSelection sel = ResultsGridAccess.ReadSelection(grid);
                if (sel != null)
                    Log.Info("Open in Excel: blocos de seleção: " + string.Join(" | ", sel.BlockDump));

                GridData data;
                string modeMsg;
                if (ResultsGridAccess.IsRealSelection(sel))
                {
                    data = ResultsGridAccess.ReadRows(grid, sel.RowIndexes);
                    modeMsg = $"{sel.RowIndexes.Count} linha(s) selecionada(s)";
                }
                else
                {
                    data = ResultsGridAccess.ReadAll(grid, out bool truncated);
                    int rowCount = data?.Rows.Count ?? 0;
                    modeMsg = $"grid inteira ({rowCount} linha(s))";
                    if (truncated)
                        Log.Info($"Open in Excel: exportação truncada em {ResultsGridAccess.MaxRows} linhas.");
                }

                if (data == null)
                {
                    Log.Info("Open in Excel: falha ao ler a grid.");
                    ShowStatus("falha ao ler a grid");
                    return;
                }

                string path = ExcelExporter.ExportToTempFile(data);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });

                string msg = $"{data.Rows.Count} linha(s) exportada(s) para Excel ({modeMsg})";
                Log.Info("Open in Excel: " + msg + " (" + path + ").");
                ShowStatus(msg);
            }
            catch (Exception ex)
            {
                Log.Error("Open in Excel", ex);
                ShowStatus("falha em Open in Excel — veja Output > SQL Beaver");
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
