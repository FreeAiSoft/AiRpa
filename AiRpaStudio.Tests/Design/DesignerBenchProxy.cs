using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Linq;
using RPA.Core.Generation;
using RPA.Helpers;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using AiRpaStudio.Tests.Generation;


using System.Windows.Threading;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Мост к стенду конструктора, работающий в отдельном домене приложения.
/// <para>
/// Зачем домен: отрисовка настоящего <c>WorkflowDesigner</c> заполняет статические кэши WF
/// (словари шаблонов и цветов) объектами <see cref="System.Windows.Freezable"/>, созданными на потоке
/// стенда. Кэши общие на процесс, поэтому после отрисовки 11 тестов темы панели «Активности» падали с
/// <see cref="InvalidOperationException"/> «вызывающий поток не может получить доступ…»: они читали
/// кисти, принадлежащие другому потоку. Домен изолирует эти кэши — вместе с ним они и удаляются.
/// </para>
/// <para>
/// Мост возвращает только сериализуемые отчёты: тесты утверждают по данным, а не по живым объектам WPF.
/// </para>
/// </summary>
public sealed class DesignerBenchProxy : MarshalByRefObject
{
    private const string DomainName = "AiRpaStudio-DesignerBench";

    private static readonly object DomainGate = new object();

    private static DesignerBenchProxy _proxy;

    /// <summary>Мост процесса: домен создаётся при первом обращении и живёт до конца прогона.</summary>
    internal static DesignerBenchProxy Shared
    {
        get
        {
            lock (DomainGate)
            {
                if (_proxy != null)
                {
                    return _proxy;
                }

                var setup = new AppDomainSetup
                {
                    ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                    ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
                };

                AppDomain domain = AppDomain.CreateDomain(DomainName, null, setup);
                _proxy = (DesignerBenchProxy)domain.CreateInstanceAndUnwrap(
                    typeof(DesignerBenchProxy).Assembly.FullName,
                    typeof(DesignerBenchProxy).FullName);

                return _proxy;
            }
        }
    }

    /// <summary>Проверки стенда: STA-поток, маршалинг вызовов, жизнь потока.</summary>
    public DesignerBenchHostInfo HostInfo()
    {
        DesignerTestHost host = DesignerTestHost.Shared;
        int callerThreadId = Thread.CurrentThread.ManagedThreadId;

        (ApartmentState state, int threadId) = host.Invoke(() =>
            (Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.ManagedThreadId));

        return new DesignerBenchHostInfo
        {
            ApartmentState = state.ToString(),
            HostThreadId = threadId,
            KnownHostThreadId = host.ThreadId,
            CallerThreadId = callerThreadId,
            IsAlive = host.IsAlive
        };
    }

    /// <summary>Прогоняет файл через конструктор на стенде.</summary>
    public DesignerBenchReport Render(string xamlPath)
    {
        DesignerRenderReport report = DesignerRenderProbe.Render(xamlPath);
        return DesignerBenchReport.From(report);
    }

