using System;
using System.Collections.Generic;
using System.Activities.Presentation;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AvalonDock.Layout;
using RPA.Converters;
using RPA.Core;
using RPA.ViewModels;
using RPA.Views;

namespace RPA.Helpers
{
    internal class DocGroupHelper
    {
        private readonly LayoutDocumentPane docGroup;
        private readonly Action<string> removeDesignerDelegate;
        private readonly Action<WorkflowDesigner> syncWFControlsDelegate;
        private readonly MainViewModel _mainViewModel;
        private readonly Action<string> setActiveDesignerDelegate;
        private readonly Action activeDocumentChangedDelegate;
        private readonly Func<string, LayoutDocument> _takeReplaceableCodeTab;
        private readonly PropertyWindowStyleHelper _propertyStyleHelper = new PropertyWindowStyleHelper();
        private readonly Dictionary<string, DesignerErrorBanner> _errorBanners =
            new Dictionary<string, DesignerErrorBanner>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DesignerErrorSnapshot> _pendingErrors =
            new Dictionary<string, DesignerErrorSnapshot>(StringComparer.OrdinalIgnoreCase);



        #region Singleton

        public DocGroupHelper(LayoutDocumentPane docGroup, Action<string> removeDesigner, Action<WorkflowDesigner> syncWFControls, MainViewModel mainViewModel, Action<string> setActiveDesigner, Action activeDocumentChanged, Func<string, LayoutDocument> takeReplaceableCodeTab = null)
        {
            this.docGroup = docGroup;
            removeDesignerDelegate = removeDesigner;
            syncWFControlsDelegate = syncWFControls;
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += DataContext_PropertyChanged;
            setActiveDesignerDelegate = setActiveDesigner;
            activeDocumentChangedDelegate = activeDocumentChanged;
            _takeReplaceableCodeTab = takeReplaceableCodeTab;
            instance = this;
        }

        private static DocGroupHelper instance;

        public static DocGroupHelper GetInstance()
        {
            return instance;
        }

        #endregion Singleton

        internal void AddDocument(WorkflowDesigner workflowDesigner)
        {
            FrameworkElement workflowDesignerView = (FrameworkElement)workflowDesigner.View;
            ((FrameworkElement)workflowDesigner.PropertyInspectorView).Loaded += WorkflowDesignerView_Loaded;
            // Поверхность дизайнера (F-04) темизируется после загрузки: шаблон DesignerView
            // строится только к этому моменту.
            workflowDesignerView.Loaded += DesignerView_Loaded;
            string fullFilePath = ((WorkflowFileItem)workflowDesigner.Context.Items.First(i => i.ItemType == typeof(WorkflowFileItem))).LoadedFile;
            string fileName = Path.GetFileName(fullFilePath);
            workflowDesignerView.Tag = fullFilePath;

            // Баннер ошибки загрузки размещается в верхней строке вкладки и сдвигает дизайнер вниз
            // (при обычном состоянии баннер скрыт). Tag для распознавания вкладки дизайнера теперь на
            // обёртке, а не на самой поверхности дизайнера.
            var errorBanner = new DesignerErrorBanner { Visibility = Visibility.Collapsed };
            _errorBanners[fullFilePath] = errorBanner;
            if (_pendingErrors.TryGetValue(fullFilePath, out DesignerErrorSnapshot pending))
            {
                errorBanner.Show(pending);
                _pendingErrors.Remove(fullFilePath);
            }


            var host = new Grid();
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(errorBanner, 0);
            Grid.SetRow(workflowDesignerView, 1);
            host.Children.Add(workflowDesignerView);
            host.Children.Add(errorBanner);
            host.Tag = fullFilePath;


            LayoutDocument docDocument = _takeReplaceableCodeTab?.Invoke(fullFilePath);
            bool reusedCodeTab = docDocument != null;

            if (docDocument == null)
            {
                docDocument = new LayoutDocument
                {
                    Content = host
                };
            }
            else
            {
                // Вкладка редактора кода того же файла: дизайнер показывается в том же LayoutDocument.
                docDocument.Content = host;
                docDocument.ContentId = null;
            }

            // Setup MultiBinding for document title: fileName + IsEdited property
            if (workflowDesigner is WorkflowDesignerEx workflowDesignerEx)
            {
                MultiBinding multiBinding = new MultiBinding
                {
                    Converter = new DocumentTitleConverter()
                };

                // Binding to static fileName value
                Binding fileNameBinding = new Binding
                {
                    Source = fileName
                };
                multiBinding.Bindings.Add(fileNameBinding);

                // Binding to WorkflowDesignerEx.IsEdited property
                Binding isEditedBinding = new Binding("IsEdited")
                {
                    Source = workflowDesignerEx
                };
                multiBinding.Bindings.Add(isEditedBinding);

                // Apply binding to LayoutDocument.Title
                _ = BindingOperations.SetBinding(docDocument, LayoutContent.TitleProperty, multiBinding);
            }
            else
            {
                // Fallback if not WorkflowDesignerEx
                docDocument.Title = fileName;
            }

            if (!reusedCodeTab)
            {
                docGroup.Children.Add(docDocument);
            }
            docDocument.IsSelected = true;
            docDocument.Closed += DocClosed;
            docDocument.IsSelectedChanged += DocDocument_IsSelectedChanged;
        }

