using System;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Пачка правок открытого конструктором процесса (карточка <c>AG-02</c>): «агент ↔ открытый дизайнер».
    /// <para>
    /// Все действия идут через модель конструктора (<c>ModelItem</c>), поэтому правка сразу видна на
    /// диаграмме, а отмена — штатная (<c>Ctrl+Z</c>, история <c>UndoEngine</c>). Порядок работы:
    /// <c>BeginEdit</c> → несколько правок → <c>Complete</c> (одна запись в истории) либо <c>Revert</c>
    /// (модель возвращается к состоянию до пачки). Ошибка любой правки закрывает пачку откатом —
    /// «половина применённой пачки» на диаграмме не остаётся.
    /// </para>
    /// </summary>
    public interface IWorkflowEditSession : IDisposable
    {
        /// <summary>Полный путь процесса, открытого вкладкой конструктора.</summary>
        string FilePath { get; }

        /// <summary>Подпись пачки (её видно в истории отмены конструктора).</summary>
        string Label { get; }

        /// <summary>Пачка открыта и принимает правки.</summary>
        bool IsActive { get; }

        /// <summary>Вставляет активность в контейнер родителя.</summary>
        WorkflowEditResult Insert(WorkflowInsertRequest request);

        /// <summary>Удаляет активность из контейнера родителя.</summary>
        WorkflowEditResult Delete(string elementId);

        /// <summary>Переносит активность в другой контейнер (аргументы сохраняются).</summary>
        WorkflowEditResult Move(WorkflowMoveRequest request);

        /// <summary>
        /// Оборачивает активность новой активностью (<c>AG-04</c>): обёртка встаёт на место элемента,
        /// элемент становится её первым ребёнком. Так шаг попадает внутрь <c>If</c>/<c>TryCatch</c>/<c>Sequence</c>.
        /// </summary>
        WorkflowEditResult Wrap(WorkflowWrapRequest request);

        /// <summary>
        /// Задаёт аргумент активности (карточка <c>AG-05</c>): литерал или выражение с проверкой типа
        /// аргумента и синтаксиса выражения на языке документа (C# или VB).
        /// </summary>
        WorkflowEditResult SetArgument(WorkflowArgumentSetRequest request);

        /// <summary>
        /// Добавляет переменную в активность с коллекцией <c>Variables</c> (карточка <c>AG-05</c>).
        /// Переменная сразу видна в области видимости, поэтому следующие выражения могут на неё ссылаться.
        /// </summary>
        WorkflowEditResult AddVariable(WorkflowVariableAddRequest request);

        /// <summary>Удаляет переменную по имени (карточка <c>AG-05</c>).</summary>
        WorkflowEditResult RemoveVariable(WorkflowVariableRemoveRequest request);

        /// <summary>Закрывает пачку: одна запись в истории отмены, структура процесса в ответе.</summary>
        WorkflowEditResult Complete();

        /// <summary>Откатывает пачку: модель возвращается к состоянию до <c>BeginEdit</c>.</summary>
        WorkflowEditResult Revert();

        /// <summary>Текущая структура процесса из живой модели (включая несохранённые правки).</summary>
        WorkflowOutline Outline(int depth = 0, bool includeArguments = true);
    }

    /// <summary>
    /// Мост «агент ↔ открытый дизайнер» (карточка <c>AG-02</c>). Реализацию даёт хост студии: у неё есть
    /// вкладки конструктора, UI-поток и <c>ModelService</c>. Чат зависит только от контракта, поэтому
    /// инструменты правки тестируются без UI.
    /// </summary>
    public interface IWorkflowEditingService
    {
        /// <summary>
        /// Начинает пачку правок файла процесса. Если файл не открыт в конструкторе, возвращается
        /// неактивная сессия с объяснением и подсказкой — это структурированный отказ, а не исключение.
        /// </summary>
        IWorkflowEditSession BeginEdit(string path, string label = null);

        /// <summary>Отменяет последнюю закрытую правку конструктора (как <c>Ctrl+Z</c>).</summary>
        WorkflowEditResult Undo(string path);

        /// <summary>Возвращает отменённую правку (как <c>Ctrl+Y</c>).</summary>
        WorkflowEditResult Redo(string path);

        /// <summary>
        /// Структура процесса из живой модели открытой вкладки (включая несохранённые правки).
        /// <c>null</c> — файл не открыт в конструкторе: тогда читается версия с диска.
        /// </summary>
        WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true);

        /// <summary>Файл открыт вкладкой конструктора (правки пойдут в живой модели).</summary>
        bool IsOpenInDesigner(string path);
    }

    /// <summary>
    /// Null-object: хост студии не подключил мост правок. Сессия неактивна, а каждый вызов возвращает
    /// объяснение — инструмент отвечает текстом, а не падением.
    /// </summary>
    public sealed class NullWorkflowEditingService : IWorkflowEditingService
    {
        public static readonly NullWorkflowEditingService Instance = new NullWorkflowEditingService();

        public IWorkflowEditSession BeginEdit(string path, string label = null)
        {
            return new UnavailableWorkflowEditSession(
                path,
                label,
                "мост правки конструктора не подключён хостом студии",
                "правка открытого процесса недоступна в этой сборке чата");
        }

        public WorkflowEditResult Undo(string path)
        {
            return WorkflowEditResult.Failure("undo", "мост правки конструктора не подключён хостом студии");
        }

        public WorkflowEditResult Redo(string path)
        {
            return WorkflowEditResult.Failure("redo", "мост правки конструктора не подключён хостом студии");
        }

        public WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true)
        {
            return null;
        }

        public bool IsOpenInDesigner(string path)
        {
            return false;
        }
    }

    /// <summary>
    /// Неактивная сессия с причиной отказа: файл не открыт, мост недоступен, модель конструктора не готова.
    /// Каждый вызов возвращает <c>ok: false</c> и ту же причину, поэтому агент видит «почему нельзя»,
    /// а не пустой ответ.
    /// </summary>
    public sealed class UnavailableWorkflowEditSession : IWorkflowEditSession
    {
        private readonly string _reason;
        private readonly string _hint;

        public UnavailableWorkflowEditSession(string filePath, string label, string reason, string hint = null)
        {
            FilePath = filePath ?? string.Empty;
            Label = label ?? string.Empty;
            _reason = string.IsNullOrWhiteSpace(reason) ? "правка открытого процесса недоступна" : reason;
            _hint = hint;
        }

        public string FilePath { get; }

        public string Label { get; }

        public bool IsActive => false;

        /// <summary>Почему сессия неактивна — текст для ответа инструмента.</summary>
        public string Reason => _reason;

        public WorkflowEditResult Insert(WorkflowInsertRequest request)
        {
            return Failure("insert", request?.ToString());
        }

        public WorkflowEditResult Delete(string elementId)
        {
            return Failure("delete", elementId);
        }

        public WorkflowEditResult Move(WorkflowMoveRequest request)
        {
            return Failure("move", request?.ToString());
        }

        public WorkflowEditResult Wrap(WorkflowWrapRequest request)
        {
            return Failure("wrap", request?.ToString());
        }

        public WorkflowEditResult SetArgument(WorkflowArgumentSetRequest request)
        {
            return Failure("argument_set", request?.ElementId);
        }

        public WorkflowEditResult AddVariable(WorkflowVariableAddRequest request)
        {
            return Failure("variable_add", request?.ElementId);
        }

        public WorkflowEditResult RemoveVariable(WorkflowVariableRemoveRequest request)
        {
            return Failure("variable_remove", request?.ElementId);
        }

        public WorkflowEditResult Complete()
        {
            return Failure("complete", null);
        }

        public WorkflowEditResult Revert()
        {
            return Failure("revert", null);
        }

        public WorkflowOutline Outline(int depth = 0, bool includeArguments = true)
        {
            return null;
        }

        public void Dispose()
        {
        }

        private WorkflowEditResult Failure(string operation, string elementId)
        {
            return WorkflowEditResult.Failure(operation, _reason, _hint)
                .WithElements(elementId)
                .WithState(sessionActive: false);
        }
    }
}