    /// <summary>
    /// Показывает окно с заданным заголовком и возвращает то, что заметил страж модальных окон.
    /// </summary>
    public List<string> WindowsSeenWhileShowing(string title)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            using (DesignerModalWindowGuard guard = DesignerModalWindowGuard.Attach())
            {
                var window = new Window
                {
                    Title = title,
                    Width = 200,
                    Height = 120,
                    ShowInTaskbar = false
                };

                try
                {
                    window.Show();
                    host.Pump(TimeSpan.FromMilliseconds(300));
                    guard.Poll();
                }
                finally
                {
                    window.Close();
                }

                return new List<string>(guard.Windows);
            }
        });
    }

    /// <summary>
    /// Сценарий этапа 3 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c> на живом дизайнере:
    /// открытие файла сервисом (<c>DesignerOpenService</c>) → повторное открытие того же файла →
    /// проверка перезаписи открытого файла → попытка генерации в него через <c>WorkflowBuildService</c>.
    /// </summary>
    public DesignerBenchOpenReport OpenTabScenario(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();
            DesignerOpenService service = designerHost.CreateService();

            DesignerOpenResult first = service.OpenOrActivate(xamlPath);
            int activityCount = designerHost.CountActiveActivities();
            DesignerOpenResult second = service.OpenOrActivate(xamlPath);
            DesignerWriteCheck writeCheck = service.CheckWrite(xamlPath);

            WorkflowBuildResult regeneration = new WorkflowBuildService(
                    WorkflowTestSamples.Options(),
                    gate: null,
                    writeCheck: service)
                .Save(WorkflowTestSamples.NestedWorkflow(), xamlPath);

            var report = new DesignerBenchOpenReport
            {
                FilePath = first.FilePath,
                WorkflowId = first.WorkflowId,
                FirstOk = first.Ok,
                Activated = first.Activated,
                ActivityCount = activityCount,
                ActivityCountInfo = designerHost.ActivityCountInfo,
                SecondOk = second.Ok,
                AlreadyOpenOnSecondCall = second.AlreadyOpen,
                SecondActivated = second.Activated,
                OpenCount = designerHost.OpenCount,
                CloseCount = designerHost.CloseCount,
                WriteBlocked = !writeCheck.CanWrite,
                WriteBlockedError = writeCheck.Error,
                RegenerationOk = regeneration.Ok,
                Errors = first.Errors.Concat(second.Errors).ToList(),
                RegenerationErrors = new List<string>(regeneration.Errors)
            };

            report.Ok = report.FirstOk
                         && report.SecondOk
                         && report.Activated
                         && report.SecondActivated
                         && report.AlreadyOpenOnSecondCall
                         && report.OpenCount == 1
                         && report.ActivityCount > 0
                         && report.WriteBlocked
                         && !report.RegenerationOk;

            // Уборка после сценария: закрываем вкладки стенда. Иначе фоновая валидация продолжает
            // тикать на диспетчере, а при выгрузке домена её тик пытается показать модальное окно WF
            // (наблюдалось как AccessViolation при инициализации шрифтов на остановке процесса).
            foreach (DesignerTab tab in designerHost.GetOpenTabs())
            {
                designerHost.Close(tab);
            }


            return report;
        });
    }


    /// <summary>Домен не должен выгружаться по аренде, пока идёт прогон.</summary>
    /// <summary>
    /// Сценарий этапа 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c> на живом дизайнере:
    /// палитра открывает файл, вставляет активность через <c>ModelService</c>, сохраняет её штатным
    /// сериализатором конструктора и задаёт аргумент. Проверяется контракт п. 1.3: вкладка открыта
    /// и активна, активность видна в дереве, гейт валидации не нашёл ошибок.
    /// </summary>
    public DesignerBenchPaletteReport PaletteScenario(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();

            var service = new PaletteWorkflowService(
                ActivityPaletteRegistry.Default,
                designerHost,
                new DispatcherDesignerUiThread(host.Dispatcher),
                tab => tab?.Designer == null ? null : new DesignerModelSession(tab.Designer));

            var report = new DesignerBenchPaletteReport { FilePath = xamlPath };

            try
            {
                WorkflowSnapshot before = WorkflowModelReader.Read(xamlPath);
                report.ActivityCountBefore = before.ActivityCount;

                PaletteResult added = service.AddActivity(new PaletteAddActivityRequest
                {
                    FilePath = xamlPath,
                    Activity = new PaletteActivitySpec("WriteLine")
                        .WithArgument("Text", PaletteValue.FromExpression("\"добавлено палитрой\""))
                });

                report.AddOk = added.Ok;
                report.AddErrors = new List<string>(added.Errors);
                report.AddHints = new List<string>(added.Hints);
                report.ActivityId = added.ActivityId;
                report.WorkflowId = added.WorkflowId;
                report.ActivityCountAfterAdd = added.Snapshot?.ActivityCount ?? 0;
                report.TabOpen = designerHost.GetOpenTabs().Count > 0;
                report.TabActive = designerHost.ActiveTab != null;
                report.ActivityCountInDesigner = designerHost.CountActiveActivities();
                ModelService modelService = designerHost.ActiveTab?.Designer?.Context?.Services?.GetService<ModelService>();
                report.ModelActivityCount = modelService?.Root == null ? 0 : CountModelActivities(modelService.Root);
                report.DesignerInfo = designerHost.ActivityCountInfo;

                if (added.Ok && !string.IsNullOrWhiteSpace(added.ActivityId))
                {
                    PaletteResult changed = service.SetArgument(new PaletteSetArgumentRequest
                    {
                        FilePath = xamlPath,
                        ActivityId = added.ActivityId,
                        ArgumentName = "Text",
                        Value = PaletteValue.FromExpression("\"текст после set_argument\"")
                    });

                    report.SetArgumentOk = changed.Ok;
                    report.SetArgumentErrors = new List<string>(changed.Errors);
                    report.ExpressionInFile = File.ReadAllText(xamlPath).Contains("текст после set_argument");
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add(ErrorText.Describe("сценарий палитры не выполнен", ex));
            }
            finally
            {
                // Уборка: закрываем вкладки стенда, иначе фоновая валидация WF продолжает тикать
                // на диспетчере стенда и при выгрузке домена показывает модальное окно.
                foreach (DesignerTab tab in designerHost.GetOpenTabs())
                {
                    designerHost.Close(tab);
                }
            }

            report.Ok = report.Errors.Count == 0
                        && report.AddOk
                        && report.TabOpen
                        && report.TabActive
                        && report.ActivityCountAfterAdd == report.ActivityCountBefore + 1
                        && report.ModelActivityCount > 0
                        && report.SetArgumentOk
                        && report.ExpressionInFile;

            return report;
        });
    }

    /// <summary>
    /// Сколько активностей в дереве модели конструктора. Считается по <c>ModelItem</c>, а не по
    /// объектной модели: именно модель показывает пользователю диаграмму, и вставка «в файл, но не
    /// в дерево» — это и есть дефект, который ловит эта проверка.
    /// </summary>
    /// <summary>
    /// Сколько активностей в дереве модели конструктора. Считается по <c>ModelItem</c>, а не по
    /// объектной модели: именно модель показывает пользователю диаграмму, и вставка «в файл, но не
    /// в дерево» — это и есть дефект, который ловит эта проверка.
    /// <para>
    /// Выражения (<c>CSharpValue&lt;T&gt;</c> — тоже активности) не считаются, а посещённые объекты
    /// запоминаются: у модели есть общие узлы, и обход без защиты от повторов уходит в бесконечность.
    /// </para>
    /// </summary>
    private static int CountModelActivities(ModelItem item, HashSet<object> visited = null, int depth = 0)
    {
        if (item == null || depth > 32)
        {
            return 0;
        }

        visited ??= new HashSet<object>();

        object value = item.GetCurrentValue();
        bool isBuilder = value is System.Activities.ActivityBuilder;
        bool isActivity = value is System.Activities.Activity
                          && !(value is System.Activities.ActivityWithResult);

        if (!isBuilder && !isActivity)
        {
            return 0;
        }

        if (value != null && !visited.Add(value))
        {
            return 0;
        }

        int count = isActivity ? 1 : 0;

        foreach (ModelProperty property in item.Properties)
        {
            if (property.IsCollection && property.Collection != null)
            {
                foreach (ModelItem child in property.Collection)
                {
                    count += CountModelActivities(child, visited, depth + 1);
                }

                continue;
            }

            count += CountModelActivities(property.Value, visited, depth + 1);
        }

        return count;
    }

    public override object InitializeLifetimeService()
    {
        return null;
    }
}

