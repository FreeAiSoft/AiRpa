using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RPA.Helpers;

namespace RPA.Core.Services
{
    /// <summary>
    /// Обслуживание таблиц переменных/аргументов в дизайнере: открытие контекстного меню
    /// редактирования без выхода из режима редактирования ячейки.
    /// <para>
    /// Принципы, инварианты и карта тестов зафиксированы в
    /// <c>docs\Designer_Variable_ContextMenu_Principles.md</c>. Коротко: штатный
    /// <see cref="ContextMenu"/> на ячейке <see cref="DataGrid"/> в WF-дизайнере не работает (правый клик
    /// завершает правку и закрывает панель), поэтому меню открывается программно из
    /// <see cref="UIElement.PreviewMouseRightButtonDownEvent"/> самой таблицы — она выше ячейки в туннеле,
    /// и только так событие можно пометить обработанным до ячейки, а завершение правки отменить флагом
    /// в <c>CellEditEnding</c>.
    /// </para>
    /// <para>
    /// Все обработчики живут на <see cref="DataGrid"/>: в шаблоны правки (и в их
    /// <see cref="FrameworkTemplate.Resources"/>) ничего не пишется — шаблоны WF-дизайнера запечатаны,
    /// их словарь только для чтения (дефект 1.0.0.0: <c>InvalidOperationException: ResourceDictionary
    /// доступен только для чтения</c>).
    /// </para>
    /// <para>
    /// Меню и поле ввода строятся на палитре студии (<see cref="WfDesignerTheme.CreateEditingContextMenu"/>,
    /// <see cref="ApplyCellPalette"/>), поэтому в тёмной теме нет белого выпадающего прямоугольника и
    /// «чёрный фон с зелёными буквами», оставшихся от отладочного варианта (F-06 плана docs\Vs2026_Theme_Plan.md).
    /// </para>
    /// <para>
    /// Решения вынесены в <see cref="DesignerVariableContextMenuPolicy"/> (чистые функции) и
    /// <see cref="DesignerContextMenuState"/> (автомат флагов): их покрывают
    /// <c>DesignerVariableContextMenuPolicyTests</c> и <c>DesignerVariableContextMenuTests</c>.
    /// </para>
    /// </summary>
    internal class DesignerVariableContextMenuService
    {
        /// <summary>Имена панелей и таблиц <c>DesignerView</c> (строятся шаблоном, поиск по имени).</summary>
        internal const string VariableDesignerElementName = "VariableDesigner";

        internal const string ArgumentDesignerElementName = "ArgumentDesigner";
        internal const string VariableDataGridElementName = "variableDataGrid";
        internal const string ArgumentsDataGridElementName = "argumentsDataGrid";

        /// <summary>Таблица «Пространства имён» (ImportDesigner.xaml): подключается общим проходом по типу.</summary>
        internal const string ImportedNamespacesDataGridElementName = "importedNamespacesDataGrid";

        /// <summary>Докуда поднимаемся по цепочке видимости таблицы (панель → bottom panel → дизайнер).</summary>
        private const int VisibilityChainDepth = 4;

        private readonly UIElement _view;
        private readonly DesignerContextMenuState _state = new DesignerContextMenuState();
        private readonly KeyboardFocusChangedEventHandler _lostFocusGuard;
        private readonly HashSet<DataGrid> _prepared = new HashSet<DataGrid>();

        /// <summary>Таблицы, у которых обработчики ещё не подключены: столбцы появляются после шаблона.</summary>
        private readonly HashSet<DataGrid> _pending = new HashSet<DataGrid>();

        /// <summary>Элементы цепочки видимости таблицы, на которые уже подписаны (идемпотентность).</summary>
        private readonly HashSet<UIElement> _visibilityHooked = new HashSet<UIElement>();

        private bool _isSetArgumentsDataGrid;

        private bool _isSetVariableDataGrid;

        public DesignerVariableContextMenuService(WorkflowDesignerEx workflowDesignerEx)
            : this(workflowDesignerEx?.View)
        {
        }

