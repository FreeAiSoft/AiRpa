using System;
using System.Activities.Presentation;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonDock.Layout;
using AvalonDock;
using AvalonDock.Layout.Serialization;
using FW.EasyMvvm.Core;
using Material.Icons;
using Microsoft.Win32;
using RPA.Controls;
using RPA.Converters;
using RPA.Core;
using RPA.Helpers;
using RPA.Logging;
using RPA.Models;
using RPA.ViewModels;
using RPA.VsProject;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : Window, IView
    {
        private ObservableCollection<WorkflowDesignerEx> WorkflowDesigners => ((MainViewModel)DataContext).WorkflowDesigners;

        private ToolboxPanel _wfToolbox;
        private DocGroupHelper docGroupHelper;
        private Border WfToolboxBorder;
        private Border WfOutlineViewBorder;
        private Border WfPropertyBorder;
        private readonly PropertyWindowStyleHelper _propertyStyleHelper = new PropertyWindowStyleHelper();

        private LayoutDocumentPane docGroup;

        /// <summary>Активация соседней вкладки при закрытии (правило — <see cref="DocumentTabPolicy"/>).</summary>
        private DocumentTabsCloseCoordinator tabsCloseCoordinator;
        public LogControlLibrary.Controls.LogViewerControl LogViewerControl { get; private set; }
        private ProjectManagerControl projectManagerControl;
        private Border wfProjectBorder;
        private Chat.Views.ChatPanel chatPanel;
        private NuGet.Views.NuGetPanel nuGetPanel;
        /// <summary>ContentId вкладки NuGet: по нему панель находится при повторном открытии.</summary>
        private const string NuGetContentId = "NuGet";

        /// <summary>
        /// Открытая панель NuGet: по умолчанию это вкладка панели документов (как менеджер пакетов
        /// в Visual Studio), но сохранённая раскладка может вернуть её и в боковой док — поэтому
        /// тип <see cref="LayoutContent"/>, а не <see cref="LayoutDocument"/>.
        /// </summary>
        private LayoutContent nuGetContent;

        private DispatcherTimer _layoutSaveTimer;
        private bool _resetLayout;
        private bool _paletteRebuildInProgress;

        /// <summary>Каркас окна инициализирован (панели созданы, раскладка подготовлена к подъёму).</summary>
        private bool _shellInitialized;

        /// <summary>Раскладка, подготовленная до первого кадра и поднимаемая в DockingManager после него.</summary>
        private AvalonDock.Layout.LayoutRoot _preparedLayoutRoot;

        /// <summary>Результат фоновой пересборки палитры: данные, безопасные для построения вне UI-потока.</summary>
        private sealed class PaletteBuildResult
        {
            internal Core.Generation.ActivityPaletteRegistry Registry;
            internal Core.Workflow.StudioActivityCatalog Catalog;
            internal List<System.Activities.Presentation.Toolbox.ToolboxCategory> ToolboxCategories;
        }

        public MainView()
        {
            StartupTimer.Mark("MainView: конструктор, начало");
            StudioBrushes.ApplyToApplication();
            StudioFonts.ApplyToApplication();
            StartupTimer.Mark("MainView: StudioBrushes + StudioFonts");
            InitializeComponent();
            StartupTimer.Mark("MainView: InitializeComponent()");
            Services.AppSettings.SetDockingManager(dockManager);// DockingManager можно отловить только здесь, такие дела
            StartupTimer.Mark("MainView: SetDockingManager()");

            // П. 3.3: тяжёлые панели и раскладка переносятся в MainView_Loaded (EnsureShellInitialized),
            // чтобы первый рендер view.Show() рисовал только лёгкий каркас окна.
            StartupTimer.Mark("MainView: каркас окна готов (тяжёлые панели отложены до первого кадра)");
        }

        private void InitializeControls()
        {
            // Initialize named borders and controls
            WfToolboxBorder = new Border();
            WfOutlineViewBorder = new Border { Margin = new Thickness(0, 5, 0, 5) };
            WfPropertyBorder = new Border { BorderThickness = new Thickness(0) };
            LogViewerControl = new LogControlLibrary.Controls.LogViewerControl();
            // Этапы загрузки выводятся в панель «Log» (только [Startup]-метки; остальной журнал — log.txt).
            StartupTimer.Attach(message => LogViewerControl.AddLog(LogControlLibrary.Models.LogLevel.Info, message));
            docGroup ??= new LayoutDocumentPane();
            projectManagerControl = new ProjectManagerControl();
            Binding binding = new Binding("FSNodes");
            _ = BindingOperations.SetBinding(projectManagerControl, ProjectManagerControl.ItemsSourceProperty, binding);
            wfProjectBorder = new Border
            {
                Child = projectManagerControl
            };
            chatPanel = new Chat.Views.ChatPanel();
            // Route chat diagnostics to the studio log (viewed via LogControlLibrary).
            Chat.Services.OutputWindowService.WriteSink = message => Log.Write(message);
            Chat.Services.ChatLog.Sink = message => Log.Write(message);
            // Сетевая диагностика панели NuGet — в тот же журнал: поиск, скачивание .nupkg, README.
            RPA.NuGet.Services.NuGetLog.Sink = message => Log.Write(message);
            // The chat resolves the current project folder for agent tools + MSBuild feedback.
            Chat.Services.ChatEnvironment.SolutionPathProvider = () => ProjectManager.GetInstance().ProjectFolder;
            // Каталог активностей для агента (AG-01): строится из тех же типов, что и панель «Активности».
            Chat.Services.ChatEnvironment.ActivityCatalog = Core.Workflow.StudioActivityCatalog.Default;
            // Структура процесса для агента (AG-03): дерево активностей с адресами, аргументами и ошибками WF.
            Chat.Services.ChatEnvironment.WorkflowOutline = Core.Workflow.StudioWorkflowOutlineService.Default;
            // Мост правок открытого процесса (AG-02): пачки правок модели конструктора с историей отмены.
            Chat.Services.ChatEnvironment.WorkflowEditing = Core.Workflow.StudioWorkflowEditingService.Default;
            // Гейт валидации XAML для агента (AG-07): editor проверяет записанный .xaml тем же гейтом, что и генерация.
            Chat.Services.ChatEnvironment.WorkflowXamlGate = Core.Generation.StudioWorkflowXamlGate.Default;

            // Обновление палитры после создания активности (AG-08): студия загружает собранную сборку в домен,
            // добавляет её типы в палитру, сбрасывает кэши каталога и реестра и пересобирает панель «Активности».
            Chat.Services.ChatEnvironment.ActivityPaletteNotifier = Core.Generation.StudioActivityPaletteNotifier.Create(RefreshActivityPalette);
            // Прогон тест-кейсов ТЗ (волна 8, TZ-08): прогонщик за контрактом IWorkflowRunService — движок студии,
            // позже тот же контракт закроет RpaRunner.exe (OR-02) без правок карточки.
            Chat.Services.ChatEnvironment.WorkflowRun = Core.Workflow.StudioWorkflowRunService.Default;
            // Структура открытого документа важнее файла: правки AG-02 живут в модели до сохранения.
            Core.Workflow.StudioWorkflowOutlineService.LiveOutlineSource = (path, depth, includeArguments) =>
                Chat.Services.ChatEnvironment.WorkflowEditing.GetLiveOutline(path, depth, includeArguments);

            // Theme: the studio window follows the chat theme (single source of truth).
            // Порядок смены темы задан явно: студия пересобирает палитру Studio.* (ThemeApplying)
            // ДО того, как ChatPanel применит свой словарь и перекроет кисти мостом (ThemeChanged).
            // Иначе ChatStudioBridge копирует кисти предыдущей темы и чат отстаёт на один переключатель.
            chatPanel.ViewModel.ThemeApplying = Services.AppSettings.ApplyTheme;

            chatPanel.ViewModel.ThemeChanged += OnChatThemeChanged;
            Services.AppSettings.ApplyTheme(chatPanel.ViewModel.Theme);
            // Словарь чата собран в конструкторе из палитры по умолчанию, а сохранённая тема могла быть
            // светлой — перекрываем словарь актуальной палитрой студии до первого переключения.
            chatPanel.ReapplyTheme();

            _ = ThemeToggleButton.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding(nameof(Chat.ViewModels.ChatViewModel.IsDarkTheme))
                {
                    Source = chatPanel.ViewModel,
                    Mode = BindingMode.TwoWay
                });

            nuGetPanel = new NuGet.Views.NuGetPanel();
            // Выбор источника пакетов персистится на воркспейс: панель не знает про студию и настройки,
            // поэтому хост подключает чтение/запись через хуки.
            nuGetPanel.ViewModel.LoadSelectedSourceUrl = () =>
                NuGetSourceSelectionStore.Read(
                    Properties.Settings.Default.NuGetSelectedSources,
                    ProjectManager.GetInstance().ProjectFolder);

            nuGetPanel.ViewModel.SaveSelectedSourceUrl = url =>
            {
                Properties.Settings.Default.NuGetSelectedSources =
                    NuGetSourceSelectionStore.Write(
                        Properties.Settings.Default.NuGetSelectedSources,
                        ProjectManager.GetInstance().ProjectFolder,
                        url);
                Properties.Settings.Default.Save();
            };

            // Список источников проекта живёт в собственном JSON-хранилище студии
            // (%AppData%\AiRpaStudio\NuGet\nuget_sources.json), а не в NuGet.Config проекта.
            nuGetPanel.ViewModel.LoadProjectSources = () =>
                NuGetProjectSourcesStore.Read(
                    ReadNuGetProjectSourcesJson(),
                    ProjectManager.GetInstance().ProjectFolder);

            nuGetPanel.ViewModel.SaveProjectSources = sources =>
            {
                NuGetPaths.EnsureDirectories();
                string json = NuGetProjectSourcesStore.Write(
                    ReadNuGetProjectSourcesJson(),
                    ProjectManager.GetInstance().ProjectFolder,
                    sources);
                File.WriteAllText(NuGetPaths.ProjectSourcesFile, json);
            };

            // Re-detect the .csproj whenever the current studio project folder changes.
            ProjectManager.GetInstance().PropertyChanged += OnProjectManagerPropertyChanged;
            // После install/uninstall пакета перечитываем References и активности в палитре.
            nuGetPanel.ViewModel.ProjectChanged += OnNuGetProjectChanged;
            _ = nuGetPanel.SetProjectFolder(ProjectManager.GetInstance().ProjectFolder);
        }

        private void OnProjectManagerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProjectManager.ProjectFolder))
            {
                // Этап 0 (StartupPerformance_Fix_Plan.md): не блокируем появление окна. Тяжёлая
                // инициализация NuGet-панели (LoadSources → SelectedSource → SearchAsync) и сканирование
                // активностей пакетов (Assembly.LoadFrom + GetTypes + пересборка палитры) откладываются
                // до ApplicationIdle — окно успевает отрисоваться до старта дорогой работы.
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    _ = nuGetPanel.SetProjectFolder(ProjectManager.GetInstance().ProjectFolder);
                    ScanNuGetActivities(nuGetPanel?.ViewModel?.ProjectPath);
                }));
            }
        }

        private static string ReadNuGetProjectSourcesJson()
        {
            return File.Exists(NuGetPaths.ProjectSourcesFile)
                ? File.ReadAllText(NuGetPaths.ProjectSourcesFile)
                : null;
        }


        private void OnChatThemeChanged()
        {
            // Палитра Studio.* пересобрана в ChatViewModel.ThemeApplying — до того, как контрол сменил
            // свой словарь. Здесь остаётся перекраска открытых документов и панелей; повторный
            // ApplyTheme дал бы второй полный прогон темы на каждый клик.
            ReapplyWorkflowControlThemes();
        }

        /// <summary>После изменения NuGet-пакетов перечитываем References и активности пакетов на UI-потоке.</summary>
        private void OnNuGetProjectChanged(object sender, string projectPath)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnNuGetProjectChanged(sender, projectPath)));
                return;
            }

            ProjectManager.GetInstance().RefreshVsProject(projectPath);
            ScanNuGetActivities(projectPath);
        }

        /// <summary>
        /// Сканирует NuGet-пакеты проекта и, если нашлись активности, пересобирает панель «Активности».
        /// Вызывается и при изменении пакетов, и при загрузке/смене проекта — чтобы активности из уже
        /// установленных пакетов появлялись в тулбоксе автоматически, без перезапуска студии.
        /// </summary>
        private void ScanNuGetActivities(string projectPath)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ScanNuGetActivities(projectPath)));
                return;
            }

            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
                return;

            // Этап 3 (StartupPerformance_Fix_Plan.md): сканирование пакетных активностей
            // (Assembly.LoadFrom + GetTypes) — это CPU/IO, поэтому выполняется в фоне, а не на
            // UI-потоке. На UI-поток возвращается только результат (Changed), и лишь там
            // пересобирается панель «Активности».
            _ = ScanNuGetActivitiesAsync(projectPath);
        }

        private async Task ScanNuGetActivitiesAsync(string projectPath)
        {
            var result = await Task.Run(() => new Core.Workflow.NuGetActivityPaletteLoader().LoadFromProject(projectPath));

            if (result?.Changed == true)
            {
                RefreshActivityPalette();
            }
        }

        private void ReapplyWorkflowControlThemes()
        {
            _wfToolbox?.ApplyTheme();

            // Поверхность каждого открытого дизайнера (F-04): ключи палитры + дефолтные цвета WF,
            // пришедшие через StaticResource, заменяются по значению.
            foreach (WorkflowDesignerEx designer in WorkflowDesigners)
            {
                WfDesignerTheme.Apply(designer.View as FrameworkElement);
            }
            WfVisualTreeThemer.ApplyWhenLoaded(WfOutlineViewBorder?.Child as FrameworkElement);

            if (WfPropertyBorder?.Child is UIElement propertyInspector)
            {
                _propertyStyleHelper.SetPropertyStyles(propertyInspector);
            }

            // Редакторы кода и XAML (этап 12): фон, текст и подсветка по схеме Visual Studio.
            foreach (CodeEditorView codeEditor in docGroup.Children
                .OfType<LayoutDocument>()
                .Select(document => document.Content)
                .OfType<CodeEditorView>())
            {
                codeEditor.ApplyTheme();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление главного окна (W-01): DWM на Windows 11; на Windows 10 углы режет region
            // из WindowChrome.CornerRadius, заданного в XAML радиусом палитры.
            _ = WindowCorner.Enable(this);
        }

        private void MainView_StateChanged(object sender, EventArgs e)
        {
            ApplyMaximizeMargin();

            if (MaximizeIcon == null)
            {
                return;
            }

            bool maximized = WindowState == WindowState.Maximized;
            MaximizeIcon.Kind = maximized ? MaterialIconKind.WindowRestore : MaterialIconKind.WindowMaximize;
            MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        }

        private void ApplyMaximizeMargin()
        {
            if (RootGrid == null)
            {
                return;
            }

            // В развёрнутом состоянии рамка окна уходит за границы экрана на толщину
            // ResizeBorderThickness (см. MainView.xaml), поэтому контент по краям срезается.
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(6) : new Thickness(0);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TitleBarMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void TitleBarMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void TitleBarClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        /// <summary>
        /// Пересборка панели «Активности» после появления новой библиотеки (карточка <c>AG-08</c>).
        /// Панель — элемент WPF, поэтому подмена содержимого идёт на UI-потоке; счётчик панели сбрасывает
        /// сам <see cref="WFToolboxHelper.Build"/>. Сохранение скролла/выделения и кнопка «Обновить» — UI-08.
        /// </summary>
        private void RefreshActivityPalette()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshActivityPalette));
                return;
            }

            _ = RefreshActivityPaletteAsync();
        }

        private async Task RefreshActivityPaletteAsync()
        {
            if (_paletteRebuildInProgress)
            {
                return;
            }
            _paletteRebuildInProgress = true;

            if (_wfToolbox == null)
            {
                _wfToolbox = new ToolboxPanel();
                WfToolboxBorder.Child = _wfToolbox;
            }

            _wfToolbox.ShowLoading();

            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                Log.Write($"[Palette] RefreshActivityPalette: начало (поток {Thread.CurrentThread.ManagedThreadId}, UI-поток={Dispatcher.CheckAccess()})");

                PaletteBuildResult result = await Task.Run(BuildPaletteModel);
                Log.Write($"[Palette] BuildPaletteModel (фон): +{sw.ElapsedMilliseconds} ms");

                _wfToolbox.SetToolboxControl(new WFToolboxHelper().BuildControl(result.ToolboxCategories));
                Log.Write($"[Palette] WFToolboxHelper.BuildControl + SetToolboxControl: +{sw.ElapsedMilliseconds} ms");

                _wfToolbox.ApplyTheme();
                Log.Write($"[Palette] ApplyTheme: +{sw.ElapsedMilliseconds} ms");

                InstallActivityCatalog(result);
                Log.Write($"[Palette] InstallActivityCatalog: +{sw.ElapsedMilliseconds} ms");

                Log.Write($"Палитра «Активности» пересобрана: появилась новая библиотека активностей. (всего {sw.ElapsedMilliseconds} ms)");
            }
            finally
            {
                _paletteRebuildInProgress = false;
                _wfToolbox?.HideLoading();
            }
        }

        private PaletteBuildResult BuildPaletteModel()
        {
            IReadOnlyList<Core.Generation.ActivityPaletteCategory> categories =
                Core.Generation.StudioActivityPalette.Categories();

            var registry = new Core.Generation.ActivityPaletteRegistry(categories);
            registry.PersistDiskCache();
            var catalog = new Core.Workflow.StudioActivityCatalog(categories, registry);

            Dictionary<Type, string> displayNames = registry.All
                .ToDictionary(descriptor => descriptor.Type, descriptor => descriptor.DisplayName);

            return new PaletteBuildResult
            {
                Registry = registry,
                Catalog = catalog,
                // Отображаемые имена берём из уже построенного реестра (в т.ч. из дискового кэша),
                // чтобы CreateCategory не рефлексировал атрибуты типов повторно (дорого на Browser/OS).
                ToolboxCategories = new WFToolboxHelper().BuildModel(categories, displayNames)
            };
        }

        private void InstallActivityCatalog(PaletteBuildResult result)
        {
            Core.Generation.ActivityPaletteRegistry.Install(result.Registry);
            Core.Workflow.StudioActivityCatalog.Install(result.Catalog);
            Chat.Services.ChatEnvironment.ActivityCatalog = result.Catalog;
        }



        private LayoutRoot PrepareLayout()
        {
            // Try to load layout from saved settings
            if (!string.IsNullOrEmpty(Properties.Settings.Default.ADLayout))
            {
                try
                {
                    // Десериализация в невидимый (staging) DockingManager: XmlLayoutSerializer сам
                    // присваивает Layout менеджеру, а подъём доков в реальный dockManager откладывается
                    // до первого кадра (п. 3.3).
                    DockingManager staging = new DockingManager();
                    XmlLayoutSerializer layoutSerializer = new XmlLayoutSerializer(staging);

                    // Set up the callback to restore content during deserialization
                    layoutSerializer.LayoutSerializationCallback += (s, args) =>
                    {
                        // Restore content based on ContentId or Title
                        string contentId = args.Model.ContentId;

                        if (contentId == "ProjectManager" || args.Model.Title == (FindResource("lang_main_project") as string))
                        {
                            args.Content = wfProjectBorder;
                        }
                        else if (contentId == "Activities" || args.Model.Title == (FindResource("lang_main_activities") as string))
                        {
                            args.Content = WfToolboxBorder;
                        }
                        else if (contentId == "Outline" || args.Model.Title == (FindResource("lang_main_outline") as string))
                        {
                            args.Content = WfOutlineViewBorder;
                        }
                        else if (contentId == "Properties" || args.Model.Title == (FindResource("lang_main_activity_properties") as string))
                        {
                            args.Content = WfPropertyBorder;
                        }
                        else if (contentId == "Log" || args.Model.Title == (FindResource("lang_main_log") as string))
                        {
                            args.Content = LogViewerControl;
                        }
                        else if (contentId == "Chat")
                        {
                            args.Content = chatPanel;
                        }
                        // NuGet открывается вкладкой панели документов (как менеджер пакетов VS).
                        // Ветка обязана стоять до отмены LayoutDocument ниже: иначе сохранённая
                        // вкладка NuGet не восстановится при старте студии.
                        else if (contentId == "NuGet")
                        {
                            args.Content = nuGetPanel;
                            args.Model.CanClose = true; // гарантия: крестик закрытия у вкладки NuGet виден всегда
                            nuGetContent = args.Model as LayoutContent;
                        }

                        // Handle document pane
                        if (args.Model is LayoutDocument)
                        {
                            // Documents will be managed by docGroupHelper
                            args.Cancel = true;
                        }
                    };

                    // Deserialize the layout
                    using (StringReader stringReader = new StringReader(Properties.Settings.Default.ADLayout))
                    {
                        layoutSerializer.Deserialize(stringReader);
                    }

                    // Find and assign docGroup
                    List<LayoutDocumentPane> documentPanes = FindAllDocumentPanes(staging.Layout);
                    if (documentPanes.Count > 0)
                    {
                        docGroup = documentPanes[0];
                    }
                    else
                    {
                        Log.Write("WARNING: No document panes found in deserialized layout!");
                    }

                    LayoutRoot prepared = staging.Layout;

                    // Отсоединяем раскладку от невидимого staging-менеджера: иначе LayoutItem-ы и
                    // логические дочерние элементы останутся привязаны к нему, и перенос в реальный
                    // dockManager упадёт с «уже является логическим дочерним элементом».
                    staging.Layout = new LayoutRoot { RootPanel = new LayoutPanel() };

                    return prepared;

                }
                catch (Exception ex)
                {
                    // If deserialization fails, fall back to default layout
                    Log.Write($"Failed to load saved layout: {ex}. Using default layout.");
                }
            }
            else
            {
                Log.Write("No saved layout found (ADLayout is empty), creating default layout");
            }

            // Create default layout
            return CreateDefaultLayout();

        }

        /// <summary>
        /// Отложенная инициализация тяжёлых панелей и подготовка раскладки (п. 3.3): вызывается в
        /// <see cref="MainView_Loaded"/> до создания <see cref="DocGroupHelper"/>, чтобы <c>docGroup</c>
        /// был финализирован до восстановления вкладок.
        /// </summary>
        private void EnsureShellInitialized()
        {
            if (_shellInitialized)
            {
                return;
            }
            _shellInitialized = true;

            InitializeControls();
            StartupTimer.Mark("MainView: InitializeControls()");

            _preparedLayoutRoot = PrepareLayout();
            StartupTimer.Mark("MainView: PrepareLayout()");

            // Подъём доков откладывается до первого кадра: пустой DockingManager уже отрисован,
            // а тяжёлые панели и раскладка добавляются следующим циклом диспетчера.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(AttachPreparedLayout));
        }

        /// <summary>
        /// Поднимает подготовленную раскладку в DockingManager после первого кадра и запускает
        /// пересборку палитры «Активности» (она тоже асинхронна).
        /// </summary>
        private void AttachPreparedLayout()
        {
            if (_preparedLayoutRoot != null)
            {
                dockManager.Layout = _preparedLayoutRoot;
                _preparedLayoutRoot = null;
            }

            InitializeLayoutAutoSave();
            StartupTimer.Mark("MainView: раскладка поднята после первого кадра");

            RefreshActivityPalette();
        }


        private List<LayoutDocumentPane> FindAllDocumentPanes(LayoutRoot layoutRoot)
        {
            List<LayoutDocumentPane> result = new List<LayoutDocumentPane>();

            void SearchDocumentPanes(ILayoutElement element)
            {
                if (element is LayoutDocumentPane documentPane)
                {
                    result.Add(documentPane);
                }
                else if (element is ILayoutContainer container)
                {
                    foreach (ILayoutElement child in container.Children)
                    {
                        SearchDocumentPanes(child);
                    }
                }
            }

            if (layoutRoot?.RootPanel != null)
            {
                SearchDocumentPanes(layoutRoot.RootPanel);
            }

            return result;
        }

        private LayoutRoot CreateDefaultLayout()
        {
            Log.Write("Creating default layout");

            // Create LayoutRoot
            LayoutRoot layoutRoot = new LayoutRoot();

            // Create main horizontal panel
            LayoutPanel layoutPanel = new LayoutPanel { Orientation = Orientation.Horizontal };

            // Left panel - LayoutAnchorablePaneGroup
            LayoutAnchorablePaneGroup leftLayoutAnchorablePaneGroup = new LayoutAnchorablePaneGroup
            {
                Orientation = Orientation.Vertical,
                DockWidth = new GridLength(0.5, GridUnitType.Star),
                DockMinWidth = 350
            };

            LayoutAnchorablePane leftPane = new LayoutAnchorablePane();

            // Project anchorable
            LayoutAnchorable projectAnchorable = new LayoutAnchorable
            {
                ContentId = "ProjectManager",
                Title = FindResource("lang_main_project") as string,
                AutoHideMinWidth = 350,
                CanClose = false,
                CanHide = false,
                Content = wfProjectBorder
            };
            leftPane.Children.Add(projectAnchorable);

            // Activities anchorable
            LayoutAnchorable activitiesAnchorable = new LayoutAnchorable
            {
                ContentId = "Activities",
                Title = FindResource("lang_main_activities") as string,
                AutoHideMinWidth = 350,
                CanClose = false,
                CanHide = false,
                Content = WfToolboxBorder
            };
            leftPane.Children.Add(activitiesAnchorable);

            // Outline anchorable
            LayoutAnchorable outlineAnchorable = new LayoutAnchorable
            {
                ContentId = "Outline",
                Title = FindResource("lang_main_outline") as string,
                AutoHideMinWidth = 350,
                CanClose = false,
                CanHide = false,
                Content = WfOutlineViewBorder
            };
            leftPane.Children.Add(outlineAnchorable);

            leftLayoutAnchorablePaneGroup.Children.Add(leftPane);
            layoutPanel.Children.Add(leftLayoutAnchorablePaneGroup);

            // Center - LayoutDocumentPaneGroup
            LayoutDocumentPaneGroup documentPaneGroup = new LayoutDocumentPaneGroup();
            documentPaneGroup.Children.Add(docGroup);
            layoutPanel.Children.Add(documentPaneGroup);

            // Right panel - LayoutAnchorablePaneGroup
            LayoutAnchorablePaneGroup rightLayoutAnchorablePaneGroup = new LayoutAnchorablePaneGroup
            {
                Orientation = Orientation.Vertical,
                DockWidth = new GridLength(600)
            };

            // Properties pane
            LayoutAnchorablePane propertiesPane = new LayoutAnchorablePane();
            LayoutAnchorable propertiesAnchorable = new LayoutAnchorable
            {
                ContentId = "Properties",
                Title = FindResource("lang_main_activity_properties") as string,
                CanClose = false,
                CanHide = false,
                Content = WfPropertyBorder
            };
            propertiesPane.Children.Add(propertiesAnchorable);
            rightLayoutAnchorablePaneGroup.Children.Add(propertiesPane);

            // Log pane
            LayoutAnchorablePane logPane = new LayoutAnchorablePane();
            LayoutAnchorable logAnchorable = new LayoutAnchorable
            {
                ContentId = "Log",
                Title = FindResource("lang_main_log") as string,
                CanClose = false,
                CanHide = false,
                Content = LogViewerControl
            };
            logPane.Children.Add(logAnchorable);
            rightLayoutAnchorablePaneGroup.Children.Add(logPane);

            // Chat pane
            LayoutAnchorablePane chatPane = new LayoutAnchorablePane();
            LayoutAnchorable chatAnchorable = new LayoutAnchorable
            {
                ContentId = "Chat",
                Title = "Chat",
                CanClose = false,
                CanHide = false,
                Content = chatPanel
            };
            chatPane.Children.Add(chatAnchorable);
            rightLayoutAnchorablePaneGroup.Children.Add(chatPane);

            layoutPanel.Children.Add(rightLayoutAnchorablePaneGroup);

            // Set the layout
            layoutRoot.RootPanel = layoutPanel;
            Log.Write("Default layout created successfully");

            return layoutRoot;
        }

        public void OnSendMessage(string message)
        {
            ErrorDialog.Show(message, this);
        }

        private void NuGetPanelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowNuGetPanel();
        }

        private void CheckActivityCompatibilityMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = LocalizeHelper.Get("lang_main_check_activity_compat"),
                Filter = "C# project (*.csproj)|*.csproj|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(dialog.FileName);
                ProjectCompatibilityView view = new ProjectCompatibilityView();
                view.SetReport(report);

                Window owner = Application.Current?.MainWindow;
                if (owner != null && owner.IsLoaded && owner.IsVisible)
                {
                    view.Owner = owner;
                    view.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }

                _ = view.ShowDialog();
            }
            catch (Exception ex)
            {
                ErrorDialog.ShowException(ex, "Проверка совместимости проекта", this);
            }
        }

        /// <summary>Сброс всех кэшей оптимизаций (палитра, NuGet-индекс и мемоизация) из меню «Настройки».</summary>
        private void ResetCachesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            RPA.Core.Services.StudioCacheReset.ResetAll();
            nuGetPanel?.ViewModel?.ResetCaches();
            RefreshActivityPalette();
        }

        /// <summary>Пункт «Настройки → Стартовый скрипт…»: тематизированный диалог ввода имени файла.</summary>
        private void StartScriptMenuItem_Click(object sender, RoutedEventArgs e)
        {
            StartScriptView view = new StartScriptView
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            _ = view.ShowDialog();
        }

        private void ShowNuGetPanel()
        {
            if (nuGetPanel == null)
            {
                return;
            }

            // Панель открывается вкладкой в панели документов: там широкая область, в которой
            // двухколоночный макет менеджера пакетов читается. Если пользователь утащил вкладку
            // в боковой док, второй экземпляр не создаётся — находится по ContentId.
            LayoutContent opened = FindNuGetContent();
            if (opened != null)
            {
                if (!ReferenceEquals(opened.Content, nuGetPanel))
                {
                    opened.Content = nuGetPanel;
                }

                opened.IsSelected = true;
                opened.IsActive = true;
                nuGetContent = opened;
                return;
            }

            LayoutDocument document = FindNuGetDocument();
            if (document == null)
            {
                document = new LayoutDocument
                {
                    ContentId = NuGetContentId,
                    Title = "NuGet",
                    Content = nuGetPanel
                };
                document.Closed += NuGetDocument_Closed;
                docGroup.Children.Add(document);
            }
            else
            {
                document.Content = nuGetPanel;
            }

            document.IsSelected = true;
            document.IsActive = true;
            nuGetContent = document;
        }

        /// <summary>Вкладка NuGet, если она уже открыта (содержимое панели при этом переиспользуется).</summary>
        private LayoutDocument FindNuGetDocument()
        {
            if (nuGetContent is LayoutDocument current && docGroup.Children.Contains(current))
            {
                return current;
            }

            LayoutDocument existing = docGroup.Children
                .OfType<LayoutDocument>()
                .FirstOrDefault(document => document.ContentId == NuGetContentId);

            if (existing != null)
            {
                existing.Closed -= NuGetDocument_Closed;
                existing.Closed += NuGetDocument_Closed;
            }

            return existing;
        }

        /// <summary>Открытая панель NuGet в любом месте раскладки — вкладка документа или боковой док.</summary>
        private LayoutContent FindNuGetContent()
        {
            return dockManager.Layout?.Descendents()
                .OfType<LayoutContent>()
                .FirstOrDefault(content => content.ContentId == NuGetContentId);
        }

        private void NuGetDocument_Closed(object sender, EventArgs e)
        {
            if (sender is LayoutDocument document)
            {
                document.Closed -= NuGetDocument_Closed;
            }

            if (ReferenceEquals(nuGetContent, sender))
            {
                nuGetContent = null;
            }
        }


        /// <summary>
        /// Показывает панель NuGet для указанного проекта (пункт «Добавить зависимость»).
        /// В параметре может прийти папка или файл проекта (.csproj) — панель принимает папку.
        /// </summary>
        public void ShowNuGetPanelFor(string projectPath)
        {
            string projectFolder = ProjectPathResolver.GetProjectFolder(projectPath);
            if (nuGetPanel != null && !string.IsNullOrWhiteSpace(projectFolder))
            {
                _ = nuGetPanel.SetProjectFolder(projectFolder);
            }

            ShowNuGetPanel();
        }

        private void RemoveDesigner(string loadedFile)
        {
            ((BaseViewModel)DataContext).Cmd.Execute(new List<object> { "CmdRemoveDesigner", loadedFile });
        }

        private void SetActiveDesigner(string loadedFile)
        {
            ((BaseViewModel)DataContext).Cmd.Execute(new List<object> { "CmdSetActiveDesigner", loadedFile });
        }

        private void MainView_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyMaximizeMargin();
            // Создаём панели и готовим раскладку ДО создания DocGroupHelper: он фиксирует ссылку на
            // docGroup, а docGroup окончательно определяется при десериализации/построении раскладки.
            EnsureShellInitialized();


            // Панель документов: закрытие вкладки активирует соседнюю (дизайнер или редактор кода).
            tabsCloseCoordinator = new DocumentTabsCloseCoordinator(docGroup);

            docGroupHelper = new DocGroupHelper(docGroup, RemoveDesigner, SyncWFControls, (MainViewModel)DataContext, SetActiveDesigner);
            WorkflowDesigners.CollectionChanged += docGroupHelper.WorkflowDesigners_CollectionChanged;
            // Подписка идёт после DocGroupHelper: соседняя вкладка активируется уже после того,
            // как модель дизайнеров убрала документ и выбрала вкладку по-своему.
            WorkflowDesigners.CollectionChanged += WorkflowDesigners_CollectionChanged_ActivateNeighbour;
            ((MainViewModel)DataContext).OpenCodeDocuments.CollectionChanged += OpenCodeDocuments_CollectionChanged;
        }

        /// <summary>
        /// Вкладку дизайнера закрыли: модель уже убрала документ (и могла выбрать вкладку по-своему) —
        /// активируем соседнюю вкладку по фактическому порядку панели документов.
        /// </summary>
        private void WorkflowDesigners_CollectionChanged_ActivateNeighbour(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove
                || e.Action == NotifyCollectionChangedAction.Reset)
            {
                _ = tabsCloseCoordinator?.ApplyPendingActivation();
            }
        }

        /// <summary>
        /// Путь активной вкладки панели документов (дизайнер или редактор кода/XAML) — то, что видит
        /// пользователь. По нему <c>Main.SaveSessionState</c> запоминает активную вкладку сессии.
        /// </summary>
        internal string GetActiveDocumentPath()
        {
            if (docGroup == null)
            {
                return null;
            }

            return DocumentTabsCloseCoordinator.PathOf(
                DocumentTabsCloseCoordinator.FindActive(docGroup.Children.OfType<LayoutDocument>()));
        }

        private void OpenCodeDocuments_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (CodeDocument document in e.NewItems)
                {
                    AddCodeDocumentTab(document);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (CodeDocument document in e.OldItems)
                {
                    CloseCodeDocumentTab(document.FilePath);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (LayoutDocument document in docGroup.Children.OfType<LayoutDocument>()
                    .Where(d => d.Content is CodeEditorView).ToList())
                {
                    document.Close();
                }
            }
        }

        private void AddCodeDocumentTab(CodeDocument document)
        {
            CodeEditorView view = new CodeEditorView();
            view.LoadFile(document.FilePath);

            LayoutDocument layoutDocument = new LayoutDocument
            {
                Content = view,
                ContentId = "code:" + document.FilePath,
                Title = document.Title
            };

            MultiBinding titleBinding = new MultiBinding
            {
                Converter = new DocumentTitleConverter()
            };
            titleBinding.Bindings.Add(new Binding { Source = document.Title });
            titleBinding.Bindings.Add(new Binding("IsDirty") { Source = document });
            _ = BindingOperations.SetBinding(layoutDocument, LayoutContent.TitleProperty, titleBinding);

            layoutDocument.Closed += (s, e) =>
            {
                ((BaseViewModel)DataContext).Cmd.Execute(new List<object> { "CmdCloseCodeDocument", document.FilePath });
            };

            docGroup.Children.Add(layoutDocument);
            layoutDocument.IsSelected = true;
            layoutDocument.IsActive = true;
        }

        private void CloseCodeDocumentTab(string filePath)
        {
            LayoutDocument document = docGroup.Children.OfType<LayoutDocument>()
                .FirstOrDefault(d => d.ContentId == "code:" + filePath);
            document?.Close();
        }

        public void ActivateCodeDocument(string filePath)
        {
            LayoutDocument document = docGroup.Children.OfType<LayoutDocument>()
                .FirstOrDefault(d => d.ContentId == "code:" + filePath);
            if (document != null)
            {
                document.IsActive = true;
                document.IsSelected = true;
            }
        }

        private void InitializeLayoutAutoSave()
        {
            // Initialize debounce timer for auto-saving layout
            _layoutSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;

            // Subscribe to layout change events
            dockManager.LayoutChanged += DockManager_LayoutChanged;
        }

        private void DockManager_LayoutChanged(object sender, EventArgs e)
        {
            // Откреплённая панель получает собственную копию темы Metro, где вкладки объявлены
            // светлыми — переносим на неё (и на сам DockingManager) текущую палитру.
            Services.AppSettings.ReapplyDockManagerResources();

            // Reset and restart timer on each layout change (debounce pattern)
            _layoutSaveTimer.Stop();
            _layoutSaveTimer.Start();
        }

        private void LayoutSaveTimer_Tick(object sender, EventArgs e)
        {
            // Timer elapsed - user stopped making changes for 2 seconds
            _layoutSaveTimer.Stop();
            SaveWorkspaceLayout();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Состояние вкладок (какие документы были открыты и какая вкладка активна): при следующем
            // запуске студия их восстанавливает. Финальная запись — даже если пользователь закрыл окно
            // сразу после открытия/закрытия вкладки.
            if ((DataContext as MainViewModel)?.Model is RPA.Models.Main mainModel)
            {
                mainModel.SaveSessionState();
            }

            // Save layout one final time before closing
            if (_layoutSaveTimer != null)
            {
                _layoutSaveTimer.Stop();
                SaveWorkspaceLayout();
            }

            // Cleanup
            if (_layoutSaveTimer != null)
            {
                _layoutSaveTimer.Tick -= LayoutSaveTimer_Tick;
                _layoutSaveTimer = null;
            }

            if (dockManager != null)
            {
                dockManager.LayoutChanged -= DockManager_LayoutChanged;
            }

            base.OnClosing(e);
        }

        private void SyncWFControls(WorkflowDesigner workflowDesigner)
        {
            WfPropertyBorder.Child = workflowDesigner?.PropertyInspectorView;
            WfOutlineViewBorder.Child = workflowDesigner?.OutlineView;
            WfVisualTreeThemer.ApplyWhenLoaded(workflowDesigner?.OutlineView as FrameworkElement);

            if (WfPropertyBorder.Child is UIElement propertyInspector)
            {
                _propertyStyleHelper.SetPropertyStyles(propertyInspector);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DesignerView designerView = ((MainViewModel)DataContext).ActiveWorkflowDesigner.Context.Services.GetService<DesignerView>();

            designerView.WorkflowShellBarItemVisibility =
                ShellBarItemVisibility.Imports |
                ShellBarItemVisibility.MiniMap |
                ShellBarItemVisibility.Variables |
                ShellBarItemVisibility.Arguments |
                ShellBarItemVisibility.Zoom |
                ShellBarItemVisibility.PanMode;
        }

        private void ListView_SourceUpdated(object sender, DataTransferEventArgs e)
        {
            ListView listView = sender as ListView;
            if (listView.Items.Count > 0)
            {
                object item = listView.Items[listView.Items.Count - 1];
                listView.ScrollIntoView(item);
            }

        }

        private void SaveWorkspaceLayoutToStringCommand_Click(object sender, RoutedEventArgs e)
        {
            SaveWorkspaceLayout();
        }

        private void SaveWorkspaceLayout()
        {
            try
            {
                if (_resetLayout)
                {
                    return;
                }

                XmlLayoutSerializer layoutSerializer = new XmlLayoutSerializer(dockManager);

                using StringWriter stringWriter = new StringWriter();
                layoutSerializer.Serialize(stringWriter);
                string layoutXml = stringWriter.ToString();
                Properties.Settings.Default.ADLayout = layoutXml;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                ErrorDialog.ShowException(ex, "Failed to save workspace layout", this);
            }
        }

        private void RestWorkspaceLayout_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ADLayout = null;
            Properties.Settings.Default.Save();
            _resetLayout = true;
            _ = MessageBox.Show("Please reboot application for skip layout settings");
        }
    }
}
