using System;
using System.Activities;
using System.Activities.XamlIntegration;
using System.IO;
using System.Text;
using System.Xml;
using System.Xaml;
using RPA.Chat.Services.Workflow;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Запись рабочего процесса в XAML сериализатором WF (этап 2 плана
    /// конвейер генерации XAML).
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
        /// <summary>Файлы студии — UTF-8 без BOM: BOM ломает сравнение XAML в тестах и diff в git.</summary>
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);

        /// <summary>Канонический вид файла студии: пространства имён, без XML-декларации, CRLF.</summary>
        private static readonly WorkflowXamlNormalizeOptions CanonicalOptions = new WorkflowXamlNormalizeOptions();

        /// <summary>Пространство имён C#-выражений студии (<c>mca</c>): без него выражения не редактируются.</summary>
        internal const string CSharpActivitiesNamespace = WorkflowXamlText.CSharpActivitiesNamespace;

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
        /// <c>xmlns:mc</c> + <c>mc:Ignorable="sap sap2010"</c> (объявления sap/sap2010 при этом обязаны
        /// существовать: <c>mc:Ignorable</c> с недекларированным префиксом — это <see cref="XmlException"/>
        /// при загрузке).
        /// <para>
        /// Сами правила живут в одном месте — <see cref="WorkflowXamlText"/> (карточка <c>AG-07</c>): тот же
        /// нормализатор применяет инструмент <c>editor</c>, поэтому сериализатор и текстовая правка не могут
        /// дать разные файлы на одном и том же процессе.
        /// </para>
        /// </summary>
        private static string Normalize(string xaml)
        {
            return WorkflowXamlText.Normalize(xaml, CanonicalOptions).Text;
        }
    }
}
