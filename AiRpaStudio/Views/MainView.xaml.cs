using System;
using System.Activities.Presentation;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using FW.EasyMvvm.Core;
using Material.Icons;
using RPA.Controls;
using RPA.Converters;
using RPA.Core;
using RPA.Helpers;
using RPA.Logging;
using RPA.Models;
using RPA.ViewModels;

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
        public LogControlLibrary.Controls.LogViewerControl LogViewerControl { get; private set; }
        private ProjectManagerControl projectManagerControl;
        private Border wfProjectBorder;
        private Chat.Views.ChatPanel chatPanel;
        private NuGet.Views.NuGetPanel nuGetPanel;
        private LayoutAnchorable nuGetAnchorable;
        private DispatcherTimer _layoutSaveTimer;
        private bool _resetLayout;

        public MainView()
        {
            StudioBrushes.ApplyToApplication();
            StudioFonts.ApplyToApplication();
            InitializeComponent();
            Services.AppSettings.SetDockingManager(dockManager);// DockingManager можно отловить только здесь, такие дела

            InitializeControls();
            InitializeLayoutRoot();
            InitializeLayoutAutoSave();
        }

        private void InitializeControls()
        {
            // Initialize named borders and controls
            WfToolboxBorder = new Border();
            WfOutlineViewBorder = new Border { Margin = new Thickness(0, 5, 0, 5) };
            WfPropertyBorder = new Border { BorderThickness = new Thickness(0) };
            LogViewerControl = new LogControlLibrary.Controls.LogViewerControl();
            docGroup = new LayoutDocumentPane();
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
            // Re-detect the .csproj whenever the current studio project folder changes.
            ProjectManager.GetInstance().PropertyChanged += OnProjectManagerPropertyChanged;
            _ = nuGetPanel.SetProjectFolder(ProjectManager.GetInstance().ProjectFolder);
        }

        private void OnProjectManagerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProjectManager.ProjectFolder))
            {
                _ = nuGetPanel.SetProjectFolder(ProjectManager.GetInstance().ProjectFolder);
            }
        }

        private void OnChatThemeChanged()
        {
            // Палитра Studio.* пересобрана в ChatViewModel.ThemeApplying — до того, как контрол сменил
            // свой словарь. Здесь остаётся перекраска открытых документов и панелей; повторный
            // ApplyTheme дал бы второй полный прогон темы на каждый клик.
            ReapplyWorkflowControlThemes();
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

            _wfToolbox = new WFToolboxHelper().Build();
            WfToolboxBorder.Child = _wfToolbox;
            _wfToolbox.ApplyTheme();
            Log.Write("Палитра «Активности» пересобрана: появилась новая библиотека активностей.");
        }
        private void InitializeLayoutRoot()
        {
            // Try to load layout from saved settings
            if (!string.IsNullOrEmpty(Properties.Settings.Default.ADLayout))
            {
                try
                {
                    XmlLayoutSerializer layoutSerializer = new XmlLayoutSerializer(dockManager);

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
                        else if (contentId == "NuGet")
                        {
                            args.Content = nuGetPanel;
                            nuGetAnchorable = args.Model as LayoutAnchorable;
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
                    List<LayoutDocumentPane> documentPanes = FindAllDocumentPanes(dockManager.Layout);
                    if (documentPanes.Count > 0)
                    {
                        docGroup = documentPanes[0];
                    }
                    else
                    {
                        Log.Write("WARNING: No document panes found in deserialized layout!");
                    }

                    _wfToolbox = new WFToolboxHelper().Build();
                    WfToolboxBorder.Child = _wfToolbox;

                    return;
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
            CreateDefaultLayout();

            _wfToolbox = new WFToolboxHelper().Build();
            WfToolboxBorder.Child = _wfToolbox;
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

        private void CreateDefaultLayout()
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

            // NuGet pane
            LayoutAnchorablePane nuGetPane = new LayoutAnchorablePane();
            LayoutAnchorable nuGetPanelAnchorable = new LayoutAnchorable
            {
                ContentId = "NuGet",
                Title = "NuGet",
                CanClose = false,
                CanHide = false,
                Content = nuGetPanel
            };
            nuGetPane.Children.Add(nuGetPanelAnchorable);
            nuGetAnchorable = nuGetPanelAnchorable;
            rightLayoutAnchorablePaneGroup.Children.Add(nuGetPane);

            layoutPanel.Children.Add(rightLayoutAnchorablePaneGroup);

            // Set the layout
            layoutRoot.RootPanel = layoutPanel;
            dockManager.Layout = layoutRoot;

            Log.Write("Default layout created successfully");
        }

        public void OnSendMessage(string message)
        {
            ErrorDialog.Show(message, this);
        }

        private void NuGetPanelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowNuGetPanel();
        }

        private void ShowNuGetPanel()
        {
            if (nuGetAnchorable != null)
            {
                nuGetAnchorable.Show();
                nuGetAnchorable.IsActive = true;
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

            docGroupHelper = new DocGroupHelper(docGroup, RemoveDesigner, SyncWFControls, (MainViewModel)DataContext, SetActiveDesigner);
            WorkflowDesigners.CollectionChanged += docGroupHelper.WorkflowDesigners_CollectionChanged;
            ((MainViewModel)DataContext).OpenCodeDocuments.CollectionChanged += OpenCodeDocuments_CollectionChanged;
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
