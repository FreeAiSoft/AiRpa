using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RPA.NuGet.Commands;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace RPA.NuGet.ViewModels
{
    /// <summary>
    /// Модель представления докируемой панели «NuGet» в духе менеджера пакетов Visual Studio 2026:
    /// вкладки Browse/Installed/Updates, поиск с отменой, выбор источника, панель деталей с выбором
    /// версии и действием (Install/Update/Downgrade/Uninstall), пакетное обновление.
    /// <para>
    /// Вся работа с сетью — асинхронная и отменяемая: поиск и загрузка деталей держат собственный
    /// <see cref="CancellationTokenSource"/>, поэтому быстрый набор в поле поиска не копит запросы,
    /// а переключение пакета не оставляет «догоняющий» ответ в панели деталей.
    /// </para>
    /// </summary>
    public class NuGetPanelViewModel : ObservableObject
    {
        private const int PageSize = 20;

        private readonly NuGetIconService _icons;
        private readonly NuGetSourcesService _sourcesService;
        private readonly NuGetPackageDetailsService _detailsService;
        private readonly NuGetInstallService _installService;
        private readonly bool _fixedSource;

        private NuGetSearchService _search;
        private CancellationTokenSource _searchCts;
        private CancellationTokenSource _detailsCts;
        private NuGetProjectPackages _project;
        private int _projectGeneration;

        private NuGetPackageTab _activeTab = NuGetPackageTab.Browse;
        private string _searchQuery;
        private bool _includePrerelease;
        private string _projectPath;
        private string _statusText;
        private bool _isBusy;
        private bool _isCompact;
        private bool _updatesChecked;
        private bool _canLoadMore;
        private NuGetPackageItemViewModel _selectedItem;
        private NuGetSourceDefinition _selectedSource;
        private string _sourceMappingNotice;
        private bool _autoRestoreAfterInstall = true;
        private double _progressValue;
        private CancellationTokenSource _debounceCts;
        private Task _pendingSearch = Task.CompletedTask;
        private bool _isSourcesOverlayOpen;
        private bool _isEditingSource;
        private bool _isNewSource;
        private string _editingSourceName;
        private string _editingSourceUrl;
        private string _sourceEditError;
        private NuGetSourceItemViewModel _selectedSourceItem;
        private NuGetSourceDefinition _selectedExternalSource;
        private bool _isPanelVisible;

        public NuGetPanelViewModel(
            NuGetSearchService search = null,
            NuGetPackageDownloader downloader = null,
            NuGetIconService icons = null,
            NuGetSourcesService sources = null,
            NuGetPackageDetailsService details = null,
            NuGetInstallService install = null)
        {
            _search = search;
            // Явно переданный сервис — фиксированный источник (тесты, будущие сценарии):
            // список «Package source» в этом случае его не подменяет.
            _fixedSource = search != null;
            _icons = icons ?? new NuGetIconService();
            _sourcesService = sources ?? new NuGetSourcesService();
            _detailsService = details ?? new NuGetPackageDetailsService();
            _installService = install ?? new NuGetInstallService(downloader);

            Details = new NuGetPackageDetailsViewModel();
            // Кнопка действия зависит от выбранной версии — она меняется внутри панели деталей.
            Details.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(NuGetPackageDetailsViewModel.Action))
                    ActionCommand.RaiseCanExecuteChanged();
            };

            SearchCommand = new AsyncRelayCommand(_ => SearchAsync());
            LoadMoreCommand = new AsyncRelayCommand(_ => LoadMoreAsync(), _ => CanLoadMore && !IsBusy);
            ClearSearchCommand = new RelayCommand(_ => ClearSearch());
            SelectTabCommand = new RelayCommand(parameter => SelectTab(ParseTab(parameter)));
            ActionCommand = new AsyncRelayCommand(_ => ExecuteActionAsync(), _ => CanExecuteAction);
            InstallItemCommand = new AsyncRelayCommand(InstallItemAsync, _ => HasProject && !IsBusy);
            UninstallItemCommand = new AsyncRelayCommand(UninstallItemAsync, _ => HasProject && !IsBusy);
            UpdateAllCommand = new AsyncRelayCommand(_ => UpdateAllAsync(), _ => UpdateItems.Count > 0 && !IsBusy);
            CancelCommand = new RelayCommand(_ => Cancel());
            OpenLinkCommand = new RelayCommand(parameter => OpenLink(parameter as string));
            ConfigureSourceMappingCommand = new RelayCommand(_ => OpenSources());
            OpenSourcesCommand = new RelayCommand(_ => OpenSources(), _ => !_isSourcesOverlayOpen);
            CloseSourcesCommand = new RelayCommand(_ => CloseSources());
            AddSourceCommand = new RelayCommand(_ => BeginAddSource(), _ => !_isEditingSource);
            EditSourceCommand = new RelayCommand(_ => BeginEditSource(), _ => SelectedSourceItem != null && !_isEditingSource);
            RemoveSourceCommand = new RelayCommand(_ => RemoveSource(), _ => SelectedSourceItem != null && !_isEditingSource);
            SaveSourceCommand = new RelayCommand(_ => SaveSource());
            CancelSourceEditCommand = new RelayCommand(_ => CancelSourceEdit());
            OpenConfigFileCommand = new RelayCommand(_ => OpenNuGetConfig());
            ImportSourceCommand = new RelayCommand(_ => ImportSelectedExternalSource(), _ => SelectedExternalSource != null);
            ExportProjectSourcesToConfigCommand = new RelayCommand(_ => ExportProjectSourcesToConfig());
            CreateNuGetConfigCommand = new RelayCommand(_ => CreateNuGetConfig());
            RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => HasProject && !IsBusy);
        }

        /// <summary>Просьба открыть внешнюю ссылку (её выполняет представление: <c>Process.Start</c>).</summary>
        public event EventHandler<string> LinkRequested;

        /// <summary>
        /// Проект изменился на диске (установка/удаление пакета и, если включено, restore завершён).
        /// Параметр — полный путь к <c>.csproj</c>. Студия подписывается и перечитывает References
        /// и активности пакетов в панели «Активности» без перезапуска.
        /// </summary>
        public event EventHandler<string> ProjectChanged;

        // --- Коллекции ---
        /// <summary>Читает сохранённый для текущего воркспейса URL источника (подключается хостом-студией).</summary>
        public Func<string> LoadSelectedSourceUrl { get; set; }

        /// <summary>Сохраняет URL выбранного источника для текущего воркспейса (подключается хостом-студией).</summary>
        public Action<string> SaveSelectedSourceUrl { get; set; }

        /// <summary>Читает per-project список источников (подключается хостом-студией).</summary>
        public Func<IReadOnlyList<NuGetSourceDefinition>> LoadProjectSources { get; set; }

        /// <summary>Сохраняет per-project список источников (подключается хостом-студией).</summary>
        public Action<IReadOnlyList<NuGetSourceDefinition>> SaveProjectSources { get; set; }



        public ObservableCollection<NuGetPackageItemViewModel> BrowseItems { get; } =
            new ObservableCollection<NuGetPackageItemViewModel>();

        public ObservableCollection<NuGetPackageItemViewModel> InstalledItems { get; } =
            new ObservableCollection<NuGetPackageItemViewModel>();

        public ObservableCollection<NuGetPackageItemViewModel> UpdateItems { get; } =
            new ObservableCollection<NuGetPackageItemViewModel>();

        public ObservableCollection<NuGetSourceDefinition> Sources { get; } =
            new ObservableCollection<NuGetSourceDefinition>();

        /// <summary>Строки оверлея «Package sources» (редактируемая копия конфигурации).</summary>
        public ObservableCollection<NuGetSourceItemViewModel> SourceItems { get; } =
            new ObservableCollection<NuGetSourceItemViewModel>();

        /// <summary>Внешние источники из NuGet.Config (машина/решение) — только чтение, можно импортировать.</summary>
        public ObservableCollection<NuGetSourceDefinition> ExternalSources { get; } =
            new ObservableCollection<NuGetSourceDefinition>();

        /// <summary>Правая панель деталей.</summary>
        public NuGetPackageDetailsViewModel Details { get; }

        /// <summary>Пакеты активной вкладки — то, что показывает список.</summary>
        public IReadOnlyList<NuGetPackageItemViewModel> VisibleItems
        {
            get
            {
                switch (_activeTab)
                {
                    case NuGetPackageTab.Installed:
                        return InstalledItems;
                    case NuGetPackageTab.Updates:
                        return UpdateItems;
                    default:
                        return BrowseItems;
                }
            }
        }

        // --- Команды ---

        public AsyncRelayCommand SearchCommand { get; }

        public AsyncRelayCommand LoadMoreCommand { get; }

        public RelayCommand ClearSearchCommand { get; }

        public RelayCommand SelectTabCommand { get; }

        public AsyncRelayCommand ActionCommand { get; }

        /// <summary>Кнопка «Install/Update» на плашке пакета в списке.</summary>
        public AsyncRelayCommand InstallItemCommand { get; }

        /// <summary>Кнопка «Uninstall» на плашке пакета в списке.</summary>
        public AsyncRelayCommand UninstallItemCommand { get; }

        public AsyncRelayCommand UpdateAllCommand { get; }

        public RelayCommand CancelCommand { get; }

        public RelayCommand OpenLinkCommand { get; }

        /// <summary>Шестерня в тулбаре и ссылка «Configure» в плашке: открыть оверлей источников.</summary>
        public RelayCommand ConfigureSourceMappingCommand { get; }

        public RelayCommand OpenSourcesCommand { get; }

        public RelayCommand CloseSourcesCommand { get; }

        public RelayCommand AddSourceCommand { get; }

        public RelayCommand EditSourceCommand { get; }

        public RelayCommand RemoveSourceCommand { get; }

        public RelayCommand SaveSourceCommand { get; }

        public RelayCommand CancelSourceEditCommand { get; }

        /// <summary>Открыть NuGet.Config в оболочке (кнопка в оверлее).</summary>
        public RelayCommand OpenConfigFileCommand { get; }

        /// <summary>Импортировать выбранный внешний источник (NuGet.Config машины/решения) в источники проекта.</summary>
        public RelayCommand ImportSourceCommand { get; }

        /// <summary>Явно экспортировать источники проекта в NuGet.Config.</summary>
        public RelayCommand ExportProjectSourcesToConfigCommand { get; }

        /// <summary>Явно создать NuGet.Config рядом с проектом из источников проекта.</summary>
        public RelayCommand CreateNuGetConfigCommand { get; }


        public AsyncRelayCommand RefreshCommand { get; }

        // --- Состояние ---

        public NuGetPackageTab ActiveTab
        {
            get => _activeTab;
            private set
            {
                if (!SetProperty(ref _activeTab, value))
                    return;

                RaisePropertyChanged(nameof(VisibleItems));
                RaisePropertyChanged(nameof(IsBrowseTab));
                RaisePropertyChanged(nameof(IsInstalledTab));
                RaisePropertyChanged(nameof(IsUpdatesTab));
                RaisePropertyChanged(nameof(ShowBrowseToolbar));
                RaisePropertyChanged(nameof(EmptyText));
            }
        }

        public bool IsBrowseTab => _activeTab == NuGetPackageTab.Browse;

        public bool IsInstalledTab => _activeTab == NuGetPackageTab.Installed;

        public bool IsUpdatesTab => _activeTab == NuGetPackageTab.Updates;

        /// <summary>Поле поиска и «Include prerelease» нужны только на вкладке Browse.</summary>
        public bool ShowBrowseToolbar => IsBrowseTab;

        /// <summary>
        /// Поиск запускается сам, как только пользователь перестал печатать: Enter больше не нужен.
        /// Задержка задаётся <see cref="SearchDebounceMilliseconds"/>, предыдущая отложенная попытка
        /// отменяется — в источник уходит только последний запрос.
        /// </summary>
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (!SetProperty(ref _searchQuery, value))
                    return;

                RaisePropertyChanged(nameof(HasSearchQuery));
                ScheduleSearch();
            }
        }

        /// <summary>Задержка авто-поиска после последнего нажатия клавиши (мс). 0 — искать сразу.</summary>
        public int SearchDebounceMilliseconds { get; set; } = 300;

        /// <summary>
        /// Отложенный поиск. Нужен, чтобы вызывающая сторона (и тесты) могли дождаться результата
        /// вместо ожидания фиксированной паузы.
        /// </summary>
        public Task PendingSearch
        {
            get => _pendingSearch;
            private set => _pendingSearch = value ?? Task.CompletedTask;
        }

        public bool HasSearchQuery => !string.IsNullOrWhiteSpace(_searchQuery);

        public bool IncludePrerelease
        {
            get => _includePrerelease;
            set
            {
                if (SetProperty(ref _includePrerelease, value))
                {
                    // Prerelease влияет и на поиск, и на обновления: старые отметки больше не верны.
                    _updatesChecked = false;
                    _ = RefreshForPrereleaseAsync();
                }
            }
        }

        /// <summary>После установки выполнять <c>msbuild -t:restore</c> (иначе проект не соберётся).</summary>
        public bool AutoRestoreAfterInstall
        {
            get => _autoRestoreAfterInstall;
            set => SetProperty(ref _autoRestoreAfterInstall, value);
        }

        public string ProjectPath
        {
            get => _projectPath;
            private set
            {
                if (SetProperty(ref _projectPath, value))
                {
                    RaisePropertyChanged(nameof(ProjectHeader));
                    RaisePropertyChanged(nameof(HasProject));
                    InstallItemCommand.RaiseCanExecuteChanged();
                    UninstallItemCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Заголовок панели: «NuGet Package Manager: Проект».</summary>
        public string ProjectHeader
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(_projectPath)
                    ? null
                    : Path.GetFileNameWithoutExtension(_projectPath);
                return string.IsNullOrWhiteSpace(name)
                    ? "NuGet Package Manager"
                    : "NuGet Package Manager: " + name;
            }
        }

        public bool HasProject => _project != null;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(IsIdle));
                    RaiseActionState();
                    UpdateAllCommand.RaiseCanExecuteChanged();
                    RefreshCommand.RaiseCanExecuteChanged();
                    LoadMoreCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsIdle => !_isBusy;

        /// <summary>Прогресс долгой операции в процентах (0 — полоса скрыта).</summary>
        public double ProgressValue
        {
            get => _progressValue;
            private set
            {
                if (SetProperty(ref _progressValue, value))
                    RaisePropertyChanged(nameof(ShowProgress));
            }
        }

        public bool ShowProgress => _progressValue > 0;

        /// <summary>Узкая панель (перетащили в боковой док): детали открываются вместо списка.</summary>
        public bool IsCompact
        {
            get => _isCompact;
            set => SetProperty(ref _isCompact, value);
        }

        public bool CanLoadMore
        {
            get => _canLoadMore;
            private set
            {
                if (SetProperty(ref _canLoadMore, value))
                    LoadMoreCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Выбранная строка списка — она же источник данных панели деталей.</summary>
        public NuGetPackageItemViewModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!SetProperty(ref _selectedItem, value))
                    return;

                RaisePropertyChanged(nameof(HasSelection));
                if (value == null)
                {
                    Details.Clear();
                    return;
                }

                _ = LoadDetailsAsync(value);
            }
        }

        public bool HasSelection => _selectedItem != null;

        /// <summary>Активный источник пакетов; смена пересоздаёт сервис поиска.</summary>
        public NuGetSourceDefinition SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (!SetProperty(ref _selectedSource, value))
                    return;

                if (value == null)
                {
                    if (!_fixedSource)
                    {
                        _search = null;
                        NuGetLog.Info("Selected source cleared; search service disabled.");
                    }
                    return;
                }

                if (!_fixedSource)
                {
                    _search = new NuGetSearchService(value.Url);
                    SaveSelectedSourceUrl?.Invoke(value.Url);
                    NuGetLog.Info("Selected source: " + value.Url);
                }

                _updatesChecked = false;
                UpdateSourceMappingNotice();
                RaisePropertyChanged(nameof(EmptyText));
                _ = OnSourceChangedAsync();
            }
        }

        /// <summary>Предупреждение о выключенном сопоставлении источников (как в VS).</summary>
        public string SourceMappingNotice
        {
            get => _sourceMappingNotice;
            private set
            {
                if (SetProperty(ref _sourceMappingNotice, value))
                    RaisePropertyChanged(nameof(HasSourceMappingNotice));
            }
        }

        public bool HasSourceMappingNotice => !string.IsNullOrWhiteSpace(_sourceMappingNotice);

        /// <summary>Выбран источник-папка/UNC (offline): для него пустой поиск показывает все пакеты.</summary>
        private bool IsLocalSourceSelected => SelectedSource != null && !SelectedSource.IsRemote;

        /// <summary>
        /// Панель показана пользователю. Первичный поиск откладывается до этого момента: при старте
        /// студии источник уже выбран и <see cref="OnSourceChangedAsync"/> не запускает
        /// <see cref="SearchAsync"/>, чтобы не блокировать запуск длинным локальным поиском. Здесь
        /// отложенный поиск уходит один раз при первом открытии панели.
        /// </summary>
        public void NotifyPanelShown()
        {
            if (_isPanelVisible)
                return;

            _isPanelVisible = true;

            if (IsBrowseTab && (IsLocalSourceSelected || !string.IsNullOrWhiteSpace(SearchQuery)))
                _ = SearchAsync();
        }

        /// <summary>
        /// Сбрасывает кэши оптимизации панели: in-memory мемоизацию текущего сервиса поиска.
        /// Дисковый индекс локального источника чистится хостом через
        /// <see cref="NuGetOptimizationCache.InvalidateAll"/>.
        /// </summary>
        public void ResetCaches()
        {
            _search?.ClearMemoization();
        }

        // --- Оверлей «Package sources» ---

        /// <summary>Оверлей добавления/удаления источников пакетов открыт.</summary>
        public bool IsSourcesOverlayOpen
        {
            get => _isSourcesOverlayOpen;
            private set
            {
                if (!SetProperty(ref _isSourcesOverlayOpen, value))
                    return;

                OpenSourcesCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Открыт встроенный редактор строки (Add/Edit).</summary>
        public bool IsEditingSource
        {
            get => _isEditingSource;
            private set
            {
                if (!SetProperty(ref _isEditingSource, value))
                    return;

                AddSourceCommand.RaiseCanExecuteChanged();
                EditSourceCommand.RaiseCanExecuteChanged();
                RemoveSourceCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Заголовок редактора: «Add source» или «Edit source».</summary>
        public string SourceEditorTitle => _isNewSource ? "Add source" : "Edit source";

        public string EditingSourceName
        {
            get => _editingSourceName;
            set => SetProperty(ref _editingSourceName, value);
        }

        public string EditingSourceUrl
        {
            get => _editingSourceUrl;
            set => SetProperty(ref _editingSourceUrl, value);
        }

        /// <summary>Ошибка проверки полей редактора (пусто — ошибки нет).</summary>
        public string SourceEditError
        {
            get => _sourceEditError;
            private set
            {
                if (SetProperty(ref _sourceEditError, value))
                    RaisePropertyChanged(nameof(HasSourceEditError));
            }
        }

        public bool HasSourceEditError => !string.IsNullOrWhiteSpace(_sourceEditError);

        /// <summary>Выделенная строка таблицы источников.</summary>
        public NuGetSourceItemViewModel SelectedSourceItem
        {
            get => _selectedSourceItem;
            set
            {
                if (!SetProperty(ref _selectedSourceItem, value))
                    return;

                EditSourceCommand.RaiseCanExecuteChanged();
                RemoveSourceCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Выбранный внешний источник (NuGet.Config) в секции импорта.</summary>
        public NuGetSourceDefinition SelectedExternalSource
        {
            get => _selectedExternalSource;
            set
            {
                if (SetProperty(ref _selectedExternalSource, value))
                    ImportSourceCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Текст заглушки пустого списка — зависит от вкладки.</summary>
        public string EmptyText
        {
            get
            {
                if (!HasProject)
                    return "Open a project to manage its NuGet packages.";

                switch (_activeTab)
                {
                    case NuGetPackageTab.Installed:
                        return "No packages installed in this project.";
                    case NuGetPackageTab.Updates:
                        return "All packages are up to date.";
                    default:
                        return IsLocalSourceSelected
                            ? "No packages found in the selected source."
                            : "Type a package name and press Enter.";
                }
            }
        }

        private bool CanExecuteAction => Details.HasDetails && Details.Action != NuGetPackageAction.None && !IsBusy;

        // --- Проект ---

        /// <summary>Detects a .csproj inside the given folder (current studio project folder).</summary>
        public bool TryDetectProject(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return false;

            string csproj = Directory.EnumerateFiles(folder, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault()
                ?? Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories)
                    .FirstOrDefault();

            if (csproj == null)
            {
                ResetProjectState();
                StatusText = "No .csproj found in the current project folder. Open a Visual Studio project or browse for one.";
                return false;
            }

            return LoadProject(csproj);
        }

        public bool LoadProject(string projectPath)
        {
            try
            {
                ResetProjectState();
                _project = NuGetProjectConfig.Load(projectPath);
                ProjectPath = _project.ProjectPath;
                RaisePropertyChanged(nameof(HasProject));
                RaisePropertyChanged(nameof(EmptyText));
                RefreshCommand.RaiseCanExecuteChanged();

                LoadSources();
                _updatesChecked = false;
                RefreshInstalledItems(checkUpdates: false);

                StatusText = $"Loaded {(_project.IsSdkStyle ? "SDK-style" : "packages.config")} project: "
                             + Path.GetFileName(projectPath);

                // Обновления считаются лениво — при первом переходе на вкладку Installed/Updates
                // (StartupPerformance_Fix_Plan.md, этап 2): при старте не ходим в источник за
                // версиями и метаданными установленных пакетов.
                return true;
            }
            catch (Exception ex)
            {
                StatusText = "Failed to load project: " + ex.Message;
                return false;
            }
        }

        /// <summary>Повторно читает проект с диска (после install/update/uninstall).</summary>
        public void ReloadProject()
        {
            if (_project == null || !File.Exists(_project.ProjectPath))
                return;

            string path = _project.ProjectPath;
            _project = NuGetProjectConfig.Load(path);
            RaisePropertyChanged(nameof(HasProject));
        }

        /// <summary>
        /// Полностью сбрасывает состояние, привязанное к проекту: вкладки Installed/Updates,
        /// список Browse и панель деталей. Вызывается при смене проекта и когда <c>.csproj</c>
        /// определить не удалось — иначе панель продолжает показывать «установленные» пакеты
        /// предыдущего проекта.
        /// </summary>
        private void ResetProjectState()
        {
            _project = null;
            ProjectPath = null;
            _updatesChecked = false;
            _projectGeneration++;

            InstalledItems.Clear();
            UpdateItems.Clear();
            BrowseItems.Clear();
            SelectedItem = null;
            Details.Clear();
            CanLoadMore = false;

            RaisePropertyChanged(nameof(HasProject));
            RaisePropertyChanged(nameof(EmptyText));
            RaiseActionState();
            UpdateAllCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
        }

        // --- Поиск ---

        /// <summary>Перезапускает отложенный поиск при каждом изменении запроса.</summary>
        private void ScheduleSearch()
        {
            _debounceCts?.Cancel();

            // Пустой запрос для локального источника означает «показать все пакеты» — только на
            // вкладке Browse. Для удалённого источника пустая строка, как и раньше, очищает список.
            if (string.IsNullOrWhiteSpace(_searchQuery) && !(IsLocalSourceSelected && IsBrowseTab))
            {
                BrowseItems.Clear();
                CanLoadMore = false;
                SelectedItem = null;
                StatusText = string.Empty;
                PendingSearch = Task.CompletedTask;
                return;
            }

            var cts = new CancellationTokenSource();
            _debounceCts = cts;
            PendingSearch = DebounceSearchAsync(cts);
        }

        private async Task DebounceSearchAsync(CancellationTokenSource cts)
        {
            try
            {
                int delay = Math.Max(0, SearchDebounceMilliseconds);
                if (delay > 0)
                    await Task.Delay(delay, cts.Token).ConfigureAwait(true);

                if (!cts.IsCancellationRequested)
                    await SearchAsync();
            }
            catch (OperationCanceledException)
            {
                // Пользователь продолжил печатать — предыдущая попытка просто отменена.
            }
        }

        /// <summary>Ищет пакеты по текущему запросу (первая страница).</summary>
        public async Task SearchAsync()
        {
            string query = SearchQuery?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query) && !IsLocalSourceSelected)
                return;

            if (_search == null)
            {
                StatusText = "Select a package source first.";
                BrowseItems.Clear();
                CanLoadMore = false;
                SelectedItem = null;
                return;
            }

            ActiveTab = NuGetPackageTab.Browse;
            CancelSearch();
            var cts = _searchCts = new CancellationTokenSource();

            IsBusy = true;
            ProgressValue = 0;
            StatusText = string.IsNullOrEmpty(query) ? "Loading packages..." : "Searching...";
            NuGetLog.Info((string.IsNullOrEmpty(query) ? "List all packages" : "Search: \"" + query + "\"")
                          + " prerelease=" + IncludePrerelease + " source=" + _search.SourceUrl);
            try
            {
                List<NuGetPackageInfo> results = await _search.SearchAsync(query, PageSize, IncludePrerelease);
                if (cts.IsCancellationRequested)
                    return;

                BrowseItems.Clear();
                foreach (NuGetPackageInfo info in results)
                    BrowseItems.Add(CreateItem(info));

                CanLoadMore = results.Count >= PageSize;
                StatusText = results.Count == 0 ? "No packages found." : $"Found {results.Count} package(s).";
                SelectedItem = null;
            }
            catch (OperationCanceledException)
            {
                // Запрос отменён новым вводом — статус не трогаем.
            }
            catch (Exception ex)
            {
                StatusText = "Search failed: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_searchCts, cts))
                    IsBusy = false;
            }
        }

        /// <summary>Догружает следующую страницу результатов (кнопка «Load more»).</summary>
        public async Task LoadMoreAsync()
        {
            string query = SearchQuery?.Trim() ?? string.Empty;
            if (!CanLoadMore || (string.IsNullOrEmpty(query) && !IsLocalSourceSelected))
                return;

            if (_search == null)
            {
                StatusText = "Select a package source first.";
                return;
            }

            IsBusy = true;
            try
            {
                List<NuGetPackageInfo> results =
                    await _search.SearchAsync(query, PageSize, IncludePrerelease, BrowseItems.Count);
                foreach (NuGetPackageInfo info in results)
                    BrowseItems.Add(CreateItem(info));

                CanLoadMore = results.Count >= PageSize;
                StatusText = $"Showing {BrowseItems.Count} package(s).";
            }
            catch (Exception ex)
            {
                StatusText = "Search failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ClearSearch()
        {
            SearchQuery = string.Empty;

            // Для локального источника Esc/крестик возвращают полный список, а не пустую панель.
            if (IsLocalSourceSelected && IsBrowseTab)
            {
                ScheduleSearch();
                CanLoadMore = false;
                SelectedItem = null;
                return;
            }

            BrowseItems.Clear();
            CanLoadMore = false;
            SelectedItem = null;
            StatusText = string.Empty;
        }

        // --- Детали ---

        private async Task LoadDetailsAsync(NuGetPackageItemViewModel item)
        {
            CancelDetails();
            var cts = _detailsCts = new CancellationTokenSource();

            string installedVersion = GetInstalledVersion(item.Id);
            NuGetLog.Info("Load details: " + item.Id + " installed=" + installedVersion + " source=" + (_search == null ? "?" : _search.SourceUrl));

            // Сначала карточка из списка: версия и кнопка действия появляются мгновенно, ещё до
            // ответа источника. Ответ источника затем уточняет сведения и список версий.
            Details.Load(
                NuGetPackageDetailsService.FromPackageInfo(item.Package, installedVersion),
                item,
                installedVersion,
                SelectedSource?.Name);
            RaiseActionState();
            StatusText = "Loading package details...";

            try
            {
                NuGetPackageDetails details =
                    await _detailsService.LoadAsync(_search, item.Package, installedVersion, IncludePrerelease);
                if (cts.IsCancellationRequested || !ReferenceEquals(_selectedItem, item))
                    return;

                Details.Load(details, item, installedVersion, SelectedSource?.Name);
                StatusText = string.Empty;
                RaiseActionState();

                if (details != null)
                {
                    // README показываем для выбранной версии; запасная — последняя из списка
                    // (в проекте может быть зафиксирована версия, которой у источника уже нет).
                    string fallbackVersion = details.Versions.Count > 0 ? details.Versions[0].Version : null;
                    _ = LoadReadmeAsync(details.Id, details.Version, fallbackVersion, item);
                }
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested)
                    StatusText = "Failed to load package details: " + ex.Message;
            }
        }

        private async Task LoadReadmeAsync(string id, string version, string fallbackVersion, NuGetPackageItemViewModel item)
        {
            // README берём из выбранного источника; если показанной версии там уже нет,
            // сервис пробует запасную (последнюю из списка).
            string readme = await _detailsService.TryReadReadmeAsync(_search, id, version, fallbackVersion);
            NuGetLog.Info("README result for " + id + " " + version + ": " + (readme == null ? "NULL (" + (_detailsService.LastReadmeError ?? "-") + ")" : readme.Length + " chars"));

            // Пока README грузился, пользователь мог выбрать другой пакет.
            if (!ReferenceEquals(_selectedItem, item))
                return;

            // README нет — панель деталей сама уберёт вкладку и оставит «Package Details».
            // Известную причину (источник, сеть) показываем в строке состояния: раньше пропавшая
            // вкладка README выглядела как «в пакете нет README».
            Details.Readme = readme;
            if (readme == null && !string.IsNullOrWhiteSpace(_detailsService.LastReadmeError))
                StatusText = "README unavailable: " + _detailsService.LastReadmeError;
        }

        // --- Действия ---

        private async Task ExecuteActionAsync()
        {
            if (_project == null)
            {
                StatusText = "Open a project first: the package list belongs to a .csproj.";
                return;
            }

            NuGetPackageDetailsViewModel details = Details;
            if (!details.HasDetails)
                return;

            string id = details.Id;
            string version = details.PackageVersionText;
            NuGetPackageAction action = details.Action;
            NuGetLog.Info("Action " + action + " on " + id + " " + version + " project=" + _project.ProjectPath);
            if (action == NuGetPackageAction.None)
                return;

            IsBusy = true;
            ProgressValue = 15;
            try
            {
                switch (action)
                {
                    case NuGetPackageAction.Uninstall:
                        StatusText = $"Uninstalling {id}...";
                        _installService.Uninstall(_project, id);
                        ProgressValue = 90;
                        StatusText = $"Uninstalled {id}.";
                        break;

                    default:
                        StatusText = $"{NuGetPackageStateResolver.ActionText(action)} {id} {version}...";
                        if (_search == null)
                        {
                            StatusText = "Select a package source first.";
                            return;
                        }
                        await _installService.InstallAsync(_project, id, version, _search);
                        ProgressValue = 70;
                        StatusText = $"{NuGetPackageStateResolver.ActionText(action)}ed {id} {version}.";
                        break;
                }

                await AfterProjectChangedAsync(id, action != NuGetPackageAction.Uninstall);
            }
            catch (Exception ex)
            {
                StatusText = "Operation failed: " + ex.Message;
            }
            finally
            {
                ProgressValue = 0;
                IsBusy = false;
            }
        }

        /// <summary>Установка/обновление пакета кнопкой на плашке списка.</summary>
        public async Task InstallItemAsync(object parameter)
        {
            if (!(parameter is NuGetPackageItemViewModel item) || _project == null)
                return;

            string version = item.LatestVersion;
            if (string.IsNullOrWhiteSpace(version))
                version = item.Package?.Version;

            if (string.IsNullOrWhiteSpace(version))
            {
                StatusText = "No version to install for " + item.Id + ".";
                return;
            }

            IsBusy = true;
            ProgressValue = 20;
            try
            {
                if (_search == null)
                {
                    StatusText = "Select a package source first.";
                    return;
                }

                StatusText = $"Installing {item.Id} {version}...";
                NuGetLog.Info("Install item: " + item.Id + " " + version + " project=" + _project.ProjectPath);
                await _installService.InstallAsync(_project, item.Id, version, _search);
                StatusText = $"Installed {item.Id} {version}.";
                await AfterProjectChangedAsync(item.Id, restore: true);
            }
            catch (Exception ex)
            {
                StatusText = "Install failed: " + ex.Message;
            }
            finally
            {
                ProgressValue = 0;
                IsBusy = false;
            }
        }

        /// <summary>Удаление пакета кнопкой на плашке списка.</summary>
        public async Task UninstallItemAsync(object parameter)
        {
            if (!(parameter is NuGetPackageItemViewModel item) || _project == null)
                return;

            IsBusy = true;
            ProgressValue = 20;
            try
            {
                StatusText = $"Uninstalling {item.Id}...";
                _installService.Uninstall(_project, item.Id);
                StatusText = $"Uninstalled {item.Id}.";
                await AfterProjectChangedAsync(item.Id, restore: false);
            }
            catch (Exception ex)
            {
                StatusText = "Uninstall failed: " + ex.Message;
            }
            finally
            {
                ProgressValue = 0;
                IsBusy = false;
            }
        }

        /// <summary>Пересчитывает доступность кнопок, зависящих от выбранного пакета и версии.</summary>
        private void RaiseActionState()
        {
            ActionCommand.RaiseCanExecuteChanged();
            InstallItemCommand.RaiseCanExecuteChanged();
            UninstallItemCommand.RaiseCanExecuteChanged();
        }

        /// <summary>Все доступные обновления одной операцией (вкладка Updates).</summary>
        public async Task UpdateAllAsync()
        {
            if (_project == null || UpdateItems.Count == 0)
                return;

            if (_search == null)
            {
                StatusText = "Select a package source first.";
                return;
            }

            List<NuGetPackageItemViewModel> targets = UpdateItems.ToList();
            IsBusy = true;
            int done = 0;
            string lastError = null;

            try
            {
                foreach (NuGetPackageItemViewModel item in targets)
                {
                    ProgressValue = Math.Round(100d * done / targets.Count, 0);
                    StatusText = $"Updating {item.Id} to {item.LatestVersion} ({done + 1}/{targets.Count})...";
                    try
                    {
                        await _installService.InstallAsync(_project, item.Id, item.LatestVersion, _search);
                        done++;
                    }
                    catch (Exception ex)
                    {
                        lastError = item.Id + ": " + ex.Message;
                    }
                }

                await AfterProjectChangedAsync(null, restore: true);
                StatusText = lastError == null
                    ? $"Updated {done} package(s)."
                    : $"Updated {done} package(s). Failed: {lastError}";
            }
            finally
            {
                ProgressValue = 0;
                IsBusy = false;
            }
        }

        private async Task AfterProjectChangedAsync(string changedId, bool restore)
        {
            ReloadProject();
            _updatesChecked = false;
            await RefreshInstalledAsync(checkUpdates: true);

            if (changedId != null)
            {
                Details.ApplyInstalledVersion(GetInstalledVersion(changedId));
                ActionCommand.RaiseCanExecuteChanged();
            }

            if (restore && AutoRestoreAfterInstall)
            {
                StatusText = StatusText + " Restoring...";
                string error = await _installService.RestoreAsync(_project.ProjectPath);
                if (error != null)
                    StatusText = StatusText + " " + error;
                else
                    StatusText = StatusText.Replace(" Restoring...", " Done.");
            }

            // После restore (для PackageReference) или распаковки в packages\ (packages.config)
            // пакет уже на диске — сообщаем студии перечитать References и палитру активностей.
            ProjectChanged?.Invoke(this, _project.ProjectPath);
        }

        // --- Вкладки, источники, обновления ---

        /// <summary>Переключает вкладку и, если нужно, догружает состояние пакетов проекта.</summary>
        public void SelectTab(NuGetPackageTab tab)
        {
            ActiveTab = tab;
            _ = OnTabActivatedAsync(tab);
        }

        private async Task OnTabActivatedAsync(NuGetPackageTab tab)
        {
            if (tab == NuGetPackageTab.Browse || _project == null || _updatesChecked || IsBusy)
                return;

            await RefreshInstalledAsync(checkUpdates: true);
        }

        /// <summary>Обновляет пакеты проекта и список доступных обновлений.</summary>
        public async Task RefreshInstalledAsync(bool checkUpdates)
        {
            if (_project == null)
                return;

            if (checkUpdates && _search == null)
            {
                StatusText = "Select a package source first.";
                return;
            }

            int generation = _projectGeneration;
            IsBusy = true;
            if (checkUpdates)
                StatusText = "Checking for updates...";

            try
            {
                RefreshInstalledItems(checkUpdates: false);

                if (generation != _projectGeneration)
                    return;

                var withUpdate = new List<NuGetPackageItemViewModel>();

                if (checkUpdates)
                {
                    int index = 0;
                    foreach (NuGetPackageItemViewModel item in InstalledItems.ToList())
                    {
                        if (generation != _projectGeneration)
                            return;

                        index++;
                        ProgressValue = Math.Round(100d * index / Math.Max(InstalledItems.Count, 1), 0);
                        try
                        {
                            NuGetPackageInfo info = await _search.GetPackageInfoAsync(item.Id, IncludePrerelease);
                            if (info != null)
                            {
                                item.ApplyMetadata(info);
                                _ = item.LoadIconAsync();
                            }

                            IReadOnlyList<string> versions = await _search.GetVersionsAsync(item.Id, IncludePrerelease);
                            item.LatestVersion = NuGetVersionResolver.SelectBestVersion(versions, item.InstalledVersion, IncludePrerelease);
                        }
                        catch (Exception)
                        {
                            // Один недоступный пакет не должен ломать список обновлений.
                        }

                        if (item.HasUpdate)
                            withUpdate.Add(item);
                    }

                    _updatesChecked = true;
                }

                if (generation != _projectGeneration)
                    return;

                UpdateItems.Clear();
                foreach (NuGetPackageItemViewModel item in withUpdate)
                    UpdateItems.Add(item);

                UpdateAllCommand.RaiseCanExecuteChanged();
                if (checkUpdates)
                {
                    StatusText = withUpdate.Count == 0
                        ? $"{InstalledItems.Count} package(s) installed, all up to date."
                        : $"{withUpdate.Count} update(s) available for {InstalledItems.Count} package(s).";
                }
            }
            finally
            {
                if (generation == _projectGeneration)
                {
                    ProgressValue = 0;
                    IsBusy = false;
                }
            }
        }

        /// <summary>Перечитывает пакеты проекта без обращения к источнику (быстро).</summary>
        private void RefreshInstalledItems(bool checkUpdates)
        {
            InstalledItems.Clear();
            if (_project == null)
                return;

            foreach (NuGetPackageReference reference in _project.Packages
                .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                var info = new NuGetPackageInfo { Id = reference.Id, Version = reference.Version };
                var item = new NuGetPackageItemViewModel(info, _icons.LoadAsync)
                {
                    InstalledVersion = reference.Version
                };

                InstalledItems.Add(item);
                _ = item.LoadIconAsync();
            }
        }

        private NuGetPackageItemViewModel CreateItem(NuGetPackageInfo info)
        {
            var item = new NuGetPackageItemViewModel(info, _icons.LoadAsync);
            string installed = GetInstalledVersion(info.Id);
            if (!string.IsNullOrWhiteSpace(installed))
                item.InstalledVersion = installed;

            _ = item.LoadIconAsync();
            return item;
        }

        private string GetInstalledVersion(string id)
        {
            if (_project == null || string.IsNullOrWhiteSpace(id))
                return null;

            return NuGetProjectConfig.Find(_project, id)?.Version;
        }

        private void LoadSources()
        {
            if (_fixedSource)
            {
                Sources.Clear();
                Sources.Add(new NuGetSourceDefinition { Name = _search.SourceUrl, Url = _search.SourceUrl });
                _selectedSource = Sources[0];
                RaisePropertyChanged(nameof(SelectedSource));
                RaisePropertyChanged(nameof(EmptyText));
                UpdateSourceMappingNotice();
                return;
            }

            // В выпадающий список попадают все включённые источники (сетевые и локальные папки);
            // выключенные строки видны только в оверлее «Package sources».
            IReadOnlyList<NuGetSourceDefinition> sources = (LoadProjectSources?.Invoke() ?? Enumerable.Empty<NuGetSourceDefinition>())
                .Where(source => source.IsEnabled)
                .ToList();

            Sources.Clear();
            foreach (NuGetSourceDefinition source in sources)
                Sources.Add(source);

            if (Sources.Count == 0)
            {
                StatusText = "All package sources are disabled: enable at least one in Package sources.";
                _selectedSource = null;
                _search = null;
                RaisePropertyChanged(nameof(SelectedSource));
                RaisePropertyChanged(nameof(EmptyText));
                UpdateSourceMappingNotice();
                return;
            }

            string persistedUrl = LoadSelectedSourceUrl?.Invoke();
            NuGetSourceDefinition preferred = string.IsNullOrWhiteSpace(persistedUrl)
                ? null
                : Sources.FirstOrDefault(source => string.Equals(source.Url, persistedUrl, StringComparison.OrdinalIgnoreCase));

            SelectedSource = preferred ?? Sources.FirstOrDefault();
            UpdateSourceMappingNotice();
        }

        /// <summary>Перечитывает источники после правки конфигурации и восстанавливает выбор.</summary>
        private void ReloadSources()
        {
            string previousUrl = _selectedSource?.Url;

            if (_fixedSource)
                return;

            IReadOnlyList<NuGetSourceDefinition> definitions = (LoadProjectSources?.Invoke() ?? Enumerable.Empty<NuGetSourceDefinition>())
                .Where(source => source.IsEnabled)
                .ToList();

            Sources.Clear();
            foreach (NuGetSourceDefinition definition in definitions)
                Sources.Add(definition);

            NuGetSourceDefinition restored = Sources.FirstOrDefault(
                                               source => string.Equals(source.Url, previousUrl, StringComparison.OrdinalIgnoreCase))
                                           ?? Sources.FirstOrDefault();

            if (restored == null)
            {
                StatusText = "All package sources are disabled: enable at least one in Package sources.";
                _selectedSource = null;
                _search = null;
                RaisePropertyChanged(nameof(SelectedSource));
                RaisePropertyChanged(nameof(EmptyText));
                UpdateSourceMappingNotice();
                return;
            }

            if (!ReferenceEquals(restored, _selectedSource))
                SelectedSource = restored;
            else
                UpdateSourceMappingNotice();
        }

        private void UpdateSourceMappingNotice()
        {
            string folder = string.IsNullOrWhiteSpace(_projectPath) ? null : Path.GetDirectoryName(_projectPath);
            SourceMappingNotice = _sourcesService.IsSourceMappingConfigured(folder)
                ? null
                : "Package source mapping is off.";
        }

        /// <summary>Ссылка «Configure» и шестерня открывают оверлей источников.</summary>
        private void ConfigureSourceMapping()
        {
            OpenSources();
        }

        private void OpenNuGetConfig()
        {
            string folder = GetProjectFolder();
            string config = _sourcesService.FindConfigPath(folder);
            OpenLink(config ?? "https://learn.microsoft.com/nuget/consume-packages/package-source-mapping");
        }

        /// <summary>Открывает оверлей «Package sources» и заполняет его текущей конфигурацией.</summary>
        public void OpenSources()
        {
            ReloadSourceItems();
            SourceEditError = null;
            IsEditingSource = false;
            IsSourcesOverlayOpen = true;
        }

        public void CloseSources()
        {
            IsEditingSource = false;
            IsSourcesOverlayOpen = false;
        }

        private void ReloadSourceItems()
        {
            IReadOnlyList<NuGetSourceDefinition> definitions = (LoadProjectSources?.Invoke() ?? Enumerable.Empty<NuGetSourceDefinition>()).ToList();

            SourceItems.Clear();
            foreach (NuGetSourceDefinition definition in definitions)
            {

                var item = new NuGetSourceItemViewModel(definition);
                item.PropertyChanged += SourceItem_PropertyChanged;
                SourceItems.Add(item);
            }

            SelectedSourceItem = SourceItems.FirstOrDefault();
            LoadExternalSources();
        }

        /// <summary>Галочка «Enabled» сохраняется сразу — как в Visual Studio.</summary>
        private void SourceItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NuGetSourceItemViewModel.IsEnabled))
                PersistSources();
        }

        private void BeginAddSource()
        {
            _isNewSource = true;
            EditingSourceName = string.Empty;
            EditingSourceUrl = string.Empty;
            SourceEditError = null;
            RaisePropertyChanged(nameof(SourceEditorTitle));
            IsEditingSource = true;
        }

        private void BeginEditSource()
        {
            if (SelectedSourceItem == null)
                return;

            _isNewSource = false;
            EditingSourceName = SelectedSourceItem.Name;
            EditingSourceUrl = SelectedSourceItem.Source;
            SourceEditError = null;
            RaisePropertyChanged(nameof(SourceEditorTitle));
            IsEditingSource = true;
        }

        private void CancelSourceEdit()
        {
            IsEditingSource = false;
            SourceEditError = null;
        }

        /// <summary>Проверяет поля и записывает строку в список (в файл — по <see cref="PersistSources"/>).</summary>
        private void SaveSource()
        {
            string name = (EditingSourceName ?? string.Empty).Trim();
            string url = (EditingSourceUrl ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                SourceEditError = "Source name is required.";
                return;
            }

            if (url.Length == 0)
            {
                SourceEditError = "Source path or URL is required.";
                return;
            }

            if (SourceItems.Any(item =>
                    string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
                    && (_isNewSource || !ReferenceEquals(item, SelectedSourceItem))))
            {
                SourceEditError = "A source with this name already exists.";
                return;
            }
            if (SourceItems.Any(item =>
                    string.Equals(item.Source, url, StringComparison.OrdinalIgnoreCase)
                    && (_isNewSource || !ReferenceEquals(item, SelectedSourceItem))))
            {
                SourceEditError = "A source with this address already exists.";
                return;
            }

            if (_isNewSource)
            {
                var item = new NuGetSourceItemViewModel(new NuGetSourceDefinition { Name = name, Url = url, IsEnabled = true });
                item.PropertyChanged += SourceItem_PropertyChanged;
                SourceItems.Add(item);
                SelectedSourceItem = item;
            }
            else if (SelectedSourceItem != null)
            {
                SelectedSourceItem.Name = name;
                SelectedSourceItem.Source = url;
            }

            SourceEditError = null;
            IsEditingSource = false;
            PersistSources();
        }

        private void RemoveSource()
        {
            if (SelectedSourceItem == null)
                return;

            NuGetSourceItemViewModel removed = SelectedSourceItem;
            SourceItems.Remove(removed);
            removed.PropertyChanged -= SourceItem_PropertyChanged;
            SelectedSourceItem = SourceItems.FirstOrDefault();
            PersistSources();
        }

        /// <summary>Записывает per-project список источников и обновляет выпадающий список источников.</summary>
        private void PersistSources()
        {
            try
            {
                SaveProjectSources?.Invoke(SourceItems.Select(item => item.ToDefinition()).ToList());
                SourceEditError = null;
                ReloadSources();
                StatusText = "Package sources updated.";
            }
            catch (Exception ex)
            {
                SourceEditError = "Failed to save package sources: " + ex.Message;
                StatusText = SourceEditError;
            }
        }

        private void LoadExternalSources()
        {
            ExternalSources.Clear();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NuGetSourceDefinition source in _sourcesService.LoadMachineAndUserSources()
                         .Concat(_sourcesService.LoadSolutionSources(GetProjectFolder())))
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Url))
                    continue;

                if (seen.Add(source.Url))
                    ExternalSources.Add(source);
            }

            SelectedExternalSource = ExternalSources.FirstOrDefault();
        }

        private void ImportSelectedExternalSource()
        {
            if (SelectedExternalSource == null)
                return;

            var copy = new NuGetSourceDefinition
            {
                Name = SelectedExternalSource.Name,
                Url = SelectedExternalSource.Url,
                IsEnabled = true
            };

            if (SourceItems.Any(item =>
                    string.Equals(item.Name, copy.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Source, copy.Url, StringComparison.OrdinalIgnoreCase)))
            {
                SourceEditError = "A source with this name or address already exists.";
                StatusText = SourceEditError;
                return;
            }

            var item = new NuGetSourceItemViewModel(copy);
            item.PropertyChanged += SourceItem_PropertyChanged;
            SourceItems.Add(item);
            SelectedSourceItem = item;
            SourceEditError = null;
            PersistSources();
        }

        private void ExportProjectSourcesToConfig()
        {
            try
            {
                string path = ResolveProjectConfigPath();
                _sourcesService.WriteConfig(path, CurrentProjectSources());
                SourceEditError = null;
                StatusText = "Project sources exported to " + path;
            }
            catch (Exception ex)
            {
                SourceEditError = "Failed to export package sources: " + ex.Message;
                StatusText = SourceEditError;
            }
        }

        private void CreateNuGetConfig()
        {
            try
            {
                string path = ResolveProjectConfigPath();
                if (File.Exists(path))
                {
                    StatusText = "NuGet.Config already exists: " + path;
                    return;
                }

                _sourcesService.WriteConfig(path, CurrentProjectSources());
                SourceEditError = null;
                StatusText = "NuGet.Config created: " + path;
            }
            catch (Exception ex)
            {
                SourceEditError = "Failed to create NuGet.Config: " + ex.Message;
                StatusText = SourceEditError;
            }
        }

        private string ResolveProjectConfigPath()
        {
            string folder = GetProjectFolder();
            return string.IsNullOrWhiteSpace(folder)
                ? Path.Combine(Directory.GetCurrentDirectory(), "NuGet.Config")
                : Path.Combine(folder, "NuGet.Config");
        }

        private IReadOnlyList<NuGetSourceDefinition> CurrentProjectSources()
        {
            return LoadProjectSources?.Invoke() ?? new List<NuGetSourceDefinition>();
        }



        private string GetProjectFolder()
        {
            return string.IsNullOrWhiteSpace(_projectPath) ? null : Path.GetDirectoryName(_projectPath);
        }

        private async Task OnSourceChangedAsync()
        {
            if (HasProject)
                await RefreshInstalledAsync(checkUpdates: _activeTab != NuGetPackageTab.Browse);

            // При старте источник уже выбран, но панель ещё не показана: поиск не запускаем —
            // он уйдёт в NotifyPanelShown() при первом открытии панели.
            if (!_isPanelVisible)
                return;

            if (IsBrowseTab && (IsLocalSourceSelected || !string.IsNullOrWhiteSpace(SearchQuery)))
                await SearchAsync();
        }

        /// <summary>Смена «Include prerelease» перепроверяет обновления и текущий поиск.</summary>
        private async Task RefreshForPrereleaseAsync()
        {
            if (IsBrowseTab && (IsLocalSourceSelected || !string.IsNullOrWhiteSpace(SearchQuery)))
            {
                await SearchAsync();
                return;
            }

            if (HasProject && !IsBrowseTab)
                await RefreshInstalledAsync(checkUpdates: true);
        }

        private async Task RefreshAsync()
        {
            if (IsBrowseTab)
            {
                await SearchAsync();
                return;
            }

            await RefreshInstalledAsync(checkUpdates: true);
        }

        // --- Служебное ---

        public void Cancel()
        {
            CancelSearch();
            CancelDetails();
            ProgressValue = 0;
            IsBusy = false;
            StatusText = "Cancelled.";
        }

        private void CancelSearch()
        {
            _searchCts?.Cancel();
            _searchCts = null;
        }

        private void CancelDetails()
        {
            _detailsCts?.Cancel();
            _detailsCts = null;
        }

        private void OpenLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            LinkRequested?.Invoke(this, url);
        }

        private static NuGetPackageTab ParseTab(object parameter)
        {
            if (parameter is NuGetPackageTab tab)
                return tab;

            string text = parameter as string;
            if (!string.IsNullOrWhiteSpace(text) &&
                Enum.TryParse(text, ignoreCase: true, out NuGetPackageTab parsed))
            {
                return parsed;
            }

            return NuGetPackageTab.Browse;
        }
    }
}
