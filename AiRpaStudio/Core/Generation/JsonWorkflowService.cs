using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>Запрос инструмента JSON AST: описание процесса, файл и параметры класса.</summary>
    internal sealed class JsonWorkflowRequest
    {
        /// <summary>JSON-описание процесса (текст; схема — <see cref="WorkflowJsonSchema"/>).</summary>
        internal string Json { get; set; }

        /// <summary>Путь к файлу процесса. Пустой — только собрать и проверить XAML, ничего не записывая.</summary>
        internal string FilePath { get; set; }

        /// <summary>Имя проекта (сборки). Пусто — берётся из описания процесса или из папки файла.</summary>
        internal string ProjectName { get; set; }

        internal string NamespaceName { get; set; }

        internal string ClassName { get; set; }
    }

    /// <summary>
    /// Ответ инструмента JSON AST: единый формат
    /// <c>{ok, workflowId, filePath, activityCount, variables[], errors[], warnings[], hints[]}</c>.
    /// </summary>
    internal sealed class JsonWorkflowResult
    {
        internal bool Ok { get; set; }

        internal string WorkflowId { get; set; }

        internal string FilePath { get; set; }

        internal string Xaml { get; set; }

        /// <summary>
        /// Число активностей: до сборки — количество описанных узлов, после — размер дерева WF
        /// (включая неявный корневой <c>Sequence</c>).
        /// </summary>
        internal int ActivityCount { get; set; }

        internal bool Opened { get; set; }

        internal bool Activated { get; set; }

        /// <summary>Объявленные переменные процесса (по ним агент правит выражения).</summary>
        internal List<string> Variables { get; } = new List<string>();

        /// <summary>Пространства имён C#-выражений, попавшие в XAML из описания.</summary>
        internal List<string> Namespaces { get; } = new List<string>();

        /// <summary>Сборки, попавшие в XAML из описания.</summary>
        internal List<string> References { get; } = new List<string>();

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Warnings { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "json ast: ок" : "json ast: ОШИБКИ");
            text.AppendLine($"workflowId: {WorkflowId ?? "(нет)"}");
            text.AppendLine($"файл: {FilePath ?? "(не сохранён)"}");
            text.AppendLine($"активностей: {ActivityCount}, переменных: {Variables.Count}");
            text.AppendLine($"ошибок: {Errors.Count}, предупреждений: {Warnings.Count}");

            AppendBlock(text, "ошибки", Errors);
            AppendBlock(text, "предупреждения", Warnings);
            AppendBlock(text, "подсказки", Hints);

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
                text.AppendLine("  - " + item);
            }
        }
    }

    /// <summary>Инструменты JSON AST через сервис: так их можно проверять без записи файла и конструктора.</summary>
    internal interface IJsonWorkflowService
    {
        /// <summary>Собирает (и при заданном пути сохраняет) процесс по JSON-описанию.</summary>
        JsonWorkflowResult Create(JsonWorkflowRequest request);
    }

    /// <summary>
    /// Сервис JSON AST (этап 6 конвейера генерации XAML):
    /// <code>
    /// JSON AST → WorkflowJsonValidator (строгая схема, whitelist)
    ///   → WorkflowJsonTranslator (Activity/ActivityBuilder через ActivityPaletteFactory)
    ///   → WorkflowBuildService (канонизация, сериализатор WF, временный файл, гейт)
    ///   → DesignerOpenService (вкладка конструктора)
    /// </code>
    /// <para>
    /// Это тот же конвейер, что у палитры (этап 4) и Code-First (этап 5): отличие только во входе.
    /// Валидация идёт <b>до</b> построения модели — агент получает список всех ошибок с путями
    /// до полей, а не первое исключение рефлексии.
    /// </para>
    /// </summary>
    internal sealed class JsonWorkflowService : IJsonWorkflowService
    {
        private readonly ActivityPaletteRegistry _registry;
        private readonly ActivityPaletteFactory _factory;
        private readonly IWorkflowValidationGate _gate;
        private readonly DesignerOpenService _openService;
        private readonly Action<string> _log;

        internal JsonWorkflowService(
            ActivityPaletteRegistry registry = null,
            IWorkflowDesignerHost host = null,
            IDesignerUiThread uiThread = null,
            IWorkflowValidationGate gate = null,
            Action<string> log = null)
        {
            _registry = registry ?? ActivityPaletteRegistry.Default;
            _factory = new ActivityPaletteFactory(_registry);
            _gate = gate;
            _log = log;

            if (host != null && uiThread != null)
            {
                _openService = new DesignerOpenService(host, uiThread, log);
            }
        }

        /// <summary>Сервис для запущенной студии: реальная среда конструктора, UI-поток и гейт по умолчанию.</summary>
        internal static JsonWorkflowService ForStudio(Action<string> log = null)
        {
            return new JsonWorkflowService(
                registry: null,
                host: new StudioWorkflowDesignerHost(),
                uiThread: DispatcherDesignerUiThread.ForStudio(),
                gate: null,
                log: log);
        }

        /// <summary>
        /// Собирает процесс по описанию. Путь не задан — только сборка и гейт (без записи на диск);
        /// путь задан — атомарное сохранение и открытие вкладки конструктора.
        /// </summary>
        public JsonWorkflowResult Create(JsonWorkflowRequest request)
        {
            if (request == null)
            {
                return Failure("xaml.request: запрос не задан");
            }

            if (string.IsNullOrWhiteSpace(request.Json))
            {
                JsonWorkflowResult empty = Failure("xaml.ast: описание процесса не задано");
                empty.Hints.Add(WorkflowJsonSchema.Describe());
                empty.Hints.Add("пример: " + WorkflowJsonSchema.Example());
                empty.Hints.Add("полная схема: вызовите инструмент с {\"describe\": true}");
                return empty;
            }

            string pathError = GenerationTarget.ValidateFilePath(request.FilePath, out string fullPath);
            if (pathError != null)
            {
                return Failure(pathError);
            }

            var result = new JsonWorkflowResult { FilePath = fullPath };

            // Строгая проверка по схеме — до любого построения модели (этап 6, п. 2).
            WorkflowJsonValidation validation = new WorkflowJsonValidator(_registry).Validate(request.Json);
            result.Hints.AddRange(validation.Hints);

            if (!validation.Ok)
            {
                result.Ok = false;
                result.Errors.AddRange(validation.Errors);
                result.ActivityCount = validation.Document?.ActivityCount ?? 0;
                return result;
            }

            WorkflowJsonDocument document = validation.Document;

            result.ActivityCount = document.ActivityCount;
            result.Variables.AddRange(document.VariableNames);
            result.Namespaces.AddRange(document.Namespaces);
            result.References.AddRange(document.References);

            string optionsError = GenerationTarget.BuildOptions(
                FirstNonEmpty(request.ProjectName, document.ProjectName),
                FirstNonEmpty(request.NamespaceName, document.NamespaceName),
                FirstNonEmpty(request.ClassName, document.ClassName),
                fullPath,
                out WorkflowCanonicalizationOptions options);

            if (optionsError != null)
            {
                JsonWorkflowResult failure = Failure(optionsError);
                failure.Hints.Add(GenerationTarget.ProjectHint);
                return failure;
            }

            // Пространства имён и ссылки из описания → в XAML: иначе конструктор покажет
            // нерезолвленные выражения (та же логика, что у Code-First с using из кода).
            foreach (string name in document.Namespaces)
            {
                if (!options.Namespaces.Contains(name))
                {
                    options.Namespaces.Add(name);
                }
            }

            foreach (string reference in document.References)
            {
                if (!options.References.Contains(reference))
                {
                    options.References.Add(reference);
                }
            }

            WorkflowJsonTranslation translation = WorkflowJsonTranslator.Translate(document, _factory);
            result.Hints.AddRange(translation.Hints);

            if (!translation.Ok)
            {
                result.Ok = false;
                result.Errors.AddRange(translation.Errors);
                return result;
            }

            result.ActivityCount = CodeFirstWorkflowTranslator.CountActivities(translation.Activity);

            var buildService = new WorkflowBuildService(options, _gate, fullPath == null ? null : _openService);
            WorkflowBuildResult build = fullPath == null
                ? buildService.Build(translation.Activity)
                : buildService.Save(translation.Activity, fullPath);

            result.Ok = build.Ok;
            result.WorkflowId = build.WorkflowId;
            result.Xaml = build.Xaml;

            if (!string.IsNullOrWhiteSpace(build.FilePath))
            {
                result.FilePath = build.FilePath;
            }

            AddDistinct(result.Errors, build.Errors);
            AddDistinct(result.Warnings, build.Warnings);
            result.Hints.AddRange(build.Hints.Where(hint => !result.Hints.Contains(hint)));

            if (!result.Ok || fullPath == null)
            {
                return result;
            }

            OpenInDesigner(result, fullPath);
            return result;
        }

        private void OpenInDesigner(JsonWorkflowResult result, string fullPath)
        {
            if (_openService == null)
            {
                result.Hints.Add("вкладка не открыта: среда конструктора недоступна");
                return;
            }

            DesignerOpenResult opened = _openService.OpenOrActivate(fullPath);
            result.Opened = opened.Ok;
            result.Activated = opened.Activated;

            if (!opened.Ok)
            {
                result.Ok = false;
                AddDistinct(result.Errors, opened.Errors);
                result.Hints.AddRange(opened.Hints);
                result.Hints.Add("файл записан и валиден, но вкладка конструктора не открылась");
                Log($"вкладка конструктора не открыта для {fullPath}: {string.Join("; ", opened.Errors)}");
                return;
            }

            result.WorkflowId = result.WorkflowId ?? opened.WorkflowId;
            result.Hints.AddRange(opened.Hints.Where(hint => !result.Hints.Contains(hint)));
        }

        private static string FirstNonEmpty(string preferred, string fallback)
        {
            return !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() : fallback;
        }

        private void Log(string message)
        {
            if (_log == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                _log(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Лог JSON AST недоступен: " + ErrorText.Of(ex));
            }
        }

        private static void AddDistinct(ICollection<string> target, IEnumerable<string> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (string item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!target.Contains(item))
                {
                    target.Add(item);
                }
            }
        }

        private static JsonWorkflowResult Failure(string error)
        {
            var result = new JsonWorkflowResult { Ok = false };
            result.Errors.Add(error);

            return result;
        }
    }
}
