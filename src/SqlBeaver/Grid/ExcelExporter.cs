using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SqlBeaver.Scripting;

namespace SqlBeaver.Grid
{
    /// <summary>
    /// Exporta um GridData para .xlsx (DocumentFormat.OpenXml, MIT) em %TEMP% e
    /// retorna o caminho. Tipos OpenXml apenas dentro dos corpos de método —
    /// nunca em assinaturas (restrição de carga MEF do SSMS).
    /// </summary>
    internal static class ExcelExporter
    {
        public static string ExportToTempFile(GridData data)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "SqlBeaver_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".xlsx");
            Export(data, path);
            return path;
        }

        private static void Export(GridData data, string path)
        {
            using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(
                path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();

                var worksheetPart = workbookPart.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
                var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
                worksheetPart.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(sheetData);

                var sheets = workbookPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
                sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = "Resultados",
                });

                // cabeçalho
                var headerRow = new DocumentFormat.OpenXml.Spreadsheet.Row();
                foreach (GridColumn column in data.Columns)
                    headerRow.Append(TextCell(column.Name));
                sheetData.Append(headerRow);

                // dados
                foreach (string[] gridRow in data.Rows)
                {
                    var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
                    for (int c = 0; c < data.Columns.Count; c++)
                        row.Append(DataCell(gridRow[c], data.Columns[c].ClrType));
                    sheetData.Append(row);
                }

                workbookPart.Workbook.Save();
            }
        }

        private static DocumentFormat.OpenXml.Spreadsheet.Cell DataCell(string display, Type clrType)
        {
            if (display == null || display == "NULL")
                return TextCell(string.Empty);

            // números como número de verdade (CPF/CNPJ são varchar → continuam texto e preservam zeros)
            if (SqlNumberNormalizer.IsNumericClrType(clrType))
            {
                string normalized = SqlNumberNormalizer.TryNormalize(display);
                if (normalized != null)
                {
                    return new DocumentFormat.OpenXml.Spreadsheet.Cell
                    {
                        DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.Number,
                        CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(normalized),
                    };
                }
            }

            return TextCell(display.Length > 32767 ? display.Substring(0, 32767) : display);
        }

        private static DocumentFormat.OpenXml.Spreadsheet.Cell TextCell(string text)
            => new DocumentFormat.OpenXml.Spreadsheet.Cell
            {
                DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString,
                InlineString = new DocumentFormat.OpenXml.Spreadsheet.InlineString(
                    new DocumentFormat.OpenXml.Spreadsheet.Text(text ?? string.Empty)
                    {
                        Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve,
                    }),
            };
    }
}
