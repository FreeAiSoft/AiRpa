using System;
using System.Activities.Presentation;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using FW.EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using RPA.Controls;
using RPA.Core;
using RPA.Helpers;
using RPA.Logging;
using RPA.ViewModels;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : MetroWindow, IView
    {
        private ObservableCollection<WorkflowDesignerEx> WorkflowDesigners => ((MainViewModel)DataContext).WorkflowDesigners;

        private ToolboxPanel _wfToolbox;
        private DocGroupHelper docGroupHelper;
        private Border WfToolboxBorder;
        private Border WfOutlineViewBorder;
        private Border WfPropertyBorder;
        private LayoutDocumentPane docGroup;
        public LogControlLibrary.Controls.LogViewerControl LogViewerControl { get; private set; }
        private ProjectManagerControl projectManagerControl;
        private Border wfProjectBorder;
        private DispatcherTimer _layoutSaveTimer;
        private bool _resetLayout;

        public MainView()
        {
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
                    Log.Write($"Failed to load saved layout: {ex.Message}. Stack trace: {ex.StackTrace}. Using default layout.");
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

            layoutPanel.Children.Add(rightLayoutAnchorablePaneGroup);

            // Set the layout
            layoutRoot.RootPanel = layoutPanel;
            dockManager.Layout = layoutRoot;

            Log.Write("Default layout created successfully");
        }

        public async void OnSendMessage(string message)
        {
            _ = await this.ShowMessageAsync("", message);
        }

        private void MenuItem_UpdateTheme_Click(object sender, RoutedEventArgs e)
        {
            dockManager.FloatingWindows.ToList().ForEach(floatingWindow =>
            {
                floatingWindow.UpdateLayout();
                floatingWindow.OnApplyTemplate();
                floatingWindow.UpdateDefaultStyle();
            });
        }

        private void RemoveDesigner(string loadedFile)
        {
            ((BaseViewModel)DataContext).Cmd.Execute(new List<object> { "CmdRemoveDesigner", loadedFile });
        }

        private void SetActiveDesigner(string loadedFile)
        {
            ((BaseViewModel)DataContext).Cmd.Execute(new List<object> { "CmdSetActiveDesigner", loadedFile });
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            docGroupHelper = new DocGroupHelper(docGroup, RemoveDesigner, SyncWFControls, (MainViewModel)DataContext, SetActiveDesigner);
            WorkflowDesigners.CollectionChanged += docGroupHelper.WorkflowDesigners_CollectionChanged;
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
            // Reset and restart timer on each layout change (debounce pattern)
            _layoutSaveTimer.Stop();
            _layoutSaveTimer.Start();
        }

        private async void LayoutSaveTimer_Tick(object sender, EventArgs e)
        {
            // Timer elapsed - user stopped making changes for 2 seconds
            _layoutSaveTimer.Stop();
            await SaveWorkspaceLayout();
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            // Save layout one final time before closing
            if (_layoutSaveTimer != null)
            {
                _layoutSaveTimer.Stop();
                await SaveWorkspaceLayout();
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

        private async void SaveWorkspaceLayoutToStringCommand_Click(object sender, RoutedEventArgs e)
        {
            await SaveWorkspaceLayout();
        }

        private async Task SaveWorkspaceLayout()
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
                _ = await this.ShowMessageAsync("Error",
                    $"Failed to save workspace layout: {ex.Message}",
                    MessageDialogStyle.Affirmative);
            }
        }

        private async void RestWorkspaceLayout_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ADLayout = null;
            Properties.Settings.Default.Save();
            _resetLayout = true;
            _ = await this.ShowMessageAsync("", "Please reboot application for skip layout settings",
                    MessageDialogStyle.Affirmative);
        }
    }
}
