using System;
using System.Activities;
using System.Activities.XamlIntegration;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xaml;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Запись рабочего процесса в XAML сериализатором WF (этап 2 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Текст XAML не собирается конкатенацией строк: используется тот же сериализатор, которым
    /// сохраняет сама студия (<c>ActivityXamlServices.CreateBuilderWriter</c> + <c>XamlServices.Save</c>).
    /// Поэтому вложенность, коллекции, вложенные свойства и пространства имён получаются «как в студии»
    /// автоматически, а наша правка — только то, что сериализатор не выводит из объектной модели
    /// (см. <see cref="Normalize"/>).
    /// </para>
    /// </summary>
    internal static class WorkflowXamlSerializer
    {
        private const string McNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private const string SapNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities/presentation";
        private const string Sap2010Namespace = "http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation";

        /// <summary>Пространство имён C#-выражений студии (<c>mca</c>): без него выражения не редактируются.</summary>
        internal const string CSharpActivitiesNamespace = "clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities";

        private const string McaPrefix = "mca";

        private const string McPrefix = "mc";
        private const string SapPrefix = "sap";
        private const string Sap2010Prefix = "sap2010";

        /// <summary>Файлы студии — UTF-8 без BOM: BOM ломает сравнение XAML в тестах и diff в git.</summary>
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);

        /// <summary>
        /// Сериализует модель в канонический текст XAML: без XML-декларации (как файлы студии),
        /// с отступами и переводами строк CRLF.
        /// </summary>
        internal static string Serialize(ActivityBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var text = new StringBuilder();

            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false
            };

            using (XmlWriter xml = XmlWriter.Create(text, settings))
            using (var inner = new XamlXmlWriter(xml, new XamlSchemaContext()))
            using (XamlWriter writer = ActivityXamlServices.CreateBuilderWriter(inner))
            {
                XamlServices.Save(writer, builder);
            }

            return Normalize(text.ToString());
        }

        /// <summary>Записывает канонический XAML в файл (UTF-8 без BOM, CRLF).</summary>
        internal static void Save(ActivityBuilder builder, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Путь к файлу процесса не задан", nameof(filePath));
            }

            File.WriteAllText(filePath, Serialize(builder), FileEncoding);
        }

        /// <summary>
        /// Достраивает то, чего WF-сериализатор не пишет сам, но что есть в файле студии:
        /// <c>xmlns:mc</c> + <c>mc:Ignorable="sap sap2010"</c> (объявления sap/sap2010 при этом
        /// обязаны существовать: <c>mc:Ignorable</c> с недекларированным префиксом — это
        /// <see cref="XmlException"/> при загрузке).
        /// <para>
        /// Это правка уже сериализованного документа (XML DOM), а не генерация XAML: элементы,
        /// атрибуты и вложенность по-прежнему создаёт сериализатор WF.
        /// </para>
        /// </summary>
        private static string Normalize(string xaml)
        {
            XmlDocument document = Load(xaml);
            if (document?.DocumentElement == null)
            {
                return xaml;
            }

            XmlElement root = document.DocumentElement;

            EnsureNamespace(root, SapPrefix, SapNamespace);
            EnsureNamespace(root, Sap2010Prefix, Sap2010Namespace);
            EnsureNamespace(root, McPrefix, McNamespace);

            // Пространство имён C#-выражений объявляется всегда, как в эталоне студии: гейт проверяет
            // форму корня, а процесс без выражений должен быть готов к первому же выражению
            // (иначе его пришлось бы пересохранять другим инструментом).
            EnsureNamespace(root, McaPrefix, CSharpActivitiesNamespace);

            MergeIgnorable(root);

            var settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace
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

        private static XmlDocument Load(string xaml)
        {
            if (string.IsNullOrWhiteSpace(xaml))
            {
                return null;
            }

            var document = new XmlDocument { PreserveWhitespace = true };

            try
            {
                document.LoadXml(xaml);
                return document;
            }
            catch (XmlException)
            {
                // Сериализатор WF пишет корректный XML; сюда попадать не должны. Возвращаем текст
                // без правки — гейт валидации всё равно проверит документ и вернёт ошибку.
                return null;
            }
        }

        /// <summary>Объявляет префикс на корне, если такого объявления ещё нет.</summary>
        private static void EnsureNamespace(XmlElement root, string prefix, string uri)
        {
            string declared = root.GetNamespaceOfPrefix(prefix);
            if (string.Equals(declared, uri, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(root.GetPrefixOfNamespace(uri)))
            {
                // URI уже объявлен под другим префиксом — добавлять второй незачем.
                return;
            }

            root.SetAttribute("xmlns:" + prefix, uri);
        }

        /// <summary>
        /// <c>mc:Ignorable="sap sap2010"</c>: разметка студии помечает presentation-пространства
        /// как необязательные для понимания (как в эталоне). Существующие префиксы сохраняются.
        /// </summary>
        private static void MergeIgnorable(XmlElement root)
        {
            string current = root.GetAttribute("Ignorable", McNamespace) ?? string.Empty;

            var prefixes = current
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            foreach (string prefix in new[] { SapPrefix, Sap2010Prefix })
            {
                if (!prefixes.Any(existing => string.Equals(existing, prefix, StringComparison.Ordinal)))
                {
                    prefixes.Add(prefix);
                }
            }

            root.SetAttribute("Ignorable", McNamespace, string.Join(" ", prefixes));
        }
    }
}