        /// <summary>Конструктор для тестов: достаточно любого <see cref="UIElement"/> (HWND не нужен).</summary>
        internal DesignerVariableContextMenuService(UIElement view)
        {
            _view = view;
            _lostFocusGuard = VariableDataGrid_PreviewLostKeyboardFocus;
        }

        /// <summary>Состояние меню (для тестов и диагностики).</summary>
        internal bool IsMenuOpen => _state.IsMenuOpen;

        /// <summary>Подключены ли обработчики к таблице (для тестов и диагностики).</summary>
        internal bool IsAttached(DataGrid dataGrid) => dataGrid != null && _prepared.Contains(dataGrid);

        /// <summary>Сколько таблиц дизайнера обслуживается (для тестов и диагностики).</summary>
        internal int AttachedTableCount => _prepared.Count;


        /// <summary>
        /// Инициализировать отслеживание панелей переменных и аргументов.
        /// Вызывать после загрузки XAML в дизайнер.
        /// </summary>
        public void InitializeVariableDesignerTracking()
        {
            if (_view is FrameworkElement viewElement)
            {
                viewElement.Loaded += DesignerView_Loaded;
            }
        }

        private void DesignerView_Loaded(object sender, RoutedEventArgs e)
        {
            // Панели строятся шаблоном DesignerView, поэтому ищем их после Loaded по визуальному дереву.
            if (XamlHelper.FindVisualChild<Control>(_view, VariableDesignerElementName) is Control variableDesigner)
            {
                variableDesigner.IsVisibleChanged += VariableDesigner_IsVisibleChanged;
            }

            if (XamlHelper.FindVisualChild<Control>(_view, ArgumentDesignerElementName) is Control argumentDesigner)
            {
                argumentDesigner.IsVisibleChanged += ArgumentDesigner_IsVisibleChanged;
            }
            // Таблицы, которые не находятся по именам панелей: «Пространства имён» (importedNamespacesDataGrid)
            // и любые таблицы, которые WF переименует в следующей версии. Поиск идёт по типу.
            _ = RefreshTables(_view);

        }