/// <summary>Сериализуемые данные о среде стенда.</summary>
[Serializable]
public sealed class DesignerBenchHostInfo
{
    public string ApartmentState { get; set; }

    public int HostThreadId { get; set; }

    public int KnownHostThreadId { get; set; }

    public int CallerThreadId { get; set; }

    public bool IsAlive { get; set; }
}

/// <summary>Сериализуемый отчёт пробы отрисовки (см. <c>DesignerRenderReport</c>).</summary>
[Serializable]
public sealed class DesignerBenchReport
{
    public string FilePath { get; set; }

    public bool LoadSucceeded { get; set; }

    public string LoadError { get; set; }

    public bool SurfaceReady { get; set; }

    public List<string> RenderedActivities { get; set; } = new List<string>();

    public List<string> UnrecognizedActivities { get; set; } = new List<string>();

    public List<string> ValidationErrors { get; set; } = new List<string>();

    public List<string> ValidationWarnings { get; set; } = new List<string>();

    public List<string> DesignerValidationErrors { get; set; } = new List<string>();

    public List<string> ModalWindows { get; set; } = new List<string>();

    public List<string> Hints { get; set; } = new List<string>();

    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description { get; set; }

    internal static DesignerBenchReport From(DesignerRenderReport report)
    {
        return new DesignerBenchReport
        {
            FilePath = report.FilePath,
            LoadSucceeded = report.LoadSucceeded,
            LoadError = report.LoadError,
            SurfaceReady = report.SurfaceReady,
            RenderedActivities = new List<string>(report.RenderedActivities),
            UnrecognizedActivities = new List<string>(report.UnrecognizedActivities),
            ValidationErrors = new List<string>(report.ValidationErrors),
            ValidationWarnings = new List<string>(report.ValidationWarnings),
            DesignerValidationErrors = new List<string>(report.DesignerValidationErrors),
            ModalWindows = new List<string>(report.ModalWindows),
            Hints = new List<string>(report.Hints),
            Ok = report.Ok,
            Description = report.Describe()
        };
    }
}

