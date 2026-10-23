using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using LogControlLibrary.Configuration;
using LogControlLibrary.Models;

namespace LogControlLibrary.Virtualization
{
    /// <summary>
    /// Интерфейс для виртуализирующей коллекции логов с поддержкой инкрементальной загрузки
    /// </summary>
    public interface IVirtualizingLogCollection : IList<ILogEntry>, INotifyCollectionChanged, INotifyPropertyChanged, IDisposable
    {
        #region Свойства

        /// <summary>
        /// Настройки виртуализации
        /// </summary>
        VirtualizationSettings Settings { get; set; }

        /// <summary>
        /// Общее количество записей в коллекции (включая незагруженные)
        /// </summary>
        int TotalCount { get; }

        /// <summary>
        /// Количество загруженных в память записей
        /// </summary>
        int LoadedCount { get; }

        /// <summary>
        /// Количество страниц в кэше
        /// </summary>
        int CachedPagesCount { get; }

        /// <summary>
        /// Текущая позиция просмотра (для отслеживания прокрутки)
        /// </summary>
        int ViewPosition { get; set; }

        /// <summary>
        /// Размер видимого окна (количество элементов, отображаемых одновременно)
        /// </summary>
        int ViewportSize { get; }

        /// <summary>
        /// Индикатор выполнения операции загрузки
        /// </summary>
        bool IsLoading { get; }

        /// <summary>
        /// Активные критерии фильтрации
        /// </summary>
        LogFilterCriteria FilterCriteria { get; set; }

        /// <summary>
        /// Количество записей, соответствующих текущему фильтру
        /// </summary>
        int FilteredCount { get; }

        #endregion

        #region События

        /// <summary>
        /// Событие начала загрузки данных
        /// </summary>
        event EventHandler LoadingStarted;

        /// <summary>
        /// Событие завершения загрузки данных
        /// </summary>
        event EventHandler LoadingCompleted;

        /// <summary>
        /// Событие ошибки загрузки
        /// </summary>
        event EventHandler<LoadingErrorEventArgs> LoadingError;

        /// <summary>
        /// Событие изменения позиции просмотра
        /// </summary>
        event EventHandler<ViewPositionChangedEventArgs> ViewPositionChanged;

        #endregion

        #region Методы управления данными

        /// <summary>
        /// Асинхронное добавление записи лога
        /// </summary>
        /// <param name="logEntry">Запись лога</param>
        /// <returns>Task для ожидания завершения операции</returns>
        Task AddLogAsync(ILogEntry logEntry);

        /// <summary>
        /// Асинхронное массовое добавление записей логов
        /// </summary>
        /// <param name="logEntries">Коллекция записей логов</param>
        /// <returns>Task для ожидания завершения операции</returns>
        Task AddLogsAsync(IEnumerable<ILogEntry> logEntries);

        /// <summary>
        /// Асинхронная очистка всех данных
        /// </summary>
        /// <returns>Task для ожидания завершения операции</returns>
        Task ClearAsync();

        #endregion

        #region Методы виртуализации

        /// <summary>
        /// Загружает страницу данных по индексу
        /// </summary>
        /// <param name="pageIndex">Индекс страницы</param>
        /// <returns>Task с коллекцией записей страницы</returns>
        Task<IList<ILogEntry>> LoadPageAsync(int pageIndex);

        /// <summary>
        /// Предварительная загрузка страниц вокруг указанной позиции
        /// </summary>
        /// <param name="centerPosition">Центральная позиция</param>
        /// <returns>Task для ожидания завершения операции</returns>
        Task PreloadPagesAsync(int centerPosition);

        /// <summary>
        /// Освобождает страницы из кэша для экономии памяти
        /// </summary>
        /// <param name="keepPages">Количество страниц для сохранения</param>
        void ReleaseCachedPages(int keepPages);

        /// <summary>
        /// Получение диапазона записей для отображения
        /// </summary>
        /// <param name="startIndex">Начальный индекс</param>
        /// <param name="count">Количество записей</param>
        /// <returns>Task с коллекцией записей</returns>
        Task<IList<ILogEntry>> GetRangeAsync(int startIndex, int count);

        /// <summary>
        /// Синхронное получение диапазона записей (если они уже загружены)
        /// </summary>
        /// <param name="startIndex">Начальный индекс</param>
        /// <param name="count">Количество записей</param>
        /// <returns>Коллекция записей или null если не загружены</returns>
        IList<ILogEntry> GetRangeSync(int startIndex, int count);

        #endregion

        #region Методы фильтрации и поиска

        /// <summary>
        /// Применение фильтра к коллекции
        /// </summary>
        /// <param name="criteria">Критерии фильтрации</param>
        /// <returns>Task для ожидания завершения операции</returns>
        Task ApplyFilterAsync(LogFilterCriteria criteria);

        /// <summary>
        /// Сброс всех фильтров
        /// </summary>
        /// <returns>Task для ожидания завершения операции</returns>
        Task ClearFilterAsync();

        /// <summary>
        /// Поиск записей по критериям
        /// </summary>
        /// <param name="searchCriteria">Критерии поиска</param>
        /// <returns>Task с результатами поиска</returns>
        Task<IList<ILogEntry>> SearchAsync(LogSearchCriteria searchCriteria);