        private void ArgumentDesigner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is UIElement argumentDesigner
                && DesignerVariableContextMenuPolicy.ShouldInitializeOnVisibilityChanged(argumentDesigner.IsVisible, _isSetArgumentsDataGrid))
            {
                _isSetArgumentsDataGrid = true;
                OnVariableDesignerVisible(argumentDesigner, ArgumentsDataGridElementName);
            }
        }

        private void VariableDesigner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is UIElement variableDesigner
                && DesignerVariableContextMenuPolicy.ShouldInitializeOnVisibilityChanged(variableDesigner.IsVisible, _isSetVariableDataGrid))
            {
                _isSetVariableDataGrid = true;
                OnVariableDesignerVisible(variableDesigner, VariableDataGridElementName);
            }
        }

        /// <summary>
        /// Вызывается, когда панель переменных или аргументов становится видимой: подключает обработчики
        /// таблицы ровно один раз на таблицу (правый клик, начало и завершение правки, удержание фокуса).
        /// <para>
        /// В шаблоны правки ничего не пишется: у запечатанных шаблонов WF-дизайнера
        /// <see cref="FrameworkTemplate.Resources"/> только для чтения — это и роняло панель в 1.0.0.0.
        /// </para>
        /// </summary>
        protected virtual void OnVariableDesignerVisible(UIElement designer, string nameDataGrid)
        {
            // Таблица ищется по логическому дереву: панель может быть ещё не отрисована.
            Attach(XamlHelper.FindLogicalChild<DataGrid>(designer, nameDataGrid));
        }

        /// <summary>
        /// Подключает обработчики к таблице ровно один раз (правый клик, начало и завершение правки,
        /// удержание фокуса). В шаблоны правки ничего не пишется: у запечатанных шаблонов WF-дизайнера
        /// <see cref="FrameworkTemplate.Resources"/> только для чтения — это и роняло панель в 1.0.0.0.
        /// </summary>
        private void Attach(DataGrid dataGrid)
        {
            if (!DesignerVariableContextMenuPolicy.CanAttach(dataGrid) || !_prepared.Add(dataGrid))
            {
                return;
            }

            // Взвод отмены — на самой таблице: PreviewMouseRightButtonDown туннелирует сверху вниз,
            // поэтому обработчик таблицы срабатывает раньше обработчиков ячейки и поля ввода.
            dataGrid.PreviewMouseRightButtonDown += VariableDataGrid_PreviewMouseRightButtonDown;
            dataGrid.CellEditEnding += VariableDataGrid_CellEditEnding;

            // Палитра поля ввода ставится при входе в правку — полю, а не стилю в шаблоне ячейки.
            dataGrid.PreparingCellForEdit += VariableDataGrid_PreparingCellForEdit;

            // Меню забирает клавиатурный фокус: без удержания фокуса DataGrid завершает правку и роняет панель.
            dataGrid.AddHandler(UIElement.PreviewLostKeyboardFocusEvent, _lostFocusGuard, true);

            HookTableVisibility(dataGrid);
        }

        /// <summary>
        /// Подключает обработчики ко всем таблицам дизайнера — «Переменные», «Аргументы» и «Пространства
        /// имён», — не опираясь на имена элементов: имена объявляет шаблон WF (<c>variableDataGrid</c>,
        /// <c>argumentsDataGrid</c>, <c>importedNamespacesDataGrid</c>), а до появления панели таблиц в
        /// дереве нет вовсе. Возвращает число подключённых таблиц.
        /// </summary>
        internal int RefreshTables(UIElement root)
        {
            if (root == null)
            {
                return _prepared.Count;
            }

            foreach (DataGrid dataGrid in WfDataGridTheme.FindTables(root))
            {
                if (DesignerVariableContextMenuPolicy.CanAttach(dataGrid))
                {
                    Attach(dataGrid);
                    continue;
                }

                // Таблица уже есть, но столбцы объявляет шаблон панели: повторная попытка на Loaded.
                if (!_pending.Add(dataGrid))
                {
                    continue;
                }

                void onLoaded(object sender, RoutedEventArgs args)
                {
                    dataGrid.Loaded -= onLoaded;
                    _ = _pending.Remove(dataGrid);

                    if (DesignerVariableContextMenuPolicy.CanAttach(dataGrid))
                    {
                        Attach(dataGrid);
                    }
                }

                dataGrid.Loaded += onLoaded;
            }

            return _prepared.Count;
        }

        /// <summary>
        /// Подписка на цепочку видимости таблицы: панель «Пространства имён» открывается по вкладке shell bar,
        /// и столбцы таблица получает только к этому моменту. Проход идемпотентный, поэтому повторная
        /// подписка на уже известные элементы не ставится.
        /// </summary>
        private void HookTableVisibility(DataGrid dataGrid)
        {
            DependencyObject element = dataGrid;

            for (int depth = 0; element != null && depth < VisibilityChainDepth; depth++)
            {
                if (element is UIElement visual && _visibilityHooked.Add(visual))
                {
                    visual.IsVisibleChanged += TableVisibility_IsVisibleChanged;
                }

                element = ParentOf(element);
            }
        }

        private void TableVisibility_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(e.NewValue is bool isVisible) || !isVisible || _view == null)
            {
                return;
            }

            // Отложенно: к этому моменту панель уже применила шаблон и таблица получила столбцы.
            _ = _view.Dispatcher.BeginInvoke(
                new Action(() => RefreshTables(_view)),
                DispatcherPriority.Loaded);
        }

        private static DependencyObject ParentOf(DependencyObject element)
        {
            if (element == null)
            {
                return null;
            }

            DependencyObject parent = LogicalTreeHelper.GetParent(element);
            if (parent == null && XamlHelper.IsVisual(element))
            {
                parent = VisualTreeHelper.GetParent(element);
            }

            return parent;
        }

        /// <summary>
        /// Правая кнопка мыши в таблице. Если клик пришёл в поле ввода правки — взводит одноразовую отмену
        /// завершения правки и открывает меню программно; клик мимо поля (заголовок, скроллбар, пустая
        /// область) не взводит ничего, иначе флаг «залипал» и глушил следующую правку.
        /// </summary>
        private void VariableDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TextBox textBox = DesignerVariableContextMenuPolicy.FindEditingField(e.OriginalSource as DependencyObject);
            if (textBox == null)
            {
                return;
            }

            _state.NotifyRightButtonDown();
            OpenEditingContextMenu(textBox);

            // Событие помечается обработанным целиком: парное MouseRightButtonDown (на нём висит
            // ContextMenuService) не поднимается, ячейка не завершает правку, меню открываем мы сами.
            e.Handled = true;
        }

        /// <summary>
        /// Показ меню у поля ввода. Фокус ставится до открытия: иначе <c>ApplicationCommands.Cut/Copy/Paste</c>
        /// уходят дизайнеру (у него есть свой обработчик) и работают с чужой целью. Само открытие
        /// откладывается до <see cref="DispatcherPriority.Input"/>, чтобы фокус успел встать.
        /// </summary>
        private void OpenEditingContextMenu(TextBox textBox)
        {
            ContextMenu contextMenu = textBox.ContextMenu ?? CreateContextMenu(textBox);

            // Палитра ставится перед показом меню: путь через PreparingCellForEdit не гарантирован
            // (шаблон правки может перекрыть кисти своими), а поле уже существует.
            ApplyCellPalette(textBox, StudioPalette.Active);
            _ = textBox.Focus();

            _ = textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                DesignerVariableContextMenuPolicy.ConfigureMenuPlacement(contextMenu, textBox);
                contextMenu.IsOpen = true;
            }));
        }

        /// <summary>
        /// Начало правки ячейки: палитра студии ставится редактору значения локальными значениями.
        /// Стиль в ресурсы шаблона писать нельзя (шаблоны правки запечатаны), а неявный ресурс выше по
        /// дереву перекрывался бы собственным стилем редактора в шаблоне WF.
        /// <para>
        /// Редактор — не только поле ввода: у <c>DataGridComboBoxColumn</c> (столбцы Scope и Modifiers)
        /// в <see cref="DataGridPreparingCellForEditEventArgs.EditingElement"/> лежит сам <see cref="ComboBox"/>,
        /// и без палитры он остаётся с белой плашкой шаблона Aero2, а его список — с чёрными буквами на
        /// тёмном фоне. Поэтому палитра ставится поддереву целиком, а для
        /// <c>DataGridTemplateColumn</c> (в EditingElement — <see cref="ContentPresenter"/>) ещё раз —
        /// после применения шаблона правки.
        /// </para>
        /// </summary>
        private void VariableDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (!(e.EditingElement is DependencyObject editingElement))
            {
                return;
            }

            ApplyEditorPalette(editingElement, StudioPalette.Active);

            if (editingElement is FrameworkElement host && host.Dispatcher != null)
            {
                // Отложенный проход: содержимое шаблона правки появляется после раскладки.
                _ = host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    ApplyEditorPalette(editingElement, StudioPalette.Active)));
            }
        }

        /// <summary>
        /// Кисти поля ввода в ячейке из палитры студии (раньше здесь оставались закомментированные
        /// чёрный фон и зелёные буквы). Применяются на каждую правку, поэтому смена темы подхватывается
        /// без пересоздания шаблонов.
        /// <para>
        /// Реализация — в <see cref="WfVisualTreeThemer.ApplyTextEditor"/>: тем же набором кистей красятся
        /// поля ввода строк панели «Свойства» и редакторы ячеек таблиц (один источник цвета на всю студию).
        /// </para>
        /// </summary>
        internal static void ApplyCellPalette(TextBox textBox, StudioThemeColors palette)
        {
            WfVisualTreeThemer.ApplyTextEditor(textBox, palette, strongBorder: true);
        }

        /// <summary>
        /// Палитра редактора ячейки целиком: поля ввода и комбобоксы (Scope, Modifiers). Один вход и для
        /// сервиса контекстного меню, и для таблицы (<see cref="WfDataGridTheme"/>).
        /// </summary>
        internal static void ApplyEditorPalette(DependencyObject editingElement, StudioThemeColors palette)
        {
            WfVisualTreeThemer.ApplyEditorPalette(editingElement, palette);
        }

        /// <summary>
        /// Обработчик завершения редактирования ячейки DataGrid.
        /// Предотвращает выход из режима редактирования при открытии контекстного меню.
        /// Отмена одноразовая: второй <c>CellEditEnding</c> уже не отменяется.
        /// </summary>
        private void VariableDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            e.Cancel = _state.NotifyCellEditEnding();
        }

        /// <summary>
        /// Потеря клавиатурного фокуса: пока меню открыто, поле ввода фокус не отдаёт — иначе DataGrid
        /// завершает правку и панель закрывается.
        /// </summary>
        private void VariableDataGrid_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.OriginalSource is TextBox textBox && textBox.ContextMenu != null && textBox.ContextMenu.IsOpen)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Меню создаётся один раз на поле ввода. Цвета применяются на каждое открытие
        /// (<see cref="WfDesignerTheme.RecolorEditingContextMenu"/>), иначе после смены темы меню
        /// оставалось в палитре, в которой было создано. Глобальные обработчики закрытия живут
        /// только на время показа меню и снимаются в <see cref="ContextMenu.Closed"/>.
        /// </summary>
        private ContextMenu CreateContextMenu(TextBox textBox)
        {
            ContextMenu contextMenu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Active);

            foreach (MenuItem item in contextMenu.Items.OfType<MenuItem>())
            {
                item.Click += (s, args) => CloseMenu(textBox, contextMenu);
            }

            UIElement interactionRoot = TryGetInteractiveRoot();

            void mouseHandler(object s, MouseButtonEventArgs args)
            {
                if (DesignerVariableContextMenuPolicy.ShouldCloseOnMouseDown(contextMenu.IsMouseOver, textBox.IsMouseOver))
                {
                    CloseMenu(textBox, contextMenu);
                }
            }

            void keyHandler(object s, KeyEventArgs args)
            {
                if (DesignerVariableContextMenuPolicy.ShouldCloseOnKey(args.Key))
                {
                    args.Handled = true;
                    CloseMenu(textBox, contextMenu);
                }
            }

            contextMenu.Opened += (s, args) =>
            {
                WfDesignerTheme.RecolorEditingContextMenu(contextMenu, StudioPalette.Active);
                _state.NotifyMenuOpened();

                if (interactionRoot != null)
                {
                    Mouse.AddPreviewMouseDownHandler(interactionRoot, mouseHandler);
                    Keyboard.AddPreviewKeyDownHandler(interactionRoot, keyHandler);
                }
            };

            // Снятие глобальных обработчиков — только здесь: раньше они снимались в самом обработчике
            // и оставались на окне при закрытии меню пунктом/кликом вне/Escape (утечка на каждый показ).
            contextMenu.Closed += (s, args) =>
            {
                if (interactionRoot != null)
                {
                    Mouse.RemovePreviewMouseDownHandler(interactionRoot, mouseHandler);
                    Keyboard.RemovePreviewKeyDownHandler(interactionRoot, keyHandler);
                }

                _state.NotifyMenuClosed();
            };

            textBox.ContextMenu = contextMenu;
            return contextMenu;
        }

        private void CloseMenu(TextBox textBox, ContextMenu contextMenu)
        {
            if (contextMenu != null)
            {
                contextMenu.IsOpen = false;
            }

            _state.NotifyMenuClosed();
            _ = textBox?.Focus();
        }

        /// <summary>
        /// Корневое окно для глобальных обработчиков закрытия. <see cref="Application.Current"/> и
        /// <see cref="Application.MainWindow"/> могут отсутствовать (тесты, дизайн-тайм) — тогда меню
        /// открывается без глобальных хуков, а не падает с <c>ArgumentNullException</c>.
        /// </summary>
        private static UIElement TryGetInteractiveRoot()
        {
            return Application.Current?.MainWindow;
        }
    }
}
