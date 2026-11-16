using System;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Threading;
using RPA.Core.Services;
using RPA.Core.Workflow;
using RPA.Helpers;

namespace RPA.Core
{
    public class WorkflowDesignerEx : WorkflowDesigner, INotifyPropertyChanged
    {
        internal DesignerVariableContextMenuService DesignerVariableContextMenuService { get; private set; }

        private bool _isEdited;

        public bool IsEdited
        {
            get => _isEdited;
            set
            {
                if (_isEdited != value)
                {
                    _isEdited = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEdited)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isDependencyLoading;

        /// <summary>
        /// Пока документ ещё не загружен в дизайнер: идёт фоновая подготовка зависимостей
        /// NuGet-пакетов. Пока <c>true</c>, вкладка показывает indeterminate-индикатор.
        /// </summary>
        public bool IsDependencyLoading
        {
            get => _isDependencyLoading;
            set
            {
                if (_isDependencyLoading != value)
                {
                    _isDependencyLoading = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDependencyLoading)));
                }
            }
        }

        public Dictionary<object, SourceLocation> DesignerSourceLocationMapping { get; } = new Dictionary<object, SourceLocation>();

        public List<SourceLocation> BreakpointList { get; set; } = new List<SourceLocation>();

        /// <summary>
        /// Реестр вложенных XAML-активностей, с которым документ открыт loose-путём
        /// (<c>null</c> для штатного пути). Нужен для десериализации свежего текста
        /// при обновлении SourceLocation-маппинга после сохранения.
        /// </summary>
        internal WorkflowTypeRegistry LooseTypeRegistry { get; set; }

        /// <summary>
        /// Путь к файлу, который загружен в дизайнер
        /// </summary>
        public string CurrentWorkflowFile => Context.Items.GetValue<WorkflowFileItem>().LoadedFile;

        public SourceLocation LastBreakpoint { get; set; }

        private DesignerValidator _designerCustomValidationService;

        /// <summary>
        /// Менеджер дерева модели. Через него отслеживаются реальные правки модели
        /// (<see cref="ModelTreeManager.EditingScopeCompleted"/>), когда <see cref="ModelChanged"/>
        /// не поднимается из-за опубликованного <see cref="System.Activities.Presentation.Hosting.IDocumentPersistenceService"/>
        /// (loose-XAML путь открытия).
        /// </summary>
        private ModelTreeManager _modelTreeManager;

        /// <summary>Отложенный <see cref="WorkflowDesigner.Flush"/> после правки модели уже запланирован.</summary>
        private bool _namespaceFlushScheduled;

        public WorkflowDesignerEx()
        {
            DesignerVariableContextMenuService = new DesignerVariableContextMenuService(this);
            ModelChanged += WorkflowDesignerEx_ModelChanged;
            Context.Services.Subscribe<ModelTreeManager>(OnModelTreeManagerAvailable);
            _designerCustomValidationService = new DesignerValidator(this);
        }

        /// <summary>
        /// Останавливает фоновую валидацию дизайнера и отписывается от изменений модели.
        /// <para>
        /// Вызывается при закрытии вкладки: таймер <see cref="DesignerValidator"/> живёт на диспетчере
        /// и продолжает тикать после закрытия вкладки, хотя проверять уже нечего. При остановке
        /// приложения такой «осиротевший» тик пытается показать модальное окно конструктора
        /// (наблюдалось как падение при закрытии домена в тестах).
        /// </para>
        /// </summary>
        /// <summary>
        /// Валидатор дизайнера (замеры прохода, поколения, отмена).
        /// <para>
        /// Нужен стенду и тестам фоновой валидации: иначе про то, что проход действительно выполнился
        /// в фоне и доехал до иконок, можно судить только по визуальному дереву. <c>null</c> после
        /// <see cref="DetachValidation"/>.
        /// </para>
        /// </summary>
        internal DesignerValidator Validation => _designerCustomValidationService;

        private void OnModelTreeManagerAvailable(ModelTreeManager treeManager)
        {
            if (treeManager == null)
            {
                return;
            }

            _modelTreeManager = treeManager;
            treeManager.EditingScopeCompleted += OnEditingScopeCompleted;
        }

        /// <summary>
        /// Зафиксирован <see cref="EditingScope"/> модели — реальная правка (добавление активности,
        /// изменение свойства, добавление аргумента). Помечает вкладку «изменённой» только при
        /// эффективных изменениях — так же фильтрует сам <see cref="WorkflowDesigner"/>.
        /// </summary>
        private void OnEditingScopeCompleted(object sender, EditingScopeEventArgs e)
        {
            if (e.EditingScope != null && e.EditingScope.HasEffectiveChanges)
            {
                IsEdited = true;
                ScheduleNamespaceFlush();
            }
        }


        /// <summary>
        /// После эффективной правки модели (например, добавления активности из тулбокса) актуализирует
        /// <see cref="WorkflowDesigner.Text"/>: loose-сериализатор обязан добавить недостающий <c>xmlns</c>
        /// для типа активности сразу, а не только при сохранении. Отложенный вызов схлопывает серию правок
        /// в один <see cref="Flush"/> и не реентерит в модель.
        /// </summary>
        private void ScheduleNamespaceFlush()
        {
            if (LooseTypeRegistry == null)
            {
                // Штатный (не loose) путь сериализует сам и добавляет xmlns при сохранении.
                return;
            }

            if (_namespaceFlushScheduled)
            {
                return;
            }

            Dispatcher dispatcher = View?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            _namespaceFlushScheduled = true;
            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    try
                    {
                        Flush();
                    }
                    catch (Exception ex)
                    {
                        // Flush — сервисная операция: её сбой не должен отменять уже сделанную правку модели.
                        System.Diagnostics.Debug.WriteLine(ErrorText.Of(ex));
                    }
                    finally
                    {
                        // Сброс только после Flush: если коммит незакоммиченных правок внутри Flush
                        // снова поднимет EditingScopeCompleted, повторный Flush не должен планироваться.
                        _namespaceFlushScheduled = false;
                    }
                }));
        }

        public void DetachValidation()
        {
            _designerCustomValidationService?.Dispose();
            _designerCustomValidationService = null;

            if (_modelTreeManager != null)
            {
                _modelTreeManager.EditingScopeCompleted -= OnEditingScopeCompleted;
                _modelTreeManager = null;
            }
        }

        private void WorkflowDesignerEx_ModelChanged(object sender, EventArgs e)
        {
            IsEdited = true;
            try
            {
                DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ErrorText.Of(ex));
            }
        }
    }
}