        private void WorkflowDesignerView_Loaded(object sender, RoutedEventArgs e)
        {
            _propertyStyleHelper.SetPropertyStyles((UIElement)sender);
        }

        private void DesignerView_Loaded(object sender, RoutedEventArgs e)
        {
            // Поверхность дизайнера: shell bar, breadcrumb, MiniMap и адаптеры ошибок (F-04/F-06).
            if (sender is FrameworkElement designerView)
            {
                WfDesignerTheme.Apply(designerView);
            }
        }

        private void DocDocument_IsSelectedChanged(object sender, EventArgs e)
        {
            // Событие приходит и при снятии выбора (IsSelected = false), а в панели лежат не только
            // вкладки дизайнера, но и редакторы кода/XAML (MainView.AddCodeDocumentTab) — у них Tag == null.
            if (!(sender is LayoutDocument document) || !document.IsSelected)
            {
                return;
            }

            // Активная вкладка сменилась (дизайнер или редактор кода): «Сохранить» пересчитывается,
            // потому что активной может быть вкладка кода/XAML.
            activeDocumentChangedDelegate?.Invoke();

            // Признак вкладки дизайнера — путь к файлу активности в Tag (проставляется в AddDocument).
            if (!(document.Content is FrameworkElement view) || !(view.Tag is string loadedFile) || loadedFile.Length == 0)
            {
                return;
            }

            // Активного дизайнера может ещё/уже не быть (CustomWfDesigner.LoadProject, закрытие
            // последней вкладки), поэтому обращаемся к нему только через null-условный доступ.
            FrameworkElement activeView = _mainViewModel.ActiveWorkflowDesigner?.View as FrameworkElement;
            if (ReferenceEquals(activeView, view) || (activeView?.Tag is string activeFile && activeFile == loadedFile))
            {
                return;
            }

            setActiveDesignerDelegate(loadedFile);
        }

        private void DocClosed(object sender, EventArgs e)
        {
            // Закрываться может и вкладка редактора кода (Content без Tag) — такие пропускаем.
            if (sender is LayoutDocument document
                && document.Content is FrameworkElement view
                && view.Tag is string loadedFile)
            {
                _errorBanners.Remove(loadedFile);
                removeDesignerDelegate(loadedFile);
            }

            GC.Collect();
        }

        internal void SetSelectedTab(WorkflowDesigner activeWorkflowDesigner)
        {
            if (docGroup.Children.Count == 0)
            {
                return;
            }

            if (activeWorkflowDesigner == null)
            {
                return;
            }

            syncWFControlsDelegate(activeWorkflowDesigner);

            string activePath = (activeWorkflowDesigner.View as FrameworkElement)?.Tag as string;
            LayoutDocument doc = (LayoutDocument)docGroup.Children.FirstOrDefault(c =>
                c.Content is FrameworkElement view && view.Tag is string tag && tag == activePath);
            if (doc != null)
            {
                doc.IsActive = true;
            }
        }

