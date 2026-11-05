using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Agent.Core.Services.Documents
{
    /// <summary>
    /// Reader <c>xlsx</c> (<c>TZ-02</c>): каждый лист → markdown-таблица с заголовком листа.
    /// Строки и столбцы ограничены явно, а усечение называется в ответе — «молчаливого» обрезания нет.
    /// </summary>
    public sealed class OpenXmlExcelReader : IDocumentTextReader
    {
        private static readonly string[] SupportedExtensions = { ".xlsx", ".xlsm" };

        public IReadOnlyList<string> Extensions => SupportedExtensions;

        public DocumentTextResult Read(string path, DocumentTextReadOptions options)
        {
            var settings = options ?? DocumentTextReadOptions.Default;
            try
            {
                using (var document = SpreadsheetDocument.Open(path, false))
                {
                    var workbookPart = document.WorkbookPart;
                    var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList();
                    if (sheets == null || sheets.Count == 0)
                    {
                        return DocumentTextResult.NoTextLayer("xlsx", "в книге нет листов");
                    }

                    var sharedStrings = ReadSharedStrings(workbookPart);
                    var notes = new List<string>();
                    var text = new StringBuilder();
                    int usedSheets = 0;
                    int totalRows = 0;

                    foreach (var sheet in sheets)
                    {
                        WorksheetPart worksheetPart;
                        try
                        {
                            worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
                        }
                        catch (Exception)
                        {
                            notes.Add("лист «" + sheet.Name + "» не прочитан");
                            continue;
                        }

                        var rows = worksheetPart.Worksheet?.GetFirstChild<SheetData>()?.Elements<Row>().ToList();
                        if (rows == null || rows.Count == 0)
                        {
                            continue;
                        }

                        usedSheets++;
                        int maxRows = settings.MaxTableRows > 0 ? settings.MaxTableRows : int.MaxValue;
                        int maxColumns = settings.MaxTableColumns > 0 ? settings.MaxTableColumns : int.MaxValue;

                        var rendered = new List<List<string>>();
                        bool columnsCut = false;
                        foreach (var row in rows.Take(maxRows))
                        {
                            var cells = new SortedDictionary<int, string>();
                            foreach (var cell in row.Elements<Cell>())
                            {
                                int index = ColumnIndex(cell.CellReference?.Value);
                                if (index >= maxColumns)
                                {
                                    columnsCut = true;
                                    continue;
                                }

                                string value = ReadCell(cell, sharedStrings);
                                if (!string.IsNullOrEmpty(value))
                                {
                                    cells[index] = value;
                                }
                            }

                            totalRows++;
                            int width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
                            var line = new List<string>();
                            for (int i = 0; i < width; i++)
                            {
                                line.Add(cells.TryGetValue(i, out string value) ? value : string.Empty);
                            }

                            rendered.Add(line);
                        }

                        int realWidth = rendered.Count == 0 ? 0 : rendered.Max(r => r.Count);
                        if (realWidth == 0)
                        {
                            continue;
                        }

                        text.Append("## ").Append(string.IsNullOrWhiteSpace(sheet.Name?.Value)
                            ? "лист " + usedSheets
                            : sheet.Name.Value).AppendLine().AppendLine();

                        text.AppendLine(BuildRow(rendered[0], realWidth));
                        text.AppendLine(BuildRow(Enumerable.Repeat("---", realWidth).ToList(), realWidth));
                        for (int i = 1; i < rendered.Count; i++)
                        {
                            text.AppendLine(BuildRow(rendered[i], realWidth));
                        }

                        text.AppendLine();

                        if (rows.Count > maxRows)
                        {
                            notes.Add("лист «" + sheet.Name + "» усечён до " + maxRows + " строк из " + rows.Count);
                        }

                        if (columnsCut)
                        {
                            notes.Add("лист «" + sheet.Name + "»: показаны первые " + maxColumns + " столбцов");
                        }
                    }

                    if (text.Length == 0)
                    {
                        return DocumentTextResult.NoTextLayer("xlsx", "листы не содержат заполненных ячеек");
                    }

                    var result = DocumentTextResult.Success("xlsx")
                        .WithCounts(0, usedSheets, 0)
                        .WithText(text.ToString().Trim(), settings);

                    foreach (string note in notes.Distinct())
                    {
                        result.WithReason(note);
                    }

                    if (totalRows > 0 && result.Length == 0)
                    {
                        result.WithReason("строки прочитаны, но текст пуст");
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                return DocumentTextResult.Failure("xlsx", "книга не прочитана: " + ex.Message);
            }
        }

        private static List<string> ReadSharedStrings(WorkbookPart workbookPart)
        {
            var part = workbookPart?.SharedStringTablePart;
            if (part?.SharedStringTable == null)
            {
                return new List<string>();
            }

            return part.SharedStringTable.Elements<SharedStringItem>()
                .Select(item => item.InnerText ?? string.Empty)
                .ToList();
        }

        private static string ReadCell(Cell cell, IReadOnlyList<string> sharedStrings)
        {
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                if (int.TryParse(cell.CellValue?.InnerText, out int index)
                    && index >= 0 && index < sharedStrings.Count)
                {
                    return Clean(sharedStrings[index]);
                }

                return string.Empty;
            }

            if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
            {
                return Clean(cell.InlineString?.InnerText);
            }

            return Clean(cell.CellValue?.InnerText);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static int ColumnIndex(string cellReference)
        {
            if (string.IsNullOrEmpty(cellReference))
            {
                return 0;
            }

            int index = 0;
            foreach (char symbol in cellReference)
            {
                if (symbol < 'A' || symbol > 'Z')
                {
                    break;
                }

                index = index * 26 + (symbol - 'A' + 1);
            }

            return Math.Max(0, index - 1);
        }

        private static string BuildRow(IReadOnlyList<string> cells, int width)
        {
            var sb = new StringBuilder("|");
            for (int i = 0; i < width; i++)
            {
                sb.Append(' ').Append(i < cells.Count ? cells[i] : string.Empty).Append(" |");
            }

            return sb.ToString();
        }
    }
}
