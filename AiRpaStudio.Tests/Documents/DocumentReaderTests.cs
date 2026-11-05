using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Agent.Core.Services.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
// Оба пространства имён (Wordprocessing и Spreadsheet) объявляют Run/Text/Table — нужен явный выбор:
// в docx-фикстуре используются типы Wordprocessing.
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using Table = DocumentFormat.OpenXml.Wordprocessing.Table;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Documents
{
    /// <summary>
    /// <c>TZ-02</c>: извлечение текста из <c>docx</c>, <c>xlsx</c>, <c>pdf</c> и текстовых файлов одним набором
    /// reader'ов. Фикстуры строятся в тесте (OpenXml SDK и минимальный PDF), поэтому в репозиторий не попадают
    /// бинарные файлы, а форматы проверяются на настоящих контейнерах.
    /// </summary>
    [TestFixture]
    public class DocumentReaderTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "doc-readers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------
        // Текст
        // ------------------------------------------------------------------

        [Test]
        public void PlainText_Utf8WithBom_IsDecoded()
        {
            string path = Path.Combine(_root, "tz.md");
            File.WriteAllText(path, "# ТЗ\n\nЦель: отчёт.", new UTF8Encoding(true));

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Text, Does.Contain("Цель: отчёт"));
                Assert.That(result.Reason, Does.Contain("utf-8"));
            });
        }

        [Test]
        public void PlainText_Cp1251_IsDecodedInsteadOfMojibake()
        {
            string path = Path.Combine(_root, "tz.txt");
            byte[] bytes = Encoding.GetEncoding(1251).GetBytes("Цель: собрать отчёт по складу");
            File.WriteAllBytes(path, bytes);

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Text, Does.Contain("Цель: собрать отчёт по складу"));
                Assert.That(result.Reason, Does.Contain("cp1251"));
            });
        }

        [Test]
        public void FileWithoutExtension_IsReadAsText()
        {
            string path = Path.Combine(_root, "spec");
            File.WriteAllText(path, "просто текст", new UTF8Encoding(false));

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Text, Is.EqualTo("просто текст"));
            });
        }

        [Test]
        public void UnsupportedExtension_IsNamedNotFailed()
        {
            string path = Path.Combine(_root, "image.psd");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(DocumentTextStatus.Unsupported));
                Assert.That(result.Reason, Does.Contain("поддерживаются"));
            });
        }

        // ------------------------------------------------------------------
        // docx
        // ------------------------------------------------------------------

        [Test]
        public void Docx_ReadsHeadingsParagraphsAndTables()
        {
            string path = Path.Combine(_root, "tz.docx");
            WriteDocx(path);

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Format, Is.EqualTo("docx"));
                Assert.That(result.Text, Does.Contain("# Цель"));
                Assert.That(result.Text, Does.Contain("Открыть файл склада"));
                Assert.That(result.Text, Does.Contain("| ID | Имя |"));
                Assert.That(result.SheetCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Docx_BrokenFile_ReturnsReasonInsteadOfThrowing()
        {
            string path = Path.Combine(_root, "broken.docx");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("это не docx"));

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Status, Is.EqualTo(DocumentTextStatus.Failed));
                Assert.That(result.Format, Is.EqualTo("docx"));
            });
        }

        // ------------------------------------------------------------------
        // xlsx
        // ------------------------------------------------------------------

        [Test]
        public void Xlsx_ReadsSheetsAsMarkdownTables()
        {
            string path = Path.Combine(_root, "data.xlsx");
            WriteXlsx(path);

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Text, Does.Contain("## Данные"));
                Assert.That(result.Text, Does.Contain("| ID | Имя |"));
                Assert.That(result.Text, Does.Contain("| S1 | Открыть |"));
                Assert.That(result.SheetCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Xlsx_RowLimit_IsReportedOutLoud()
        {
            string path = Path.Combine(_root, "many.xlsx");
            WriteXlsx(path, rows: 30);

            var result = DocumentTextReaders.Read(path, new DocumentTextReadOptions
            {
                MaxTableRows = 5,
                MaxTableColumns = 10
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Text, Does.Not.Contain("строка 30"));
                Assert.That(result.Reason, Does.Contain("усечён до 5 строк"));
            });
        }

        // ------------------------------------------------------------------
        // pdf
        // ------------------------------------------------------------------

        [Test]
        public void Pdf_TextLayer_IsExtracted()
        {
            string path = Path.Combine(_root, "tz.pdf");
            WritePdf(path, "BT /F1 12 Tf 72 720 Td (Spec goal: build report) Tj ET", flate: false);

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Text, Does.Contain("Spec goal: build report"));
                Assert.That(result.PageCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Pdf_FlateCompressedStream_IsInflated()
        {
            string path = Path.Combine(_root, "flate.pdf");
            WritePdf(path, "BT /F1 12 Tf 72 720 Td (Собрать отчёт) Tj ET", flate: true);

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Describe());
                Assert.That(result.Text, Does.Contain("Собрать отчёт"));
            });
        }

        [Test]
        public void Pdf_WithoutTextLayer_IsNamedAsScan()
        {
            string path = Path.Combine(_root, "scan.pdf");
            WritePdf(path, "0 0 0 rg 0 0 100 100 re f", flate: false);

            var result = DocumentTextReaders.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Status, Is.EqualTo(DocumentTextStatus.NoTextLayer));
                Assert.That(result.Reason, Does.Contain("изображениями"));
            });
        }

        // ------------------------------------------------------------------
        // Общие правила
        // ------------------------------------------------------------------

        [Test]
        public void MissingFile_ReturnsReason()
        {
            var result = DocumentTextReaders.Read(Path.Combine(_root, "нет.docx"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(DocumentTextStatus.Failed));
                Assert.That(result.Reason, Does.Contain("не найден"));
            });
        }

        [Test]
        public void LongText_IsTruncatedWithFlag()
        {
            string path = Path.Combine(_root, "long.txt");
            File.WriteAllText(path, new string('a', 500), new UTF8Encoding(false));

            var result = DocumentTextReaders.Read(path, new DocumentTextReadOptions { MaxChars = 100 });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True);
                Assert.That(result.Truncated, Is.True);
                Assert.That(result.Length, Is.EqualTo(100));
                Assert.That(result.Reason, Does.Contain("усечён"));
            });
        }

        // ------------------------------------------------------------------
        // Фикстуры
        // ------------------------------------------------------------------

        private static void WriteDocx(string path)
        {
            using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = document.AddMainDocumentPart();
                main.Document = new Document();
                var body = new Body();

                body.Append(new Paragraph(
                    new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                    new Run(new Text("Цель"))));

                body.Append(new Paragraph(new Run(new Text("Открыть файл склада"))));

                body.Append(new Paragraph(
                    new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 })),
                    new Run(new Text("проверить остатки"))));

                var table = new Table(
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("ID")))),
                        new TableCell(new Paragraph(new Run(new Text("Имя"))))),
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("S1")))),
                        new TableCell(new Paragraph(new Run(new Text("Открыть"))))));
                body.Append(table);

                main.Document.Append(body);
                main.Document.Save();
            }
        }

        private static void WriteXlsx(string path, int rows = 2)
        {
            using (var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();

                var header = new Row { RowIndex = 1 };
                header.Append(Cell("A1", "ID"));
                header.Append(Cell("B1", "Имя"));
                sheetData.Append(header);

                var bodyRow = new Row { RowIndex = 2 };
                bodyRow.Append(Cell("A2", "S1"));
                bodyRow.Append(Cell("B2", "Открыть"));
                sheetData.Append(bodyRow);

                for (int i = 3; i <= rows; i++)
                {
                    var row = new Row { RowIndex = (uint)i };
                    row.Append(Cell("A" + i, "S" + i));
                    row.Append(Cell("B" + i, "строка " + i));
                    sheetData.Append(row);
                }

                worksheetPart.Worksheet = new Worksheet(sheetData);

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Данные"
                });

                workbookPart.Workbook.Save();
            }
        }

        private static Cell Cell(string reference, string text)
        {
            return new Cell
            {
                CellReference = reference,
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(text))
            };
        }

        private static void WritePdf(string path, string content, bool flate)
        {
            byte[] contentBytes = Encoding.GetEncoding(1251).GetBytes(content);
            string filter = string.Empty;

            if (flate)
            {
                contentBytes = ZlibCompress(contentBytes);
                filter = " /Filter /FlateDecode";
            }

            var pdf = new StringBuilder();
            pdf.Append("%PDF-1.4\n");
            pdf.Append("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
            pdf.Append("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n");
            pdf.Append("3 0 obj << /Type /Page /Parent 2 0 R /Contents 4 0 R >> endobj\n");
            pdf.Append("4 0 obj << /Length ").Append(contentBytes.Length).Append(filter).Append(" >>\nstream\n");
            pdf.Append(Latin1(contentBytes));
            pdf.Append("\nendstream\nendobj\n");
            pdf.Append("trailer << /Root 1 0 R >>\n%%EOF\n");

            File.WriteAllBytes(path, Latin1Bytes(pdf.ToString()));
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                uint a = 1, b = 0;
                foreach (byte value in data)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }

                uint adler = (b << 16) | a;
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);

                return output.ToArray();
            }
        }

        private static string Latin1(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            return new string(chars);
        }

        private static byte[] Latin1Bytes(string text)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                bytes[i] = (byte)(text[i] & 0xFF);
            }

            return bytes;
        }
    }
}