        /// <summary>
        /// Показывает баннер ошибки загрузки в верхней строке вкладки дизайнера. Если вкладка
        /// ещё не создана (ошибка при открытии), баннер появится сразу после создания вкладки.
        /// </summary>
        internal void ShowDesignerError(string filePath, DesignerErrorSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(filePath) || snapshot == null)
            {
                return;
            }

            if (_errorBanners.TryGetValue(filePath, out DesignerErrorBanner banner))
            {
                banner.Show(snapshot);
            }
            else
            {
                _pendingErrors[filePath] = snapshot;
            }

        }

        public void WorkflowDesigners_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                WorkflowDesigner workflowDesigner = (WorkflowDesigner)e.NewItems[0];
                AddDocument(workflowDesigner);
                syncWFControlsDelegate(workflowDesigner);
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                syncWFControlsDelegate(_mainViewModel.ActiveWorkflowDesigner);
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _errorBanners.Clear();
                _pendingErrors.Clear();
                docGroup.Children.Clear();
                syncWFControlsDelegate(null);
            }
            else if (e.Action == NotifyCollectionChangedAction.Move)
            {
                ReorderDesignerTabsToModelOrder();
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Reorders designer document tabs to match WorkflowDesigners order. Code/XAML editor
        /// tabs keep their slots: session restore never reorders them.
        /// </summary>
        private void ReorderDesignerTabsToModelOrder()
        {
            List<int> slots = new List<int>();
            List<LayoutDocument> designerTabs = new List<LayoutDocument>();

            for (int i = 0; i < docGroup.Children.Count; i++)
            {
                if (docGroup.Children[i] is LayoutDocument document && IsDesignerTab(document))
                {
                    slots.Add(i);
                    designerTabs.Add(document);
                }
            }

            if (designerTabs.Count < 2)
            {
                return;
            }

            Dictionary<string, int> modelOrder = _mainViewModel.WorkflowDesigners
                .Select((designer, index) => new { designer, index })
                .ToDictionary(item => (item.designer.View as FrameworkElement)?.Tag as string, item => item.index);

            List<LayoutDocument> ordered = designerTabs
                .OrderBy(document => modelOrder.TryGetValue((document.Content as FrameworkElement)?.Tag as string, out int index) ? index : int.MaxValue)
                .ToList();

            // Detach designer tabs, then put them back in model order at the same slots so that
            // interleaved code-editor tabs stay put.
            for (int i = 0; i < designerTabs.Count; i++)
            {
                docGroup.Children.Remove(designerTabs[i]);
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                docGroup.Children.Insert(slots[i], ordered[i]);
            }
        }

        private static bool IsDesignerTab(LayoutDocument document)
        {
            return document != null
                && document.Content is FrameworkElement view
                && view.Tag is string tag
                && tag.Length > 0;
        }

        private void DataContext_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_mainViewModel.ActiveWorkflowDesigner))
            {
                SetSelectedTab(_mainViewModel.ActiveWorkflowDesigner);
                syncWFControlsDelegate(_mainViewModel.ActiveWorkflowDesigner);
            }
        }

        /// <summary>
        /// Отписывает вкладку дизайнера от её обработчиков. Нужно перед заменой «дизайнер → код»,
        /// чтобы обработчики не накапливались при многократном переключении вида.
        /// </summary>
        internal void DetachDesignerTabHandlers(LayoutDocument document)
        {
            if (document == null)
            {
                return;
            }

            document.Closed -= DocClosed;
            document.IsSelectedChanged -= DocDocument_IsSelectedChanged;
        }

        /// <summary>
        /// Закрывает вкладку дизайнера по полному пути к файлу
        /// </summary>
        /// <param name="loadedFile">Полный путь к файлу активности</param>
        internal void CloseDocumentByPath(string loadedFile)
        {
            LayoutDocument doc = (LayoutDocument)docGroup.Children.FirstOrDefault(c =>
            {
                return c.Content is FrameworkElement view && view.Tag != null && view.Tag.ToString() == loadedFile;
            });

            doc?.Close();
        }
    }
}
