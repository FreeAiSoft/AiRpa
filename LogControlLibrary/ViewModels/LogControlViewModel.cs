using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using LogControlLibrary.Collections;
using LogControlLibrary.Models;
using LogControlLibrary.Services;

namespace LogControlLibrary.ViewModels
{
    /// <summary>
    /// ViewModel для управления отображением логов
    /// </summary>
    public class LogControlViewModel : BaseViewModel
    {
        #region Поля

        private readonly VirtualizedLogCollection _allLogs;
        private readonly CollectionViewSource _filteredLogsViewSource;
        private readonly LogExportService _exportService;
        private readonly DispatcherTimer _searchDebounceTimer;
        private string _searchText;
        private string _pendingSearchText;
        private bool _isTraceVisible = true;
        private bool _isDebugVisible = true;
        private bool _isInfoVisible = true;
        private bool _isWarningVisible = true;
        private bool _isErrorVisible = true;
        private bool _isFatalVisible = true;
        private int _maxDisplayedEntries = 10000;
        private bool _autoScroll = true;
        private int _totalLogsCount;
        private int _filteredLogsCount;
        private bool _isUpdatingFilters = false;

        #endregion

        #region Свойства

        /// <summary>
        /// Основная коллекция всех логов
        /// </summary>
        public VirtualizedLogCollection AllLogs => _allLogs;

        /// <summary>
        /// Отфильтрованная коллекция логов для отображения
        /// </summary>
        public ICollectionView FilteredLogs => _filteredLogsViewSource.View;

        /// <summary>
        /// Текст для поиска по логам
        /// </summary>
        public string SearchText
        {
            get => _pendingSearchText; // Показываем то, что пользователь вводит
            set
            {
                if (SetProperty(ref _pendingSearchText, value))
                {
                    // Используем debouncing для поиска
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }
            }
        }

        /// <summary>
        /// Отображение логов уровня Trace
        /// </summary>
        public bool IsTraceVisible
        {
            get => _isTraceVisible;
            set => SetProperty(ref _isTraceVisible, value, ApplyFilters);
        }

        /// <summary>
        /// Отображение логов уровня Debug
        /// </summary>
        public bool IsDebugVisible
        {
            get => _isDebugVisible;
            set => SetProperty(ref _isDebugVisible, value, ApplyFilters);
        }

        /// <summary>
        /// Отображение логов уровня Info
        /// </summary>
        public bool IsInfoVisible
        {
            get => _isInfoVisible;
            set => SetProperty(ref _isInfoVisible, value, ApplyFilters);
        }

        /// <summary>
        /// Отображение логов уровня Warning
        /// </summary>
        public bool IsWarningVisible
        {
            get => _isWarningVisible;
            set => SetProperty(ref _isWarningVisible, value, ApplyFilters);
        }

        /// <summary>
        /// Отображение логов уровня Error
        /// </summary>
        public bool IsErrorVisible
        {
            get => _isErrorVisible;
            set => SetProperty(ref _isErrorVisible, value, ApplyFilters);
        }

        /// <summary>
        /// Отображение логов уровня Fatal
        /// </summary>
        public bool IsFatalVisible
        {
            get => _isFatalVisible;
            set => SetProperty(ref _isFatalVisible, value, ApplyFilters);
        }

        /// <summary>
        /// Максимальное количество отображаемых записей
        /// </summary>
        public int MaxDisplayedEntries
        {
            get => _maxDisplayedEntries;
            set
            {
                if (value > 0 && SetProperty(ref _maxDisplayedEntries, value))
                {
                    _allLogs.MaxCapacity = value;
                    ApplyFilters();
                }
            }
        }

        /// <summary>
        /// Автоматическая прокрутка к новым записям
        /// </summary>
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        /// <summary>
        /// Общее количество логов
        /// </summary>
        public int TotalLogsCount
        {
            get => _totalLogsCount;
            private set => SetProperty(ref _totalLogsCount, value);
        }

        /// <summary>
        /// Количество отфильтрованных логов
        /// </summary>
        public int FilteredLogsCount
        {
            get => _filteredLogsCount;
            private set => SetProperty(ref _filteredLogsCount, value);
        }

