using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogControlLibrary.Collections;
using LogControlLibrary.Configuration;
using LogControlLibrary.Models;

namespace LogControlLibrary.Virtualization
{
    /// <summary>
    /// Провайдер данных для виртуализированной коллекции логов с кэшированием и предзагрузкой
    /// </summary>
    public class LogDataProvider : IDisposable
    {
        #region Поля

        private readonly VirtualizedLogCollection _sourceCollection;
        private readonly VirtualizationSettings _settings;
        private readonly VirtualizationPerformanceStats _stats;
        private readonly ReaderWriterLockSlim _cacheLock;
        private readonly SemaphoreSlim _loadingSemaphore;
        private readonly CancellationTokenSource _cancellationTokenSource;
        
        // Кэш страниц: ключ - индекс страницы, значение - страница данных
        private readonly ConcurrentDictionary<int, CachedPage> _pageCache;
        
        // Очередь LRU для управления кэшем
        private readonly LinkedList<int> _lruQueue;
        private readonly object _lruLock = new object();
        
        // Фильтрация
        private LogFilterCriteria _currentFilter;
        private readonly ConcurrentDictionary<int, bool> _filterCache;
        
        // Статистика
        private readonly Stopwatch _performanceStopwatch;

        #endregion

        #region Свойства

        /// <summary>
        /// Текущие настройки виртуализации
        /// </summary>
        public VirtualizationSettings Settings => _settings;

        /// <summary>
        /// Статистика производительности
        /// </summary>
        public VirtualizationPerformanceStats PerformanceStats => _stats;

