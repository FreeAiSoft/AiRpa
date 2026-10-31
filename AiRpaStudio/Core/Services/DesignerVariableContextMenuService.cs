using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RPA.Helpers;

namespace RPA.Core.Services
{
    /// <summary>
    /// Обслуживание таблиц переменных/аргументов в дизайнере: открытие контекстного меню
    /// редактирования без выхода из режима редактирования ячейки.
    /// <para>
    /// Меню и поле ввода строятся на палитре студии (<see cref="WfDesignerTheme.CreateEditingContextMenu"/>),
    /// поэтому в тёмной теме нет белого выпадающего прямоугольника и «чёрный фон с зелёными буквами»,
    /// оставшихся от отладочного варианта (F-06 плана docs\Vs2026_Theme_Plan.md).
    /// </para>
    /// </summary>
    internal class DesignerVariableContextMenuService
    {
        private readonly UIElement _view;
        private bool _isContextMenuOpen = false;
        private bool _isSetArgumentsDataGrid = false;
        private bool _isSetVariableDataGrid = false;

        public DesignerVariableContextMenuService(WorkflowDesignerEx workflowDesignerEx)
        {
            _view = workflowDesignerEx.View;
        }

        /// <summary>
        /// Инициализировать отслеживание VariableDesigner.
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
            // Поиск VariableDesigner после загрузки контрола
            Control variableDesigner = XamlHelper.FindVisualChild<Control>(_view, "VariableDesigner");
            if (variableDesigner != null)
            {
                variableDesigner.IsVisibleChanged += VariableDesigner_IsVisibleChanged;
            }

            Control argumentDesigner = XamlHelper.FindVisualChild<Control>(_view, "ArgumentDesigner");
            if (argumentDesigner != null)
            {
                argumentDesigner.IsVisibleChanged += ArgumentDesigner_IsVisibleChanged;
            }
        }

