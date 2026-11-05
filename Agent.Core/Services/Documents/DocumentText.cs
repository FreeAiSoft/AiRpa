using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Agent.Core.Services.Documents
{
    /// <summary>Чем закончилось извлечение текста из документа (<c>TZ-02</c>).</summary>
    public enum DocumentTextStatus
    {
        /// <summary>Текст получен.</summary>
        Ok,

        /// <summary>Формат понятен, но текстового слоя нет (скан-PDF): нужны изображения страниц.</summary>
        NoTextLayer,

        /// <summary>Формат не поддерживается ни одним reader'ом.</summary>
        Unsupported,

        /// <summary>Файл не прочитан: нет файла, нет прав, битый контейнер. Причина — в <see cref="DocumentTextResult.Reason"/>.</summary>
        Failed
    }

    /// <summary>
    /// Лимиты извлечения. Значения по умолчанию совпадают с правилами волны 8:
    /// 200 000 символов текста на версию ТЗ и 200 строк/30 столбцов таблицы (иначе <c>xlsx</c> на 50 000 строк
    /// забивает контекст агента целиком).
    /// </summary>
    public sealed class DocumentTextReadOptions
    {
        /// <summary>Предел извлечённого текста в символах.</summary>
        public int MaxChars { get; set; } = 200000;

        /// <summary>Предел строк таблицы (на лист/таблицу).</summary>
        public int MaxTableRows { get; set; } = 200;

        /// <summary>Предел столбцов таблицы.</summary>
        public int MaxTableColumns { get; set; } = 30;

        /// <summary>Предел размера исходного файла (байты) — проверяется до чтения.</summary>
        public long MaxSourceBytes { get; set; } = 20L * 1024 * 1024;

        /// <summary>Лимиты по умолчанию.</summary>
        public static DocumentTextReadOptions Default => new DocumentTextReadOptions();

        /// <summary>Копия с другим пределом символов.</summary>
        public DocumentTextReadOptions WithMaxChars(int maxChars)
        {
            var copy = (DocumentTextReadOptions)MemberwiseClone();
            copy.MaxChars = maxChars > 0 ? maxChars : MaxChars;
            return copy;
        }
    }

    /// <summary>
    /// Итог извлечения текста. Метаданные (`страниц`, `листов`, `картинок`) нужны, чтобы агент видел,
    /// что именно он получил, а не догадывался по «пустому» тексту.
    /// </summary>
    public sealed class DocumentTextResult
    {
        private DocumentTextResult(DocumentTextStatus status)
        {
            Status = status;
        }

        /// <summary>Состояние извлечения.</summary>
        public DocumentTextStatus Status { get; private set; }

        /// <summary>Извлечённый текст (markdown-совместимый).</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>Формат: <c>md</c>, <c>docx</c>, <c>xlsx</c>, <c>pdf</c>, …</summary>
        public string Format { get; private set; } = string.Empty;

        /// <summary>Почему текст не получен или что усечено. <c>null</c> — замечаний нет.</summary>
        public string Reason { get; private set; }

        /// <summary>Страниц (pdf, docx — по разрывам страниц); 0 — неизвестно.</summary>
        public int PageCount { get; private set; }

        /// <summary>Листов (xlsx) или таблиц (docx); 0 — нет.</summary>
        public int SheetCount { get; private set; }

        /// <summary>Картинок найдено в документе.</summary>
        public int ImageCount { get; private set; }

        /// <summary>Текст усечён по <see cref="DocumentTextReadOptions.MaxChars"/>.</summary>
        public bool Truncated { get; private set; }

        /// <summary>Символов в <see cref="Text"/>.</summary>
        public int Length => Text?.Length ?? 0;

        /// <summary>Текст получен.</summary>
        public bool Ok => Status == DocumentTextStatus.Ok;

        public DocumentTextResult WithFormat(string format)
        {
            Format = (format ?? string.Empty).TrimStart('.').ToLowerInvariant();
            return this;
        }

        public DocumentTextResult WithReason(string reason)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Reason = reason;
            }

            return this;
        }

        public DocumentTextResult WithCounts(int pageCount, int sheetCount, int imageCount)
        {
            PageCount = Math.Max(0, pageCount);
            SheetCount = Math.Max(0, sheetCount);
            ImageCount = Math.Max(0, imageCount);
            return this;
        }

        /// <summary>Готовый текст; усечение по лимиту помечается явно и не теряется молча.</summary>
        public DocumentTextResult WithText(string text, DocumentTextReadOptions options)
        {
            string value = text ?? string.Empty;
            int limit = (options ?? DocumentTextReadOptions.Default).MaxChars;
            if (limit > 0 && value.Length > limit)
            {
                value = value.Substring(0, limit);
                Truncated = true;
                Reason = AppendReason(Reason,
                    "текст усечён до " + limit + " символов — импортируйте ТЗ по разделам");
            }

            Text = value;
            return this;
        }

        /// <summary>Успешный результат.</summary>
        public static DocumentTextResult Success(string format)
        {
            return new DocumentTextResult(DocumentTextStatus.Ok).WithFormat(format);
        }

        /// <summary>Формат есть, текстового слоя нет.</summary>
        public static DocumentTextResult NoTextLayer(string format, string reason)
        {
            return new DocumentTextResult(DocumentTextStatus.NoTextLayer).WithFormat(format).WithReason(reason);
        }

        /// <summary>Формат не поддерживается.</summary>
        public static DocumentTextResult Unsupported(string format, string reason)
        {
            return new DocumentTextResult(DocumentTextStatus.Unsupported).WithFormat(format).WithReason(reason);
        }

        /// <summary>Файл не прочитан; причина обязательна.</summary>
        public static DocumentTextResult Failure(string format, string reason)
        {
            return new DocumentTextResult(DocumentTextStatus.Failed).WithFormat(format).WithReason(reason);
        }

        /// <summary>Человекочитаемое состояние — для ответов инструментов.</summary>
        public string Describe()
        {
            switch (Status)
            {
                case DocumentTextStatus.Ok:
                    var text = new StringBuilder();
                    text.Append("формат: ").Append(Format.Length == 0 ? "?" : Format)
                        .Append(", символов: ").Append(Length);
                    if (PageCount > 0) text.Append(", страниц: ").Append(PageCount);
                    if (SheetCount > 0) text.Append(", листов/таблиц: ").Append(SheetCount);
                    if (ImageCount > 0) text.Append(", картинок: ").Append(ImageCount);
                    if (Truncated) text.Append(", усечено: да");
                    if (!string.IsNullOrWhiteSpace(Reason)) text.Append(" (").Append(Reason).Append(')');
                    return text.ToString();

                case DocumentTextStatus.NoTextLayer:
                    return "текстового слоя нет (" + Format + "): " + Reason;

                case DocumentTextStatus.Unsupported:
                    return "формат не поддерживается (" + Format + "): " + Reason;

                default:
                    return "файл не прочитан (" + Format + "): " + Reason;
            }
        }

        private static string AppendReason(string current, string extra)
        {
            if (string.IsNullOrWhiteSpace(extra))
            {
                return current;
            }

            return string.IsNullOrWhiteSpace(current) ? extra : current + "; " + extra;
        }
    }

    /// <summary>
    /// Один reader формата (<c>TZ-02</c>). Readers не бросают исключений наружу: любая проблема — это
    /// <see cref="DocumentTextResult"/> с причиной, иначе чат падал бы на первом битом файле.
    /// </summary>
    public interface IDocumentTextReader
    {
        /// <summary>Расширения, которые reader обслуживает (с точкой, в нижнем регистре).</summary>
        IReadOnlyList<string> Extensions { get; }

        /// <summary>Читает документ. Исключений не бросает.</summary>
        DocumentTextResult Read(string path, DocumentTextReadOptions options);
    }

    /// <summary>
    /// Единая точка извлечения текста (<c>TZ-02</c>, шаг 5): одна и та же логика для <c>spec_import</c>,
    /// вложений чата и инструмента <c>read_files</c>. Вторых правил чтения одного формата не заводится.
    /// </summary>
    public static class DocumentTextReaders
    {
        private static readonly object Sync = new object();
        private static IReadOnlyList<IDocumentTextReader> _readers;

        /// <summary>Readers по умолчанию (порядок важен: первый подходящий по расширению).</summary>
        public static IReadOnlyList<IDocumentTextReader> Default
        {
            get
            {
                lock (Sync)
                {
                    return _readers ?? (_readers = new IDocumentTextReader[]
                    {
                        new OpenXmlWordReader(),
                        new OpenXmlExcelReader(),
                        new PdfTextReader(),
                        new PlainTextReader()
                    });
                }
            }
        }

        /// <summary>Подменяет набор readers (тесты и хосты с другим набором форматов).</summary>
        public static void Override(IReadOnlyList<IDocumentTextReader> readers)
        {
            lock (Sync)
            {
                _readers = readers;
            }
        }

        /// <summary>Сбрасывает набор readers к набору по умолчанию.</summary>
        public static void Reset()
        {
            lock (Sync)
            {
                _readers = null;
            }
        }

        /// <summary>Формат (расширение без точки, нижний регистр) файла.</summary>
        public static string FormatOf(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            return string.IsNullOrEmpty(extension) ? string.Empty : extension.TrimStart('.').ToLowerInvariant();
        }

        /// <summary>Формат обслуживается набором readers по умолчанию.</summary>
        public static bool CanRead(string path)
        {
            return FindReader(path) != null;
        }

        /// <summary>
        /// Извлекает текст. Файл больше лимита не читается вовсе; отсутствие файла — причина, а не исключение.
        /// </summary>
        public static DocumentTextResult Read(string path, DocumentTextReadOptions options = null)
        {
            var settings = options ?? DocumentTextReadOptions.Default;
            string format = FormatOf(path);

            if (string.IsNullOrWhiteSpace(path))
            {
                return DocumentTextResult.Failure(format, "пустой путь к документу");
            }

            var reader = FindReader(path);
            if (reader == null)
            {
                return DocumentTextResult.Unsupported(format,
                    "поддерживаются: " + string.Join(", ", Default.SelectMany(r => r.Extensions).Distinct().OrderBy(e => e)));
            }

            try
            {
                if (!File.Exists(path))
                {
                    return DocumentTextResult.Failure(format, "файл не найден: " + path);
                }

                var info = new FileInfo(path);
                if (settings.MaxSourceBytes > 0 && info.Length > settings.MaxSourceBytes)
                {
                    return DocumentTextResult.Failure(format,
                        "файл больше лимита " + settings.MaxSourceBytes + " байт (размер " + info.Length + ")");
                }

                return reader.Read(path, settings) ?? DocumentTextResult.Failure(format, "reader не вернул результат");
            }
            catch (Exception ex)
            {
                return DocumentTextResult.Failure(format, "ошибка чтения: " + ex.Message);
            }
        }

        /// <summary>Извлекает текст из готовой строки (ТЗ, вставленное в чат текстом).</summary>
        public static DocumentTextResult ReadText(string text, string displayName = null, DocumentTextReadOptions options = null)
        {
            var settings = options ?? DocumentTextReadOptions.Default;
            string format = string.IsNullOrWhiteSpace(displayName) ? "txt" : FormatOf(displayName);
            if (format.Length == 0)
            {
                format = "txt";
            }

            return DocumentTextResult.Success(format).WithText(text, settings);
        }

        /// <summary>
        /// Формат требует извлечения текста и не читается как текст напрямую (<c>docx</c>, <c>xlsx</c>, <c>pdf</c>).
        /// Нужен инструменту <c>read_files</c>: <c>.xaml</c> и прочие текстовые форматы идут прежним путём
        /// (с шапкой метаданных XAML), а контейнеры — через reader.
        /// </summary>
        public static bool IsBinaryDocument(string path)
        {
            var reader = FindReader(path);
            return reader != null && !(reader is PlainTextReader);
        }

        private static IDocumentTextReader FindReader(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            if (string.IsNullOrEmpty(extension))
            {
                // Файл без расширения читаем как текст: это самый частый вид «просто ТЗ в файле».
                extension = ".txt";
            }

            extension = extension.ToLowerInvariant();
            return Default.FirstOrDefault(reader => reader.Extensions.Any(e =>
                string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