        #endregion

        #region Команды

        /// <summary>
        /// Команда очистки всех логов
        /// </summary>
        public ICommand ClearLogsCommand { get; }

        /// <summary>
        /// Команда сброса фильтров
        /// </summary>
        public ICommand ResetFiltersCommand { get; }

        /// <summary>
        /// Команда экспорта логов
        /// </summary>
        public ICommand ExportLogsCommand { get; }

        /// <summary>
        /// Команда выбора всех уровней логирования
        /// </summary>
        public ICommand SelectAllLevelsCommand { get; }

        /// <summary>
        /// Команда снятия выбора всех уровней логирования
        /// </summary>
        public ICommand DeselectAllLevelsCommand { get; }

        #endregion

        #region Конструктор

        /// <summary>
        /// Конструктор ViewModel
        /// </summary>
        public LogControlViewModel()
        {
            // Инициализация сервисов
            _exportService = new LogExportService();
            
            // Инициализация таймера для debouncing поиска
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300) // 300ms задержка
            };
            _searchDebounceTimer.Tick += OnSearchDebounceTimerTick;
            
            // Инициализация коллекции логов
            _allLogs = new VirtualizedLogCollection(_maxDisplayedEntries, true);
            _allLogs.CollectionChanged += OnAllLogsCollectionChanged;

            // Инициализация представления для фильтрации
            _filteredLogsViewSource = new CollectionViewSource { Source = _allLogs };
            _filteredLogsViewSource.Filter += OnLogEntryFilter;

            // Инициализация команд
            ClearLogsCommand = new RelayCommand(ClearLogs);
            ResetFiltersCommand = new RelayCommand(ResetFilters);
            ExportLogsCommand = new RelayCommand(() => ExportLogs(), CanExportLogs);
            SelectAllLevelsCommand = new RelayCommand(SelectAllLevels);
            DeselectAllLevelsCommand = new RelayCommand(DeselectAllLevels);

            // Применяем начальные фильтры
            ApplyFilters();
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Добавление одной записи лога
        /// </summary>
        /// <param name="logEntry">Запись лога</param>
        public void AddLog(ILogEntry logEntry)
        {
            if (logEntry != null)
            {
                _allLogs.Add(logEntry);
            }
        }

        /// <summary>
        /// Массовое добавление записей логов
        /// </summary>
        /// <param name="logEntries">Коллекция записей логов</param>
        public void AddLogs(IEnumerable<ILogEntry> logEntries)
        {
            if (logEntries != null)
            {
                // Оптимизируем массовое добавление
                _isUpdatingFilters = true;
                try
                {
                    _allLogs.AddRange(logEntries);
                }
                finally
                {
                    _isUpdatingFilters = false;
                    ApplyFilters();
                }
            }
        }

        /// <summary>
        /// Применение фильтров к логам
        /// </summary>
        public void ApplyFilters()
        {
            if (_filteredLogsViewSource?.View != null && !_isUpdatingFilters)
            {
                _filteredLogsViewSource.View.Refresh();
                UpdateCounters();
            }
        }

        #endregion

        #region Приватные методы