        /// <summary>
        /// Получение количества записей, соответствующих фильтру
        /// </summary>
        /// <param name="criteria">Критерии фильтрации</param>
        /// <returns>Task с количеством записей</returns>
        Task<int> GetFilteredCountAsync(LogFilterCriteria criteria);

        #endregion

        #region Методы навигации

        /// <summary>
        /// Переход к указанной позиции
        /// </summary>
        /// <param name="position">Позиция для перехода</param>
        /// <returns>Task для ожидания завершения операции</returns>
        Task NavigateToPositionAsync(int position);

        /// <summary>
        /// Переход к первой записи
        /// </summary>
        /// <returns>Task для ожидания завершения операции</returns>
        Task NavigateToFirstAsync();

        /// <summary>
        /// Переход к последней записи
        /// </summary>
        /// <returns>Task для ожидания завершения операции</returns>
        Task NavigateToLastAsync();

        /// <summary>
        /// Поиск и переход к записи по ID
        /// </summary>
        /// <param name="logId">Идентификатор записи</param>
        /// <returns>Task с позицией записи или -1 если не найдена</returns>
        Task<int> FindAndNavigateToLogAsync(long logId);

        #endregion

        #region Методы мониторинга

        /// <summary>
        /// Получение статистики производительности
        /// </summary>
        /// <returns>Объект со статистикой</returns>
        VirtualizationPerformanceStats GetPerformanceStats();

        /// <summary>
        /// Сброс статистики производительности
        /// </summary>
        void ResetPerformanceStats();

        #endregion
    }

    /// <summary>
    /// Критерии фильтрации логов
    /// </summary>
    public class LogFilterCriteria
    {
        /// <summary>
        /// Фильтр по уровням логирования
        /// </summary>
        public LogLevel[] AllowedLevels { get; set; }

        /// <summary>
        /// Текст для поиска в сообщениях
        /// </summary>
        public string SearchText { get; set; }

        /// <summary>
        /// Начальная дата фильтрации
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// Конечная дата фильтрации
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Регистрозависимый поиск
        /// </summary>
        public bool CaseSensitive { get; set; }

        /// <summary>
        /// Поиск по регулярному выражению
        /// </summary>
        public bool UseRegex { get; set; }

        /// <summary>
        /// Создание копии критериев
        /// </summary>
        public LogFilterCriteria Clone()
        {
            return new LogFilterCriteria
            {
                AllowedLevels = this.AllowedLevels?.Clone() as LogLevel[],
                SearchText = this.SearchText,
                StartDate = this.StartDate,
                EndDate = this.EndDate,
                CaseSensitive = this.CaseSensitive,
                UseRegex = this.UseRegex
            };
        }
    }

    /// <summary>
    /// Критерии поиска логов
    /// </summary>
    public class LogSearchCriteria : LogFilterCriteria
    {
        /// <summary>
        /// Максимальное количество результатов
        /// </summary>
        public int MaxResults { get; set; } = 1000;

        /// <summary>
        /// Начальная позиция для поиска
        /// </summary>
        public int StartPosition { get; set; } = 0;

        /// <summary>
        /// Направление поиска
        /// </summary>
        public SearchDirection Direction { get; set; } = SearchDirection.Forward;
    }

    /// <summary>
    /// Направление поиска
    /// </summary>
    public enum SearchDirection
    {
        Forward,
        Backward
    }

    /// <summary>
    /// Аргументы события ошибки загрузки
    /// </summary>
    public class LoadingErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string ErrorMessage { get; }
        public int PageIndex { get; }

        public LoadingErrorEventArgs(Exception exception, string errorMessage, int pageIndex = -1)
        {
            Exception = exception;
            ErrorMessage = errorMessage;
            PageIndex = pageIndex;
        }
    }

    /// <summary>
    /// Аргументы события изменения позиции просмотра
    /// </summary>
    public class ViewPositionChangedEventArgs : EventArgs
    {
        public int OldPosition { get; }
        public int NewPosition { get; }
        public int PageIndex { get; }

        public ViewPositionChangedEventArgs(int oldPosition, int newPosition, int pageIndex)
        {
            OldPosition = oldPosition;
            NewPosition = newPosition;
            PageIndex = pageIndex;
        }
    }

    /// <summary>
    /// Статистика производительности виртуализации
    /// </summary>
    public class VirtualizationPerformanceStats
    {
        /// <summary>
        /// Количество загруженных страниц
        /// </summary>
        public int PagesLoaded { get; set; }

        /// <summary>
        /// Количество попаданий в кэш
        /// </summary>
        public int CacheHits { get; set; }

        /// <summary>
        /// Количество промахов кэша
        /// </summary>
        public int CacheMisses { get; set; }

        /// <summary>
        /// Среднее время загрузки страницы (мс)
        /// </summary>
        public double AveragePageLoadTime { get; set; }

        /// <summary>
        /// Объем используемой памяти (байт)
        /// </summary>
        public long MemoryUsage { get; set; }

        /// <summary>
        /// Коэффициент попаданий в кэш
        /// </summary>
        public double CacheHitRatio => CacheHits + CacheMisses > 0 ? (double)CacheHits / (CacheHits + CacheMisses) : 0;

        /// <summary>
        /// Время начала сбора статистики
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Сброс статистики
        /// </summary>
        public void Reset()
        {
            PagesLoaded = 0;
            CacheHits = 0;
            CacheMisses = 0;
            AveragePageLoadTime = 0;
            MemoryUsage = 0;
            StartTime = DateTime.Now;
        }
    }
}