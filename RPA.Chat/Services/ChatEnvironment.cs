using System;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Host-provided integration points for the chat module. The studio host (AiRpaStudio)
    /// assigns these before the chat panel is shown; RPA.Chat itself only depends on the
    /// defaults so it can run standalone.
    /// </summary>
    public static class ChatEnvironment
    {
        /// <summary>
        /// Returns the current studio project/solution folder. Used by the MSBuild feedback
        /// service and the agent tools to resolve relative paths. Defaults to empty.
        /// </summary>
        public static Func<string> SolutionPathProvider { get; set; } = () => string.Empty;

        /// <summary>
        /// Reads the user's open editor tabs (AvalonEdit buffers). Defaults to the null object
        /// until the studio host injects a real implementation.
        /// </summary>
        public static IStudioDocumentService DocumentService { get; set; } = NullStudioDocumentService.Instance;

        /// <summary>
        /// Machine-readable catalog of the studio workflow activities (card <c>AG-01</c>). Built by the
        /// studio host from real WF activity types; defaults to the null object so the chat module
        /// stays usable standalone. The <c>activity_search</c>/<c>activity_describe</c> tools read it.
        /// </summary>
        public static Workflow.IActivityCatalog ActivityCatalog { get; set; } = Workflow.NullActivityCatalog.Instance;

        /// <summary>
        /// Structure of a workflow process (card <c>AG-03</c>): activity tree with ids, types, arguments,
        /// variables and validation issues. Implemented by the studio host (it owns WF loading and
        /// validation); defaults to the null object, and the <c>workflow_outline</c> tool then explains
        /// that the service is unavailable instead of throwing.
        /// </summary>
        public static Workflow.IWorkflowOutlineService WorkflowOutline { get; set; } =
            Workflow.NullWorkflowOutlineService.Instance;

        /// <summary>
        /// Мост «агент ↔ открытый дизайнер» (карточка <c>AG-02</c>): пачки правок модели конструктора
        /// с историей отмены. Реализацию даёт хост студии; по умолчанию — null-object, и инструменты
        /// правки объясняют, что мост не подключён, вместо падения.
        /// </summary>
        public static Workflow.IWorkflowEditingService WorkflowEditing { get; set; } =
            Workflow.NullWorkflowEditingService.Instance;

        /// <summary>
        /// Гейт валидации XAML (карточка <c>AG-07</c>): после записи <c>.xaml</c> инструмент <c>editor</c>
        /// спрашивает у студии, примет ли файл конструктор. Реализацию даёт студия (у неё есть WF-загрузка
        /// и валидация); по умолчанию — null-object, и инструмент отвечает «валидация не выполнена»,
        /// а не «ошибок нет».
        /// </summary>
        public static Workflow.IWorkflowXamlGate WorkflowXamlGate { get; set; } =
            Workflow.NullWorkflowXamlGate.Instance;

        /// <summary>
        /// Обновление палитры «Активности» после создания активности (карточка <c>AG-08</c>): студия
        /// загружает собранную сборку в домен, добавляет её типы в палитру, сбрасывает кэши каталога и реестра
        /// и пересобирает панель. По умолчанию — null-object: инструмент честно сообщает, что без хоста
        /// тип появится только после перезапуска студии.
        /// </summary>
        public static Workflow.IActivityPaletteNotifier ActivityPaletteNotifier { get; set; } =
            Workflow.NullActivityPaletteNotifier.Instance;

        /// <summary>
        /// Хранилище ТЗ проекта (волна 8, <c>TZ-01</c>): <c>&lt;проект&gt;\docs\spec\&lt;slug&gt;\</c>.
        /// По умолчанию — файловая реализация поверх <see cref="SolutionPathProvider"/>; тесты и хосты
        /// подменяют её своим корнем, чтобы не писать в настоящий проект.
        /// </summary>
        public static Spec.ISpecificationStore SpecStore
        {
            get => _specStore ?? (_specStore = new Spec.SpecificationStore(() => SolutionPathProvider()));
            set => _specStore = value;
        }

        /// <summary>
        /// Прогонщик робота для тест-кейсов ТЗ (волна 8, <c>TZ-08</c>). Реализацию даёт студия
        /// (движок <c>WorkflowRunEngine</c>); позже за тем же контрактом встанет <c>RpaRunner.exe</c>
        /// (<c>OR-02</c>). По умолчанию — null-object: инструмент отвечает «прогон не выполнен», а не падает.
        /// </summary>
        public static Spec.IWorkflowRunService WorkflowRun { get; set; } = Spec.NullWorkflowRunService.Instance;

        /// <summary>Сбрасывает подменённое хранилище ТЗ к файловой реализации по умолчанию (тесты).</summary>
        public static void ResetSpecStore()
        {
            _specStore = null;
        }

        private static Spec.ISpecificationStore _specStore;
    }
}
