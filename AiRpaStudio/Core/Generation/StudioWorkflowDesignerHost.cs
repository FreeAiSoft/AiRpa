using System;
using System.Collections.Generic;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Настоящая среда конструктора студии (этап 3 плана
    /// конвейер генерации XAML): вкладки — это
    /// <see cref="CustomWfDesigner.WorkflowDesigners"/>, открытие — единственный существующий путь
    /// <see cref="LoadActivityToDesignerHelper.LoadActivityToDesigner"/>, активация — свойство
    /// <see cref="CustomWfDesigner.ActiveWorkflowDesignerEx"/> (по нему <c>DocGroupHelper</c> выбирает
    /// вкладку в панели документов).
    /// <para>
    /// Своей логики загрузки здесь быть не должно: среда только переводит действия сервиса в вызовы
    /// студии. Иначе появятся два пути открытия, и «работает в одном инструменте, ломается в другом»
    /// станет вопросом времени.
    /// </para>
    /// </summary>
    internal sealed class StudioWorkflowDesignerHost : IWorkflowDesignerHost
    {
        private readonly CustomWfDesigner _designer;
        private readonly Func<string, WorkflowDesignerEx> _load;
        private readonly Action<string> _log;

        internal StudioWorkflowDesignerHost(
            CustomWfDesigner designer = null,
            Func<string, WorkflowDesignerEx> load = null,
            Action<string> log = null)
        {
            _designer = designer ?? CustomWfDesigner.GetInstance();
            _load = load ?? LoadActivityToDesignerHelper.LoadActivityToDesigner;
            _log = log;
        }

        public IReadOnlyList<DesignerTab> GetOpenTabs()
        {
            var tabs = new List<DesignerTab>();

            foreach (WorkflowDesignerEx designer in _designer.WorkflowDesigners)
            {
                // GetLoadedFile возвращает null для вкладки, созданной до загрузки файла.
                string filePath = CustomWfDesigner.GetLoadedFile(designer);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    tabs.Add(new DesignerTab(filePath, designer.IsEdited, designer));
                }
            }

            return tabs;
        }

        public DesignerTab ActiveTab
        {
            get
            {
                WorkflowDesignerEx active = _designer.ActiveWorkflowDesignerEx;
                if (active == null)
                {
                    return null;
                }

                string filePath = CustomWfDesigner.GetLoadedFile(active);
                return string.IsNullOrWhiteSpace(filePath)
                    ? null
                    : new DesignerTab(filePath, active.IsEdited, active);
            }
        }

        public DesignerTab Open(string filePath)
        {
            WorkflowDesignerEx designer = _load(filePath);
            if (designer == null)
            {
                return null;
            }

            // Штатный загрузчик сам добавляет дизайнер в WorkflowDesigners; страховка нужна для
            // другого загрузчика (тесты, будущие пути) — вкладка обязана оказаться в панели документов.
            if (!_designer.WorkflowDesigners.Contains(designer))
            {
                _designer.WorkflowDesigners.Add(designer);
            }

            return new DesignerTab(filePath, designer.IsEdited, designer);
        }

        public void Activate(DesignerTab tab)
        {
            if (tab?.Designer is WorkflowDesignerEx designer)
            {
                // Сеттер сам выбирает вкладку в DocGroupHelper и обновляет состояние кнопок сохранения.
                _designer.ActiveWorkflowDesignerEx = designer;
                return;
            }

            _log?.Invoke($"Вкладка {tab?.FilePath} активирована без дизайнера: активация невозможна");
        }

        public void Close(DesignerTab tab)
        {
            if (string.IsNullOrWhiteSpace(tab?.FilePath))
            {
                return;
            }

            _designer.RemoveDesignerByLoadedFile(tab.FilePath);
        }

        public void MarkClean(DesignerTab tab)
        {
            tab.IsEdited = false;

            if (tab.Designer != null)
            {
                // Сброс IsEdited обновляет CanSave/CanSaveAll через подписку CustomWfDesigner.
                tab.Designer.IsEdited = false;
            }
        }
    }
}