/// <summary>
/// Сериализуемый отчёт сценария открытия вкладки (<c>DesignerOpenRealDesignerTests</c>):
/// каждый флаг — отдельный пункт контракта п. 1.3 плана.
/// </summary>
[Serializable]
public sealed class DesignerBenchOpenReport
{
    public string FilePath { get; set; }

    public string WorkflowId { get; set; }

    /// <summary>Файл открылся сервисом в настоящем конструкторе.</summary>
    public bool FirstOk { get; set; }

    /// <summary>Вкладка стала активной.</summary>
    public bool Activated { get; set; }

    /// <summary>Активностей в дереве дизайнера: ноль означал бы «пустую» диаграмму.</summary>
    public int ActivityCount { get; set; }

    /// <summary>Диагностика подсчёта активностей (тип корня модели дизайнера).</summary>
    public string ActivityCountInfo { get; set; }

    /// <summary>Повторный вызов сервиса прошёл успешно.</summary>
    public bool SecondOk { get; set; }

    /// <summary>Повторный вызов узнал уже открытую вкладку.</summary>
    public bool AlreadyOpenOnSecondCall { get; set; }

    /// <summary>Повторный вызов активировал существующую вкладку.</summary>
    public bool SecondActivated { get; set; }

    /// <summary>Сколько конструкторов создано (обязано быть ровно один).</summary>
    public int OpenCount { get; set; }

    public int CloseCount { get; set; }

    /// <summary>Перезапись открытого файла запрещена.</summary>
    public bool WriteBlocked { get; set; }

    public string WriteBlockedError { get; set; }

    /// <summary>Генерация в открытый файл отклонена (обязано быть <c>false</c>).</summary>
    public bool RegenerationOk { get; set; }

    public List<string> RegenerationErrors { get; set; } = new List<string>();

    public List<string> Errors { get; set; } = new List<string>();