        /// <summary>
        /// Фильтр для записей логов
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы фильтрации</param>
        private void OnLogEntryFilter(object sender, FilterEventArgs e)
        {
            if (e.Item is ILogEntry logEntry)
            {
                // Фильтрация по уровню логирования
                bool levelVisible;
                switch (logEntry.Level)
                {
                    case LogLevel.Trace:
                        levelVisible = _isTraceVisible;
                        break;
                    case LogLevel.Debug:
                        levelVisible = _isDebugVisible;
                        break;
                    case LogLevel.Info:
                        levelVisible = _isInfoVisible;
                        break;
                    case LogLevel.Warning:
                        levelVisible = _isWarningVisible;
                        break;
                    case LogLevel.Error:
                        levelVisible = _isErrorVisible;
                        break;
                    case LogLevel.Fatal:
                        levelVisible = _isFatalVisible;
                        break;
                    default:
                        levelVisible = true;
                        break;
                }

                if (!levelVisible)
                {
                    e.Accepted = false;
                    return;
                }

                // Фильтрация по тексту поиска
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    var searchLower = _searchText.ToLowerInvariant();
                    if (!logEntry.Message.ToLowerInvariant().Contains(searchLower))
                    {
                        e.Accepted = false;
                        return;
                    }
                }

                e.Accepted = true;
            }
            else
            {
                e.Accepted = false;
            }
        }

        /// <summary>
        /// Обработчик изменения коллекции всех логов
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы изменения коллекции</param>
        private void OnAllLogsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Оптимизируем: не обновляем счетчики при массовом добавлении
            if (!_isUpdatingFilters)
            {
                UpdateCounters();
            }
        }

        /// <summary>
        /// Обновление счетчиков логов
        /// </summary>
        private void UpdateCounters()
        {
            TotalLogsCount = _allLogs.Count;
            
            if (_filteredLogsViewSource?.View != null)
            {
                var filteredView = _filteredLogsViewSource.View.Cast<ILogEntry>();
                FilteredLogsCount = filteredView.Count();
            }
        }

        /// <summary>
        /// Очистка всех логов
        /// </summary>
        private void ClearLogs()
        {
            _allLogs.Clear();
        }

        /// <summary>
        /// Сброс всех фильтров
        /// </summary>
        private void ResetFilters()
        {
            // Останавливаем таймер и сбрасываем поиск напрямую
            _searchDebounceTimer.Stop();
            _searchText = string.Empty;
            _pendingSearchText = string.Empty;
            OnPropertyChanged(nameof(SearchText));
            
            IsTraceVisible = true;
            IsDebugVisible = true;
            IsInfoVisible = true;
            IsWarningVisible = true;
            IsErrorVisible = true;
            IsFatalVisible = true;
        }

        /// <summary>
        /// Экспорт логов в файл
        /// </summary>
        private async void ExportLogs()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Экспорт логов",
                    Filter = _exportService.GetFileFilter(),
                    DefaultExt = ".txt",
                    FileName = $"logs_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var filePath = saveFileDialog.FileName;
                    var logsToExport = _filteredLogsViewSource.View.Cast<ILogEntry>().ToList();
                    
                    await _exportService.ExportLogsAsync(logsToExport, filePath);
                    
                    // Можно добавить уведомление о успешном экспорте
                    System.Windows.MessageBox.Show(
                        $"Логи успешно экспортированы в файл:\n{filePath}\n\nЭкспортировано записей: {logsToExport.Count}",
                        "Экспорт завершен",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Ошибка при экспорте логов:\n{ex.Message}",
                    "Ошибка экспорта",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Проверка возможности экспорта логов
        /// </summary>
        /// <returns>True, если экспорт возможен</returns>
        private bool CanExportLogs()
        {
            return _allLogs.Count > 0;
        }

        /// <summary>
        /// Выбор всех уровней логирования
        /// </summary>
        private void SelectAllLevels()
        {
            IsTraceVisible = true;
            IsDebugVisible = true;
            IsInfoVisible = true;
            IsWarningVisible = true;
            IsErrorVisible = true;
            IsFatalVisible = true;
        }

        /// <summary>
        /// Снятие выбора всех уровней логирования
        /// </summary>
        private void DeselectAllLevels()
        {
            IsTraceVisible = false;
            IsDebugVisible = false;
            IsInfoVisible = false;
            IsWarningVisible = false;
            IsErrorVisible = false;
            IsFatalVisible = false;
        }

        /// <summary>
        /// Обработчик таймера debouncing для поиска
        /// </summary>
        /// <param name="sender">Источник события</param>
        /// <param name="e">Аргументы события</param>
        private void OnSearchDebounceTimerTick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            
            // Применяем отложенный поисковый запрос только если текст изменился
            var oldSearchText = _searchText;
            _searchText = _pendingSearchText;
            
            if (oldSearchText != _searchText)
            {
                ApplyFilters();
            }
        }

        #endregion
    }

    /// <summary>
    /// Простая реализация ICommand для команд ViewModel
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object parameter)
        {
            _execute();
        }
    }
}