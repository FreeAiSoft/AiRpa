namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Структура процесса для агента (карточка <c>AG-03</c>, инструмент <c>workflow_outline</c>):
    /// дерево активностей с типами, адресами, аргументами и замечаниями валидации.
    /// <para>
    /// Реализацию предоставляет хост студии (у неё есть WF-загрузка и валидация), поэтому чат зависит
    /// только от интерфейса, а инструмент тестируется без UI и без WF.
    /// </para>
    /// </summary>
    public interface IWorkflowOutlineService
    {
        /// <summary>
        /// Структура процесса из файла. <paramref name="depth"/>: 0 — всё дерево, 1 — только корень,
        /// N — N уровней. <paramref name="includeArguments"/> = <c>false</c> — без аргументов
        /// (дерево остаётся, ответ заметно короче). <c>null</c> — служба не подключена хостом:
        /// инструмент отвечает объяснением, а не исключением.
        /// </summary>
        WorkflowOutline GetOutline(string path, int depth = 0, bool includeArguments = true);
    }

    /// <summary>
    /// Null-object: хост студии не подключил службу структуры процесса. Возвращается <c>null</c>,
    /// чтобы инструмент отличил «службы нет» от «файл не прочитан» (последнее — outline с
    /// <c>Ok = false</c> и причиной в <c>Issues</c>).
    /// </summary>
    public sealed class NullWorkflowOutlineService : IWorkflowOutlineService
    {
        public static readonly NullWorkflowOutlineService Instance = new NullWorkflowOutlineService();

        public WorkflowOutline GetOutline(string path, int depth = 0, bool includeArguments = true) => null;
    }
}