    /// <summary>Все пункты контракта выполнены.</summary>
    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"файл: {FilePath ?? "(нет)"}");
            text.AppendLine($"workflowId: {WorkflowId ?? "(не определён)"}");
            text.AppendLine($"открытие: {FirstOk}, активно: {Activated}, активностей: {ActivityCount}");
            text.AppendLine($"повторное открытие: {SecondOk}, уже открыт: {AlreadyOpenOnSecondCall}, активно: {SecondActivated}");
            text.AppendLine($"конструкторов создано: {OpenCount}, закрыто: {CloseCount}");
            text.AppendLine($"перезапись запрещена: {WriteBlocked} ({WriteBlockedError ?? "нет ошибки"})");
            text.AppendLine($"генерация в открытый файл: {(RegenerationOk ? "разрешена (ошибка!)" : "отклонена")}");

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка: " + error);
            }

            foreach (string error in RegenerationErrors)
            {
                text.AppendLine("  отказ генерации: " + error);
            }

            return text.ToString();
        }
    }
}

/// <summary>
/// Сериализуемый отчёт сценария палитры (<c>PaletteDesignerTests</c>): каждый флаг — отдельный пункт
/// контракта п. 1.3 плана (вкладка открыта и активна, активность в дереве, гейт без ошибок).
/// </summary>
[Serializable]
public sealed class DesignerBenchPaletteReport
{
    public string FilePath { get; set; }

    public string WorkflowId { get; set; }

    /// <summary>Вставка активности через <c>ModelService</c> прошла и гейт её принял.</summary>
    public bool AddOk { get; set; }

    /// <summary>Идентификатор добавленной активности (путь в дереве).</summary>
    public string ActivityId { get; set; }

    public int ActivityCountBefore { get; set; }

    public int ActivityCountAfterAdd { get; set; }

    /// <summary>Активностей в дереве живого дизайнера (ноль означал бы «пустую» диаграмму).</summary>
    public int ActivityCountInDesigner { get; set; }

    /// <summary>Активностей в дереве модели конструктора (ModelItem) — то, что видит пользователь.</summary>
    public int ModelActivityCount { get; set; }

    public string DesignerInfo { get; set; }

    /// <summary>Вкладка конструктора открыта.</summary>
    public bool TabOpen { get; set; }

    /// <summary>Вкладка активна.</summary>
    public bool TabActive { get; set; }

    public bool SetArgumentOk { get; set; }

    /// <summary>Новое выражение аргумента попало в сохранённый файл процесса.</summary>
    public bool ExpressionInFile { get; set; }

    public List<string> Errors { get; set; } = new List<string>();

    public List<string> AddErrors { get; set; } = new List<string>();

    public List<string> AddHints { get; set; } = new List<string>();

    public List<string> SetArgumentErrors { get; set; } = new List<string>();

    /// <summary>Все пункты контракта выполнены.</summary>
    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"файл: {FilePath ?? "(нет)"}");
            text.AppendLine($"workflowId: {WorkflowId ?? "(не определён)"}");
            text.AppendLine($"вставка: {AddOk}, активность: {ActivityId ?? "(нет)"}");
            text.AppendLine($"активностей было: {ActivityCountBefore}, стало: {ActivityCountAfterAdd}, в дереве: {ActivityCountInDesigner}");
            text.AppendLine($"дерево дизайнера: {DesignerInfo}");
            text.AppendLine($"вкладка открыта: {TabOpen}, активна: {TabActive}");
            text.AppendLine($"set_argument: {SetArgumentOk}, выражение в файле: {ExpressionInFile}");

            foreach (string error in AddErrors)
            {
                text.AppendLine("  ошибка вставки: " + error);
            }

            foreach (string error in SetArgumentErrors)
            {
                text.AppendLine("  ошибка set_argument: " + error);
            }

            foreach (string hint in AddHints)
            {
                text.AppendLine("  подсказка: " + hint);
            }

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка сценария: " + error);
            }

            return text.ToString();
        }
    }
}