        private void ArgumentDesigner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is UIElement argumentDesigner && argumentDesigner.IsVisible && !_isSetArgumentsDataGrid)
            {
                // ArgumentDesigner стал видимым
                _isSetArgumentsDataGrid = true;
                OnVariableDesignerVisible(argumentDesigner, "argumentsDataGrid");
            }
        }

        private void VariableDesigner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs _)
        {
            if (sender is UIElement variableDesigner && variableDesigner.IsVisible && !_isSetVariableDataGrid)
            {
                // VariableDesigner стал видимым
                _isSetVariableDataGrid = true;
                OnVariableDesignerVisible(variableDesigner, "variableDataGrid");
            }
        }

        /// <summary>
        /// Вызывается когда VariableDesigner становится видимым
        /// </summary>
        protected virtual void OnVariableDesignerVisible(UIElement designer, string nameDataGrid)
        {
            // Найдем variableDataGrid внутри variableDesigner через логическое дерево (т.к. может быть невидимым)
            DataGrid dataGrid = XamlHelper.FindLogicalChild<DataGrid>(designer, nameDataGrid);
            if (dataGrid == null || dataGrid.Columns.Count == 0)
            {
                return;
            }

            // Добавляем обработчик для предотвращения выхода из режима редактирования при открытии контекстного меню
            dataGrid.PreviewMouseRightButtonDown += VariableDataGrid_PreviewMouseRightButtonDown;
            dataGrid.CellEditEnding += VariableDataGrid_CellEditEnding;

            DataGridColumn firstColumn = dataGrid.Columns[0];

            // Получаем DataTemplate в зависимости от типа столбца
            DataTemplate columnTemplate = firstColumn is DataGridTemplateColumn templateColumn
                ? templateColumn.CellEditingTemplate
                : null;

            if (columnTemplate == null)
            {
                return;
            }

            Style textBoxStyle = CreateTextBoxStyle();
            columnTemplate.Resources ??= new ResourceDictionary();

            // Устанавливаем стиль как ресурс по умолчанию для TextBox
            columnTemplate.Resources[typeof(TextBox)] = textBoxStyle;
        }

        /// <summary>
        /// Стиль поля ввода в ячейке: обработчики контекстного меню + кисти палитры
        /// (раньше здесь оставались закомментированные чёрный фон и зелёные буквы).
        /// </summary>
        private Style CreateTextBoxStyle()
        {
            StudioThemeColors palette = StudioPalette.Detect(Application.Current?.Resources);

            var textBoxStyle = new Style(typeof(TextBox));
            textBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Surface)));
            textBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
            textBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, WfThemeStyles.Brush(palette.SeparatorStrong)));
            textBoxStyle.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, WfThemeStyles.Brush(palette.Foreground)));
            textBoxStyle.Setters.Add(new Setter(Control.FontFamilyProperty, WfThemeStyles.Font));

            // Добавляем обработчики для правильной работы контекстного меню
            // Используем PreviewMouseRightButtonDown, чтобы захватить событие раньше
            textBoxStyle.Setters.Add(new EventSetter(UIElement.PreviewMouseRightButtonDownEvent,
                new MouseButtonEventHandler(TextBox_PreviewMouseRightButtonDown)));
            // Предотвращаем потерю фокуса при открытом меню
            textBoxStyle.Setters.Add(new EventSetter(UIElement.PreviewLostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(TextBox_PreviewLostKeyboardFocus)));

            return textBoxStyle;
        }

        /// <summary>
        /// Обработчик правой кнопки мыши на DataGrid для отслеживания открытия контекстного меню
        /// </summary>
        private void VariableDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isContextMenuOpen = true;
        }

        /// <summary>
        /// Обработчик завершения редактирования ячейки DataGrid.
        /// Предотвращает выход из режима редактирования при открытии контекстного меню.
        /// </summary>
        private void VariableDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_isContextMenuOpen)
            {
                // Отменяем завершение редактирования, если открывается контекстное меню
                e.Cancel = true;
                _isContextMenuOpen = false;
            }
        }

        /// <summary>
        /// Обработчик нажатия правой кнопки мыши для TextBox в DataGrid.
        /// Показывает контекстное меню программно, обходя блокировку NoContextMenuGrid.
        /// </summary>
        private void TextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is TextBox textBox))
            {
                return;
            }

            // Создаем контекстное меню, если его еще нет
            if (textBox.ContextMenu == null)
            {
                StudioThemeColors palette = StudioPalette.Detect(Application.Current?.Resources);
                ContextMenu contextMenu = WfDesignerTheme.CreateEditingContextMenu(palette);

                // Закрываем меню после выбора команды
                void CloseMenu()
                {
                    contextMenu.IsOpen = false;
                    _isContextMenuOpen = false;
                    _ = textBox.Focus();
                }

                foreach (MenuItem item in contextMenu.Items)
                {
                    if (item != null)
                    {
                        item.Click += (s, args) => CloseMenu();
                    }
                }

                textBox.ContextMenu = contextMenu;

                // Обработчик для закрытия меню при клике вне его
                void mouseHandler(object s, MouseButtonEventArgs args)
                {
                    if (!contextMenu.IsMouseOver && !textBox.IsMouseOver)
                    {
                        CloseMenu();
                        Mouse.RemovePreviewMouseDownHandler(Application.Current.MainWindow, mouseHandler);
                    }
                }

                // Обработчик для закрытия меню по Escape
                void keyHandler(object s, KeyEventArgs args)
                {
                    if (args.Key == Key.Escape)
                    {
                        CloseMenu();
                        Keyboard.RemovePreviewKeyDownHandler(Application.Current.MainWindow, keyHandler);
                    }
                }

                contextMenu.Opened += (s, args) =>
                {
                    Mouse.AddPreviewMouseDownHandler(Application.Current.MainWindow, mouseHandler);
                    Keyboard.AddPreviewKeyDownHandler(Application.Current.MainWindow, keyHandler);
                };
            }

            // Фокусируемся на TextBox и показываем меню с небольшой задержкой
            _ = textBox.Focus();

            // Открываем меню через Dispatcher, чтобы фокус успел установиться
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() =>
                {
                    textBox.ContextMenu.PlacementTarget = textBox;
                    textBox.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    textBox.ContextMenu.IsOpen = true;
                }));

            // Помечаем событие как обработанное полностью
            e.Handled = true;
        }

        /// <summary>
        /// Обработчик потери фокуса клавиатуры для TextBox.
        /// Предотвращает потерю фокуса при открытом контекстном меню.
        /// </summary>
        private void TextBox_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.ContextMenu != null && textBox.ContextMenu.IsOpen)
            {
                // Если контекстное меню открыто, отменяем потерю фокуса
                e.Handled = true;
            }
        }
    }
}
