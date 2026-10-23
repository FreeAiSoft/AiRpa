using System;
using System.ComponentModel;

namespace LogControlLibrary.Configuration
{
    /// <summary>
    /// Настройки виртуализации для оптимизации производительности LogViewerControl
    /// </summary>
    public class VirtualizationSettings : INotifyPropertyChanged
    {
        private int _pageSize = 500;
        private int _viewportSize = 50;
        private int _maxCachedPages = 20;
        private int _preloadPagesAhead = 3;
        private int _preloadPagesBehind = 2;
        private int _filterDebounceMs = 300;
        private int _indexUpdateThreshold = 1000;
        private bool _enableLazyLoading = true;
        private bool _enableIndexing = true;
        private bool _enableCompression = false;
        private bool _enablePerformanceMetrics = false;
        private int _metricsUpdateIntervalMs = 1000;
        private PerformanceProfile _profile = PerformanceProfile.Balanced;

        /// <summary>
        /// Количество записей логов на одну страницу виртуализации
        /// Рекомендуемые значения: 100-1000
        /// </summary>
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (value > 0 && _pageSize != value)
                {
                    _pageSize = value;
                    OnPropertyChanged(nameof(PageSize));
                }
            }
        }

        /// <summary>
        /// Количество записей, видимых одновременно в пользовательском интерфейсе
        /// Должно быть меньше PageSize
        /// </summary>
        public int ViewportSize
        {
            get => _viewportSize;
            set
            {
                if (value > 0 && _viewportSize != value)
                {
                    _viewportSize = value;
                    OnPropertyChanged(nameof(ViewportSize));
                }
            }
        }

        /// <summary>
        /// Максимальное количество страниц, хранящихся в памяти
        /// Рекомендуемые значения: 5-50
        /// </summary>
        public int MaxCachedPages
        {
            get => _maxCachedPages;
            set
            {
                if (value > 0 && _maxCachedPages != value)
                {
                    _maxCachedPages = value;
                    OnPropertyChanged(nameof(MaxCachedPages));
                }
            }
        }

        /// <summary>
        /// Количество страниц для предварительной загрузки вперед
        /// </summary>
        public int PreloadPagesAhead
        {
            get => _preloadPagesAhead;
            set
            {
                if (value >= 0 && _preloadPagesAhead != value)
                {
                    _preloadPagesAhead = value;
                    OnPropertyChanged(nameof(PreloadPagesAhead));
                }
            }
        }

        /// <summary>
        /// Количество страниц для предварительной загрузки назад
        /// </summary>
        public int PreloadPagesBehind
        {
            get => _preloadPagesBehind;
            set
            {
                if (value >= 0 && _preloadPagesBehind != value)
                {
                    _preloadPagesBehind = value;
                    OnPropertyChanged(nameof(PreloadPagesBehind));
                }
            }
        }

        /// <summary>
        /// Задержка в миллисекундах перед применением фильтра (debounce)
        /// </summary>
        public int FilterDebounceMs
        {
            get => _filterDebounceMs;
            set
            {
                if (value >= 0 && _filterDebounceMs != value)
                {
                    _filterDebounceMs = value;
                    OnPropertyChanged(nameof(FilterDebounceMs));
                }
            }
        }

        /// <summary>
        /// Порог количества записей, при котором происходит пересборка индекса
        /// </summary>
        public int IndexUpdateThreshold
        {
            get => _indexUpdateThreshold;
            set
            {
                if (value > 0 && _indexUpdateThreshold != value)
                {
                    _indexUpdateThreshold = value;
                    OnPropertyChanged(nameof(IndexUpdateThreshold));
                }
            }
        }

        /// <summary>
        /// Включить ленивую загрузку данных
        /// </summary>
        public bool EnableLazyLoading
        {
            get => _enableLazyLoading;
            set
            {
                if (_enableLazyLoading != value)
                {
                    _enableLazyLoading = value;
                    OnPropertyChanged(nameof(EnableLazyLoading));
                }
            }
        }

        /// <summary>
        /// Включить индексирование для быстрой фильтрации
        /// </summary>
        public bool EnableIndexing
        {
            get => _enableIndexing;
            set
            {
                if (_enableIndexing != value)
                {
                    _enableIndexing = value;
                    OnPropertyChanged(nameof(EnableIndexing));
                }
            }
        }

        /// <summary>
        /// Включить сжатие данных в кэше
        /// </summary>
        public bool EnableCompression
        {
            get => _enableCompression;
            set
            {
                if (_enableCompression != value)
                {
                    _enableCompression = value;
                    OnPropertyChanged(nameof(EnableCompression));
                }
            }
        }

        /// <summary>
        /// Включить сбор метрик производительности
        /// </summary>
        public bool EnablePerformanceMetrics
        {
            get => _enablePerformanceMetrics;
            set
            {
                if (_enablePerformanceMetrics != value)
                {
                    _enablePerformanceMetrics = value;
                    OnPropertyChanged(nameof(EnablePerformanceMetrics));
                }
            }
        }

        /// <summary>
        /// Интервал обновления метрик производительности в миллисекундах
        /// </summary>
        public int MetricsUpdateIntervalMs
        {
            get => _metricsUpdateIntervalMs;
            set
            {
                if (value > 0 && _metricsUpdateIntervalMs != value)
                {
                    _metricsUpdateIntervalMs = value;
                    OnPropertyChanged(nameof(MetricsUpdateIntervalMs));
                }
            }
        }

        /// <summary>
        /// Профиль производительности
        /// </summary>
        public PerformanceProfile Profile
        {
            get => _profile;
            set
            {
                if (_profile != value)
                {
                    _profile = value;
                    ApplyProfile(value);
                    OnPropertyChanged(nameof(Profile));
                }
            }
        }

        /// <summary>
        /// Общий размер кэша в записях
        /// </summary>
        public int TotalCacheSize => MaxCachedPages * PageSize;

        /// <summary>
        /// Размер окна предзагрузки в записях
        /// </summary>
        public int PreloadWindowSize => (PreloadPagesAhead + PreloadPagesBehind) * PageSize;

        /// <summary>
        /// Событие изменения свойства
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Конструктор с профилем по умолчанию
        /// </summary>
        public VirtualizationSettings() : this(PerformanceProfile.Balanced)
        {
        }

        /// <summary>
        /// Конструктор с указанным профилем
        /// </summary>
        /// <param name="profile">Профиль производительности</param>
        public VirtualizationSettings(PerformanceProfile profile)
        {
            ApplyProfile(profile);
        }

        /// <summary>
        /// Применить настройки профиля производительности
        /// </summary>
        /// <param name="profile">Профиль для применения</param>
        public void ApplyProfile(PerformanceProfile profile)
        {
            switch (profile)
            {
                case PerformanceProfile.LowMemory:
                    PageSize = 100;
                    ViewportSize = 25;
                    MaxCachedPages = 5;
                    PreloadPagesAhead = 1;
                    PreloadPagesBehind = 1;
                    EnableCompression = true;
                    EnableLazyLoading = true;
                    EnableIndexing = false;
                    break;

                case PerformanceProfile.Balanced:
                    PageSize = 500;
                    ViewportSize = 50;
                    MaxCachedPages = 20;
                    PreloadPagesAhead = 3;
                    PreloadPagesBehind = 2;
                    EnableCompression = false;
                    EnableLazyLoading = true;
                    EnableIndexing = true;
                    break;

                case PerformanceProfile.HighPerformance:
                    PageSize = 1000;
                    ViewportSize = 100;
                    MaxCachedPages = 50;
                    PreloadPagesAhead = 5;
                    PreloadPagesBehind = 3;
                    EnableCompression = false;
                    EnableLazyLoading = true;
                    EnableIndexing = true;
                    break;

                case PerformanceProfile.Custom:
                    // Не изменяем настройки для пользовательского профиля
                    break;
            }

            _profile = profile;
        }

        /// <summary>
        /// Валидация настроек
        /// </summary>
        /// <returns>True, если настройки корректны</returns>
        public bool ValidateSettings()
        {
            if (PageSize <= 0 || PageSize > 10000)
                return false;

            if (ViewportSize <= 0 || ViewportSize > PageSize)
                return false;

            if (MaxCachedPages <= 0 || MaxCachedPages > 1000)
                return false;

            if (PreloadPagesAhead < 0 || PreloadPagesAhead > MaxCachedPages / 2)
                return false;

            if (PreloadPagesBehind < 0 || PreloadPagesBehind > MaxCachedPages / 2)
                return false;

            return true;
        }

        /// <summary>
        /// Создать копию настроек
        /// </summary>
        /// <returns>Копия настроек</returns>
        public VirtualizationSettings Clone()
        {
            return new VirtualizationSettings(PerformanceProfile.Custom)
            {
                PageSize = this.PageSize,
                ViewportSize = this.ViewportSize,
                MaxCachedPages = this.MaxCachedPages,
                PreloadPagesAhead = this.PreloadPagesAhead,
                PreloadPagesBehind = this.PreloadPagesBehind,
                FilterDebounceMs = this.FilterDebounceMs,
                IndexUpdateThreshold = this.IndexUpdateThreshold,
                EnableLazyLoading = this.EnableLazyLoading,
                EnableIndexing = this.EnableIndexing,
                EnableCompression = this.EnableCompression,
                EnablePerformanceMetrics = this.EnablePerformanceMetrics,
                MetricsUpdateIntervalMs = this.MetricsUpdateIntervalMs
            };
        }

        /// <summary>
        /// Уведомление об изменении свойства
        /// </summary>
        /// <param name="propertyName">Имя свойства</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}