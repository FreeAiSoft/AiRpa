using System;
using System.Activities.Debugger;
using System.Activities.Presentation.Hosting;
using System.Activities.XamlIntegration;
using System.IO;
using System.Xaml;
using System.Xml;
using RPA.Logging;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Реализация <see cref="IDocumentPersistenceService"/> для loose-XAML: загружает
    /// <c>Main.xaml</c> без сборки проекта, резолвя вложенные ссылки <c>&lt;local:SubProcessN/&gt;</c>
    /// в <see cref="System.Activities.DynamicActivity"/> через <see cref="LooseActivityXamlSchemaContext"/>.
    /// <para>
    /// Обратная сериализация (<c>Flush</c>) возвращает фасады обратно в <c>&lt;local:SubProcessN/&gt;</c>
    /// и отдаёт готовый XAML через <see cref="_setText"/>, чтобы штатный <c>WorkflowDesigner.Save</c>
    /// записал в файл ровно этот текст.
    /// </para>
    /// </summary>
    internal sealed class LooseXamlDocumentPersistenceService : IDocumentPersistenceService
    {
        private readonly WorkflowTypeRegistry _registry;
        private readonly Action<string> _setText;

        internal LooseXamlDocumentPersistenceService(WorkflowTypeRegistry registry, Action<string> setText = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _setText = setText;
        }

        public object Load(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Путь к XAML-файлу не задан.", nameof(fileName));
            }

            string xaml = File.ReadAllText(fileName);
            SourceLocationMappingDiagnostics.Write($"LooseXamlDocumentPersistenceService.Load: file={fileName} length={xaml.Length}");

            var schemaContext = new LooseActivityXamlSchemaContext(_registry);
            SourceLocationMappingDiagnostics.Write($"LooseXamlDocumentPersistenceService.Load: schemaContext={schemaContext.GetType().FullName}");

            // XamlDebuggerXmlReader обязателен: обычная загрузка (XamlXmlReader) оставляет отладочные
            // символы <sads:DebugSymbol.Symbol> сырой строкой, и SourceLocationProvider.CollectMapping
            // не строит карту SourceLocation. Без неё DebuggerService.CurrentLocation всегда null, а вставка
            // точки останова падает с ArgumentNullException (пустой SourceLocation как ключ словаря).
            using (var textReader = new StringReader(xaml))
            using (var debuggerReader = new XamlDebuggerXmlReader(textReader, schemaContext))
            using (var builderReader = ActivityXamlServices.CreateBuilderReader(debuggerReader, schemaContext))
            {
                object loaded = XamlServices.Load(builderReader);
                SourceLocationMappingDiagnostics.Write($"LooseXamlDocumentPersistenceService.Load: loaded={(loaded == null ? "<null>" : loaded.GetType().FullName)}");
                return loaded;
            }
        }

        public void Flush(object documentRoot)
        {
            if (documentRoot == null)
            {
                throw new ArgumentNullException(nameof(documentRoot));
            }

            string xaml = LooseXamlRoundTripSerializer.Serialize(documentRoot, _registry);
            _setText?.Invoke(xaml);
        }

        public void OnModelChanged(object documentRoot)
        {
            // Текст документа обновляет сам WorkflowDesigner; наша модель не хранит состояние.
        }
    }
}
