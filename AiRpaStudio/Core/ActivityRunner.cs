using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Presentation.View;
using System.Activities.Validation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;

using FW.EasyMvvm.Core;
using RPA.Enums;
using RPA.Helpers;
using RPA.Runner;
using RPA.VsProject;
using RPA.Chat.Services.Workflow;

namespace RPA.Core
{
    /// <summary>
    /// Студийный запуск WF-процесса: загрузка в дизайнер, отладка (точки останова, подсветка,
    /// субворкфлоу) и прогон.
    /// <para>
    /// Прогон исполняется на фоновом потоке: <see cref="WorkflowApplication.Run()"/> — синхронный
    /// вызов до первой точки простоя/завершения, и на UI-потоке он замораживал бы студию.
    /// Все обновления UI (лог, подсветка, read-only дизайнера, состояние) маршалятся через
    /// <see cref="Dispatcher"/>. Субворкфлоу запускается по трекинг-событию и резюмирует закладку
    /// родителя после своего завершения — поллинг по таймеру больше не нужен.
    /// </para>
    /// </summary>
    internal class ActivityRunner : BaseModel
    {
        private readonly Action<string, int> _log = CustomWfDesigner.GetInstance().Log;

        private WorkflowDesignerEx _workflowDesignerEx;
        private CustomTrackingParticipant _trackingParticipant;
        private WorkflowApplication _wfApp;
        private AutoResetEvent _resumeRuntimeFromHost;

        private string _bookmarkName;
        private ActivityRunner _subWorkflowActivityRunner;
        private readonly Dictionary<string, object> _arguments;
        private WorkflowState _workflowState;
        private bool _isNeedPause;

        private Dictionary<object, SourceLocation> _runtimeSourceLocationMapping;

        /// <summary>
        /// Завершение прогона (Completed/Aborted/StartingError). Поднимается на потоке, где прогон
        /// закончился, чтобы родитель мог возобновить свою закладку тоже не на UI-потоке.
        /// </summary>
        internal event Action<WorkflowState> RunFinished;

        public WorkflowState WorkflowState
        {
            get => _workflowState;
            set
            {
                _workflowState = value;
                OnPropertyChanged();
            }
        }

        public ActivityRunner(Dictionary<string, object> arguments = null)
        {
            _arguments = arguments;
        }

        internal void Run(string pathToXaml = null)
        {
            _isNeedPause = false;

            if (WorkflowState == WorkflowState.Complete)
            {
                return;
            }

            try
            {
                if (_wfApp == null)
                {
                    if (string.IsNullOrWhiteSpace(pathToXaml))
                    {
                        throw new InvalidOperationException("Не указан XAML-файл для запуска.");
                    }

                    Start(pathToXaml);
                }
                else
                {
                    // Продолжить с точки останова/паузы: разбудить фоновый поток прогона.
                    _resumeRuntimeFromHost?.Set();
                }
            }
            catch (Exception ex)
            {
                Log(ErrorText.Of(ex), RpaCommon.Enums.LogLevel.Error);
                OnUi(() => WorkflowState = WorkflowState.StartingError);
                SetDesignerIsReadonly(false);
                RaiseRunFinished(WorkflowState.StartingError);
            }
        }

