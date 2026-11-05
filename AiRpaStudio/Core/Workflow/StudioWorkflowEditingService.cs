using System;
using System.Activities.Presentation;
using System.IO;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using RPA.Helpers;

// «WorkflowEditResult» — ответ агенту (этот файл), «RPA.Core.Generation.WorkflowEditResult» —
// результат правки модели конструктора через палитру; имена совпадают, поэтому алиас обязателен.
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Мост «агент ↔ открытый дизайнер» (карточка <c>AG-02</c>): находит вкладку конструктора по пути
    /// файла и отдаёт пачку правок живой модели, историю отмены и структуру несохранённого документа.
    /// <para>
    /// Вкладки и активация — те же, что у <see cref="DesignerOpenService"/> (среда
    /// <see cref="IWorkflowDesignerHost"/>), поэтому «открыт в студии» означает одно и то же для открытия,
    /// генерации и правок. Своего пути открытия здесь нет намеренно: два пути открытия дают
    /// «работает в одном инструменте, ломается в другом».
    /// </para>
    /// <para>
    /// Файл, не открытый вкладкой, — не исключение, а структурированный отказ с подсказкой: агент видит
    /// «откройте файл в конструкторе» и не считает, что правка применилась.
    /// </para>
    /// </summary>
    internal sealed class StudioWorkflowEditingService : IWorkflowEditingService
    {
        private static readonly Lazy<StudioWorkflowEditingService> Shared =
            new Lazy<StudioWorkflowEditingService>(() => new StudioWorkflowEditingService());

        private readonly IWorkflowDesignerHost _host;
        private readonly Func<WorkflowDesignerEx, IDesignerUiThread> _uiThreadFactory;

        /// <param name="host">Среда вкладок конструктора; по умолчанию — студия.</param>
        /// <param name="uiThreadFactory">Маршалинг на поток дизайнера; по умолчанию — его диспетчер.</param>
        internal StudioWorkflowEditingService(
            IWorkflowDesignerHost host = null,
            Func<WorkflowDesignerEx, IDesignerUiThread> uiThreadFactory = null)
        {
            _host = host ?? new StudioWorkflowDesignerHost();
            _uiThreadFactory = uiThreadFactory
                ?? new Func<WorkflowDesignerEx, IDesignerUiThread>(
                    designer => new DispatcherDesignerUiThread(UiDispatcherOf(designer)));
        }

        /// <summary>Служба для хоста чата: <c>ChatEnvironment.WorkflowEditing</c>.</summary>
        internal static StudioWorkflowEditingService Default => Shared.Value;

        public bool IsOpenInDesigner(string path)
        {
            return FindTab(path) != null;
        }

        public IWorkflowEditSession BeginEdit(string path, string label = null)
        {
            string fullPath = Normalize(path);

            if (fullPath == null)
            {
                return new UnavailableWorkflowEditSession(
                    path,
                    label,
                    "xaml.path: путь к файлу процесса недопустим — " + (path ?? "(пусто)"),
                    "передайте полный путь к XAML процесса");
            }

            DesignerTab tab = FindTab(fullPath);
            if (tab?.Designer == null)
            {
                return new UnavailableWorkflowEditSession(
                    fullPath,
                    label,
                    "файл не открыт в конструкторе студии: " + fullPath,
                    "откройте файл вкладкой конструктора — правки идут в живую модель, " +
                    "а файл на диске правится инструментами xaml.*");
            }

            WorkflowDesignerEx designer = tab.Designer;
            var session = new StudioWorkflowEditSession(
                designer,
                fullPath,
                label,
                _uiThreadFactory(designer));

            if (session.IsActive)
            {
                return session;
            }

            session.Dispose();

            return new UnavailableWorkflowEditSession(
                fullPath,
                label,
                session.InactiveReason ?? "модель конструктора недоступна",
                "перезагрузите вкладку процесса и повторите вызов");
        }

        public WorkflowEditResult Undo(string path)
        {
            return ExecuteHistory("undo", path);
        }

        public WorkflowEditResult Redo(string path)
        {
            return ExecuteHistory("redo", path);
        }

        public WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true)
        {
            DesignerTab tab = FindTab(path);
            if (tab?.Designer == null)
            {
                return null;
            }

            WorkflowDesignerEx designer = tab.Designer;

            try
            {
                return _uiThreadFactory(designer)
                    .Invoke(() => StudioWorkflowOutlineService.FromDesigner(designer, tab.FilePath, depth, includeArguments));
            }
            catch (Exception ex)
            {
                // Структура открытого документа — уточнение к файлу; её сбой не должен ломать чтение.
                System.Diagnostics.Debug.WriteLine("Структура открытого процесса не прочитана: " + ErrorText.Of(ex));
                return null;
            }
        }

        private WorkflowEditResult ExecuteHistory(string operation, string path)
        {
            DesignerTab tab = FindTab(path);

            if (tab?.Designer == null)
            {
                return WorkflowEditResult.Failure(
                    operation,
                    "файл не открыт в конструкторе студии: " + (path ?? "(пусто)"),
                    "история отмены принадлежит вкладке конструктора");
            }

            WorkflowDesignerEx designer = tab.Designer;
            IDesignerUiThread ui = _uiThreadFactory(designer);

            try
            {
                return ui.Invoke(() =>
                {
                    UndoEngine engine = designer.Context?.Services?.GetService<UndoEngine>();

                    if (engine == null)
                    {
                        return WorkflowEditResult.Failure(
                            operation,
                            "история конструктора недоступна (UndoEngine не найден)")
                            .WithState(false);
                    }

                    bool applied = operation == "undo" ? engine.Undo() : engine.Redo();

                    try
                    {
                        designer.Flush();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Flush после истории не выполнен: " + ErrorText.Of(ex));
                    }

                    WorkflowEditResult result = applied
                        ? WorkflowEditResult.Success(
                            operation,
                            operation == "undo" ? "последняя правка отменена" : "правка возвращена")
                        : WorkflowEditResult.Failure(
                            operation,
                            operation == "undo" ? "отменять нечего" : "возвращать нечего");

                    (bool canUndo, bool canRedo) = StudioWorkflowEditSession.UndoStateOf(designer, ui);

                    return result.WithState(false, canUndo, canRedo);
                });
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(
                    operation,
                    ErrorText.Describe("«" + operation + "» не выполнен", ex),
                    "UI-поток студии занят: закройте модальные окна конструктора и повторите");
            }
        }

        /// <summary>Вкладка конструктора с этим файлом; <c>null</c> — файл не открыт.</summary>
        private DesignerTab FindTab(string path)
        {
            string fullPath = Normalize(path);
            if (fullPath == null)
            {
                return null;
            }

            try
            {
                foreach (DesignerTab tab in _host.GetOpenTabs())
                {
                    if (tab?.Designer != null && PathsEqual(tab.FilePath, fullPath))
                    {
                        return tab;
                    }
                }
            }
            catch (Exception ex)
            {
                // Среды вкладок нет (студия не запущена, тестовый хост без конструктора) — это «не открыт».
                System.Diagnostics.Debug.WriteLine("Вкладки конструктора недоступны: " + ErrorText.Of(ex));
            }

            return null;
        }

        /// <summary>
        /// Диспетчер, на котором создан дизайнер. У самого <see cref="WorkflowDesignerEx"/> свойства
        /// <c>Dispatcher</c> нет — его даёт <c>View</c> (элемент WPF). Если поверхность ещё не создана,
        /// это ошибка, а не «диспетчер вызывающего потока»: молча работать в чужом потоке нельзя.
        /// </summary>
        private static System.Windows.Threading.Dispatcher UiDispatcherOf(WorkflowDesignerEx designer)
        {
            var element = designer?.View as System.Windows.Threading.DispatcherObject;

            if (element == null)
            {
                throw new InvalidOperationException(
                    "UI-поток дизайнера недоступен: поверхность конструктора не создана");
            }

            return element.Dispatcher;
        }

        private static bool PathsEqual(string left, string right)
        {
            string a = Normalize(left);
            string b = Normalize(right);

            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Путь процесса не разобран: " + ErrorText.Of(ex));
                return null;
            }
        }
    }
}
