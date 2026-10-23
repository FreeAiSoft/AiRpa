using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LogControlLibrary.Models;
using LogControlLibrary.ViewModels;

namespace LogControlLibrary.Controls
{
    /// <summary>
    /// Высокопроизводительный UserControl для отображения логов
    /// </summary>
    public partial class LogViewerControl : UserControl
    {
        #region Dependency Properties

        /// <summary>
        /// Dependency Property для ViewModel
        /// </summary>
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(LogControlViewModel), typeof(LogViewerControl),
                new PropertyMetadata(null, OnViewModelChanged));

        /// <summary>
        /// Dependency Property для автопрокрутки
        /// </summary>
        public static readonly DependencyProperty AutoScrollToEndProperty =
            DependencyProperty.Register(nameof(AutoScrollToEnd), typeof(bool), typeof(LogViewerControl),
                new PropertyMetadata(true));

        /// <summary>
        /// ViewModel для управления логами
        /// </summary>
        public LogControlViewModel ViewModel
        {
            get => (LogControlViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        /// <summary>
        /// Автоматическая прокрутка к концу при добавлении новых записей
        /// </summary>
        public bool AutoScrollToEnd
        {
            get => (bool)GetValue(AutoScrollToEndProperty);
            set => SetValue(AutoScrollToEndProperty, value);
        }

        #endregion

        #region Поля

        private bool _autoScrollEnabled = true;
        private ScrollViewer _scrollViewer;
        private readonly DispatcherTimer _batchUpdateTimer;
        private bool _hasPendingUpdates = false;
        
        // Поля для оптимизации
        private readonly DispatcherTimer _scrollPositionTimer;
        private double _lastScrollPosition = -1;
        private readonly DispatcherTimer _autoScrollDebounceTimer;
        private bool _pendingAutoScrollState = true;

        #endregion

        #region Конструктор

        /// <summary>
        /// Конструктор UserControl
        /// </summary>
        public LogViewerControl()
        {
            InitializeComponent();
            
            // Инициализация таймера для батчевых обновлений
            _batchUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // Батчевые обновления каждые 100ms
            };
            _batchUpdateTimer.Tick += OnBatchUpdateTimerTick;
            
            // Инициализация таймера для отслеживания позиции прокрутки
            _scrollPositionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200) // Обновление позиции каждые 200ms
            };
            _scrollPositionTimer.Tick += OnScrollPositionTimerTick;
            