        private void Start(string pathToXaml)
        {
            _resumeRuntimeFromHost = new AutoResetEvent(false);

            // CP-08: зависимости робота ищутся в его выходном каталоге — до десериализации XAML,
            // иначе собственные пакеты робота (bin\<Config>[\<TFM>]) не находятся и WF падает.
            RobotAssemblyResolver.InstallForProject(CustomWfDesigner.GetInstance().Project?.ProjectDirPath);

            _workflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(
                pathToXaml,
                CustomWfDesigner.GetInstance().GetLooseRegistryFor(pathToXaml));
            SetDesignerIsReadonly(true);

            CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = _workflowDesignerEx;
            DocGroupHelper.GetInstance().SetSelectedTab(_workflowDesignerEx);

            DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(_workflowDesignerEx);
            foreach (WorkflowDesignerEx designer in CustomWfDesigner.GetInstance().WorkflowDesigners)
            {
                designer.LastBreakpoint = null;
            }
            _workflowDesignerEx.LastBreakpoint = null;

            // Загрузка активности для прогона: обычный робот — из XAML, WF-проект со вложенными процессами — из сборки (OR-02).
            Activity activity = LoadRunActivity(pathToXaml);
            if (activity == null)
            {
                throw new InvalidOperationException("Не удалось загрузить активность для прогона.");
            }
            // Шаг 6 плана LooseWorkflowRun: ошибки, невидимые загрузке, ловятся до старта прогона.
            ValidateForRun(activity);

            // Шаг 7 плана LooseWorkflowRun: рантайм-маппинг по DebugSymbol покрывает все скрипты процесса.
            _runtimeSourceLocationMapping = RuntimeSourceLocationMap.Build(activity);

            // Приложение создаёт общий движок: тот же трекер и те же аргументы, что и у headless-прогона.
            _wfApp = WorkflowRunEngine.CreateApplication(activity, _arguments, out _trackingParticipant);

            _trackingParticipant.TrackingEventArgsRecieved += OnTracking;
            _wfApp.Completed = OnCompleted;
            _wfApp.OnUnhandledException = OnUnhandledException;
            _wfApp.Aborted = OnAborted;

            OnUi(() => WorkflowState = WorkflowState.Run);

            var runThread = new Thread(RunWorkflowApplication)
            {
                IsBackground = true,
                Name = "StudioWorkflowRun"
            };
            runThread.Start();
        }
        /// <summary>
        /// Грузит активность для прогона. Обычный робот (project.json или однофайловый XAML без
        /// вложенных ссылок) — из текста XAML общим движком, как и раньше. WF-проект со вложенными
        /// процессами (<c>local:SubProcessN</c>) — из собранной <c>RPA-*.dll</c>: эти CLR-типы
        /// существуют только после сборки, поэтому путь прогона совпадает с раннером (<c>OR-02</c>),
        /// а дизайнер при этом остаётся loose.
        /// </summary>
        private Activity LoadRunActivity(string pathToXaml)
        {
            return LoadRunActivity(
                pathToXaml,
                CustomWfDesigner.GetInstance().Project,
                _workflowDesignerEx.Text);
        }

        /// <summary>
        /// Тестируемое ядро выбора пути прогона: решение зависит только от проекта и текста открытого
        /// XAML, а не от живой студии. WF-проект со вложенными процессами грузится из собранной
        /// <c>RPA-*.dll</c>, обычный робот — из текста XAML в <see cref="DynamicActivity"/>.
        /// </summary>
        internal static Activity LoadRunActivity(string pathToXaml, RPA.Entities.Project project, string xamlText)
        {
            if (project != null
                && project.IsWorkflowProject
                && !string.IsNullOrWhiteSpace(project.ProjectFilePath))
            {
                Activity activity = TryLoadOrBuildFromProject(project.ProjectFilePath, pathToXaml, out string reason);

                if (activity != null)
                {
                    return activity;
                }

                throw new InvalidOperationException(
                    "Робот содержит вложенные XAML-процессы, поэтому для запуска нужна собранная сборка проекта."
                    + Environment.NewLine + (reason ?? string.Empty));
            }

            // XAML грузится общим headless-движком: путь прогона один для студии и раннера (OR-02).
            DynamicActivity dynamicActivity = WorkflowRunEngine.LoadActivityFromText(xamlText) as DynamicActivity;

            if (dynamicActivity == null)
            {
                throw new InvalidOperationException("Загруженный XAML не является DynamicActivity.");
            }

            return dynamicActivity;
        }

        /// <summary>
        /// Находит собранного робота по <c>.csproj</c> (та же логика, что у раннера) и грузит активность
        /// из его сборки. Возвращает <c>null</c> и причину в <paramref name="reason"/>, если сборки нет
        /// или она не загрузилась.
        /// </summary>
        internal static Activity TryLoadFromBuiltAssembly(string projectFilePath, string workflowPath, out string reason)
        {
            reason = null;

            RunnerOptions options = RunnerOptions.FromProject(projectFilePath, workflowPath);

            if (!RobotLocator.TryResolve(options, out RobotLocation location, out string resolveError))
            {
                reason = resolveError;
                return null;
            }

            try
            {
                return WorkflowRunEngine.LoadActivityFromAssembly(location.AssemblyPath, location.TypeName);
            }
            catch (Exception ex)
            {
                reason = "Не удалось загрузить активность «" + location.TypeName
                    + "» из сборки «" + location.AssemblyPath + "»: " + ErrorText.Of(ex);
                return null;
            }
        }



