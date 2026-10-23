using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LogControlLibrary.Collections;
using LogControlLibrary.Configuration;
using LogControlLibrary.Models;

namespace LogControlLibrary.Virtualization
{
    /// <summary>
    /// Виртуализированная коллекция логов с инкрементальной загрузкой
    /// </summary>
    public class IncrementalLogCollection : IVirtualizingLogCollection
    {
        #region Поля

        private readonly VirtualizedLogCollection _sourceCollection;
        private readonly LogDataProvider _dataProvider;
        private readonly DispatcherTimer _preloadTimer;
        private readonly SemaphoreSlim _operationSemaphore;
        
        private VirtualizationSettings _settings;
        private LogFilterCriteria _filterCriteria;
        private int _viewPosition;
        private int _totalCount;
        private int _filteredCount;
        private bool _isLoading;
        private bool _isDisposed;

        // Кэш для быстрого доступа к видимым элементам
        private readonly Dictionary<int, ILogEntry> _visibleItemsCache;
        private readonly ReaderWriterLockSlim _visibleCacheLock;

        #endregion

        #region События

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler LoadingStarted;
        public event EventHandler LoadingCompleted;
        public event EventHandler<LoadingErrorEventArgs> LoadingError;
        public event EventHandler<ViewPositionChangedEventArgs> ViewPositionChanged;

        #endregion

        #region Свойства

        /// <summary>
        /// Настройки виртуализации
        /// </summary>
        public VirtualizationSettings Settings
        {
            get => _settings;
            set
            {
                if (_settings != value)
                {
                    _settings = value ?? throw new ArgumentNullException(nameof(value));
                    _dataProvider.Settings.ApplyProfile(_settings.Profile);
                    OnPropertyChanged(nameof(Settings));
                    InvalidateData();
                }
            }
        }

        /// <summary>
        /// Общее количество записей в коллекции
        /// </summary>
        public int TotalCount
        {
            get => _totalCount;
            private set
            {
                if (_totalCount != value)
                {
                    _totalCount = value;
                    OnPropertyChanged(nameof(TotalCount));
                }
            }
        }

        /// <summary>
        /// Количество записей в базовой коллекции (IList.Count)
        /// </summary>
        public int Count => TotalCount;

        /// <summary>
        /// Количество загруженных в память записей
        /// </summary>
        public int LoadedCount => _dataProvider.CachedPagesCount * _settings.PageSize;

        /// <summary>
        /// Количество страниц в кэше
        /// </summary>
        public int CachedPagesCount => _dataProvider.CachedPagesCount;

        /// <summary>
        /// Текущая позиция просмотра
        /// </summary>
        public int ViewPosition
        {
            get => _viewPosition;
            set
            {
                var oldPosition = _viewPosition;
                var newPosition = Math.Max(0, Math.Min(value, Math.Max(0, TotalCount - 1)));
                
                if (_viewPosition != newPosition)
                {
                    _viewPosition = newPosition;
                    OnPropertyChanged(nameof(ViewPosition));
                    
                    var pageIndex = newPosition / _settings.PageSize;
                    ViewPositionChanged?.Invoke(this, new ViewPositionChangedEventArgs(oldPosition, newPosition, pageIndex));
                    
                    // Запускаем предзагрузку с задержкой
                    SchedulePreload();
                }
            }
        }

        /// <summary>
        /// Размер видимого окна
        /// </summary>
        public int ViewportSize => _settings.ViewportSize;

        /// <summary>
        /// Индикатор загрузки
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                    
                    if (value)
                        LoadingStarted?.Invoke(this, EventArgs.Empty);
                    else
                        LoadingCompleted?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Критерии фильтрации
        /// </summary>
        public LogFilterCriteria FilterCriteria
        {
            get => _filterCriteria;
            set
            {
                _filterCriteria = value;
                OnPropertyChanged(nameof(FilterCriteria));
            }
        }

