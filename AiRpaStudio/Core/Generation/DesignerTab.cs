using System.Collections.Generic;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Вкладка конструктора, открытая для файла процесса (этап 3 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Снимок состояния вкладки: путь файла, признак несохранённых правок и сам дизайнер (нужен
    /// <see cref="StudioWorkflowDesignerHost"/>, чтобы активировать вкладку). Сервис открытия работает
    /// через этот тип, а не через <c>WorkflowDesignerEx</c>, поэтому проверяется без WPF.
    /// </para>
    /// </summary>
    internal sealed class DesignerTab
    {
        internal DesignerTab(string filePath, bool isEdited, WorkflowDesignerEx designer = null)
        {
            FilePath = filePath;
            IsEdited = isEdited;
            Designer = designer;
        }

        /// <summary>Путь к файлу, загруженному во вкладку.</summary>
        internal string FilePath { get; }

        /// <summary>
        /// Во вкладке есть несохранённые правки. Сбрасывается, когда содержимое вкладки совпадает с файлом
        /// (свежая загрузка или сохранение) — иначе кнопка «Сохранить» активна сразу после открытия.
        /// </summary>
        internal bool IsEdited { get; set; }

        /// <summary>Живой дизайнер вкладки; <c>null</c> в тестовой среде без WPF.</summary>
        internal WorkflowDesignerEx Designer { get; }

        public override string ToString()
        {
            return $"{FilePath ?? "(без файла)"}{(IsEdited ? " *" : string.Empty)}";
        }
    }

    /// <summary>
    /// Среда конструктора, которой пользуется <see cref="DesignerOpenService"/>: список открытых вкладок,
    /// открытие файла, активация вкладки, закрытие и сброс «изменён».
    /// <para>
    /// Отдельный интерфейс нужен по двум причинам: настоящая среда (<see cref="StudioWorkflowDesignerHost"/>)
    /// тянет за собой документ студии и WPF, а порядок действий сервиса обязан проверяться юнит-тестами;
    /// и второй реализации не появится — «работает в одном инструменте, ломается в другом» начинается
    /// ровно с расхождения путей открытия.
    /// </para>
    /// </summary>
    internal interface IWorkflowDesignerHost
    {
        /// <summary>Все вкладки конструктора в порядке панели документов.</summary>
        IReadOnlyList<DesignerTab> GetOpenTabs();

        /// <summary>Текущая активная вкладка (по ней проверяется «вкладка активна»).</summary>
        DesignerTab ActiveTab { get; }

        /// <summary>
        /// Загружает файл в новый конструктор и добавляет вкладку в панель документов.
        /// Возвращает <c>null</c>, если загрузчик не создал дизайнер.
        /// </summary>
        DesignerTab Open(string filePath);

        /// <summary>Делает вкладку активной (выбирает её в панели документов).</summary>
        void Activate(DesignerTab tab);

        /// <summary>Закрывает вкладку без сохранения — так с файла снимается блокировка студией.</summary>
        void Close(DesignerTab tab);

        /// <summary>Сбрасывает у вкладки признак «изменён» (содержимое совпадает с файлом на диске).</summary>
        void MarkClean(DesignerTab tab);
    }
}
