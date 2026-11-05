using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>Что представляет собой файл с расширением <c>.xaml</c>.</summary>
    public enum WorkflowXamlKind
    {
        /// <summary>Рабочий процесс: корень <c>Activity</c> в пространстве имён WF — нормализация применима.</summary>
        Workflow,

        /// <summary>Корректный XML, но не рабочий процесс (окно, дизайнер активности, тема) — текст не переписывается.</summary>
        OtherXml,

        /// <summary>Не корректный XML: нормализовать нечего, файл писать нельзя.</summary>
        Malformed
    }

    /// <summary>Настройки нормализации XAML.</summary>
    public sealed class WorkflowXamlNormalizeOptions
    {
        /// <summary>
        /// Достраивать канонический вид студии: <c>xmlns:mc/sap/sap2010/mca</c> и
        /// <c>mc:Ignorable="sap sap2010"</c>. Без них файл загружается, но выражения в конструкторе
        /// не редактируются — поэтому по умолчанию включено.
        /// </summary>
        public bool EnsureCanonicalNamespaces { get; set; } = true;

        /// <summary>Убирать XML-декларацию: файлы студии её не содержат.</summary>
        public bool RemoveXmlDeclaration { get; set; } = true;
    }

    /// <summary>Результат нормализации: текст, что именно изменилось и почему нормализация не применилась.</summary>
    public sealed class WorkflowXamlNormalizeResult
    {
        private readonly List<string> _notes;

        private WorkflowXamlNormalizeResult(WorkflowXamlKind kind, string text, bool changed, string error, IEnumerable<string> notes)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Changed = changed;
            Error = error;
            _notes = notes == null ? new List<string>() : notes.ToList();
        }

        /// <summary>Тип документа: рабочий процесс, чужой XML или битый XML.</summary>
        public WorkflowXamlKind Kind { get; }

        /// <summary>
        /// Текст, который надо записать в файл. Для <see cref="WorkflowXamlKind.Malformed"/> и
        /// <see cref="WorkflowXamlKind.OtherXml"/> — исходный текст без изменений.
        /// </summary>
        public string Text { get; }

        /// <summary>Текст изменился относительно исходного (BOM в сравнении не участвует).</summary>
        public bool Changed { get; }

        /// <summary>Почему нормализация не применилась; <c>null</c> — применилась.</summary>
        public string Error { get; }

        /// <summary>Что именно сделано (для ответа инструмента, а не для машины).</summary>
        public IReadOnlyList<string> Notes => _notes;

        internal static WorkflowXamlNormalizeResult Workflow(string text, bool changed, IEnumerable<string> notes)
        {
            return new WorkflowXamlNormalizeResult(WorkflowXamlKind.Workflow, text, changed, null, notes);
        }

        internal static WorkflowXamlNormalizeResult OtherXml(string text, string note)
        {
            return new WorkflowXamlNormalizeResult(
                WorkflowXamlKind.OtherXml,
                text,
                false,
                null,
                string.IsNullOrEmpty(note) ? null : new[] { note });
        }

        internal static WorkflowXamlNormalizeResult Malformed(string text, string error)
        {
            return new WorkflowXamlNormalizeResult(WorkflowXamlKind.Malformed, text, false, error, null);
        }
    }

    /// <summary>
    /// Единственный набор правил канонического вида XAML (карточка <c>AG-07</c>).
    /// <para>
    /// Отдельного инструмента <c>xaml_write</c> нет: текст пишет <c>editor</c>, а правила вида живут здесь,
    /// чтобы у сериализатора WF (<c>WorkflowXamlSerializer</c>) и у инструмента не было двух наборов
    /// ожиданий к одному файлу. Канонический вид: UTF-8 без BOM (BOM — на уровне файла), переводы строк
    /// CRLF, без XML-декларации, с <c>xmlns:mc/sap/sap2010/mca</c> и <c>mc:Ignorable="sap sap2010"</c>.
    /// </para>
    /// <para>
    /// <b>Отступы не переиндентируются намеренно.</b> В XAML рабочего процесса значимый текст лежит внутри
    /// элементов: выражения C# (<c>mca:CSharpValue</c>) и списки <c>TextExpression.NamespacesForImplementation</c>.
    /// Переиндентация добавила бы отступ внутрь этого текста, то есть изменила бы смысл файла. Поэтому
    /// отступ только читается (<see cref="Describe"/>) и показывается агенту — чтобы он писал в том же стиле.
    /// </para>
    /// </summary>
    public static class WorkflowXamlText
    {
        /// <summary>Пространство имён рабочих процессов WF: только такой корень считается процессом.</summary>
        public const string ActivitiesNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";

        /// <summary>Пространство имён разметки, необязательной для понимания (<c>mc:Ignorable</c>).</summary>
        public const string MarkupCompatibilityNamespace =
            "http://schemas.openxmlformats.org/markup-compatibility/2006";

        /// <summary>Presentation-пространство конструктора WF 4.</summary>
        public const string DesignerNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities/presentation";

        /// <summary>Presentation-пространство конструктора WF 4.5 (в файлах студии есть оба).</summary>
        public const string Designer2010Namespace =
            "http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation";

        /// <summary>Пространство имён C#-выражений студии (<c>mca</c>): без него выражения не редактируются.</summary>
        public const string CSharpActivitiesNamespace =
            "clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities";

        private const string McPrefix = "mc";
        private const string SapPrefix = "sap";
        private const string Sap2010Prefix = "sap2010";
        private const string McaPrefix = "mca";

        private const int MaxNamespacesInHeader = 6;

        private static readonly WorkflowXamlNormalizeOptions DefaultOptions = new WorkflowXamlNormalizeOptions();

        /// <summary>
        /// Нормализует текст XAML. Возвращает и нормализованный текст, и честный вердикт: битый XML,
        /// чужой XML и «нормализация не понадобилась» — разные ответы, а не один «готово».
        /// </summary>
        public static WorkflowXamlNormalizeResult Normalize(string text, WorkflowXamlNormalizeOptions options = null)
        {
            options = options ?? DefaultOptions;

            string original = StripBom(text);
            if (string.IsNullOrWhiteSpace(original))
            {
                return WorkflowXamlNormalizeResult.Malformed(original, "текст пуст");
            }

            XmlDocument document;
            try
            {
                document = Load(original);
            }
            catch (XmlException ex)
            {
                return WorkflowXamlNormalizeResult.Malformed(original, ex.Message);
            }

            XmlElement root = document?.DocumentElement;
            if (root == null)
            {
                return WorkflowXamlNormalizeResult.Malformed(original, "в документе нет корневого элемента");
            }

            if (!IsWorkflowRoot(root))
            {
                return WorkflowXamlNormalizeResult.OtherXml(
                    original,
                    $"корень <{root.Name}> — не рабочий процесс: XAML не переписывается");
            }

            var notes = new List<string>();

            if (options.EnsureCanonicalNamespaces)
            {
                var added = new List<string>();
                EnsureNamespace(root, McPrefix, MarkupCompatibilityNamespace, added);
                EnsureNamespace(root, SapPrefix, DesignerNamespace, added);
                EnsureNamespace(root, Sap2010Prefix, Designer2010Namespace, added);
                EnsureNamespace(root, McaPrefix, CSharpActivitiesNamespace, added);

                if (added.Count > 0)
                {
                    notes.Add("добавлено: " + string.Join(", ", added));
                }

                if (MergeIgnorable(root))
                {
                    notes.Add("mc:Ignorable=\"sap sap2010\"");
                }
            }

            string result = Save(document, options);
            bool changed = !string.Equals(original, result, StringComparison.Ordinal);

            if (changed)
            {
                if (HasSingleLineFeeds(original))
                {
                    notes.Add("переводы строк → CRLF");
                }

                if (original.TrimStart().StartsWith("<?xml", StringComparison.Ordinal) && options.RemoveXmlDeclaration)
                {
                    notes.Add("удалена XML-декларация");
                }
            }

            return WorkflowXamlNormalizeResult.Workflow(result, changed, notes);
        }

        /// <summary>
        /// Шапка метаданных файла для <c>read_files</c>: без неё агент не знает про BOM, EOL и отступ
        /// и «портит» файл при первой же перезаписи.
        /// </summary>
        /// <param name="text">Содержимое файла (BOM уже снят читателем или ещё в тексте — не важно).</param>
        /// <param name="hasBom">Файл начинается с BOM UTF-8.</param>
        /// <param name="source">Откуда прочитано: «файл на диске», «буфер редактора».</param>
        /// <param name="openInDesigner">Файл открыт вкладкой конструктора (правки модели перекроют файл).</param>
        public static string Describe(string text, bool hasBom, string source = null, bool openInDesigner = false)
        {
            string original = StripBom(text);
            var line = new StringBuilder("xaml: ");

            XmlDocument document = null;
            string xmlError = null;
            try
            {
                document = Load(original);
                if (document?.DocumentElement == null)
                {
                    xmlError = "нет корневого элемента";
                }
            }
            catch (XmlException ex)
            {
                xmlError = ex.Message;
            }
            catch (Exception ex)
            {
                xmlError = ex.Message;
            }

            XmlElement root = document?.DocumentElement;
            if (xmlError == null)
            {
                line.Append(root.LocalName);
                line.Append(IsWorkflowRoot(root) ? " (рабочий процесс)" : " (не рабочий процесс)");
                line.Append(", ");
            }
            else
            {
                line.Append("не является корректным XML, ");
            }

            line.Append(Encoding.UTF8.GetByteCount(original) + (hasBom ? 3 : 0)).Append(" B, ");
            line.Append(hasBom ? "UTF-8 с BOM" : "UTF-8 без BOM");
            line.Append(", EOL ").Append(DetectLineEndings(original));
            line.Append(", ").Append(DetectIndentation(original));
            line.Append(", строк: ").Append(CountLines(original));

            if (!string.IsNullOrWhiteSpace(source))
            {
                line.Append(", источник: ").Append(source);
            }

            line.Append(", в конструкторе: ").Append(openInDesigner ? "да" : "нет");

            if (xmlError != null)
            {
                line.Append(", ошибка: ").Append(OneLine(xmlError));
                return line.ToString();
            }

            if (!IsWorkflowRoot(root))
            {
                return line.ToString();
            }

            List<string> namespaces = DeclaredNamespaces(root);
            if (namespaces.Count == 0)
            {
                return line.ToString();
            }

            if (namespaces.Count > MaxNamespacesInHeader)
            {
                namespaces = namespaces.Take(MaxNamespacesInHeader).ToList();
                namespaces.Add("…");
            }

            return line + Environment.NewLine + "xaml xmlns: " + string.Join(", ", namespaces);
        }

        /// <summary>Файл с расширением <c>.xaml</c> (без учёта регистра и без учёта <c>.xaml.cs</c>).</summary>
        public static bool IsWorkflowXamlFile(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && string.Equals(Path.GetExtension(path.Trim()), ".xaml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Корень рабочего процесса: <c>Activity</c>/<c>ActivityBuilder</c> в пространстве имён WF.</summary>
        public static bool IsWorkflowRoot(XmlElement root)
        {
            if (root == null)
            {
                return false;
            }

            bool known = string.Equals(root.LocalName, "Activity", StringComparison.Ordinal)
                         || string.Equals(root.LocalName, "ActivityBuilder", StringComparison.Ordinal);

            return known && string.Equals(root.NamespaceURI, ActivitiesNamespace, StringComparison.Ordinal);
        }

        /// <summary>Снимает BOM-символ, если читатель его не убрал.</summary>
        public static string StripBom(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            return text[0] == '\uFEFF' ? text.Substring(1) : text;
        }

        private static XmlDocument Load(string text)
        {
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            document.LoadXml(text);
            return document;
        }

        private static string Save(XmlDocument document, WorkflowXamlNormalizeOptions options)
        {
            var settings = new XmlWriterSettings
            {
                // Indent = false: документ сохраняет свои узлы-пробелы (см. комментарий класса).
                Indent = false,
                OmitXmlDeclaration = options.RemoveXmlDeclaration,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false
            };

            using (var output = new StringWriter())
            {
                using (XmlWriter writer = XmlWriter.Create(output, settings))
                {
                    document.Save(writer);
                }

                return output.ToString();
            }
        }

        private static void EnsureNamespace(XmlElement root, string prefix, string uri, IList<string> added)
        {
            if (string.Equals(root.GetNamespaceOfPrefix(prefix), uri, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(root.GetPrefixOfNamespace(uri)))
            {
                // URI уже объявлен под другим префиксом — второй не нужен.
                return;
            }

            root.SetAttribute("xmlns:" + prefix, uri);
            added.Add("xmlns:" + prefix);
        }

        private static bool MergeIgnorable(XmlElement root)
        {
            string current = root.GetAttribute("Ignorable", MarkupCompatibilityNamespace) ?? string.Empty;

            var prefixes = current
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            bool changed = false;
            foreach (string prefix in new[] { SapPrefix, Sap2010Prefix })
            {
                if (!prefixes.Any(existing => string.Equals(existing, prefix, StringComparison.Ordinal)))
                {
                    prefixes.Add(prefix);
                    changed = true;
                }
            }

            if (!changed)
            {
                return false;
            }

            root.SetAttribute("Ignorable", MarkupCompatibilityNamespace, string.Join(" ", prefixes));
            return true;
        }

        private static bool HasSingleLineFeeds(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))
                {
                    return true;
                }

                if (c == '\n' && (i == 0 || text[i - 1] != '\r'))
                {
                    return true;
                }
            }

            return false;
        }

        private static string DetectLineEndings(string text)
        {
            int crlf = 0, lf = 0, cr = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlf++;
                        i++;
                    }
                    else
                    {
                        cr++;
                    }
                }
                else if (c == '\n')
                {
                    lf++;
                }
            }

            if (crlf == 0 && lf == 0 && cr == 0)
            {
                return "нет (одна строка)";
            }

            var kinds = new List<string>();
            if (crlf > 0) kinds.Add("CRLF");
            if (lf > 0) kinds.Add("LF");
            if (cr > 0) kinds.Add("CR");

            return kinds.Count == 1 ? kinds[0] : "смешанные (" + string.Join(" + ", kinds) + ")";
        }

        private static string DetectIndentation(string text)
        {
            var widths = new List<int>();
            bool tabbed = false;

            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (rawLine.Trim().Length == 0)
                {
                    continue;
                }

                int spaces = 0;
                while (spaces < rawLine.Length && rawLine[spaces] == ' ')
                {
                    spaces++;
                }

                if (spaces < rawLine.Length && rawLine[spaces] == '\t')
                {
                    tabbed = true;
                    continue;
                }

                if (spaces > 0)
                {
                    widths.Add(spaces);
                }
            }

            if (tabbed)
            {
                return "отступ табуляцией";
            }

            if (widths.Count == 0)
            {
                return "нет отступа (однострочный файл)";
            }

            int step = widths[0];
            foreach (int width in widths)
            {
                step = Gcd(step, width);
            }

            if (step <= 1)
            {
                return "отступ смешанный";
            }

            return "отступ " + step + " " + Plural(step, "пробел", "пробела", "пробелов");
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }

            return a == 0 ? 1 : a;
        }

        private static string Plural(int value, string one, string few, string many)
        {
            int mod10 = value % 10;
            int mod100 = value % 100;

            if (mod10 == 1 && mod100 != 11) return one;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
            return many;
        }

        private static int CountLines(string text)
        {
            if (text.Length == 0)
            {
                return 0;
            }

            return text.Replace("\r\n", "\n").Split('\n').Length;
        }

        private static List<string> DeclaredNamespaces(XmlElement root)
        {
            var namespaces = new List<string>();

            foreach (XmlAttribute attribute in root.Attributes)
            {
                if (string.Equals(attribute.Name, "xmlns", StringComparison.Ordinal))
                {
                    namespaces.Add("default → " + attribute.Value);
                }
                else if (string.Equals(attribute.Prefix, "xmlns", StringComparison.Ordinal))
                {
                    namespaces.Add(attribute.LocalName + " → " + attribute.Value);
                }
            }

            return namespaces;
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", " ")
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Trim();
        }
    }
}
