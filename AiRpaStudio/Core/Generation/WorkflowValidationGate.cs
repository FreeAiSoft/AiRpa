using System;
using System.Activities;
using System.Activities.Validation;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Параметры гейта валидации: что именно считать «валидным XAML» для студии.
    /// </summary>
    internal sealed class WorkflowValidationOptions
    {
        /// <summary>
        /// Имя сборки проекта, ссылка на которую обязана быть в <c>TextExpression.ReferencesForImplementation</c>.
        /// Пустая строка — не проверять (для чужих процессов, не относящихся к проекту студии).
        /// </summary>
        internal string ProjectAssemblyName { get; set; } = string.Empty;

        /// <summary>Ожидаемый редактор выражений (в студии — только C#).</summary>
        internal string ExpectedExpressionActivityEditor { get; set; } =
            WorkflowCanonicalizationOptions.CSharpExpressionActivityEditor;

        /// <summary>
        /// Требовать канонический вид документа (п. 1.1 плана). Выключается только в тестах,
        /// чтобы проверить именно WF-валидацию.
        /// </summary>
        internal bool RequireCanonicalShape { get; set; } = true;
    }

    /// <summary>Отчёт гейта валидации: единый формат ответа <c>{ok, errors[], warnings[], hints[]}</c>.</summary>
    internal sealed class WorkflowValidationReport
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _hints = new List<string>();

        /// <summary>Ошибки: каждая — «код: объяснение». Пустой список — документ валиден.</summary>
        internal List<string> Errors => _errors;

        /// <summary>Предупреждения WF: не блокируют открытие, но возвращаются вызывающему.</summary>
        internal List<string> Warnings => _warnings;

        /// <summary>Подсказки, как исправить ошибку (для самокоррекции агента).</summary>
        internal List<string> Hints => _hints;

        /// <summary>Документ прошёл гейт: ошибок нет.</summary>
        internal bool Ok => _errors.Count == 0;

        internal void AddError(string code, string message)
        {
            _errors.Add(string.IsNullOrEmpty(code) ? message : code + ": " + message);
        }

        internal void AddWarning(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _warnings.Add(message);
            }
        }

        internal void AddHint(string hint)
        {
            if (!string.IsNullOrWhiteSpace(hint) && !_hints.Contains(hint))
            {
                _hints.Add(hint);
            }
        }

        /// <summary>Текст отчёта для сообщения об ошибке теста или ответа инструмента.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "валидация: ок" : "валидация: ОШИБКИ");
            text.AppendLine($"ошибок: {_errors.Count}, предупреждений: {_warnings.Count}");

            AppendBlock(text, "ошибки", _errors);
            AppendBlock(text, "предупреждения", _warnings);
            AppendBlock(text, "подсказки", _hints);

            return text.ToString();
        }

        private static void AppendBlock(StringBuilder text, string title, IList<string> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            text.AppendLine(title + ":");
            foreach (string item in items)
            {
                text.AppendLine("  - " + item?.Replace("\r\n", "\r\n    "));
            }
        }
    }

    /// <summary>Гейт валидации: точка расширения для тестов и для будущих проверок (отрисовка, этап 3).</summary>
    internal interface IWorkflowValidationGate
    {
        /// <summary>Проверяет файл процесса.</summary>
        WorkflowValidationReport Validate(string xamlPath);
    }

    /// <summary>
    /// Гейт валидации сгенерированного XAML (этап 2 конвейера генерации XAML).
    /// <para>
    /// Проверок четыре, и все обязательны — иначе «валидный XAML» окажется «нет исключения при загрузке»:
    /// </para>
    /// <list type="number">
    /// <item><b>корректный XML</b> — иначе студия показывает повреждённый документ;</item>
    /// <item><b>канонический вид</b> (п. 1.1): <c>x:Class</c>, <c>sap2010:ExpressionActivityEditor="C#"</c>,
    /// <c>xmlns:mca</c>, <c>mc:Ignorable</c>, IdRef корня, <c>TextExpression.NamespacesForImplementation</c>
    /// и <c>ReferencesForImplementation</c> со ссылкой на сборку проекта. WF эти признаки не проверяет:
    /// без них файл загружается, но в конструкторе выражения не редактируются, а проектные типы
    /// не резолвятся — то есть «валидный» XAML всё равно не работает;</item>
    /// <item><b>загрузка как WF-активности</b> (<see cref="ActivityXamlServices.Load(string, ActivityXamlServicesSettings)"/>);</item>
    /// <item><b>валидация WF</b> (<see cref="ActivityValidationServices.Validate(Activity)"/>) — 0 ошибок;
    /// предупреждения возвращаются, но не блокируют.</item>
    /// </list>
    /// </summary>
    internal sealed class WorkflowValidationGate : IWorkflowValidationGate
    {
        private const string ActivitiesNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const string McNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private const string Sap2010Namespace = "http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation";

        /// <summary>Пространство имён C#-выражений: без него конструктор не понимает <c>mca:CSharpValue</c>.</summary>
        private const string CSharpActivitiesNamespace =
            "clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities";

        private const string ExpressionActivityEditorAttribute = "ExpressionActivityEditor.ExpressionActivityEditor";
        private const string IdRefAttribute = "WorkflowViewState.IdRef";
        private const string NamespacesElementName = "TextExpression.NamespacesForImplementation";
        private const string ReferencesElementName = "TextExpression.ReferencesForImplementation";
        private const string AssemblyReferenceElementName = "AssemblyReference";

        private const string CanonicalShapeHint =
            "канонический вид XAML (x:Class, sap2010:ExpressionActivityEditor=\"C#\", xmlns:mca, IdRef, " +
            "TextExpression.*) добавляет WorkflowBuildService: собирать эти элементы вручную нельзя";

        private static readonly ActivityXamlServicesSettings LoadSettings = new ActivityXamlServicesSettings
        {
            CompileExpressions = false
        };

        private readonly WorkflowValidationOptions _options;

        internal WorkflowValidationGate(WorkflowValidationOptions options = null)
        {
            _options = options ?? new WorkflowValidationOptions();
        }

        /// <summary>Проверяет файл: структуру читает из текста, WF-загрузку делает по пути (важно для резолва сборок).</summary>
        public WorkflowValidationReport Validate(string xamlPath)
        {
            var report = new WorkflowValidationReport();

            if (string.IsNullOrWhiteSpace(xamlPath))
            {
                report.AddError("xaml.path", "путь к файлу процесса не задан");
                return report;
            }

            string fullPath = Path.GetFullPath(xamlPath);
            if (!File.Exists(fullPath))
            {
                report.AddError("xaml.path", $"файл процесса не найден: {fullPath}");
                return report;
            }

            string xaml;
            try
            {
                xaml = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                report.AddError("xaml.read", ErrorText.Describe($"не удалось прочитать {fullPath}", ex));
                return report;
            }

            ValidateDocument(xaml, report);
            ValidateWorkflow(report, fullPath);

            return report;
        }

        /// <summary>Проверяет XAML, которого ещё нет на диске (сборка без сохранения).</summary>
        internal WorkflowValidationReport ValidateText(string xaml)
        {
            var report = new WorkflowValidationReport();

            ValidateDocument(xaml, report);
            ValidateWorkflow(report, null, xaml);

            return report;
        }

        private void ValidateDocument(string xaml, WorkflowValidationReport report)
        {
            XmlDocument document = LoadDocument(xaml, report);
            if (document?.DocumentElement == null)
            {
                return;
            }

            if (!_options.RequireCanonicalShape)
            {
                return;
            }

            XmlElement root = document.DocumentElement;
            bool shapeError = false;

            if (!string.Equals(root.LocalName, "Activity", StringComparison.Ordinal))
            {
                Report(report, ref shapeError, "shape.root",
                    $"корневой элемент «{root.Name}» вместо «Activity»: конструктор студии открывает как рабочий процесс только <Activity>");
            }

            if (!string.Equals(root.NamespaceURI, ActivitiesNamespace, StringComparison.Ordinal))
            {
                Report(report, ref shapeError, "shape.rootNamespace",
                    $"корень объявлен не в пространстве имён WF ({root.NamespaceURI}): конструктор не распознает активности");
            }

            string className = root.GetAttribute("Class", XamlNamespace);
            if (string.IsNullOrWhiteSpace(className))
            {
                Report(report, ref shapeError, "shape.xClass",
                    "у корня нет x:Class: WorkflowDesigner не связывает файл с типом проекта и показывает пустую диаграмму");
            }

            string editor = root.GetAttribute(ExpressionActivityEditorAttribute, Sap2010Namespace);
            if (!string.Equals(editor, _options.ExpectedExpressionActivityEditor, StringComparison.Ordinal))
            {
                Report(report, ref shapeError, "shape.expressionEditor",
                    $"редактор выражений «{editor}» вместо «{_options.ExpectedExpressionActivityEditor}» " +
                    "(sap2010:ExpressionActivityEditor.ExpressionActivityEditor): выражения в конструкторе не редактируются");
            }

            string csharpNamespace = root.GetNamespaceOfPrefix("mca");
            if (!string.Equals(csharpNamespace, CSharpActivitiesNamespace, StringComparison.Ordinal))
            {
                Report(report, ref shapeError, "shape.csharpNamespace",
                    $"пространство имён C#-выражений не объявлено: ожидается xmlns:mca=\"{CSharpActivitiesNamespace}\"");
            }

            string idRef = root.GetAttribute(IdRefAttribute, Sap2010Namespace);
            if (string.IsNullOrWhiteSpace(idRef))
            {
                Report(report, ref shapeError, "shape.idRef",
                    "у корня нет sap2010:WorkflowViewState.IdRef: потеряно состояние конструктора");
            }

            string ignorable = root.GetAttribute("Ignorable", McNamespace);
            if (!ContainsPrefix(ignorable, "sap2010"))
            {
                Report(report, ref shapeError, "shape.markupCompatibility",
                    "нет mc:Ignorable=\"sap sap2010\": разметка студии помечает presentation-пространства как необязательные");
            }

            XmlElement namespacesElement = FindChild(root, NamespacesElementName);
            if (namespacesElement == null)
            {
                Report(report, ref shapeError, "shape.namespaces",
                    $"нет {NamespacesElementName}: C#-выражения не компилируются в конструкторе");
            }
            else if (string.IsNullOrWhiteSpace(namespacesElement.InnerText))
            {
                Report(report, ref shapeError, "shape.namespaces",
                    $"{NamespacesElementName} пуст: ни одно пространство имён не доступно выражениям");
            }

            ValidateReferences(root, report, ref shapeError);

            if (shapeError)
            {
                report.AddHint(CanonicalShapeHint);
            }
        }

        private void ValidateReferences(XmlElement root, WorkflowValidationReport report, ref bool shapeError)
        {
            XmlElement referencesElement = FindChild(root, ReferencesElementName);
            if (referencesElement == null)
            {
                Report(report, ref shapeError, "shape.references",
                    $"нет {ReferencesElementName}: выражения не скомпилируются, конструктор покажет нерезолвленные типы");
                return;
            }

            List<string> references = referencesElement
                .GetElementsByTagName(AssemblyReferenceElementName)
                .Cast<XmlElement>()
                .Select(element => element.InnerText?.Trim())
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            if (references.Count == 0)
            {
                Report(report, ref shapeError, "shape.references",
                    $"{ReferencesElementName} пуст: в выражениях не будет доступна ни одна сборка");
                return;
            }

            string projectAssembly = _options.ProjectAssemblyName;
            if (string.IsNullOrWhiteSpace(projectAssembly))
            {
                return;
            }

            if (!references.Any(name => string.Equals(SimpleName(name), SimpleName(projectAssembly), StringComparison.OrdinalIgnoreCase)))
            {
                Report(report, ref shapeError, "shape.projectReference",
                    $"в {ReferencesElementName} нет сборки проекта «{projectAssembly}»: её типы не резолвятся, " +
                    "и конструктор открывает модальное окно «Конструктор рабочих процессов»");
            }
        }

        private void ValidateWorkflow(WorkflowValidationReport report, string xamlPath, string xaml = null)
        {
            Activity activity;
            try
            {
                activity = xamlPath != null
                    ? ActivityXamlServices.Load(xamlPath, LoadSettings)
                    : ActivityXamlServices.Load(XmlReader.Create(new StringReader(xaml ?? string.Empty)), LoadSettings);
            }
            catch (Exception ex)
            {
                report.AddError("xaml.load",
                    ErrorText.Describe("XAML не загружается как активность WF", ex));
                return;
            }

            ValidationResults results;
            try
            {
                results = ActivityValidationServices.Validate(activity);
            }
            catch (Exception ex)
            {
                report.AddError("wf.validation",
                    ErrorText.Describe("валидация WF не выполнена", ex));
                return;
            }

            foreach (ValidationError error in results.Errors)
            {
                report.AddError("wf.validation", Describe(error));
            }

            foreach (ValidationError warning in results.Warnings)
            {
                report.AddWarning(Describe(warning));
            }
        }

        private static XmlDocument LoadDocument(string xaml, WorkflowValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(xaml))
            {
                report.AddError("xaml.empty", "XAML пуст");
                return null;
            }

            var document = new XmlDocument();

            try
            {
                document.LoadXml(xaml);
                return document;
            }
            catch (XmlException ex)
            {
                report.AddError("xaml.malformed", ErrorText.Describe("XAML не является корректным XML", ex));
                return null;
            }
        }

        private static void Report(WorkflowValidationReport report, ref bool shapeError, string code, string message)
        {
            report.AddError(code, message);
            shapeError = true;
        }

        private static XmlElement FindChild(XmlElement parent, string localName)
        {
            return parent?.ChildNodes
                .OfType<XmlElement>()
                .FirstOrDefault(element => string.Equals(element.LocalName, localName, StringComparison.Ordinal));
        }

        private static bool ContainsPrefix(string prefixes, string prefix)
        {
            return (prefixes ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(existing => string.Equals(existing, prefix, StringComparison.Ordinal));
        }

        private static string SimpleName(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return string.Empty;
            }

            int comma = assemblyName.IndexOf(',');
            return (comma < 0 ? assemblyName : assemblyName.Substring(0, comma)).Trim();
        }

        private static string Describe(ValidationError error)
        {
            if (error == null)
            {
                return "(пустая ошибка валидации)";
            }

            string source = error.Source == null ? string.Empty : $" ({error.Source.GetType().Name})";
            string id = string.IsNullOrWhiteSpace(error.Id) ? string.Empty : $" [{error.Id}]";

            return $"{error.Message}{source}{id}";
        }
    }
}
