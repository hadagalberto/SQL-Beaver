using System;
using System.Collections.Generic;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;
using SqlBeaver.Connection;
using SqlBeaver.Completion;
using SqlBeaver.Diagnostics;
using SqlBeaver.Metadata;
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
        private static CommandBarButton _scriptAsSelectButton;
        private static CommandBarButton _scriptAsUpdateButton;
        private static CommandBarButton _scriptAsDeleteButton;
        private static CommandBarButton _scriptAsMergeButton;
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
                _scriptAsSelectButton = AddButton(gridBar, "Script as SELECT", OnScriptAsSelect, beginGroup: false);
                _scriptAsUpdateButton = AddButton(gridBar, "Script as UPDATE", OnScriptAsUpdate, beginGroup: false);
                _scriptAsDeleteButton = AddButton(gridBar, "Script as DELETE", OnScriptAsDelete, beginGroup: false);
                _scriptAsMergeButton  = AddButton(gridBar, "Script as MERGE",  OnScriptAsMerge,  beginGroup: false);
                _copyAsInButton       = AddButton(gridBar, "Copy as IN clause", OnCopyAsInClause, beginGroup: true);
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

        // ──────────────────────────────────────────────────────────────────
        // Script as SELECT
        // ──────────────────────────────────────────────────────────────────

        private static void OnScriptAsSelect(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null) { ShowStatus("nenhuma grid em foco"); return; }

                GridData data = ReadGridData(grid, out string modeMsg);
                if (data == null) { ShowStatus("falha ao ler a grid"); return; }

                string tableName = TableNameHeuristic.TryExtract(GetActiveQueryText()) ?? "[NomeDaTabela]";
                string script    = SelectScriptBuilder.Build(data, tableName);

                CopyToClipboard(script, "Script as SELECT");
                ShowStatus($"SELECT copiado ({modeMsg}, tabela: {tableName})");
            }
            catch (Exception ex)
            {
                Log.Error("Script as SELECT", ex);
                ShowStatus("falha em Script as SELECT — veja Output > SQL Beaver");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Script as UPDATE
        // ──────────────────────────────────────────────────────────────────

        private static void OnScriptAsUpdate(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null) { ShowStatus("nenhuma grid em foco"); return; }

                GridData data = ReadGridDataWithSelection(grid, out string modeMsg);
                if (data == null || data.Rows.Count == 0) { ShowStatus("grid vazia"); return; }

                string tableName = TableNameHeuristic.TryExtract(GetActiveQueryText()) ?? "[NomeDaTabela]";
                IReadOnlyList<string> pkCols = ResolvePkColumns(tableName);
                string script = UpdateScriptBuilder.Build(data, tableName, pkCols);

                CopyToClipboard(script, "Script as UPDATE");
                ShowStatus($"{data.Rows.Count} linha(s) UPDATE copiada(s) ({modeMsg}, tabela: {tableName})");
            }
            catch (Exception ex)
            {
                Log.Error("Script as UPDATE", ex);
                ShowStatus("falha em Script as UPDATE — veja Output > SQL Beaver");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Script as DELETE
        // ──────────────────────────────────────────────────────────────────

        private static void OnScriptAsDelete(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null) { ShowStatus("nenhuma grid em foco"); return; }

                GridData data = ReadGridDataWithSelection(grid, out string modeMsg);
                if (data == null || data.Rows.Count == 0) { ShowStatus("grid vazia"); return; }

                string tableName = TableNameHeuristic.TryExtract(GetActiveQueryText()) ?? "[NomeDaTabela]";
                IReadOnlyList<string> pkCols = ResolvePkColumns(tableName);
                string script = DeleteScriptBuilder.Build(data, tableName, pkCols);

                CopyToClipboard(script, "Script as DELETE");
                ShowStatus($"{data.Rows.Count} linha(s) DELETE copiada(s) ({modeMsg}, tabela: {tableName})");
            }
            catch (Exception ex)
            {
                Log.Error("Script as DELETE", ex);
                ShowStatus("falha em Script as DELETE — veja Output > SQL Beaver");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Script as MERGE
        // ──────────────────────────────────────────────────────────────────

        private static void OnScriptAsMerge(CommandBarButton ctrl, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                object grid = ResultsGridAccess.GetFocusedGridControl();
                if (grid == null) { ShowStatus("nenhuma grid em foco"); return; }

                GridData data = ReadGridDataWithSelection(grid, out string modeMsg);
                if (data == null || data.Rows.Count == 0) { ShowStatus("grid vazia"); return; }

                string tableName = TableNameHeuristic.TryExtract(GetActiveQueryText()) ?? "[NomeDaTabela]";
                IReadOnlyList<string> pkCols = ResolvePkColumns(tableName);
                string script = MergeScriptBuilder.Build(data, tableName, pkCols);

                CopyToClipboard(script, "Script as MERGE");
                ShowStatus($"{data.Rows.Count} linha(s) MERGE copiada(s) ({modeMsg}, tabela: {tableName})");
            }
            catch (Exception ex)
            {
                Log.Error("Script as MERGE", ex);
                ShowStatus("falha em Script as MERGE — veja Output > SQL Beaver");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Lê toda a grid (sem seleção) e retorna modeMsg.</summary>
        private static GridData ReadGridData(object grid, out string modeMsg)
        {
            GridData data = ResultsGridAccess.ReadAll(grid, out bool truncated);
            int rowCount  = data?.Rows.Count ?? 0;
            modeMsg = $"grid inteira ({rowCount} linha(s))";
            if (truncated)
                modeMsg += $" — truncada em {ResultsGridAccess.MaxRows}";
            return data;
        }

        /// <summary>Lê seleção se existir, senão toda a grid.</summary>
        private static GridData ReadGridDataWithSelection(object grid, out string modeMsg)
        {
            ResultsGridAccess.GridSelection sel = ResultsGridAccess.ReadSelection(grid);
            if (ResultsGridAccess.IsRealSelection(sel))
            {
                modeMsg = $"{sel.RowIndexes.Count} linha(s) selecionada(s)";
                return ResultsGridAccess.ReadRows(grid, sel.RowIndexes);
            }
            return ReadGridData(grid, out modeMsg);
        }

        /// <summary>
        /// Tenta resolver as colunas PK da tabela via cache de metadata.
        /// Retorna lista vazia quando não resolvido.
        /// </summary>
        private static IReadOnlyList<string> ResolvePkColumns(string tableRef)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ActiveConnection conn = ConnectionService.GetActiveConnection();
                if (conn == null) return new string[0];

                DbMetadata metadata = SqlBeaverCompletionSourceProvider.Cache.TryGet(
                    conn.Server, conn.Database,
                    new MetadataRequest
                    {
                        ConnectionString     = conn.ConnectionString,
                        AccessToken          = conn.AccessToken,
                        ProviderConnectionType = conn.ProviderConnectionType
                    });

                if (metadata == null) return new string[0];

                // Strip brackets from tableRef for lookup
                string stripped = tableRef.Trim('[', ']');
                string schema = null;
                string tname  = stripped;
                int dot = stripped.LastIndexOf('.');
                if (dot >= 0)
                {
                    schema = stripped.Substring(0, dot).Trim('[', ']');
                    tname  = stripped.Substring(dot + 1).Trim('[', ']');
                }

                if (schema == null)
                    schema = metadata.ResolveUniqueSchema(tname);

                if (schema == null) return new string[0];

                string key = DbMetadata.TableKey(schema, tname);
                if (!metadata.ColumnsByTable.TryGetValue(key, out IReadOnlyList<ColumnEntry> cols))
                    return new string[0];

                var pkList = new List<string>();
                foreach (ColumnEntry col in cols)
                {
                    if (col.IsPrimaryKey)
                        pkList.Add(col.Name);
                }
                return pkList;
            }
            catch
            {
                return new string[0];
            }
        }

        private static void CopyToClipboard(string text, string opName)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception clipEx)
            {
                Log.Error(opName + ": clipboard inacessível", clipEx);
                ShowStatus("não foi possível acessar o clipboard (em uso por outro app)");
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
