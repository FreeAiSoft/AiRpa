using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>Результат правки модели в открытой вкладке конструктора.</summary>
    internal sealed class WorkflowEditResult
    {
        private WorkflowEditResult(bool ok, string activityId, IEnumerable<string> errors, IEnumerable<string> hints)
        {
            Ok = ok;
            ActivityId = activityId;

            if (errors != null)
            {
                Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
            }

            if (hints != null)
            {
                Hints.AddRange(hints.Where(hint => !string.IsNullOrWhiteSpace(hint)));
            }
        }

        internal bool Ok { get; }

        /// <summary>Идентификатор активности в модели после правки (по нему агент потом задаёт аргументы).</summary>
        internal string ActivityId { get; }

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        internal static WorkflowEditResult Success(string activityId = null, IEnumerable<string> hints = null)
        {
            return new WorkflowEditResult(true, activityId, null, hints);
        }

        internal static WorkflowEditResult Failure(IEnumerable<string> errors, IEnumerable<string> hints = null)
        {
            return new WorkflowEditResult(false, null, errors, hints);
        }

        internal WorkflowEditResult WithActivityId(string activityId)
        {
            return Ok ? Success(activityId ?? ActivityId, Hints) : this;
        }

        internal WorkflowEditResult Merge(WorkflowEditResult other)
        {
            if (other == null)
            {
                return this;
            }

            return new WorkflowEditResult(
                Ok && other.Ok,
                ActivityId ?? other.ActivityId,
                Errors.Concat(other.Errors),
                Hints.Concat(other.Hints));
        }
    }

    /// <summary>
    /// Правка модели открытой вкладки конструктора (этап 4 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Отдельный интерфейс нужен ровно потому, что настоящая реализация (<see cref="DesignerModelSession"/>)
    /// работает с <c>ModelItem</c> и живёт только на UI-потоке. Порядок действий инструмента
    /// (проверить → открыть вкладку → вставить → сохранить → прогнать гейт → откатить при провале)
    /// проверяется юнит-тестами на поддельной сессии.
    /// </para>
    /// </summary>
    internal interface IWorkflowModelSession
    {
        /// <summary>
        /// Вставляет активность в контейнер. Вставка идёт через <c>ModelService</c>/<c>ModelItem</c>:
        /// тогда ViewState и IdRef проставляет сам конструктор, и отображение гарантировано.
        /// </summary>
        WorkflowEditResult InsertActivity(
            PaletteActivitySpec spec,
            string parentActivityId,
            string containerName,
            int? index);

        /// <summary>Задаёт аргумент существующей активности (по её <c>Id</c> из <c>xaml.get_workflow</c>).</summary>
        WorkflowEditResult SetArgument(string activityId, string argumentName, PaletteValue value);

        /// <summary>Сохраняет модель штатным сериализатором конструктора.</summary>
        WorkflowEditResult Save(string filePath);

        /// <summary>Откатывает последнюю правку модели и сохраняет файл заново.</summary>
        WorkflowEditResult Rollback(string filePath);
    }

    /// <summary>Запрос <c>xaml.create_workflow</c>.</summary>
    internal sealed class PaletteCreateWorkflowRequest
    {
        internal string FilePath { get; set; }

        /// <summary>Имя проекта (сборки): без него сборка проекта не попадёт в XAML и типы не резолвятся.</summary>
        internal string ProjectName { get; set; }

        internal string NamespaceName { get; set; }

        internal string ClassName { get; set; }

        /// <summary>Шаги процесса: они станут активностями корневого <see cref="Sequence"/>.</summary>
        internal List<PaletteActivitySpec> Activities { get; } = new List<PaletteActivitySpec>();
    }

    /// <summary>Запрос <c>xaml.add_activity</c>.</summary>
    internal sealed class PaletteAddActivityRequest
    {
        internal string FilePath { get; set; }

        internal PaletteActivitySpec Activity { get; set; }

        /// <summary><c>Id</c> родительской активности (<c>null</c> — корень процесса).</summary>
        internal string ParentActivityId { get; set; }

        /// <summary>Имя контейнера родителя (<c>Activities</c>, <c>Then</c>, <c>Body</c>).</summary>
        internal string ContainerName { get; set; }

        /// <summary>Позиция в коллекции; <c>null</c> — в конец.</summary>
        internal int? Index { get; set; }
    }

    /// <summary>Запрос <c>xaml.set_argument</c>.</summary>
    internal sealed class PaletteSetArgumentRequest
    {
        internal string FilePath { get; set; }

        internal string ActivityId { get; set; }

        internal string ArgumentName { get; set; }

        internal PaletteValue Value { get; set; }
    }

    /// <summary>Запрос <c>xaml.get_workflow</c>.</summary>
    internal sealed class PaletteGetWorkflowRequest
    {
        internal string FilePath { get; set; }

        internal bool IncludeXaml { get; set; }
    }

    /// <summary>
    /// Ответ инструментов палитры: единый формат
    /// <c>{ok, workflowId, filePath, activityId, xaml, errors[], warnings[], hints[]}</c>
    /// (п. 1.3 и 4 плана).
    /// </summary>
    internal sealed class PaletteResult
    {
        private PaletteResult(bool ok)
        {
            Ok = ok;
        }

        internal bool Ok { get; set; }

        internal string WorkflowId { get; set; }

        internal string FilePath { get; set; }

        internal string ActivityId { get; set; }

        internal string Xaml { get; set; }

        internal WorkflowSnapshot Snapshot { get; set; }

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Warnings { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        internal static PaletteResult Success(string filePath, string workflowId = null, string activityId = null)
        {
            return new PaletteResult(true)
            {
                FilePath = filePath,
                WorkflowId = workflowId,
                ActivityId = activityId
            };
        }

        internal static PaletteResult Failure(IEnumerable<string> errors, IEnumerable<string> hints = null)
        {
            var result = new PaletteResult(false);
            result.AddErrors(errors);
            result.AddHints(hints);

            return result;
        }

        internal PaletteResult AddErrors(IEnumerable<string> errors)
        {
            if (errors != null)
            {
                Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
            }

            return this;
        }

        internal PaletteResult AddWarnings(IEnumerable<string> warnings)
        {
            if (warnings != null)
            {
                Warnings.AddRange(warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));
            }

            return this;
        }

        internal PaletteResult AddHints(IEnumerable<string> hints)
        {
            if (hints == null)
            {
                return this;
            }

            foreach (string hint in hints.Where(hint => !string.IsNullOrWhiteSpace(hint)))
            {
                if (!Hints.Contains(hint))
                {
                    Hints.Add(hint);
                }
            }

            return this;
        }

        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "палитра: ок" : "палитра: ОШИБКИ");
            text.AppendLine($"файл: {FilePath ?? "(не задан)"}");
            text.AppendLine($"workflowId: {WorkflowId ?? "(не определён)"}");

            if (!string.IsNullOrWhiteSpace(ActivityId))
            {
                text.AppendLine($"активность: {ActivityId}");
            }

            text.AppendLine($"ошибок: {Errors.Count}, предупреждений: {Warnings.Count}");

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка: " + error);
            }

            foreach (string warning in Warnings)
            {
                text.AppendLine("  предупреждение: " + warning);
            }

            foreach (string hint in Hints)
            {
                text.AppendLine("  подсказка: " + hint);
            }

            return text.ToString();
        }
    }

    /// <summary>Интерфейс палитры для MCP-инструментов (<c>xaml.*</c>).</summary>
    internal interface IPaletteWorkflowService
    {
        PaletteResult CreateWorkflow(PaletteCreateWorkflowRequest request);

        PaletteResult AddActivity(PaletteAddActivityRequest request);

        PaletteResult SetArgument(PaletteSetArgumentRequest request);

        PaletteResult GetWorkflow(PaletteGetWorkflowRequest request);
    }

    /// <summary>
    /// Палитра активностей (этап 4 конвейера генерации XAML, вариант 3):
    /// создание процесса, вставка активности, установка аргумента и чтение модели.
    /// <para>
    /// Общий конвейер: проверка запроса по реестру → открытие вкладки (<see cref="DesignerOpenService"/>)
    /// → правка модели через <c>ModelService</c> → сохранение штатным сериализатором конструктора →
    /// <see cref="WorkflowValidationGate"/> → откат при провале. Ни один инструмент не пишет XAML текстом:
    /// иначе теряются ViewState, IdRef и ссылки, и диаграмма не отображается (п. 1.1 плана).
    /// </para>
    /// </summary>
    internal sealed class PaletteWorkflowService : IPaletteWorkflowService
    {
        private const string UiThreadHint =
            "если ошибка говорит о занятом UI-потоке, закройте модальные окна конструктора и повторите вызов";

        private const string OpenFirstHint =
            "откройте процесс в конструкторе (xaml.create_workflow или DesignerOpenService.OpenOrActivate) " +
            "и повторите вызов";

        private readonly ActivityPaletteRegistry _registry;
        private readonly ActivityPaletteFactory _factory;
        private readonly IWorkflowDesignerHost _host;
        private readonly IDesignerUiThread _uiThread;
        private readonly Func<DesignerTab, IWorkflowModelSession> _sessionFactory;
        private readonly IWorkflowValidationGate _gate;
        private readonly Action<string> _log;
        private readonly DesignerOpenService _openService;

        internal PaletteWorkflowService(
            ActivityPaletteRegistry registry = null,
            IWorkflowDesignerHost host = null,
            IDesignerUiThread uiThread = null,
            Func<DesignerTab, IWorkflowModelSession> sessionFactory = null,
            IWorkflowValidationGate gate = null,
            Action<string> log = null)
        {
            _registry = registry ?? ActivityPaletteRegistry.Default;
            _factory = new ActivityPaletteFactory(_registry);
            _host = host;
            _uiThread = uiThread;
            _sessionFactory = sessionFactory ?? (tab => tab?.Designer == null ? null : new DesignerModelSession(tab.Designer));
            _gate = gate;
            _log = log;

            if (_host != null && _uiThread != null)
            {
                _openService = new DesignerOpenService(_host, _uiThread, log);
            }
        }

        internal ActivityPaletteRegistry Registry => _registry;

        /// <summary>Сервис для запущенной студии: реальная среда конструктора, UI-поток и гейт по умолчанию.</summary>
        internal static PaletteWorkflowService ForStudio(Action<string> log = null)
        {
            return new PaletteWorkflowService(
                registry: null,
                host: new StudioWorkflowDesignerHost(),
                uiThread: DispatcherDesignerUiThread.ForStudio(),
                sessionFactory: null,
                gate: null,
                log: log);
        }

        /// <summary>Создаёт процесс из списка шагов палитры, сохраняет и открывает вкладку конструктора.</summary>
        public PaletteResult CreateWorkflow(PaletteCreateWorkflowRequest request)
        {
            if (request == null)
            {
                return PaletteResult.Failure(new[] { "xaml.request: запрос не задан" });
            }

            string pathError = ValidatePath(request.FilePath, out string fullPath);
            if (pathError != null)
            {
                return PaletteResult.Failure(new[] { pathError });
            }

            string optionsError = BuildOptions(request, fullPath, out WorkflowCanonicalizationOptions options);
            if (optionsError != null)
            {
                return PaletteResult.Failure(new[] { optionsError }, new[] { WorkflowCanonicalizationOptionsForProjectHint });
            }

            Activity implementation;
            var hints = new List<string>();

            if (request.Activities.Count == 0)
            {
                implementation = new Sequence();
                hints.Add("шаги не заданы: создан пустой Sequence — добавьте активности через xaml.add_activity");
            }
            else
            {
                var sequence = new Sequence();
                var errors = new List<string>();

                foreach (PaletteActivitySpec spec in request.Activities)
                {
                    PaletteBuildResult build = _factory.Build(spec);
                    if (!build.Ok)
                    {
                        errors.AddRange(build.Errors);
                        hints.AddRange(build.Hints);
                        continue;
                    }

                    sequence.Activities.Add(build.Activity);
                }

                if (errors.Count > 0)
                {
                    return PaletteResult.Failure(errors, hints.Concat(new[] { ActivityPaletteFactory.ExpressionPolicyHint }));
                }

                implementation = sequence;
            }

            var gate = _gate ?? new WorkflowValidationGate(new WorkflowValidationOptions
            {
                ProjectAssemblyName = options.ProjectAssemblyName,
                ExpectedExpressionActivityEditor = options.ExpressionActivityEditor
            });

            WorkflowBuildResult saved = new WorkflowBuildService(options, gate, _openService)
                .Save(implementation, fullPath, hints);

            if (!saved.Ok)
            {
                PaletteResult failure = PaletteResult.Failure(saved.Errors, saved.Hints.Concat(saved.Warnings));
                failure.FilePath = saved.FilePath ?? fullPath;
                failure.WorkflowId = saved.WorkflowId;
                failure.Xaml = saved.Xaml;
                return failure;
            }

            PaletteResult result = PaletteResult.Success(fullPath, saved.WorkflowId);
            result.Xaml = saved.Xaml;
            result.AddWarnings(saved.Warnings);
            result.AddHints(saved.Hints);

            AppendOpenResult(result, fullPath);
            return result;
        }

        /// <summary>Вставляет активность в открытый (или только что открытый) процесс.</summary>
        public PaletteResult AddActivity(PaletteAddActivityRequest request)
        {
            if (request?.Activity == null)
            {
                return PaletteResult.Failure(new[] { "xaml.activity: не задана активность для вставки" });
            }

            string pathError = ValidatePath(request.FilePath, out string fullPath);
            if (pathError != null)
            {
                return PaletteResult.Failure(new[] { pathError });
            }

            PaletteBuildResult build = _factory.Build(request.Activity);
            if (!build.Ok)
            {
                return PaletteResult.Failure(build.Errors, build.Hints.Concat(new[] { ActivityPaletteFactory.ExpressionPolicyHint }));
            }

            // Контейнер принадлежит родительской активности: проверяется по снимку процесса.
            string containerError = VerifyContainer(fullPath, request);
            if (containerError != null)
            {
                return PaletteResult.Failure(new[] { containerError });
            }

            PaletteResult opened = EnsureOpen(fullPath);
            if (!opened.Ok)
            {
                return opened;
            }

            return Edit(fullPath, opened, session =>
            {
                WorkflowEditResult insert = session.InsertActivity(
                    request.Activity, request.ParentActivityId, request.ContainerName, request.Index);

                if (!insert.Ok)
                {
                    return insert;
                }

                return insert.Merge(session.Save(fullPath));
            });
        }

        /// <summary>Задаёт аргумент активности (выражение или литерал) и сохраняет процесс.</summary>
        public PaletteResult SetArgument(PaletteSetArgumentRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ActivityId))
            {
                return PaletteResult.Failure(new[] { "xaml.activityId: не задан идентификатор активности" });
            }

            if (string.IsNullOrWhiteSpace(request.ArgumentName))
            {
                return PaletteResult.Failure(new[] { "xaml.argument: не задано имя аргумента" });
            }

            if (request.Value == null || (!request.Value.IsExpression && !request.Value.HasLiteral))
            {
                return PaletteResult.Failure(
                    new[] { "xaml.value: не задано ни expression, ни value" },
                    new[] { "пример: expression: \"DateTime.Now.ToString()\" или value: \"текст\"" });
            }

            string pathError = ValidatePath(request.FilePath, out string fullPath);
            if (pathError != null)
            {
                return PaletteResult.Failure(new[] { pathError });
            }

            PaletteResult reported = VerifyTarget(fullPath, request.ActivityId, request.ArgumentName);
            if (!reported.Ok && reported.Errors.Count > 0)
            {
                return reported;
            }

            PaletteResult opened = EnsureOpen(fullPath);
            if (!opened.Ok)
            {
                return opened;
            }

            return Edit(fullPath, opened, session =>
                session.SetArgument(request.ActivityId, request.ArgumentName, request.Value)
                    .Merge(session.Save(fullPath)));
        }

        /// <summary>Читает модель процесса: активности, идентификаторы, аргументы и выражения.</summary>
        public PaletteResult GetWorkflow(PaletteGetWorkflowRequest request)
        {
            if (request == null)
            {
                return PaletteResult.Failure(new[] { "xaml.request: запрос не задан" });
            }

            WorkflowSnapshot snapshot = WorkflowModelReader.Read(request.FilePath);
            if (!snapshot.Ok)
            {
                PaletteResult failure = PaletteResult.Failure(snapshot.Errors, snapshot.Hints);
                failure.FilePath = snapshot.FilePath;
                failure.WorkflowId = snapshot.WorkflowId;
                return failure;
            }

            PaletteResult result = PaletteResult.Success(snapshot.FilePath, snapshot.WorkflowId);
            result.Snapshot = snapshot;

            if (request.IncludeXaml)
            {
                try
                {
                    result.Xaml = File.ReadAllText(snapshot.FilePath);
                }
                catch (Exception ex)
                {
                    result.AddWarnings(new[] { ErrorText.Describe("текст XAML не прочитан", ex) });
                }
            }

            return result;
        }

        /// <summary>Проверки запроса <c>xaml.set_argument</c> до открытия вкладки: активность и аргумент существуют.</summary>
        private PaletteResult VerifyTarget(string fullPath, string activityId, string argumentName)
        {
            WorkflowSnapshot snapshot = WorkflowModelReader.Read(fullPath);

            // Читать нечего (файла нет или он битый) — пусть решает открытие вкладки: у него понятнее ошибка.
            if (!snapshot.Ok)
            {
                return PaletteResult.Failure(snapshot.Errors, snapshot.Hints);
            }

            WorkflowActivitySnapshot activity = snapshot.Find(activityId);
            if (activity == null)
            {
                return PaletteResult.Failure(
                    new[] { $"активность «{activityId}» не найдена в процессе {snapshot.FilePath}" },
                    new[] { "идентификаторы активностей берутся из xaml.get_workflow" });
            }

            PaletteActivityDescriptor descriptor = _registry.Find(activity.TypeName);
            if (descriptor != null)
            {
                PaletteArgumentDescriptor argument = descriptor.FindArgument(argumentName);
                if (argument == null)
                {
                    return PaletteResult.Failure(
                        new[] { $"у активности «{descriptor.Key}» нет аргумента «{argumentName}»; {descriptor.DescribeArguments()}" });
                }
            }

            return PaletteResult.Success(fullPath, snapshot.WorkflowId, activity.Id);
        }

        /// <summary>Открывает вкладку и выполняет правку на UI-потоке; затем гейт и откат при провале.</summary>
        /// <summary>
        /// Проверка контейнера до открытия вкладки: контейнер принадлежит родительской активности,
        /// поэтому родитель берётся из снимка процесса (<c>root</c> — корневой Sequence).
        /// </summary>
        private string VerifyContainer(string fullPath, PaletteAddActivityRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ContainerName))
            {
                return null;
            }

            WorkflowSnapshot snapshot = WorkflowModelReader.Read(fullPath);
            if (!snapshot.Ok)
            {
                // Файла нет или он битый — об этом понятнее скажет открытие вкладки.
                return null;
            }

            WorkflowActivitySnapshot parent = string.IsNullOrWhiteSpace(request.ParentActivityId)
                ? snapshot.Root
                : snapshot.Find(request.ParentActivityId);

            if (parent == null)
            {
                return $"активность «{request.ParentActivityId}» не найдена в процессе {snapshot.FilePath}: " +
                       "вложить активность некуда";
            }

            PaletteActivityDescriptor parentDescriptor = _registry.Find(parent.TypeName);
            return ValidateContainer(parentDescriptor, request.ContainerName);
        }

        private PaletteResult Edit(string fullPath, PaletteResult opened, Func<IWorkflowModelSession, WorkflowEditResult> edit)
        {
            WorkflowEditResult outcome;
            IWorkflowModelSession session = null;
            WorkflowSnapshot before = WorkflowModelReader.Read(fullPath);



            try
            {
                outcome = _uiThread.Invoke(() =>
                {
                    DesignerTab tab = FindTab(fullPath);
                    session = _sessionFactory(tab);

                    if (session == null)
                    {
                        return WorkflowEditResult.Failure(
                            new[] { $"вкладка {fullPath} не содержит живого конструктора" },
                            new[] { "перезагрузите вкладку (DesignerOpenService.Reload) и повторите вызов" });
                    }

                    return edit(session);
                });
            }
            catch (Exception ex)
            {
                return Failure(fullPath, opened, new[] { ErrorText.Describe($"правка модели {fullPath} не выполнена", ex) });
            }

            if (outcome == null || !outcome.Ok)
            {
                PaletteResult failure = Failure(fullPath, opened,
                    (IEnumerable<string>)outcome?.Errors ?? new[] { "правка модели не выполнена" });
                failure.AddHints(outcome?.Hints);
                return failure;
            }

            WorkflowValidationReport report = ValidateFile(fullPath);
            if (!report.Ok)
            {
                Rollback(session, fullPath);

                PaletteResult failure = Failure(fullPath, opened, report.Errors);
                failure.AddWarnings(report.Warnings);
                failure.AddHints(report.Hints);
                failure.AddHints(new[]
                {
                    "правка отменена (UndoEngine конструктора), файл процесса не изменён — исправьте аргументы и повторите"
                });

                return failure;
            }

            WorkflowSnapshot snapshot = WorkflowModelReader.Read(fullPath);

            PaletteResult result = PaletteResult.Success(
                fullPath,
                snapshot.WorkflowId ?? opened.WorkflowId,
                FindNewActivityId(before, snapshot) ?? outcome.ActivityId);
            result.Snapshot = snapshot.Ok ? snapshot : null;
            result.AddWarnings(report.Warnings);
            result.AddHints(report.Hints);
            result.AddHints(outcome.Hints);
            result.AddHints(opened.Hints);

            return result;
        }

        private void Rollback(IWorkflowModelSession session, string fullPath)
        {
            if (session == null || _uiThread == null)
            {
                return;
            }

            try
            {
                WorkflowEditResult rollback = _uiThread.Invoke(() => session.Rollback(fullPath));
                if (rollback != null && !rollback.Ok)
                {
                    Log($"откат правки {fullPath} не выполнен: {string.Join("; ", rollback.Errors)}");
                }
            }
            catch (Exception ex)
            {
                Log(ErrorText.Describe($"откат правки {fullPath} не выполнен", ex));
            }
        }

        private WorkflowValidationReport ValidateFile(string fullPath)
        {
            IWorkflowValidationGate gate = _gate ?? new WorkflowValidationGate(new WorkflowValidationOptions());
            return gate.Validate(fullPath);
        }

        private PaletteResult EnsureOpen(string fullPath)
        {
            if (_openService == null)
            {
                return PaletteResult.Failure(
                    new[] { "среда конструктора недоступна: палитра создана без вкладок и UI-потока" },
                    new[] { OpenFirstHint });
            }

            DesignerOpenResult opened = _openService.OpenOrActivate(fullPath);
            if (!opened.Ok)
            {
                PaletteResult failure = PaletteResult.Failure(opened.Errors, opened.Hints.Concat(new[] { UiThreadHint }));
                failure.FilePath = opened.FilePath ?? fullPath;
                failure.WorkflowId = opened.WorkflowId;
                return failure;
            }

            var result = PaletteResult.Success(fullPath, opened.WorkflowId);
            result.AddHints(opened.Hints);
            return result;
        }

        private void AppendOpenResult(PaletteResult result, string fullPath)
        {
            if (_openService == null)
            {
                result.AddHints(new[] { "вкладка не открыта: среда конструктора недоступна" });
                return;
            }

            DesignerOpenResult opened = _openService.OpenOrActivate(fullPath);
            if (!opened.Ok)
            {
                result.Ok = false;
                result.AddErrors(opened.Errors);
                result.AddHints(opened.Hints);
                result.AddHints(new[] { "файл записан и валиден, но вкладка конструктора не открылась" });
                return;
            }

            result.WorkflowId = result.WorkflowId ?? opened.WorkflowId;
            result.AddHints(opened.Hints);
        }

        private DesignerTab FindTab(string fullPath)
        {
            IReadOnlyList<DesignerTab> tabs = _host?.GetOpenTabs();
            return tabs?.FirstOrDefault(tab =>
                tab != null && DesignerOpenService.PathsEqual(tab.FilePath, fullPath));
        }

        private PaletteResult Failure(string fullPath, PaletteResult opened, IEnumerable<string> errors)
        {
            PaletteResult failure = PaletteResult.Failure(errors);
            failure.FilePath = fullPath;
            failure.WorkflowId = opened?.WorkflowId;
            return failure;
        }

        /// <summary>
        /// Идентификатор активности, появившейся после правки: сравниваются пути до и после.
        /// WF присваивает свои <c>Activity.Id</c> не сразу, поэтому адресом служит путь в дереве.
        /// </summary>
        private static string FindNewActivityId(WorkflowSnapshot before, WorkflowSnapshot after)
        {
            if (after?.Root == null)
            {
                return null;
            }

            var known = new HashSet<string>(
                (before?.All() ?? Enumerable.Empty<WorkflowActivitySnapshot>())
                .Select(node => node.Id)
                .Where(id => !string.IsNullOrEmpty(id)),
                StringComparer.OrdinalIgnoreCase);

            return after.All()
                .Where(node => !string.IsNullOrEmpty(node.Id) && !known.Contains(node.Id))
                .Select(node => node.Id)
                .FirstOrDefault();
        }

        private static string ValidatePath(string filePath, out string fullPath)
        {
            fullPath = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return "xaml.path: путь к файлу процесса не задан";
            }

            try
            {
                fullPath = Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                return ErrorText.Describe($"путь к файлу процесса недопустим: {filePath}", ex);
            }

            return fullPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"xaml.path: файл процесса должен иметь расширение .xaml — {fullPath}";
        }

        private const string WorkflowCanonicalizationOptionsForProjectHint =
            "имя проекта (сборки) нужно для TextExpression.ReferencesForImplementation: без него типы проекта " +
            "в конструкторе не резолвятся; по умолчанию берётся имя папки файла";

        private static string ValidateContainer(PaletteActivityDescriptor descriptor, string containerName)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(containerName))
            {
                return null;
            }

            PaletteContainerDescriptor container = descriptor.FindContainer(containerName);
            if (container == null)
            {
                return $"у активности «{descriptor.Key}» нет контейнера «{containerName}»; {descriptor.DescribeContainers()}";
            }

            return container.NeedsFlowStep
                ? $"контейнер «{container.Name}» активности «{descriptor.Key}» в базовом MVP не поддерживается"
                : null;
        }

        private static string BuildOptions(
            PaletteCreateWorkflowRequest request,
            string fullPath,
            out WorkflowCanonicalizationOptions options)
        {
            options = null;

            string projectName = !string.IsNullOrWhiteSpace(request.ProjectName)
                ? request.ProjectName.Trim()
                : new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? ".").Name;

            if (string.IsNullOrWhiteSpace(projectName))
            {
                return "xaml.project: имя проекта не задано и не выводится из папки файла";
            }

            try
            {
                string namespaceName = string.IsNullOrWhiteSpace(request.NamespaceName)
                    ? WorkflowCanonicalizationOptions.NamespaceFromProjectName(projectName)
                    : request.NamespaceName.Trim();

                string className = string.IsNullOrWhiteSpace(request.ClassName)
                    ? WorkflowCanonicalizationOptions.ClassNameFromFilePath(fullPath)
                    : request.ClassName.Trim();

                options = new WorkflowCanonicalizationOptions(namespaceName, className, projectName);
                return null;
            }
            catch (Exception ex)
            {
                return ErrorText.Describe($"параметры канонизации не приняты: проект «{projectName}»", ex);
            }
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
                System.Diagnostics.Debug.WriteLine("Лог палитры недоступен: " + ErrorText.Of(ex));
            }
        }
    }
}
