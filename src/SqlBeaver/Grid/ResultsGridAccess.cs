using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlBeaver.Diagnostics;
using SqlBeaver.Scripting;

namespace SqlBeaver.Grid
{
    /// <summary>
    /// Acesso à grid de resultados do SSMS por reflection (sem referência de compilação
    /// às DLLs do SSMS — mesma política do ConnectionService). Padrões do AxialSqlTools
    /// (Apache-2.0). Chamar na thread de UI.
    /// </summary>
    internal static class ResultsGridAccess
    {
        /// <summary>Linhas exportadas no máximo por comando (proteção contra grids gigantes).</summary>
        internal const int MaxRows = 10000;

        public static object GetFocusedGridControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var monitor = Package.GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (monitor == null) return null;

            monitor.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out object frameObj);
            if (!(frameObj is IVsWindowFrame frame)) return null;

            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object docView);
            var container = docView as ContainerControl;
            var inner = container?.ActiveControl as ContainerControl;
            Control candidate = inner?.ActiveControl;

            return candidate != null && candidate.GetType().Name == "GridControl" ? candidate : null;
        }

        /// <summary>Lê a grid inteira (até MaxRows). Retorna null em falha.</summary>
        public static GridData ReadAll(object gridControl, out bool truncated)
        {
            truncated = false;
            try
            {
                Type gridType = gridControl.GetType();
                object storage = gridType.GetProperty("GridStorage")?.GetValue(gridControl);
                if (storage == null) return null;

                long numRows = Convert.ToInt64(storage.GetType().GetMethod("NumRows")?.Invoke(storage, null));
                int columnsNumber = Convert.ToInt32(gridType.GetProperty("ColumnsNumber")?.GetValue(gridControl));

                MethodInfo getCell = GetCellMethod(storage);
                if (getCell == null || columnsNumber < 2) return null;

                IReadOnlyList<GridColumn> columns = ReadColumns(gridControl, gridType, storage, columnsNumber);

                long rowsToRead = numRows;
                if (rowsToRead > MaxRows)
                {
                    rowsToRead = MaxRows;
                    truncated = true;
                }

                var rows = new List<string[]>((int)rowsToRead);
                for (long r = 0; r < rowsToRead; r++)
                {
                    var row = new string[columns.Count];
                    for (int c = 1; c < columnsNumber; c++)
                        row[c - 1] = getCell.Invoke(storage, new object[] { r, c }) as string;
                    rows.Add(row);
                }

                return new GridData(columns, rows);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler a grid de resultados", ex);
                return null;
            }
        }

        /// <summary>
        /// Valores da coluna da primeira célula selecionada, nas linhas selecionadas,
        /// mais o tipo CLR da coluna. Retorna null sem seleção ou em falha.
        /// </summary>
        public static Tuple<List<string>, Type> ReadSelectedColumnValues(object gridControl)
        {
            try
            {
                Type gridType = gridControl.GetType();
                object storage = gridType.GetProperty("GridStorage")?.GetValue(gridControl);
                MethodInfo getCell = GetCellMethod(storage);
                if (storage == null || getCell == null) return null;

                var selectedCells = gridType.GetProperty("SelectedCells")?.GetValue(gridControl) as IEnumerable;
                if (selectedCells == null) return null;

                int column = -1;
                var rowIndexes = new SortedSet<long>();
                foreach (object block in selectedCells)
                {
                    Type blockType = block.GetType();
                    int x = Convert.ToInt32(blockType.GetProperty("X")?.GetValue(block));
                    int right = Convert.ToInt32(blockType.GetProperty("Right")?.GetValue(block));
                    long y = Convert.ToInt64(blockType.GetProperty("Y")?.GetValue(block));
                    long bottom = Convert.ToInt64(blockType.GetProperty("Bottom")?.GetValue(block));

                    if (column < 0)
                        column = Math.Max(1, x); // coluna 0 é a régua de números de linha

                    if (x <= column && column <= right)
                    {
                        for (long r = y; r <= bottom && rowIndexes.Count < MaxRows; r++)
                            rowIndexes.Add(r);
                    }
                }

                if (rowIndexes.Count >= MaxRows)
                    Log.Info($"Copy as IN clause: seleção truncada em {MaxRows} linhas.");

                if (column < 0 || rowIndexes.Count == 0) return null;

                var values = new List<string>(rowIndexes.Count);
                foreach (long r in rowIndexes)
                    values.Add(getCell.Invoke(storage, new object[] { r, column }) as string);

                Type clrType = ReadColumnClrType(storage, column);
                return Tuple.Create(values, clrType);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler a seleção da grid", ex);
                return null;
            }
        }

        private static MethodInfo GetCellMethod(object storage)
            => storage?.GetType().GetMethod("GetCellDataAsString", new[] { typeof(long), typeof(int) });

        private static IReadOnlyList<GridColumn> ReadColumns(object gridControl, Type gridType, object storage, int columnsNumber)
        {
            DataTable schema = storage.GetType()
                .GetField("m_schemaTable", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(storage) as DataTable;

            MethodInfo getHeaderInfo = FindGetHeaderInfo(gridType);

            var columns = new List<GridColumn>(columnsNumber - 1);
            for (int c = 1; c < columnsNumber; c++)
            {
                string name = null;
                if (getHeaderInfo != null)
                {
                    var args = new object[] { c, null, null };
                    getHeaderInfo.Invoke(gridControl, args);
                    name = args[1] as string;
                }
                if (string.IsNullOrEmpty(name) && schema != null && c - 1 < schema.Rows.Count)
                    name = schema.Rows[c - 1]["ColumnName"] as string;

                columns.Add(new GridColumn(name ?? ("Coluna" + c), GetSchemaClrType(schema, c)));
            }
            return columns;
        }

        private static Type ReadColumnClrType(object storage, int gridColumn)
        {
            DataTable schema = storage.GetType()
                .GetField("m_schemaTable", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(storage) as DataTable;
            return GetSchemaClrType(schema, gridColumn);
        }

        private static Type GetSchemaClrType(DataTable schema, int gridColumn)
        {
            if (schema == null || gridColumn - 1 >= schema.Rows.Count) return null;
            try { return schema.Rows[gridColumn - 1]["DataType"] as Type; }
            catch { return null; }
        }

        private static MethodInfo FindGetHeaderInfo(Type gridType)
        {
            foreach (MethodInfo method in gridType.GetMethods())
            {
                if (method.Name == "GetHeaderInfo" && method.GetParameters().Length == 3)
                    return method;
            }
            return null;
        }
    }
}
