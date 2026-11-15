using System;
using System.Activities.Presentation;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace RPA.Core
{
    /// <summary>
    /// Снимок ошибки загрузки документа в конструкторе WF: путь, тип исключения, текст,
    /// точная позиция в XAML и контекст для отправки в чат.
    /// <para>
    /// Позиция берётся из <see cref="XmlException"/> цепочки исключений, а не из внешнего
    /// <c>XamlObjectWriterException.LineNumber/LinePosition</c>: объектный writer показывает свою
    /// позицию, а не позицию ошибки XML-парсера (см. карточку «отправка ошибки дизайнера в чат»).
    /// </para>
    /// </summary>
    public sealed class DesignerErrorSnapshot
    {
        private const int MaxXamlCharsInPrompt = 20000;
        private const int MaxStackTraceCharsInPrompt = 4000;

        internal DesignerErrorSnapshot(
            string filePath,
            string exceptionType,
            string message,
            IReadOnlyList<string> innerMessages,
            int lineNumber,
            int linePosition,
            bool hasPosition,
            string offendingLine,
            string xamlText,
            string stackTrace,
            DateTime capturedAt)
        {
            FilePath = filePath ?? string.Empty;
            ExceptionType = exceptionType ?? string.Empty;
            Message = message ?? string.Empty;
            InnerMessages = innerMessages ?? new string[0];
            LineNumber = lineNumber;
            LinePosition = linePosition;
            HasPosition = hasPosition;
            OffendingLine = offendingLine;
            XamlText = xamlText;
            StackTrace = stackTrace;
            CapturedAt = capturedAt;
        }

        /// <summary>Путь к XAML-файлу, при загрузке которого возникла ошибка.</summary>
        public string FilePath { get; }

        /// <summary>Тип внешнего исключения (<c>System.Xaml.XamlObjectWriterException</c> и т.п.).</summary>
        public string ExceptionType { get; }

        /// <summary>Полный текст внешнего исключения (обычно уже содержит локализованную позицию).</summary>
        public string Message { get; }

        /// <summary>Сообщения вложенных исключений (там обычно и находится конкретная причина).</summary>
        public IReadOnlyList<string> InnerMessages { get; }

        /// <summary>Номер строки в XAML (1-based); 0 — позиция не определена.</summary>
        public int LineNumber { get; }

        /// <summary>Колонка в строке XAML (1-based); 0 — позиция не определена.</summary>
        public int LinePosition { get; }

        /// <summary>Позиция достоверна (найдена из <see cref="XmlException"/>).</summary>
        public bool HasPosition { get; }

        /// <summary>Строка XAML, на которой ошибка; <c>null</c>, если позиция или текст недоступны.</summary>
        public string OffendingLine { get; }

        /// <summary>Полный текст XAML (для контекста агента).</summary>
        public string XamlText { get; }

        /// <summary>Стек внешнего исключения.</summary>
        public string StackTrace { get; }

        /// <summary>Время фиксации снимка.</summary>
        public DateTime CapturedAt { get; }

        /// <summary>Готовый промпт для отправки в чат (агент получает всё нужное для анализа).</summary>
        public string BuildPromptText()
        {
            var text = new StringBuilder();
            text.AppendLine("Проанализируй ошибку конструктора рабочего процесса.");
            text.AppendLine("Файл: " + (string.IsNullOrWhiteSpace(FilePath) ? "<не задан>" : FilePath));
            text.AppendLine("Ошибка: " + ExceptionType + ": " + Message);

            if (HasPosition)
            {
                text.AppendLine($"Позиция: строка {LineNumber}, колонка {LinePosition}");
            }

            if (!string.IsNullOrWhiteSpace(OffendingLine))
            {
                text.AppendLine("Строка с ошибкой: " + OffendingLine);
            }

            if (InnerMessages.Count > 0)
            {
                text.AppendLine("Внутренние ошибки:");
                foreach (string inner in InnerMessages)
                {
                    text.AppendLine("- " + inner);
                }
            }

            if (!string.IsNullOrWhiteSpace(StackTrace))
            {
                string stack = StackTrace;
                if (stack.Length > MaxStackTraceCharsInPrompt)
                {
                    stack = stack.Substring(0, MaxStackTraceCharsInPrompt) + "...";
                }

                text.AppendLine("Стек:");
                text.AppendLine(stack);
            }

            if (!string.IsNullOrWhiteSpace(XamlText))
            {
                string xaml = XamlText;
                bool truncated = xaml.Length > MaxXamlCharsInPrompt;
                if (truncated)
                {
                    xaml = xaml.Substring(0, MaxXamlCharsInPrompt);
                }

                text.AppendLine("XAML:");
                text.AppendLine("```xml");
                text.AppendLine(xaml);
                if (truncated)
                {
                    text.AppendLine("... (текст обрезан)");
                }

                text.AppendLine("```");
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Построение <see cref="DesignerErrorSnapshot"/> из исключения загрузки XAML.
    /// Чистая логика без UI: тестируется отдельно.
    /// </summary>
    public static class DesignerErrorCapture
    {
        /// <summary>
        /// Собирает снимок ошибки. <paramref name="xamlText"/> берётся из файла, если не передан явно.
        /// Позиция ищется сначала во вложенном <see cref="XmlException"/> (точная позиция XML-парсера),
        /// затем — фолбэк на <see cref="System.Xaml.XamlException"/>.
        /// </summary>
        public static DesignerErrorSnapshot Capture(string filePath, Exception exception, string xamlText = null)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (xamlText == null)
            {
                xamlText = ReadFileText(filePath);
            }

            XmlException xml = FindException<XmlException>(exception);
            int lineNumber = 0;
            int linePosition = 0;
            bool hasPosition = false;

            if (xml != null && xml.LineNumber > 0)
            {
                lineNumber = xml.LineNumber;
                linePosition = xml.LinePosition;
                hasPosition = true;
            }
            else
            {
                System.Xaml.XamlException xaml = FindException<System.Xaml.XamlException>(exception);
                if (xaml != null && xaml.LineNumber > 0)
                {
                    lineNumber = xaml.LineNumber;
                    linePosition = xaml.LinePosition;
                    hasPosition = true;
                }
            }

            var innerMessages = new List<string>();
            for (Exception inner = exception.InnerException; inner != null; inner = inner.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(inner.Message))
                {
                    innerMessages.Add(inner.Message);
                }
            }

            string offendingLine = ReadLineAt(xamlText, lineNumber);

            return new DesignerErrorSnapshot(
                filePath,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                innerMessages,
                lineNumber,
                linePosition,
                hasPosition,
                offendingLine,
                xamlText,
                exception.StackTrace,
                DateTime.Now);
        }

        private static T FindException<T>(Exception exception) where T : Exception
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is T match)
                {
                    return match;
                }
            }

            return null;
        }

        private static string ReadFileText(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(filePath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadLineAt(string text, int lineNumber)
        {
            if (string.IsNullOrEmpty(text) || lineNumber < 1)
            {
                return null;
            }

            string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            int index = lineNumber - 1;
            return index >= 0 && index < lines.Length ? lines[index] : null;
        }

        /// <summary>
        /// Строит снимок из ошибок, которые дизайнер отдаёт через <see cref="IXamlLoadErrorService"/>
        /// (это и есть данные родного баннера «workflow designer encountered problems…»).
        /// Ошибки приходят от WF без исключения, поэтому стек отсутствует, а позиция берётся напрямую
        /// из <see cref="XamlLoadErrorInfo"/>.
        /// </summary>
        public static DesignerErrorSnapshot CaptureFromLoadErrors(string filePath, IList<XamlLoadErrorInfo> errors)
        {
            string xamlText = ReadFileText(filePath);
            var innerMessages = new List<string>();
            int lineNumber = 0;
            int linePosition = 0;
            bool hasPosition = false;
            string firstMessage = null;

            if (errors != null)
            {
                foreach (XamlLoadErrorInfo error in errors)
                {
                    if (error == null)
                    {
                        continue;
                    }

                    if (firstMessage == null)
                    {
                        firstMessage = error.Message;
                        if (error.LineNumber > 0)
                        {
                            lineNumber = error.LineNumber;
                            linePosition = error.LinePosition;
                            hasPosition = true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(error.Message))
                    {
                        innerMessages.Add(error.Message);
                    }
                }
            }

            string offendingLine = ReadLineAt(xamlText, lineNumber);

            return new DesignerErrorSnapshot(
                filePath,
                typeof(XamlLoadErrorInfo).FullName,
                firstMessage ?? "Ошибка загрузки XAML",
                innerMessages,
                lineNumber,
                linePosition,
                hasPosition,
                offendingLine,
                xamlText,
                stackTrace: null,
                DateTime.Now);
        }
    }

    /// <summary>
    /// Перехватывает ошибки загрузки XAML, которые дизайнер отдаёт через
    /// <see cref="IXamlLoadErrorService"/> (родной баннер). Фиксирует снимок и, если передан
    /// <see cref="_inner"/>, сохраняет штатное поведение дизайнера.
    /// </summary>
    public sealed class CapturingXamlLoadErrorService : IXamlLoadErrorService
    {
        private readonly string _filePath;
        private readonly IXamlLoadErrorService _inner;

        public CapturingXamlLoadErrorService(string filePath, IXamlLoadErrorService inner = null)
        {
            _filePath = filePath;
            _inner = inner;
        }

        public void ShowXamlLoadErrors(IList<XamlLoadErrorInfo> errors)
        {
            if (errors != null && errors.Count > 0)
            {
                DesignerErrorReporter.Report(DesignerErrorCapture.CaptureFromLoadErrors(_filePath, errors));
            }

            _inner?.ShowXamlLoadErrors(errors);
        }
    }


    /// <summary>
    /// Хранит последний захваченный снимок ошибки конструктора и оповещает студию о его появлении.
    /// Не зависит от UI: <c>LooseXamlDocumentPersistenceService</c> и штатный путь открытия сообщают
    /// сюда, а <c>MainView</c> подписывается на <see cref="Captured"/> и показывает баннер.
    /// </summary>
    public static class DesignerErrorReporter
    {
        private static DesignerErrorSnapshot _last;

        /// <summary>Последний снимок ошибки; <c>null</c> — ошибок загрузки не было.</summary>
        public static DesignerErrorSnapshot Last => _last;

        /// <summary>Поднимается на UI-потоке при захвате ошибки загрузки документа.</summary>
        public static event EventHandler<DesignerErrorSnapshot> Captured;

        public static void Report(DesignerErrorSnapshot snapshot)
        {
            _last = snapshot;
            Captured?.Invoke(null, snapshot);
        }

        /// <summary>Пользователь нажал «Отправить в чат» в баннере ошибки дизайнера.</summary>
        public static event EventHandler<DesignerErrorSnapshot> SendRequested;

        /// <summary>Поднимает <see cref="SendRequested"/> (вызывается баннером ошибки дизайнера).</summary>
        public static void RequestSend(DesignerErrorSnapshot snapshot)
        {
            SendRequested?.Invoke(null, snapshot);
        }


        public static void Clear()
        {
            _last = null;
        }
    }
}