        /// <summary>
        /// Путь прогона WF-проекта с автосборкой (шаг 5 плана <c>LooseWorkflowRun</c>): если сборки
        /// нет или она устарела относительно XAML проекта, проект собирается через
        /// <see cref="VsProjectBuilder"/> и только потом активность грузится из сборки. Сборка живёт
        /// только здесь, в студии — раннер проект не собирает (страж <c>RunnerSourceGuardTests</c>).
        /// </summary>
        internal static Activity TryLoadOrBuildFromProject(string projectFilePath, string workflowPath, out string reason)
        {
            reason = null;

            RunnerOptions options = RunnerOptions.FromProject(projectFilePath, workflowPath);

            bool resolved = RobotLocator.TryResolve(options, out RobotLocation location, out string resolveError);

            if (!resolved || IsProjectOutdated(location, projectFilePath))
            {
                NormalizeWorkflowXamlBeforeBuild(Path.GetDirectoryName(projectFilePath));

                VsBuildResult build = VsProjectBuilder.Build(projectFilePath, "Debug");

                if (!build.Success)
                {
                    reason = "Автосборка перед запуском не прошла (код " + build.ExitCode + ")."
                        + Environment.NewLine + string.Join(Environment.NewLine, build.Errors)
                        + Environment.NewLine + (resolveError ?? string.Empty);
                    return null;
                }
            }

            return TryLoadFromBuiltAssembly(projectFilePath, workflowPath, out reason);
        }

