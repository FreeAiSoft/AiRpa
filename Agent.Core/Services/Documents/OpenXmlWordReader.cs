using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Agent.Core.Services.Documents
{
    /// <summary>
    /// Reader <c>docx</c> (<c>TZ-02</c>): параграфы и заголовки → markdown, таблицы → markdown-таблицы,
    /// маркированные и нумерованные списки → «- ». Раньше <c>docx</c> читался как текст и в промпт попадал
    /// zip-мусор, поэтому настоящее ТЗ до агента не доходило.
    /// </summary>
    public sealed class OpenXmlWordReader : IDocumentTextReader
    {
        private static readonly string[] SupportedExtensions = { ".docx" };

        public IReadOnlyList<string> Extensions => SupportedExtensions;

        public DocumentTextResult Read(string path, DocumentTextReadOptions options)
        {
            var settings = options ?? DocumentTextReadOptions.Default;
            try
            {
                using (var document = WordprocessingDocument.Open(path, false))
                {
                    var body = document.MainDocumentPart?.Document?.Body;
                    if (body == null)
                    {
                        return DocumentTextResult.Failure("docx", "в документе нет основного текста");
                    }

                    var notes = new List<string>();
                    var text = new StringBuilder();
                    int images = document.MainDocumentPart.ImageParts?.Count() ?? 0;
                    int pageBreaks = 0;
                    int tables = 0;

                    foreach (var element in body.ChildElements)
                    {
                        if (element is Paragraph paragraph)
                        {
                            AppendParagraph(text, paragraph, notes);
                            pageBreaks += CountPageBreaks(paragraph);
                        }
                        else if (element is Table table)
                        {
                            tables++;
                            AppendTable(text, table, settings, notes);
                        }
                        else
                        {
                            AppendNested(text, element, settings, notes);
                        }
                    }

                    if (text.Length == 0)
                    {
                        return DocumentTextResult.NoTextLayer("docx",
                            images > 0
                                ? "в документе только изображения (" + images + ") — приложите страницы картинками"
                                : "документ не содержит текста");
                    }

                    var result = DocumentTextResult.Success("docx")
                        .WithCounts(pageBreaks + 1, tables, images)
                        .WithText(text.ToString().Trim(), settings);

                    foreach (string note in notes.Distinct())
                    {
                        result.WithReason(note);
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                return DocumentTextResult.Failure("docx", "документ не прочитан: " + ex.Message);
            }
        }

        private static void AppendNested(StringBuilder text, OpenXmlElement element, DocumentTextReadOptions settings, List<string> notes)
        {
            // Содержимое, завёрнутое в блоки содержимого (SdtBlock и подобное), читаем по вложенным элементам.
            foreach (var child in element.ChildElements)
            {
                if (child is Paragraph paragraph)
                {
                    AppendParagraph(text, paragraph, notes);
                }
                else if (child is Table table)
                {
                    AppendTable(text, table, settings, notes);
                }
                else
                {
                    AppendNested(text, child, settings, notes);
                }
            }
        }

        private static void AppendParagraph(StringBuilder text, Paragraph paragraph, List<string> notes)
        {
            string content = ReadParagraphText(paragraph);
            if (content.Length == 0)
            {
                return;
            }

            int level = HeadingLevel(paragraph);
            bool listItem = paragraph.ParagraphProperties?.NumberingProperties != null;

            if (level > 0)
            {
                text.Append(new string('#', Math.Min(level, 6))).Append(' ').Append(content).AppendLine().AppendLine();
            }
            else if (listItem)
            {
                text.Append("- ").Append(content).AppendLine();
            }
            else
            {
                text.Append(content).AppendLine().AppendLine();
            }

            if (notes == null)
            {
                return;
            }
        }

        private static int HeadingLevel(Paragraph paragraph)
        {
            string styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value
                             ?? paragraph.ParagraphProperties?.ParagraphStyleId?.InnerText;
            if (string.IsNullOrWhiteSpace(styleId))
            {
                return 0;
            }

            styleId = styleId.Trim();
            bool looksLikeHeading =
                styleId.IndexOf("heading", StringComparison.OrdinalIgnoreCase) >= 0 ||
                styleId.IndexOf("заголовок", StringComparison.OrdinalIgnoreCase) >= 0 ||
                styleId.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeHeading)
            {
                return 0;
            }

            var digits = new string(styleId.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int level) && level > 0 ? level : 1;
        }

        private static string ReadParagraphText(Paragraph paragraph)
        {
            var sb = new StringBuilder();
            foreach (var run in paragraph.Descendants<Run>())
            {
                foreach (var child in run.ChildElements)
                {
                    if (child is Text value)
                    {
                        sb.Append(value.Text);
                    }
                    else if (child is TabChar)
                    {
                        sb.Append(' ');
                    }
                    else if (child is Break)
                    {
                        sb.Append(' ');
                    }
                }
            }

            return sb.ToString().Trim();
        }

        private static int CountPageBreaks(Paragraph paragraph)
        {
            return paragraph.Descendants<Break>()
                .Count(b => b.Type != null && b.Type.Value == BreakValues.Page);
        }

        private static void AppendTable(StringBuilder text, Table table, DocumentTextReadOptions settings, List<string> notes)
        {
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count == 0)
            {
                return;
            }

            int maxRows = settings.MaxTableRows > 0 ? settings.MaxTableRows : int.MaxValue;
            int maxColumns = settings.MaxTableColumns > 0 ? settings.MaxTableColumns : int.MaxValue;

            var rendered = new List<List<string>>();
            bool columnsCut = false;
            foreach (var row in rows.Take(maxRows))
            {
                var cells = row.Elements<TableCell>()
                    .Select(cell => string.Join(" ", cell.Elements<Paragraph>()
                        .Select(ReadParagraphText)
                        .Where(p => p.Length > 0)))
                    .ToList();

                if (cells.Count > maxColumns)
                {
                    cells = cells.Take(maxColumns).ToList();
                    columnsCut = true;
                }

                rendered.Add(cells.Select(Cell).ToList());
            }

            if (rows.Count > maxRows)
            {
                notes?.Add("таблица усечена до " + maxRows + " строк");
            }

            if (columnsCut)
            {
                notes?.Add("в таблице показаны первые " + maxColumns + " столбцов");
            }

            if (rendered.Count == 0)
            {
                return;
            }

            int width = rendered.Max(r => r.Count);
            if (width == 0)
            {
                return;
            }

            text.AppendLine(BuildRow(rendered[0], width));
            text.AppendLine(BuildRow(Enumerable.Repeat("---", width).ToList(), width));
            for (int i = 1; i < rendered.Count; i++)
            {
                text.AppendLine(BuildRow(rendered[i], width));
            }

            text.AppendLine();
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

        private static string Cell(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
