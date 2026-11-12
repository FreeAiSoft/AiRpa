using System;
using System.Collections.Generic;
using System.Activities.Presentation;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using AvalonDock.Layout;
using RPA.Converters;
using RPA.Core;
using RPA.ViewModels;

namespace RPA.Helpers
{
    internal class DocGroupHelper
    {
        private readonly LayoutDocumentPane docGroup;
        private readonly Action<string> removeDesignerDelegate;
        private readonly Action<WorkflowDesigner> syncWFControlsDelegate;
        private readonly MainViewModel _mainViewModel;
        private readonly Action<string> setActiveDesignerDelegate;
        private readonly PropertyWindowStyleHelper _propertyStyleHelper = new PropertyWindowStyleHelper();


        #region Singleton

        public DocGroupHelper(LayoutDocumentPane docGroup, Action<string> removeDesigner, Action<WorkflowDesigner> syncWFControls, MainViewModel mainViewModel, Action<string> setActiveDesigner)
        {
            this.docGroup = docGroup;
            removeDesignerDelegate = removeDesigner;
            syncWFControlsDelegate = syncWFControls;
            _mainViewModel = mainViewModel;
            _mainViewModel.PropertyChanged += DataContext_PropertyChanged;
            setActiveDesignerDelegate = setActiveDesigner;
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

            LayoutDocument docDocument = new LayoutDocument
            {
                Content = workflowDesignerView
            };

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

            docGroup.Children.Add(docDocument);
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

            LayoutDocument doc = (LayoutDocument)docGroup.Children.FirstOrDefault(c => c.Content == activeWorkflowDesigner.View);
            if (doc != null)
            {
                doc.IsActive = true;
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

            Dictionary<object, int> modelOrder = _mainViewModel.WorkflowDesigners
                .Select((designer, index) => new { designer, index })
                .ToDictionary(item => (object)item.designer.View, item => item.index);

            List<LayoutDocument> ordered = designerTabs
                .OrderBy(document => modelOrder.TryGetValue(document.Content, out int index) ? index : int.MaxValue)
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