        /// <summary>
        /// Сборка устарела, если какой-либо XAML проекта новее выбранной сборки: правка XAML в
        /// дизайнере не трогает dll, и прогон обязан пересобрать проект перед запуском. Каталоги
        /// <c>bin</c>/<c>obj</c> в сравнение не входят.
        /// </summary>
        internal static bool IsProjectOutdated(RobotLocation location, string projectFilePath)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.AssemblyPath) || !File.Exists(location.AssemblyPath))
            {
                return true;
            }

            string projectDirectory = Path.GetDirectoryName(projectFilePath);
            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                return false;
            }

            DateTime assemblyTime = File.GetLastWriteTimeUtc(location.AssemblyPath);

            foreach (string xaml in Directory.EnumerateFiles(projectDirectory, "*.xaml", SearchOption.AllDirectories))
            {
                if (IsBuildArtifactDirectory(xaml, projectDirectory))
                {
                    continue;
                }

                if (File.GetLastWriteTimeUtc(xaml) > assemblyTime)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBuildArtifactDirectory(string path, string projectDirectory)
        {
            string relative = path.Substring(projectDirectory.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            return segments.Length > 0
                && (string.Equals(segments[0], "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segments[0], "obj", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Приводит XAML-файлы проекта к каноническому виду студии перед автосборкой.
        /// Конструктор WF пишет presentation-атрибуты (<c>sap</c>/<c>sap2010</c>) в корень
        /// и в <c>ViewState</c>; без <c>mc:Ignorable="sap sap2010"</c> XamlBuildTask не может
        /// их пропустить и сборка падает с XC1031. Используется единственный нормализатор
        /// <see cref="WorkflowXamlText"/> (карточка AG-07), поэтому второй набор правил не заводится.
        /// </summary>
        internal static void NormalizeWorkflowXamlBeforeBuild(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                return;
            }

            foreach (string xaml in Directory.EnumerateFiles(projectDirectory, "*.xaml", SearchOption.AllDirectories))
            {
                if (IsBuildArtifactDirectory(xaml, projectDirectory))
                {
                    continue;
                }

                try
                {
                    string text = File.ReadAllText(xaml);
                    WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(text);

                    if (result.Kind == WorkflowXamlKind.Workflow && result.Changed)
                    {
                        File.WriteAllText(xaml, result.Text, new UTF8Encoding(false));
                    }
                }
                catch (IOException)
                {
                    // Файл занят (дизайнер, антивирус, OneDrive) — оставляем как есть; сборка сообщит сама.
                }
                catch (UnauthorizedAccessException)
                {
                    // Файл только для чтения — оставляем как есть; сборка сообщит сама.
                }
            }
        }


        /// <summary>
        /// Валидация активности перед запуском (шаг 6 плана <c>LooseWorkflowRun</c>): ловит ошибки,
        /// невидимые загрузке (<c>Assign</c> без <c>To</c>/<c>Value</c> и т.п.), до старта прогона.
        /// Совпадает с <c>--validate</c> раннера — при ошибках валидации бросается
        /// <see cref="InvalidOperationException"/> с перечнем замечаний.
        /// </summary>
        internal static void ValidateForRun(Activity activity)
        {
            if (activity == null)
            {
                throw new ArgumentNullException(nameof(activity));
            }

            ValidationResults results;
            try
            {
                results = ActivityValidationServices.Validate(activity);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("проверка workflow не выполнена: " + ErrorText.Of(ex));
            }

            List<ValidationError> errors = results?.Errors?.ToList() ?? new List<ValidationError>();

            if (errors.Count == 0)
            {
                return;
            }

            var text = new StringBuilder();
            text.Append("проверка workflow нашла ошибок: ").Append(errors.Count);

            foreach (ValidationError error in errors)
            {
                text.AppendLine().Append("  - ").Append(error.Message);

                if (!string.IsNullOrWhiteSpace(error.Id))
                {
                    text.Append(" (").Append(error.Id).Append(')');
                }
            }

            throw new InvalidOperationException(text.ToString());
        }

        private void RunWorkflowApplication()
        {
            try
            {
                _wfApp.Run();
            }
            catch (Exception ex)
            {
                Log(ErrorText.Of(ex), RpaCommon.Enums.LogLevel.Error);
                OnUi(() =>
                {
                    SetDesignerIsReadonly(false);
                    WorkflowState = WorkflowState.StartingError;
                });
                RaiseRunFinished(WorkflowState.StartingError);
            }
        }

        private void OnTracking(object sender, TrackingEventArgs trackingEventArgs)
        {
            TrackHandler.HandleTrackingRecord(
                trackingEventArgs,
                _workflowDesignerEx,
                _resumeRuntimeFromHost,
                _isNeedPause,
                _runtimeSourceLocationMapping,
                out Dictionary<string, object> subWorkflowArguments);

            if (subWorkflowArguments != null)
            {
                LaunchSubWorkflow(subWorkflowArguments);
            }
        }

        private void OnCompleted(WorkflowApplicationCompletedEventArgs args)
        {
            try
            {
                foreach (KeyValuePair<string, object> item in args.Outputs)
                {
                    Log($"[{item.Key}] {item.Value}" + Environment.NewLine, RpaCommon.Enums.LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString(), RpaCommon.Enums.LogLevel.Error);
            }

            OnUi(() =>
            {
                SetDesignerIsReadonly(false);
                if (_workflowDesignerEx != null)
                {
                    _workflowDesignerEx.DebugManagerView.CurrentLocation = null;
                }

                WorkflowState = WorkflowState.Complete;
            });

            _resumeRuntimeFromHost = null;
            _isNeedPause = false;
            if (_workflowDesignerEx != null)
            {
                _workflowDesignerEx.LastBreakpoint = null;
            }

            RaiseRunFinished(WorkflowState.Complete);
        }

        private UnhandledExceptionAction OnUnhandledException(WorkflowApplicationUnhandledExceptionEventArgs args)
        {
            Exception ex = Flatten(args.UnhandledException);

            var text = new StringBuilder();
            _ = text.AppendLine($"Activity: {args.ExceptionSource?.DisplayName}");
            _ = text.AppendLine(ex.Message);
            _ = text.AppendLine(ex.StackTrace);

            Log(text.ToString(), RpaCommon.Enums.LogLevel.Error);

            _isNeedPause = false;

            return UnhandledExceptionAction.Abort;
        }

        private void OnAborted(WorkflowApplicationAbortedEventArgs args)
        {
            OnUi(() =>
            {
                SetDesignerIsReadonly(false);
                if (_workflowDesignerEx != null)
                {
                    _workflowDesignerEx.DebugManagerView.CurrentLocation = null;
                }

                WorkflowState = WorkflowState.Aborted;
            });

            _resumeRuntimeFromHost = null;
            _isNeedPause = false;
            if (_workflowDesignerEx != null)
            {
                _workflowDesignerEx.LastBreakpoint = null;
            }

            RaiseRunFinished(WorkflowState.Aborted);
        }

        private void LaunchSubWorkflow(Dictionary<string, object> subWorkflowArguments)
        {
            if (!subWorkflowArguments.TryGetValue("PathToXamlActivity", out object rawPath)
                || string.IsNullOrWhiteSpace(rawPath?.ToString()))
            {
                return;
            }

            string relativePath = rawPath.ToString();
            _ = subWorkflowArguments.Remove("PathToXamlActivity");

            string projectDir = CustomWfDesigner.GetInstance().Project?.ProjectDirPath;
            if (string.IsNullOrWhiteSpace(projectDir))
            {
                return;
            }

            _bookmarkName = relativePath;

            _subWorkflowActivityRunner = new ActivityRunner(subWorkflowArguments);
            _subWorkflowActivityRunner.RunFinished += OnSubWorkflowFinished;

            string pathToXaml = Path.Combine(projectDir, relativePath);

            // Загрузка дочернего дизайнера требует UI-потока; сам прогон дочернего уйдёт в фон.
            OnUi(() => _subWorkflowActivityRunner.Run(pathToXaml));
        }

        private void OnSubWorkflowFinished(WorkflowState state)
        {
            _subWorkflowActivityRunner.RunFinished -= OnSubWorkflowFinished;

            if (state != WorkflowState.Complete)
            {
                Log($"Субворкфлоу «{_bookmarkName}» не завершилось успешно ({state}).", RpaCommon.Enums.LogLevel.Error);
                _wfApp?.Abort($"Субворкфлоу «{_bookmarkName}» не завершилось успешно ({state}).");
                return;
            }

            OnUi(() =>
            {
                CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = _workflowDesignerEx;
                DocGroupHelper.GetInstance().SetSelectedTab(_workflowDesignerEx);
            });

            _resumeRuntimeFromHost = new AutoResetEvent(false);

            // Резюмируем закладку родителя на фоне: WorkflowApplication free-threaded, а продолжение
            // на UI-потоке снова заморозило бы студию.
            var resume = new Thread(() =>
            {
                try
                {
                    _ = _wfApp.ResumeBookmark(_bookmarkName, null);
                }
                catch (Exception ex)
                {
                    Log(ErrorText.Of(ex), RpaCommon.Enums.LogLevel.Error);
                    OnUi(() =>
                    {
                        SetDesignerIsReadonly(false);
                        WorkflowState = WorkflowState.StartingError;
                    });
                    RaiseRunFinished(WorkflowState.StartingError);
                }
            })
            {
                IsBackground = true,
                Name = "StudioWorkflowResume"
            };
            resume.Start();
        }

        public void WorkflowStop()
        {
            _subWorkflowActivityRunner?.WorkflowStop();
            _resumeRuntimeFromHost?.Set();

            if (_wfApp != null)
            {
                // Aborted-обработчик очистит UI и сообщит итог; Set выше разблокирует брейкпоинт/паузу.
                _wfApp.Abort("Stopped by User");
            }
            else
            {
                OnUi(() => WorkflowState = WorkflowState.Aborted);
                RaiseRunFinished(WorkflowState.Aborted);
            }

            Log("Stopped by User", RpaCommon.Enums.LogLevel.Trace);
        }

        internal void WorkflowPause()
        {
            _subWorkflowActivityRunner?.WorkflowPause();
            _isNeedPause = true;
        }

        private void SetDesignerIsReadonly(bool isReadOnly)
        {
            DesignerView designerView = _workflowDesignerEx?.Context.Services.GetService<DesignerView>();
            if (designerView != null)
            {
                _ = designerView.Dispatcher.Invoke(() => designerView.IsReadOnly = isReadOnly);
            }
        }

        private void Log(string message, RpaCommon.Enums.LogLevel level)
        {
            OnUi(() => _log.Invoke(message, (int)level));
        }

        private void RaiseRunFinished(WorkflowState state)
        {
            RunFinished?.Invoke(state);
        }

        private static void OnUi(Action action)
        {
            Dispatcher dispatcher = UiDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
                return;
            }

            action();
        }

        private static Dispatcher UiDispatcher
        {
            get
            {
                Window window = CustomWfDesigner.GetInstance().ViewModel?.View as Window;
                if (window?.Dispatcher != null)
                {
                    return window.Dispatcher;
                }

                return Application.Current?.Dispatcher;
            }
        }

        private static Exception Flatten(Exception exception)
        {
            if (!(exception is AggregateException aggregate))
            {
                return exception;
            }

            if (aggregate.InnerException?.InnerException != null)
            {
                return aggregate.InnerException.InnerException;
            }

            return aggregate.InnerException ?? aggregate;
        }
    }
}