        /// <summary>
        /// Количество записей после фильтрации
        /// </summary>
        public int FilteredCount
        {
            get => _filteredCount;
            private set
            {
                if (_filteredCount != value)
                {
                    _filteredCount = value;
                    OnPropertyChanged(nameof(FilteredCount));
                }
            }
        }

        /// <summary>
        /// Коллекция доступна только для чтения
        /// </summary>
        public bool IsReadOnly => false;

        #endregion

        #region Конструктор

        /// <summary>
        /// Конструктор инкрементальной коллекции
        /// </summary>
        /// <param name="sourceCollection">Исходная коллекция данных</param>
        /// <param name="settings">Настройки виртуализации</param>
        public IncrementalLogCollection(VirtualizedLogCollection sourceCollection, VirtualizationSettings settings = null)
        {
            _sourceCollection = sourceCollection ?? throw new ArgumentNullException(nameof(sourceCollection));
            _settings = settings ?? new VirtualizationSettings();
            
            _dataProvider = new LogDataProvider(_sourceCollection, _settings);
            _operationSemaphore = new SemaphoreSlim(1, 1);
            _visibleItemsCache = new Dictionary<int, ILogEntry>();
            _visibleCacheLock = new ReaderWriterLockSlim();
            
            // Инициализация таймера предзагрузки
            _preloadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _preloadTimer.Tick += OnPreloadTimerTick;
            
            // Подписка на события
            _sourceCollection.CollectionChanged += OnSourceCollectionChanged;
            _dataProvider.PageLoaded += OnPageLoaded;
            _dataProvider.LoadingError += OnDataProviderError;
            
            // Инициализация начальных значений
            UpdateTotalCount();
        }

        #endregion

        #region Публичные методы IList

