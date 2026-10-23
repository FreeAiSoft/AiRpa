using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RPA.Helpers;

namespace RPA.Core.Services
{
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
            if (dataGrid != null && dataGrid.Columns.Count > 0)
            {
                // Добавляем обработчик для предотвращения выхода из режима редактирования при открытии контекстного меню
                dataGrid.PreviewMouseRightButtonDown += VariableDataGrid_PreviewMouseRightButtonDown;
                dataGrid.CellEditEnding += VariableDataGrid_CellEditEnding;

                DataGridColumn firstColumn = dataGrid.Columns[0];
                DataTemplate columnTemplate = null;

                // Получаем DataTemplate в зависимости от типа столбца
                if (firstColumn is DataGridTemplateColumn templateColumn)
                {
                    columnTemplate = templateColumn.CellEditingTemplate;
                }

                // Используем полученный DataTemplate
                if (columnTemplate != null)
                {
                    Debug.WriteLine($"Получен DataTemplate первого столбца: {columnTemplate}");

                    // Создаем стиль для TextBox с черным фоном и зелеными буквами
                    Style textBoxStyle = new Style(typeof(TextBox));
                    //textBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Black)));
                    //textBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Colors.Green)));

                    // Добавляем обработчики для правильной работы контекстного меню
                    // Используем PreviewMouseRightButtonDown, чтобы захватить событие раньше
                    textBoxStyle.Setters.Add(new EventSetter(UIElement.PreviewMouseRightButtonDownEvent,
                        new System.Windows.Input.MouseButtonEventHandler(TextBox_PreviewMouseRightButtonDown)));
                    // Предотвращаем потерю фокуса при открытом меню
                    textBoxStyle.Setters.Add(new EventSetter(UIElement.PreviewLostKeyboardFocusEvent,
                        new System.Windows.Input.KeyboardFocusChangedEventHandler(TextBox_PreviewLostKeyboardFocus)));

                    // Добавляем стиль в Resources DataTemplate
                    columnTemplate.Resources ??= new ResourceDictionary();

                    // Устанавливаем стиль как ресурс по умолчанию для TextBox
                    columnTemplate.Resources[typeof(TextBox)] = textBoxStyle;

                    Debug.WriteLine("Стиль для TextBox установлен: черный фон, зеленые буквы");
                }
                else
                {
                    Debug.WriteLine("DataTemplate первого столбца не найден");
                }
            }
            else
            {
                Debug.WriteLine("variableDataGrid не найден или не содержит столбцов");
            }

            Debug.WriteLine("VariableDesigner is now visible");
        }

        /// <summary>
        /// Обработчик правой кнопки мыши на DataGrid для отслеживания открытия контекстного меню
        /// </summary>
        private void VariableDataGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
        private void TextBox_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Создаем контекстное меню, если его еще нет
                if (textBox.ContextMenu == null)
                {
                    textBox.ContextMenu = new ContextMenu
                    {
                        StaysOpen = true  // Меню остается открытым
                    };

                    // Добавляем стандартные команды редактирования с обработчиками закрытия
                    MenuItem cutItem = new MenuItem
                    {
                        Command = System.Windows.Input.ApplicationCommands.Cut,
                        Header = "Вырезать"
                    };
                    MenuItem copyItem = new MenuItem
                    {
                        Command = System.Windows.Input.ApplicationCommands.Copy,
                        Header = "Копировать"
                    };
                    MenuItem pasteItem = new MenuItem
                    {
                        Command = System.Windows.Input.ApplicationCommands.Paste,
                        Header = "Вставить"
                    };

                    // Закрываем меню после выбора команды
                    void CloseMenu()
                    {
                        textBox.ContextMenu.IsOpen = false;
                        _isContextMenuOpen = false;
                        _ = textBox.Focus();
                    }

                    cutItem.Click += (s, args) => CloseMenu();
                    copyItem.Click += (s, args) => CloseMenu();
                    pasteItem.Click += (s, args) => CloseMenu();

                    _ = textBox.ContextMenu.Items.Add(cutItem);
                    _ = textBox.ContextMenu.Items.Add(copyItem);
                    _ = textBox.ContextMenu.Items.Add(pasteItem);

                    // Обработчик для закрытия меню при клике вне его
                    void mouseHandler(object s, System.Windows.Input.MouseButtonEventArgs args)
                    {
                        if (!textBox.ContextMenu.IsMouseOver && !textBox.IsMouseOver)
                        {
                            CloseMenu();
                            System.Windows.Input.Mouse.RemovePreviewMouseDownHandler(Application.Current.MainWindow, mouseHandler);
                        }
                    }

                    // Обработчик для закрытия меню по Escape
                    void keyHandler(object s, System.Windows.Input.KeyEventArgs args)
                    {
                        if (args.Key == System.Windows.Input.Key.Escape)
                        {
                            CloseMenu();
                            System.Windows.Input.Keyboard.RemovePreviewKeyDownHandler(Application.Current.MainWindow, keyHandler);
                        }
                    }

                    textBox.ContextMenu.Opened += (s, args) =>
                    {
                        System.Windows.Input.Mouse.AddPreviewMouseDownHandler(Application.Current.MainWindow, mouseHandler);
                        System.Windows.Input.Keyboard.AddPreviewKeyDownHandler(Application.Current.MainWindow, keyHandler);
                    };
                }

                // Фокусируемся на TextBox и показываем меню с небольшой задержкой
                _ = textBox.Focus();

                // Открываемменю через Dispatcher, чтобы фокус успел установиться
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
        }

        /// <summary>
        /// Обработчик потери фокуса клавиатуры для TextBox.
        /// Предотвращает потерю фокуса при открытом контекстном меню.
        /// </summary>
        private void TextBox_PreviewLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.ContextMenu != null && textBox.ContextMenu.IsOpen)
            {
                // Если контекстное меню открыто, отменяем потерю фокуса
                e.Handled = true;
            }
        }
    }
}