        /// <summary>
        /// Общее количество страниц в коллекции
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)_sourceCollection.Count / _settings.PageSize);

        /// <summary>
        /// Количество страниц в кэше
        /// </summary>
        public int CachedPagesCount => _pageCache.Count;

        /// <summary>
        /// Текущий фильтр
        /// </summary>
        public LogFilterCriteria CurrentFilter => _currentFilter;

        /// <summary>
        /// Индикатор активной загрузки
        /// </summary>
        public bool IsLoading => _loadingSemaphore.CurrentCount < _settings.MaxCachedPages;

        #endregion

        #region События

        /// <summary>
        /// Событие загрузки страницы
        /// </summary>
        public event EventHandler<PageLoadedEventArgs> PageLoaded;

        /// <summary>
        /// Событие ошибки загрузки
        /// </summary>
        public event EventHandler<LoadingErrorEventArgs> LoadingError;

        #endregion

        #region Конструктор

        /// <summary>
        /// Конструктор провайдера данных
        /// </summary>
        /// <param name="sourceCollection">Исходная коллекция логов</param>
        /// <param name="settings">Настройки виртуализации</param>
        public LogDataProvider(VirtualizedLogCollection sourceCollection, VirtualizationSettings settings)
        {
            _sourceCollection = sourceCollection ?? throw new ArgumentNullException(nameof(sourceCollection));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            _stats = new VirtualizationPerformanceStats();
            _cacheLock = new ReaderWriterLockSlim();
            _loadingSemaphore = new SemaphoreSlim(_settings.MaxCachedPages, _settings.MaxCachedPages);
            _cancellationTokenSource = new CancellationTokenSource();
            
            _pageCache = new ConcurrentDictionary<int, CachedPage>();
            _lruQueue = new LinkedList<int>();
            _filterCache = new ConcurrentDictionary<int, bool>();
            
            _performanceStopwatch = Stopwatch.StartNew();
            
            // Подписка на изменения в исходной коллекции
            _sourceCollection.CollectionChanged += OnSourceCollectionChanged;
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Асинхронная загрузка страницы данных
        /// </summary>
        /// <param name="pageIndex">Индекс страницы</param>
        /// <param name="forceReload">Принудительная перезагрузка из источника</param>
        /// <returns>Страница данных</returns>
        public async Task<IList<ILogEntry>> LoadPageAsync(int pageIndex, bool forceReload = false)
        {
            if (pageIndex < 0 || pageIndex >= TotalPages)
                return new List<ILogEntry>();

            var cacheKey = pageIndex;
            
            // Проверяем кэш
            if (!forceReload && _pageCache.TryGetValue(cacheKey, out var cachedPage))
            {
                UpdateLRU(pageIndex);
                _stats.CacheHits++;
                return cachedPage.Data;
            }

            _stats.CacheMisses++;
            
            // Ожидаем доступности слота для загрузки
            await _loadingSemaphore.WaitAsync(_cancellationTokenSource.Token);
            
            try
            {
                return await LoadPageFromSourceAsync(pageIndex);
            }
            finally
            {
                _loadingSemaphore.Release();
            }
        }

        /// <summary>
        /// Предварительная загрузка страниц вокруг указанной позиции
        /// </summary>
        /// <param name="centerPageIndex">Центральная страница</param>
        /// <returns>Task для ожидания завершения</returns>
        public async Task PreloadPagesAsync(int centerPageIndex)
        {
            var tasks = new List<Task>();
            
            // Загружаем страницы назад
            for (int i = Math.Max(0, centerPageIndex - _settings.PreloadPagesBehind); i < centerPageIndex; i++)
            {
                if (!_pageCache.ContainsKey(i))
                {
                    tasks.Add(LoadPageAsync(i));
                }
            }
            
            // Загружаем центральную страницу
            if (!_pageCache.ContainsKey(centerPageIndex))
            {
                tasks.Add(LoadPageAsync(centerPageIndex));
            }
            
            // Загружаем страницы вперед
            for (int i = centerPageIndex + 1; i <= Math.Min(TotalPages - 1, centerPageIndex + _settings.PreloadPagesAhead); i++)
            {
                if (!_pageCache.ContainsKey(i))
                {
                    tasks.Add(LoadPageAsync(i));
                }
            }
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Получение диапазона записей
        /// </summary>
        /// <param name="startIndex">Начальный индекс</param>
        /// <param name="count">Количество записей</param>
        /// <returns>Коллекция записей</returns>
        public async Task<IList<ILogEntry>> GetRangeAsync(int startIndex, int count)
        {
            if (startIndex < 0 || count <= 0)
                return new List<ILogEntry>();

            var result = new List<ILogEntry>();
            var currentIndex = startIndex;
            var remaining = count;

            while (remaining > 0 && currentIndex < _sourceCollection.Count)
            {
                var pageIndex = currentIndex / _settings.PageSize;
                var pageOffset = currentIndex % _settings.PageSize;
                
                var pageData = await LoadPageAsync(pageIndex);
                var takeCount = Math.Min(remaining, pageData.Count - pageOffset);
                
                if (takeCount > 0)
                {
                    result.AddRange(pageData.Skip(pageOffset).Take(takeCount));
                    remaining -= takeCount;
                    currentIndex += takeCount;
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Синхронное получение диапазона (если данные уже в кэше)
        /// </summary>
        /// <param name="startIndex">Начальный индекс</param>
        /// <param name="count">Количество записей</param>
        /// <returns>Коллекция записей или null если не в кэше</returns>
        public IList<ILogEntry> GetRangeSync(int startIndex, int count)
        {
            if (startIndex < 0 || count <= 0)
                return new List<ILogEntry>();

            var result = new List<ILogEntry>();
            var currentIndex = startIndex;
            var remaining = count;

            while (remaining > 0 && currentIndex < _sourceCollection.Count)
            {
                var pageIndex = currentIndex / _settings.PageSize;
                var pageOffset = currentIndex % _settings.PageSize;

                if (!_pageCache.TryGetValue(pageIndex, out var cachedPage))
                    return null; // Данные не в кэше

                var takeCount = Math.Min(remaining, cachedPage.Data.Count - pageOffset);
                
                if (takeCount > 0)
                {
                    result.AddRange(cachedPage.Data.Skip(pageOffset).Take(takeCount));
                    remaining -= takeCount;
                    currentIndex += takeCount;
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Применение фильтра
        /// </summary>
        /// <param name="filter">Критерии фильтрации</param>
        public async Task ApplyFilterAsync(LogFilterCriteria filter)
        {
            _currentFilter = filter;
            _filterCache.Clear();
            
            // Обновляем фильтрацию для загруженных страниц
            await Task.Run(() =>
            {
                foreach (var cachedPage in _pageCache.Values)
                {
                    cachedPage.InvalidateFilter();
                }
            });
        }

        /// <summary>
        /// Очистка кэша страниц
        /// </summary>
        /// <param name="keepRecentPages">Количество последних страниц для сохранения</param>
        public void ClearCache(int keepRecentPages = 0)
        {
            _cacheLock.EnterWriteLock();
            try
            {
                if (keepRecentPages <= 0)
                {
                    _pageCache.Clear();
                    _lruQueue.Clear();
                }
                else
                {
                    var pagesToKeep = _lruQueue.Take(keepRecentPages).ToList();
                    var pagesToRemove = _pageCache.Keys.Except(pagesToKeep).ToList();
                    
                    foreach (var pageIndex in pagesToRemove)
                    {
                        _pageCache.TryRemove(pageIndex, out _);
                        _lruQueue.Remove(pageIndex);
                    }
                }
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Инвалидация кэша при изменении данных
        /// </summary>
        public void InvalidateCache()
        {
            ClearCache();
            _filterCache.Clear();
        }

        /// <summary>
        /// Получение статистики использования памяти
        /// </summary>
        /// <returns>Объем памяти в байтах</returns>
        public long GetMemoryUsage()
        {
            long totalMemory = 0;
            
            foreach (var page in _pageCache.Values)
            {
                totalMemory += page.GetMemorySize();
            }
            
            return totalMemory;
        }

        #endregion

        #region Приватные методы

        /// <summary>
        /// Загрузка страницы из исходной коллекции
        /// </summary>
        /// <param name="pageIndex">Индекс страницы</param>
        /// <returns>Загруженная страница</returns>
        private async Task<IList<ILogEntry>> LoadPageFromSourceAsync(int pageIndex)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var startIndex = pageIndex * _settings.PageSize;
                var pageSize = Math.Min(_settings.PageSize, _sourceCollection.Count - startIndex);
                
                var pageData = await Task.Run(() => 
                {
                    return _sourceCollection.GetRange(startIndex, pageSize);
                });

                // Создаем кэшированную страницу
                var cachedPage = new CachedPage(pageIndex, pageData, DateTime.Now);
                
                // Добавляем в кэш с проверкой лимитов
                AddToCache(pageIndex, cachedPage);
                
                // Обновляем статистику
                stopwatch.Stop();
                _stats.PagesLoaded++;
                _stats.AveragePageLoadTime = (_stats.AveragePageLoadTime * (_stats.PagesLoaded - 1) + stopwatch.ElapsedMilliseconds) / _stats.PagesLoaded;
                _stats.MemoryUsage = GetMemoryUsage();
                
                PageLoaded?.Invoke(this, new PageLoadedEventArgs(pageIndex, pageData.Count));
                
                return pageData;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LoadingError?.Invoke(this, new LoadingErrorEventArgs(ex, $"Ошибка загрузки страницы {pageIndex}", pageIndex));
                return new List<ILogEntry>();
            }
        }

        /// <summary>
        /// Добавление страницы в кэш с учетом лимитов
        /// </summary>
        /// <param name="pageIndex">Индекс страницы</param>
        /// <param name="page">Страница данных</param>
        private void AddToCache(int pageIndex, CachedPage page)
        {
            _cacheLock.EnterWriteLock();
            try
            {
                // Проверяем лимит кэша
                while (_pageCache.Count >= _settings.MaxCachedPages)
                {
                    EvictLeastRecentlyUsedPage();
                }
                
                _pageCache[pageIndex] = page;
                UpdateLRU(pageIndex);
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Удаление наименее используемой страницы из кэша
        /// </summary>
        private void EvictLeastRecentlyUsedPage()
        {
            lock (_lruLock)
            {
                if (_lruQueue.Count > 0)
                {
                    var lruPageIndex = _lruQueue.Last.Value;
                    _lruQueue.RemoveLast();
                    _pageCache.TryRemove(lruPageIndex, out _);
                }
            }
        }

        /// <summary>
        /// Обновление порядка LRU
        /// </summary>
        /// <param name="pageIndex">Индекс страницы</param>
        private void UpdateLRU(int pageIndex)
        {
            lock (_lruLock)
            {
                // Удаляем существующий узел
                var existingNode = _lruQueue.Find(pageIndex);
                if (existingNode != null)
                {
                    _lruQueue.Remove(existingNode);
                }
                
                // Добавляем в начало как самый недавно используемый
                _lruQueue.AddFirst(pageIndex);
            }
        }

        /// <summary>
        /// Обработчик изменений в исходной коллекции
        /// </summary>
        private void OnSourceCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // При изменении исходной коллекции инвалидируем кэш
            InvalidateCache();
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Освобождение ресурсов
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cacheLock?.Dispose();
            _loadingSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();
            
            if (_sourceCollection != null)
            {
                _sourceCollection.CollectionChanged -= OnSourceCollectionChanged;
            }
        }

        #endregion
    }

    /// <summary>
    /// Кэшированная страница данных
    /// </summary>
    internal class CachedPage
    {
        public int PageIndex { get; }
        public IList<ILogEntry> Data { get; }
        public DateTime LoadTime { get; }
        public DateTime LastAccessTime { get; set; }
        
        private IList<ILogEntry> _filteredData;
        private LogFilterCriteria _lastFilter;

        public CachedPage(int pageIndex, IList<ILogEntry> data, DateTime loadTime)
        {
            PageIndex = pageIndex;
            Data = data;
            LoadTime = loadTime;
            LastAccessTime = loadTime;
        }

        /// <summary>
        /// Получение отфильтрованных данных
        /// </summary>
        /// <param name="filter">Критерии фильтрации</param>
        /// <returns>Отфильтрованные данные</returns>
        public IList<ILogEntry> GetFilteredData(LogFilterCriteria filter)
        {
            if (filter == null)
                return Data;

            // Проверяем, нужно ли пересчитывать фильтр
            if (_filteredData == null || !FilterEquals(_lastFilter, filter))
            {
                _filteredData = ApplyFilter(Data, filter);
                _lastFilter = filter?.Clone();
            }

            LastAccessTime = DateTime.Now;
            return _filteredData;
        }

        /// <summary>
        /// Инвалидация кэша фильтра
        /// </summary>
        public void InvalidateFilter()
        {
            _filteredData = null;
            _lastFilter = null;
        }

        /// <summary>
        /// Приблизительный размер страницы в памяти
        /// </summary>
        /// <returns>Размер в байтах</returns>
        public long GetMemorySize()
        {
            // Приблизительная оценка размера объектов LogEntry
            const int estimatedLogEntrySize = 200; // байт на запись
            return Data.Count * estimatedLogEntrySize;
        }

        /// <summary>
        /// Применение фильтра к данным
        /// </summary>
        private IList<ILogEntry> ApplyFilter(IList<ILogEntry> data, LogFilterCriteria filter)
        {
            if (filter == null)
                return data;

            return data.Where(entry => MatchesFilter(entry, filter)).ToList();
        }

        /// <summary>
        /// Проверка соответствия записи фильтру
        /// </summary>
        private bool MatchesFilter(ILogEntry entry, LogFilterCriteria filter)
        {
            // Фильтр по уровню
            if (filter.AllowedLevels != null && filter.AllowedLevels.Length > 0)
            {
                if (!filter.AllowedLevels.Contains(entry.Level))
                    return false;
            }

            // Фильтр по дате
            if (filter.StartDate.HasValue && entry.Timestamp < filter.StartDate.Value)
                return false;
            
            if (filter.EndDate.HasValue && entry.Timestamp > filter.EndDate.Value)
                return false;

            // Текстовый поиск
            if (!string.IsNullOrEmpty(filter.SearchText))
            {
                var searchText = filter.CaseSensitive ? filter.SearchText : filter.SearchText.ToLowerInvariant();
                var messageText = filter.CaseSensitive ? entry.Message : entry.Message.ToLowerInvariant();
                
                if (filter.UseRegex)
                {
                    try
                    {
                        var regex = new System.Text.RegularExpressions.Regex(searchText, 
                            filter.CaseSensitive ? System.Text.RegularExpressions.RegexOptions.None : 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (!regex.IsMatch(messageText))
                            return false;
                    }
                    catch
                    {
                        // Если регекс некорректный, используем обычный поиск
                        if (!messageText.Contains(searchText))
                            return false;
                    }
                }
                else
                {
                    if (!messageText.Contains(searchText))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Сравнение критериев фильтрации
        /// </summary>
        private bool FilterEquals(LogFilterCriteria filter1, LogFilterCriteria filter2)
        {
            if (filter1 == null && filter2 == null) return true;
            if (filter1 == null || filter2 == null) return false;

            return filter1.SearchText == filter2.SearchText &&
                   filter1.CaseSensitive == filter2.CaseSensitive &&
                   filter1.UseRegex == filter2.UseRegex &&
                   filter1.StartDate == filter2.StartDate &&
                   filter1.EndDate == filter2.EndDate &&
                   (filter1.AllowedLevels?.SequenceEqual(filter2.AllowedLevels ?? new LogLevel[0]) ?? 
                    (filter2.AllowedLevels?.Length ?? 0) == 0);
        }
    }

    /// <summary>
    /// Аргументы события загрузки страницы
    /// </summary>
    public class PageLoadedEventArgs : EventArgs
    {
        public int PageIndex { get; }
        public int RecordsCount { get; }

        public PageLoadedEventArgs(int pageIndex, int recordsCount)
        {
            PageIndex = pageIndex;
            RecordsCount = recordsCount;
        }
    }
}