        /// <summary>
        /// Индексатор для доступа к элементам
        /// </summary>
        public ILogEntry this[int index]
        {
            get
            {
                if (index < 0 || index >= TotalCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                // Сначала проверяем кэш видимых элементов
                _visibleCacheLock.EnterReadLock();
                try
                {
                    if (_visibleItemsCache.TryGetValue(index, out var cachedItem))
                        return cachedItem;
                }
                finally
                {
                    _visibleCacheLock.ExitReadLock();
                }

                // Если не в кэше, пытаемся получить синхронно
                var syncResult = _dataProvider.GetRangeSync(index, 1);
                if (syncResult?.Count > 0)
                {
                    var item = syncResult[0];
                    CacheVisibleItem(index, item);
                    return item;
                }

                // Если данные не загружены, возвращаем placeholder и запускаем асинхронную загрузку
                _ = Task.Run(async () => await LoadItemAsync(index));
                return CreatePlaceholder(index);
            }
            set => throw new NotSupportedException("Изменение элементов не поддерживается");
        }

        /// <summary>
        /// Поиск индекса элемента
        /// </summary>
        public int IndexOf(ILogEntry item)
        {
            if (item == null) return -1;
            
            // Для больших коллекций это может быть медленно, но это стандартный интерфейс
            for (int i = 0; i < TotalCount; i++)
            {
                var current = this[i];
                if (current != null && current.Equals(item))
                    return i;
            }
            
            return -1;
        }

        /// <summary>
        /// Проверка содержания элемента
        /// </summary>
        public bool Contains(ILogEntry item) => IndexOf(item) >= 0;

        /// <summary>
        /// Вставка элемента (не поддерживается)
        /// </summary>
        public void Insert(int index, ILogEntry item) => throw new NotSupportedException();

        /// <summary>
        /// Удаление элемента по индексу (не поддерживается)
        /// </summary>
        public void RemoveAt(int index) => throw new NotSupportedException();

        /// <summary>
        /// Добавление элемента (не поддерживается)
        /// </summary>
        public void Add(ILogEntry item) => throw new NotSupportedException();

        /// <summary>
        /// Удаление элемента (не поддерживается)
        /// </summary>
        public bool Remove(ILogEntry item) => throw new NotSupportedException();

        /// <summary>
        /// Очистка коллекции
        /// </summary>
        public void Clear()
        {
            _ = Task.Run(async () => await ClearAsync());
        }

        /// <summary>
        /// Копирование в массив
        /// </summary>
        public void CopyTo(ILogEntry[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < TotalCount) throw new ArgumentException("Недостаточно места в массиве");

            for (int i = 0; i < TotalCount; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        /// <summary>
        /// Получение перечислителя
        /// </summary>
        public IEnumerator<ILogEntry> GetEnumerator()
        {
            for (int i = 0; i < TotalCount; i++)
            {
                yield return this[i];
            }
        }

        /// <summary>
        /// Получение нетипизированного перечислителя
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion

        #region Методы управления данными

        /// <summary>
        /// Асинхронное добавление записи
        /// </summary>
        public async Task AddLogAsync(ILogEntry logEntry)
        {
            if (logEntry == null) return;

            await _operationSemaphore.WaitAsync();
            try
            {
                _sourceCollection.Add(logEntry);
                InvalidateData();
                
                // Уведомляем об изменении
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, logEntry, TotalCount - 1));
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// Асинхронное массовое добавление записей
        /// </summary>
        public async Task AddLogsAsync(IEnumerable<ILogEntry> logEntries)
        {
            if (logEntries == null) return;

            var entries = logEntries.ToList();
            if (entries.Count == 0) return;

            await _operationSemaphore.WaitAsync();
            try
            {
                _sourceCollection.AddRange(entries);
                InvalidateData();
                
                // Уведомляем об изменении
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// Асинхронная очистка
        /// </summary>
        public async Task ClearAsync()
        {
            await _operationSemaphore.WaitAsync();
            try
            {
                _sourceCollection.Clear();
                InvalidateData();
                
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        #endregion

        #region Методы виртуализации

        /// <summary>
        /// Загрузка страницы данных
        /// </summary>
        public async Task<IList<ILogEntry>> LoadPageAsync(int pageIndex)
        {
            IsLoading = true;
            try
            {
                return await _dataProvider.LoadPageAsync(pageIndex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Предзагрузка страниц
        /// </summary>
        public async Task PreloadPagesAsync(int centerPosition)
        {
            var pageIndex = centerPosition / _settings.PageSize;
            await _dataProvider.PreloadPagesAsync(pageIndex);
        }

        /// <summary>
        /// Освобождение кэшированных страниц
        /// </summary>
        public void ReleaseCachedPages(int keepPages)
        {
            _dataProvider.ClearCache(keepPages);
            ClearVisibleCache();
        }

        /// <summary>
        /// Получение диапазона записей
        /// </summary>
        public async Task<IList<ILogEntry>> GetRangeAsync(int startIndex, int count)
        {
            return await _dataProvider.GetRangeAsync(startIndex, count);
        }

        /// <summary>
        /// Синхронное получение диапазона
        /// </summary>
        public IList<ILogEntry> GetRangeSync(int startIndex, int count)
        {
            return _dataProvider.GetRangeSync(startIndex, count);
        }

        #endregion

        #region Методы фильтрации

        /// <summary>
        /// Применение фильтра
        /// </summary>
        public async Task ApplyFilterAsync(LogFilterCriteria criteria)
        {
            FilterCriteria = criteria;
            await _dataProvider.ApplyFilterAsync(criteria);
            InvalidateData();
            
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Сброс фильтра
        /// </summary>
        public async Task ClearFilterAsync()
        {
            await ApplyFilterAsync(null);
        }

        /// <summary>
        /// Поиск записей
        /// </summary>
        public async Task<IList<ILogEntry>> SearchAsync(LogSearchCriteria searchCriteria)
        {
            // Реализация поиска через провайдер данных
            var allResults = new List<ILogEntry>();
            var currentPosition = searchCriteria.StartPosition;
            var found = 0;

            while (found < searchCriteria.MaxResults && currentPosition < TotalCount)
            {
                var batchSize = Math.Min(1000, searchCriteria.MaxResults - found);
                var batch = await GetRangeAsync(currentPosition, batchSize);
                
                foreach (var entry in batch)
                {
                    if (MatchesSearchCriteria(entry, searchCriteria))
                    {
                        allResults.Add(entry);
                        found++;
                        
                        if (found >= searchCriteria.MaxResults)
                            break;
                    }
                }
                
                currentPosition += batchSize;
            }

            return allResults;
        }

        /// <summary>
        /// Получение количества отфильтрованных записей
        /// </summary>
        public async Task<int> GetFilteredCountAsync(LogFilterCriteria criteria)
        {
            // Для больших коллекций это может быть дорого, рассматриваем кэширование
            int count = 0;
            int batchSize = 1000;
            
            for (int i = 0; i < TotalCount; i += batchSize)
            {
                var batch = await GetRangeAsync(i, Math.Min(batchSize, TotalCount - i));
                count += batch.Count(entry => MatchesFilterCriteria(entry, criteria));
            }
            
            return count;
        }

        #endregion

        #region Методы навигации

        /// <summary>
        /// Переход к позиции
        /// </summary>
        public async Task NavigateToPositionAsync(int position)
        {
            ViewPosition = position;
            await PreloadPagesAsync(position);
        }

        /// <summary>
        /// Переход к началу
        /// </summary>
        public async Task NavigateToFirstAsync()
        {
            await NavigateToPositionAsync(0);
        }

        /// <summary>
        /// Переход к концу
        /// </summary>
        public async Task NavigateToLastAsync()
        {
            await NavigateToPositionAsync(Math.Max(0, TotalCount - 1));
        }

        /// <summary>
        /// Поиск и переход к записи по ID
        /// </summary>
        public async Task<int> FindAndNavigateToLogAsync(long logId)
        {
            // Простая реализация линейного поиска
            // В будущем можно добавить индексацию по ID
            for (int i = 0; i < TotalCount; i += 1000)
            {
                var batch = await GetRangeAsync(i, Math.Min(1000, TotalCount - i));
                for (int j = 0; j < batch.Count; j++)
                {
                    if (batch[j].Id == logId)
                    {
                        var position = i + j;
                        await NavigateToPositionAsync(position);
                        return position;
                    }
                }
            }
            
            return -1;
        }

        #endregion

        #region Методы мониторинга

        /// <summary>
        /// Получение статистики производительности
        /// </summary>
        public VirtualizationPerformanceStats GetPerformanceStats()
        {
            return _dataProvider.PerformanceStats;
        }

        /// <summary>
        /// Сброс статистики
        /// </summary>
        public void ResetPerformanceStats()
        {
            _dataProvider.PerformanceStats.Reset();
        }

        #endregion

        #region Приватные методы

        /// <summary>
        /// Асинхронная загрузка элемента
        /// </summary>
        private async Task LoadItemAsync(int index)
        {
            try
            {
                var items = await _dataProvider.GetRangeAsync(index, 1);
                if (items.Count > 0)
                {
                    CacheVisibleItem(index, items[0]);
                    
                    // Уведомляем UI об изменении элемента
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Replace, items[0], CreatePlaceholder(index), index));
                }
            }
            catch (Exception ex)
            {
                LoadingError?.Invoke(this, new LoadingErrorEventArgs(ex, $"Ошибка загрузки элемента {index}"));
            }
        }

        /// <summary>
        /// Создание placeholder для незагруженного элемента
        /// </summary>
        private ILogEntry CreatePlaceholder(int index)
        {
            return new LogEntry(LogLevel.Info, $"Загрузка записи {index}...", DateTime.Now)
            {
                Id = -index - 1 // Отрицательный ID для placeholder
            };
        }

        /// <summary>
        /// Кэширование видимого элемента
        /// </summary>
        private void CacheVisibleItem(int index, ILogEntry item)
        {
            _visibleCacheLock.EnterWriteLock();
            try
            {
                _visibleItemsCache[index] = item;
                
                // Ограничиваем размер кэша видимых элементов
                if (_visibleItemsCache.Count > _settings.ViewportSize * 3)
                {
                    var keysToRemove = _visibleItemsCache.Keys
                        .OrderBy(k => Math.Abs(k - ViewPosition))
                        .Skip(_settings.ViewportSize * 2)
                        .ToList();
                    
                    foreach (var key in keysToRemove)
                    {
                        _visibleItemsCache.Remove(key);
                    }
                }
            }
            finally
            {
                _visibleCacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Очистка кэша видимых элементов
        /// </summary>
        private void ClearVisibleCache()
        {
            _visibleCacheLock.EnterWriteLock();
            try
            {
                _visibleItemsCache.Clear();
            }
            finally
            {
                _visibleCacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Планирование предзагрузки
        /// </summary>
        private void SchedulePreload()
        {
            _preloadTimer.Stop();
            _preloadTimer.Start();
        }

        /// <summary>
        /// Обработчик таймера предзагрузки
        /// </summary>
        private async void OnPreloadTimerTick(object sender, EventArgs e)
        {
            _preloadTimer.Stop();
            
            if (_settings.EnableLazyLoading)
            {
                await PreloadPagesAsync(ViewPosition);
            }
        }

        /// <summary>
        /// Обновление общего количества записей
        /// </summary>
        private void UpdateTotalCount()
        {
            TotalCount = _sourceCollection.Count;
            FilteredCount = TotalCount; // Пока без фильтрации
        }

        /// <summary>
        /// Инвалидация данных
        /// </summary>
        private void InvalidateData()
        {
            UpdateTotalCount();
            ClearVisibleCache();
            _dataProvider.InvalidateCache();
        }

        /// <summary>
        /// Проверка соответствия записи критериям поиска
        /// </summary>
        private bool MatchesSearchCriteria(ILogEntry entry, LogSearchCriteria criteria)
        {
            return MatchesFilterCriteria(entry, criteria);
        }

        /// <summary>
        /// Проверка соответствия записи критериям фильтрации
        /// </summary>
        private bool MatchesFilterCriteria(ILogEntry entry, LogFilterCriteria criteria)
        {
            if (criteria == null) return true;

            // Фильтр по уровню
            if (criteria.AllowedLevels?.Length > 0 && !criteria.AllowedLevels.Contains(entry.Level))
                return false;

            // Фильтр по дате
            if (criteria.StartDate.HasValue && entry.Timestamp < criteria.StartDate.Value)
                return false;
            
            if (criteria.EndDate.HasValue && entry.Timestamp > criteria.EndDate.Value)
                return false;

            // Текстовый поиск
            if (!string.IsNullOrEmpty(criteria.SearchText))
            {
                var searchText = criteria.CaseSensitive ? criteria.SearchText : criteria.SearchText.ToLowerInvariant();
                var messageText = criteria.CaseSensitive ? entry.Message : entry.Message.ToLowerInvariant();
                
                if (!messageText.Contains(searchText))
                    return false;
            }

            return true;
        }

        #endregion

        #region Обработчики событий

        /// <summary>
        /// Обработчик изменения исходной коллекции
        /// </summary>
        private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateTotalCount();
            
            // Передаем событие дальше, но адаптируем его для виртуализированной коллекции
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Обработчик загрузки страницы
        /// </summary>
        private void OnPageLoaded(object sender, PageLoadedEventArgs e)
        {
            // Можно обновить кэш видимых элементов или уведомить UI
        }

        /// <summary>
        /// Обработчик ошибки провайдера данных
        /// </summary>
        private void OnDataProviderError(object sender, LoadingErrorEventArgs e)
        {
            LoadingError?.Invoke(this, e);
        }

        /// <summary>
        /// Уведомление об изменении коллекции
        /// </summary>
        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Уведомление об изменении свойства
        /// </summary>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Освобождение ресурсов
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            
            _preloadTimer?.Stop();
            _sourceCollection.CollectionChanged -= OnSourceCollectionChanged;
            _dataProvider?.Dispose();
            _operationSemaphore?.Dispose();
            _visibleCacheLock?.Dispose();
            
            _isDisposed = true;
        }

        #endregion
    }
}