            // Инициализация таймера для debouncing автопрокрутки
            _autoScrollDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300) // Debouncing для автопрокрутки 300ms
            };
            _autoScrollDebounceTimer.Tick += OnAutoScrollDebounceTimerTick;
            
            // Создаем ViewModel по умолчанию
            if (ViewModel == null)
            {
                ViewModel = new LogControlViewModel();
            }

            DataContext = ViewModel;
            Loaded += OnLoaded;
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Добавление записи лога
        /// </summary>
        /// <param name="level">Уровень логирования</param>
        /// <param name="message">Сообщение</param>
        /// <param name="timestamp">Время (опционально)</param>
        public void AddLog(LogLevel level, string message, DateTime? timestamp = null)
        {
            var logEntry = new LogEntry(level, message, timestamp);
            AddLog(logEntry);
        }

        /// <summary>
        /// Добавление записи лога
        /// </summary>
        /// <param name="logEntry">Запись лога</param>
        public void AddLog(ILogEntry logEntry)
        {
            if (logEntry == null) return;

            // Используем Dispatcher для безопасного обновления UI из любого потока
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ViewModel?.AddLog(logEntry);
                
                // Планируем батчевое обновление для автопрокрутки
                if (AutoScrollToEnd && ViewModel?.AutoScroll == true)
                {
                    ScheduleBatchUpdate();
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// Массовое добавление записей логов
        /// </summary>
        /// <param name="logEntries">Коллекция записей логов</param>
        public void AddLogs(IEnumerable<ILogEntry> logEntries)
        {
            if (logEntries == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ViewModel?.AddLogs(logEntries);
                
                if (AutoScrollToEnd && ViewModel?.AutoScroll == true)
                {
                    ScheduleBatchUpdate();
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// Очистка всех логов
        /// </summary>
        public void ClearLogs()
        {
            ViewModel?.ClearLogsCommand?.Execute(null);
        }

        /// <summary>
        /// Прокрутка к концу списка
        /// </summary>
        public void ScrollToEnd()
        {
            try
            {
                if (ViewModel?.FilteredLogs != null)
                {
                    var filteredLogs = ViewModel.FilteredLogs;
                    if (filteredLogs.Cast<object>().Any())
                    {
                        var lastItem = filteredLogs.Cast<object>().LastOrDefault();
                        if (lastItem != null)
                        {
                            LogListView.ScrollIntoView(lastItem);
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки прокрутки
            }
        }

        /// <summary>
        /// Прокрутка к началу списка
        /// </summary>
        public void ScrollToTop()
        {
            _scrollViewer?.ScrollToTop();
        }

        #endregion

        #region Обработчики событий

        /// <summary>
        /// Обработчик загрузки контрола
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события</param>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Получаем ссылку на ScrollViewer для управления прокруткой
            _scrollViewer = GetScrollViewer(LogListView);
            
            // Подписываемся на изменения коллекции логов для автопрокрутки
            if (ViewModel?.AllLogs != null)
            {
                ViewModel.AllLogs.CollectionChanged += OnLogsCollectionChanged;
            }
            
            // Дополнительная оптимизация UI
            InitializeOptimizations();
        }

        /// <summary>
        /// Обработчик изменения ViewModel
        /// </summary>
        /// <param name="d">Объект зависимости</param>
        /// <param name="e">Аргументы изменения</param>
        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogViewerControl control)
            {
                // Отписываемся от старой ViewModel
                if (e.OldValue is LogControlViewModel oldViewModel)
                {
                    oldViewModel.AllLogs.CollectionChanged -= control.OnLogsCollectionChanged;
                    oldViewModel.PropertyChanged -= control.OnViewModelPropertyChanged;
                }

                // Подписываемся на новую ViewModel
                if (e.NewValue is LogControlViewModel newViewModel)
                {
                    control.DataContext = newViewModel;
                    newViewModel.AllLogs.CollectionChanged += control.OnLogsCollectionChanged;
                    newViewModel.PropertyChanged += control.OnViewModelPropertyChanged;
                }
            }
        }

        /// <summary>
        /// Обработчик изменения коллекции логов
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы изменения коллекции</param>
        private void OnLogsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Автопрокрутка только при добавлении новых элементов
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add &&
                AutoScrollToEnd && ViewModel?.AutoScroll == true)
            {
                ScheduleBatchUpdate();
            }
        }

        /// <summary>
        /// Обработчик прокрутки ListView
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы прокрутки</param>
        private void ListView_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.OriginalSource is ScrollViewer scrollViewer)
            {
                // Определяем состояние автопрокрутки
                var isAtBottom = Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) < 1.0;
                _autoScrollEnabled = isAtBottom;

                // Используем debouncing для обновления AutoScroll в ViewModel
                _pendingAutoScrollState = _autoScrollEnabled;
                _autoScrollDebounceTimer.Stop();
                _autoScrollDebounceTimer.Start();
                
                // Обрабатываем оптимизированную прокрутку
                HandleOptimizedScroll(scrollViewer);
            }
        }

        /// <summary>
        /// Обработчик нажатия кнопки настроек
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события</param>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Получаем ссылку на Popup из XAML
            var settingsPopup = FindName("SettingsPopup") as System.Windows.Controls.Primitives.Popup;
            if (settingsPopup != null)
            {
                // Устанавливаем DataContext для Popup, чтобы привязки работали
                if (settingsPopup.Child is FrameworkElement popupChild)
                {
                    popupChild.DataContext = this.DataContext;
                }
                
                // Переключаем видимость панели настроек
                settingsPopup.IsOpen = !settingsPopup.IsOpen;
            }
        }

        /// <summary>
        /// Обработчик изменения свойств ViewModel
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы изменения свойства</param>
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogControlViewModel.AutoScroll))
            {
                // Синхронизируем внутреннее состояние автопрокрутки с ViewModel
                _autoScrollEnabled = ViewModel.AutoScroll;
            }
        }

        /// <summary>
        /// Планирование батчевого обновления
        /// </summary>
        private void ScheduleBatchUpdate()
        {
            _hasPendingUpdates = true;
            if (!_batchUpdateTimer.IsEnabled)
            {
                _batchUpdateTimer.Start();
            }
        }

        /// <summary>
        /// Обработчик батчевых обновлений
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события</param>
        private void OnBatchUpdateTimerTick(object sender, EventArgs e)
        {
            _batchUpdateTimer.Stop();
            
            if (_hasPendingUpdates)
            {
                _hasPendingUpdates = false;
                
                // Выполняем накопленные обновления
                try
                {
                    ScrollToEnd();
                }
                catch
                {
                    // Игнорируем ошибки автопрокрутки
                }
            }
        }

        /// <summary>
        /// Обработчик таймера позиции прокрутки для виртуализации
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события</param>
        private void OnScrollPositionTimerTick(object sender, EventArgs e)
        {
            _scrollPositionTimer.Stop();
            // Дополнительные оптимизации прокрутки могут быть добавлены здесь
        }

        /// <summary>
        /// Обработчик таймера debouncing для автопрокрутки
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события</param>
        private void OnAutoScrollDebounceTimerTick(object sender, EventArgs e)
        {
            _autoScrollDebounceTimer.Stop();
            
            // Обновляем AutoScroll в ViewModel только если значение изменилось
            if (ViewModel != null && ViewModel.AutoScroll != _pendingAutoScrollState)
            {
                ViewModel.AutoScroll = _pendingAutoScrollState;
            }
        }

        /// <summary>
        /// Инициализация оптимизаций
        /// </summary>
        private void InitializeOptimizations()
        {
            // Базовые оптимизации производительности
            if (LogListView != null)
            {
                // Убеждаемся что виртуализация WPF включена
                VirtualizingPanel.SetIsVirtualizing(LogListView, true);
                VirtualizingPanel.SetVirtualizationMode(LogListView, VirtualizationMode.Recycling);
            }
        }

        /// <summary>
        /// Обработка оптимизированной прокрутки
        /// </summary>
        /// <param name="scrollViewer">ScrollViewer</param>
        private void HandleOptimizedScroll(ScrollViewer scrollViewer)
        {
            var currentPosition = scrollViewer.VerticalOffset;
            
            // Проверяем, изменилась ли позиция значительно
            if (Math.Abs(currentPosition - _lastScrollPosition) > 10)
            {
                _lastScrollPosition = currentPosition;
                
                // Запускаем таймер с задержкой для дополнительных оптимизаций
                _scrollPositionTimer.Stop();
                _scrollPositionTimer.Start();
            }
        }

        #endregion

        #region Вспомогательные методы

        /// <summary>
        /// Получение ScrollViewer из ListView
        /// </summary>
        /// <param name="listView">ListView для поиска ScrollViewer</param>
        /// <returns>ScrollViewer или null</returns>
        private ScrollViewer GetScrollViewer(ListView listView)
        {
            if (listView == null) return null;

            // Ищем ScrollViewer в визуальном дереве
            try
            {
                Border border = VisualTreeHelper.GetChild(listView, 0) as Border;
                if (border != null)
                {
                    return border.Child as ScrollViewer;
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            
            return null;
        }

        #endregion
    }
}