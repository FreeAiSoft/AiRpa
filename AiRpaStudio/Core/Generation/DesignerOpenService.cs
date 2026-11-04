using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Решение о записи файла процесса, открытого в конструкторе (этап 3 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Студия открывает файл вкладкой, и вкладка держит его с <c>FileShare.Read</c>: перезапись даёт
    /// <see cref="IOException"/> уже после успешной валидации. Поэтому запрет формулируется заранее —
    /// генерация получает ошибку и подсказку, а не исключение.
    /// </para>
    /// </summary>
    internal sealed class DesignerWriteCheck
    {
        private readonly List<string> _hints = new List<string>();

        private DesignerWriteCheck(bool canWrite)
        {
            CanWrite = canWrite;
        }

        /// <summary>Запись разрешена.</summary>
        internal bool CanWrite { get; }

        /// <summary>Файл открыт вкладкой конструктора — перезапись невозможна.</summary>
        internal bool IsOpenInDesigner { get; private set; }

        /// <summary>Полный путь файла, о котором принято решение.</summary>
        internal string FilePath { get; private set; }

        /// <summary>Текст ошибки для ответа инструмента (<c>{ok:false, errors[]}</c>).</summary>
        internal string Error { get; private set; }

        /// <summary>Что делать: закрыть вкладку, перезагрузить её или писать в новый файл.</summary>
        internal IReadOnlyList<string> Hints => _hints;

        /// <summary>Запись разрешена; <paramref name="hint"/> объясняет, если проверка не выполнялась.</summary>
        internal static DesignerWriteCheck Allow(string hint = null)
        {
            var check = new DesignerWriteCheck(true);

            if (!string.IsNullOrWhiteSpace(hint))
            {
                check._hints.Add(hint);
            }

            return check;
        }

        /// <summary>Запись запрещена с указанной причиной.</summary>
        internal static DesignerWriteCheck Blocked(
            string filePath,
            string error,
            IEnumerable<string> hints = null,
            bool isOpenInDesigner = false)
        {
            var check = new DesignerWriteCheck(false)
            {
                FilePath = filePath,
                Error = error,
                IsOpenInDesigner = isOpenInDesigner
            };

            if (hints != null)
            {
                check._hints.AddRange(hints.Where(hint => !string.IsNullOrWhiteSpace(hint)));
            }

            return check;
        }
    }

    /// <summary>
    /// Проверка «можно ли писать в этот файл процесса». Реализация — <see cref="DesignerOpenService"/>;
    /// ею пользуется <see cref="WorkflowBuildService"/> перед записью файла.
    /// </summary>
    internal interface IDesignerWriteCheck
    {
        DesignerWriteCheck CheckWrite(string filePath);
    }

    /// <summary>
    /// Результат открытия процесса в конструкторе: единый ответ на действие этапа 3
    /// (<c>ok, filePath, workflowId, alreadyOpen, reloaded, activated, errors[], hints[]</c>).
    /// </summary>
    internal sealed class DesignerOpenResult
    {
        private DesignerOpenResult()
        {
        }

        /// <summary>Вкладка открыта (или уже была открыта и активирована).</summary>
        internal bool Ok { get; private set; }

        /// <summary>Полный путь файла процесса.</summary>
        internal string FilePath { get; private set; }

        /// <summary><c>x:Class</c> открытого процесса (по нему студия узнаёт файл).</summary>
        internal string WorkflowId { get; private set; }

        /// <summary>Вкладка уже существовала: второй конструктор не создавался.</summary>
        internal bool AlreadyOpen { get; private set; }

        /// <summary>Вкладка была закрыта и открыта заново (<see cref="DesignerOpenService.Reload"/>).</summary>
        internal bool Reloaded { get; private set; }

        /// <summary>Вкладка стала активной.</summary>
        internal bool Activated { get; private set; }

        /// <summary>Ошибки открытия (полный текст со стеком — <see cref="ErrorText"/>).</summary>
        internal List<string> Errors { get; } = new List<string>();

        /// <summary>Подсказки и информационные замечания.</summary>
        internal List<string> Hints { get; } = new List<string>();

        internal static DesignerOpenResult Success(
            string filePath,
            string workflowId,
            bool alreadyOpen,
            bool reloaded,
            bool activated,
            IEnumerable<string> hints = null)
        {
            var result = new DesignerOpenResult
            {
                Ok = true,
                FilePath = filePath,
                WorkflowId = workflowId,
                AlreadyOpen = alreadyOpen,
                Reloaded = reloaded,
                Activated = activated
            };

            result.AddHints(hints);
            return result;
        }

        internal static DesignerOpenResult Failure(
            string filePath,
            string error,
            IEnumerable<string> hints = null,
            string workflowId = null)
        {
            var result = new DesignerOpenResult
            {
                Ok = false,
                FilePath = filePath,
                WorkflowId = workflowId
            };

            if (!string.IsNullOrWhiteSpace(error))
            {
                result.Errors.Add(error);
            }

            result.AddHints(hints);
            return result;
        }

        /// <summary>Текст для лога и для сообщения об ошибке теста.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "открытие в конструкторе: ок" : "открытие в конструкторе: ОШИБКИ");
            text.AppendLine($"файл: {FilePath ?? "(не задан)"}");
            text.AppendLine($"workflowId: {WorkflowId ?? "(не определён)"}");
            text.AppendLine($"уже открыт: {AlreadyOpen}, перезагружен: {Reloaded}, активен: {Activated}");

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка: " + error);
            }

            foreach (string hint in Hints)
            {
                text.AppendLine("  подсказка: " + hint);
            }

            return text.ToString();
        }

        private void AddHints(IEnumerable<string> hints)
        {
            if (hints == null)
            {
                return;
            }

            foreach (string hint in hints.Where(hint => !string.IsNullOrWhiteSpace(hint)))
            {
                if (!Hints.Contains(hint))
                {
                    Hints.Add(hint);
                }
            }
        }
    }

    /// <summary>
    /// Открытие процесса в конструкторе студии — единственный путь показа вкладки (этап 3 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>, п. 1.3).
    /// <list type="bullet">
    /// <item>файл открывается один раз: повторный вызов активирует существующую вкладку
    /// (<see cref="IWorkflowDesignerHost.GetOpenTabs"/> вместо второго <c>WorkflowDesigner</c>);</item>
    /// <item>вся работа с конструктором уходит на UI-поток через <see cref="IDesignerUiThread"/>;</item>
    /// <item>свежая вкладка помечается «не изменённой»: содержимое совпадает с файлом на диске;</item>
    /// <item>перезапись открытого файла запрещена заранее (<see cref="CheckWrite"/>), а сценарий
    /// «закрыть/перезагрузить вкладку» — это <see cref="Close"/> и <see cref="Reload"/>.</item>
    /// </list>
    /// </summary>
    internal sealed class DesignerOpenService : IDesignerWriteCheck
    {
        /// <summary>Что делать, если файл занят вкладкой конструктора.</summary>
        internal const string OpenTabHint =
            "файл открыт в конструкторе: вкладка держит его с FileShare.Read, поэтому перезапись невозможна — " +
            "закройте вкладку (DesignerOpenService.Close), перезагрузите её (DesignerOpenService.Reload) " +
            "или сохраните генерацию в новый файл";

        private const string ReloadHint =
            "если во вкладке нет несохранённых правок, её можно закрыть и открыть заново через DesignerOpenService.Reload";

        private const string UiThreadHint =
            "если ошибка говорит о занятом UI-потоке, закройте модальные окна конструктора и повторите вызов";

        private readonly IWorkflowDesignerHost _host;
        private readonly IDesignerUiThread _uiThread;
        private readonly Action<string> _log;

        internal DesignerOpenService(IWorkflowDesignerHost host, IDesignerUiThread uiThread, Action<string> log = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));
            _log = log;
        }

        /// <summary>Сервис для запущенной студии: реальная среда конструктора и UI-поток приложения.</summary>
        internal static DesignerOpenService ForStudio(Action<string> log = null)
        {
            return new DesignerOpenService(
                new StudioWorkflowDesignerHost(),
                DispatcherDesignerUiThread.ForStudio(),
                log);
        }

        /// <summary>
        /// Открывает файл в конструкторе или активирует уже открытую вкладку. Ошибки возвращаются
        /// в результате, а не исключением: вызов приходит из MCP-потока.
        /// </summary>
        internal DesignerOpenResult OpenOrActivate(string filePath)
        {
            return OpenCore(filePath, reload: false);
        }

        /// <summary>
        /// Перезагружает вкладку: закрывает её и открывает файл заново. Отказывается, если во вкладке есть
        /// несохранённые правки, — иначе перезагрузка молча теряет работу пользователя.
        /// </summary>
        internal DesignerOpenResult Reload(string filePath)
        {
            return OpenCore(filePath, reload: true);
        }

        /// <summary>
        /// Закрывает вкладку файла, снимая блокировку студии (сценарий «перезаписать открытый файл»).
        /// Вкладка с несохранёнными правками не закрывается.
        /// </summary>
        internal DesignerOpenResult Close(string filePath)
        {
            string fullPath = null;

            try
            {
                string pathError = ValidatePath(filePath, out fullPath);
                if (pathError != null)
                {
                    return DesignerOpenResult.Failure(fullPath, pathError);
                }

                return _uiThread.Invoke(() =>
                {
                    DesignerTab tab = FindTab(fullPath);
                    if (tab == null)
                    {
                        return DesignerOpenResult.Success(
                            fullPath, workflowId: null, alreadyOpen: false, reloaded: false, activated: false,
                            hints: new[] { "файл не открыт в конструкторе — закрывать нечего" });
                    }

                    if (tab.IsEdited)
                    {
                        return DesignerOpenResult.Failure(
                            fullPath,
                            $"закрытие {Path.GetFileName(fullPath)} не выполнено: во вкладке есть несохранённые правки",
                            new[] { "сохраните процесс (WorkflowSave) или закройте вкладку вручную" });
                    }

                    _host.Close(tab);

                    Log($"Вкладка {fullPath} закрыта сервисом открытия — файл снова доступен для записи");

                    return DesignerOpenResult.Success(
                        fullPath, workflowId: null, alreadyOpen: false, reloaded: false, activated: false,
                        hints: new[] { "вкладка закрыта: файл больше не занят студией и доступен для перезаписи" });
                });
            }
            catch (Exception ex)
            {
                string description = ErrorText.Describe($"не удалось закрыть вкладку {fullPath ?? filePath}", ex);
                Log(description);

                return DesignerOpenResult.Failure(fullPath, description, new[] { UiThreadHint });
            }
        }

        /// <summary>
        /// Можно ли писать в файл. Ответ «нет» — это ошибка с подсказкой, а не исключение.
        /// Если проверить не удалось (нет UI-потока, среда конструктора недоступна), запись разрешается:
        /// настоящая ошибка записи всё равно вернётся из <see cref="WorkflowBuildService"/>.
        /// </summary>
        public DesignerWriteCheck CheckWrite(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return DesignerWriteCheck.Blocked(null, "xaml.path: путь к файлу процесса не задан");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                return DesignerWriteCheck.Blocked(
                    filePath, ErrorText.Describe($"путь к файлу процесса недопустим: {filePath}", ex));
            }

            try
            {
                DesignerTab tab = _uiThread.Invoke(() => FindTab(fullPath));
                if (tab != null)
                {
                    return DesignerWriteCheck.Blocked(
                        fullPath,
                        $"файл открыт в конструкторе — перезапись невозможна: {fullPath}",
                        new[] { OpenTabHint, ReloadHint },
                        isOpenInDesigner: true);
                }

                return DesignerWriteCheck.Allow();
            }
            catch (Exception ex)
            {
                string description = ErrorText.Describe($"не удалось проверить, открыт ли файл {fullPath}", ex);
                Log(description);

                return DesignerWriteCheck.Allow(
                    "проверка занятости файла конструктором не выполнена — запись разрешена, " +
                    "ошибка ввода-вывода вернётся из генерации: " + description);
            }
        }

        /// <summary>
        /// <c>x:Class</c> процесса из файла. Читается один раз и только корневой атрибут: полноценная
        /// загрузка документа — дело конструктора, а не сервиса открытия.
        /// </summary>
        internal static string ReadWorkflowId(string filePath)
        {
            try
            {
                using (XmlReader reader = XmlReader.Create(filePath))
                {
                    if (reader.MoveToContent() != XmlNodeType.Element)
                    {
                        return null;
                    }

                    string className = reader.GetAttribute("Class", XamlNamespace);
                    return string.IsNullOrWhiteSpace(className) ? null : className.Trim();
                }
            }
            catch (Exception ex)
            {
                // workflowId — сведения ответа, а не условие открытия: битый XAML опишет конструктор.
                System.Diagnostics.Debug.WriteLine("workflowId не прочитан: " + ErrorText.Of(ex));
                return null;
            }
        }

        /// <summary>Сравнение путей так, как это делает Windows: без учёта регистра, по полному пути.</summary>
        internal static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(ToFullPath(left), ToFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        private DesignerOpenResult OpenCore(string filePath, bool reload)
        {
            string fullPath = null;

            try
            {
                string pathError = ValidatePath(filePath, out fullPath);
                if (pathError != null)
                {
                    return DesignerOpenResult.Failure(fullPath, pathError);
                }

                if (!File.Exists(fullPath))
                {
                    return DesignerOpenResult.Failure(
                        fullPath,
                        $"xaml.path: файл процесса не найден — {fullPath}",
                        new[] { "сначала соберите и сохраните процесс (WorkflowBuildService), затем откройте его" });
                }

                string workflowId = ReadWorkflowId(fullPath);

                return _uiThread.Invoke(() => OpenOnUiThread(fullPath, workflowId, reload));
            }
            catch (Exception ex)
            {
                string description = ErrorText.Describe(
                    $"не удалось открыть {fullPath ?? filePath} в конструкторе", ex);
                Log(description);

                return DesignerOpenResult.Failure(fullPath, description, new[] { UiThreadHint });
            }
        }

        private DesignerOpenResult OpenOnUiThread(string fullPath, string workflowId, bool reload)
        {
            DesignerTab tab = FindTab(fullPath);
            bool reloaded = false;

            if (tab != null && reload)
            {
                if (tab.IsEdited)
                {
                    return DesignerOpenResult.Failure(
                        fullPath,
                        $"перезагрузка {Path.GetFileName(fullPath)} невозможна: во вкладке есть несохранённые правки",
                        new[] { "сохраните процесс (WorkflowSave) и повторите перезагрузку" },
                        workflowId);
                }

                _host.Close(tab);
                tab = null;
                reloaded = true;

                Log($"Вкладка {fullPath} закрыта для повторного открытия (DesignerOpenService.Reload)");
            }

            if (tab != null)
            {
                _host.Activate(tab);
                Log($"Процесс {fullPath} уже открыт — вкладка активирована, второй конструктор не создавался");

                return DesignerOpenResult.Success(
                    fullPath, workflowId, alreadyOpen: true, reloaded: false, activated: IsActive(tab),
                    hints: new[] { "файл уже открыт в конструкторе: вкладка активирована" });
            }

            DesignerTab opened = _host.Open(fullPath);
            if (opened == null)
            {
                return DesignerOpenResult.Failure(
                    fullPath,
                    $"конструктор не создан для {fullPath}: загрузчик файла вернул null",
                    new[] { "проверьте, что файл является рабочим процессом (.xaml) и не занят другим процессом" },
                    workflowId);
            }

            // Содержимое только что открытой вкладки совпадает с файлом: «изменён» не выставляется.
            // Иначе кнопка «Сохранить» активна сразу после открытия (фантомное dirty-состояние),
            // а WorkflowSaveAll перезаписывает файлы, которые никто не менял.
            _host.MarkClean(opened);
            _host.Activate(opened);

            bool activated = IsActive(opened);
            var hints = new List<string>();
            if (!activated)
            {
                hints.Add("вкладка открыта, но не стала активной: проверьте панель документов студии");
            }

            Log($"Процесс {fullPath} открыт в конструкторе заново (активен: {activated})");

            return DesignerOpenResult.Success(
                fullPath, workflowId, alreadyOpen: false, reloaded: reloaded, activated: activated, hints: hints);
        }

        private bool IsActive(DesignerTab tab)
        {
            DesignerTab active = _host.ActiveTab;
            return active != null && PathsEqual(active.FilePath, tab.FilePath);
        }

        private DesignerTab FindTab(string fullPath)
        {
            IReadOnlyList<DesignerTab> tabs = _host.GetOpenTabs();
            if (tabs == null)
            {
                return null;
            }

            return tabs.FirstOrDefault(tab => tab != null && PathsEqual(tab.FilePath, fullPath));
        }

        /// <summary>Путь задан и это файл процесса (<c>.xaml</c>).</summary>
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

            if (!fullPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                return $"xaml.path: файл процесса должен иметь расширение .xaml — {fullPath}";
            }

            return null;
        }

        private static string ToFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
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
                // Лог не должен портить ответ инструмента.
                System.Diagnostics.Debug.WriteLine("Лог сервиса открытия недоступен: " + ErrorText.Of(ex));
            }
        }
    }
}
