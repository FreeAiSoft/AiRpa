using System;
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
            if (docGroup.Children.FirstOrDefault(c => c.IsSelected) is LayoutDocument activeDoc)
            {
                FrameworkElement view = (FrameworkElement)activeDoc.Content;
                string tag = view.Tag.ToString();
                if (((FrameworkElement)_mainViewModel.ActiveWorkflowDesigner.View).Tag.ToString() != tag)
                {
                    setActiveDesignerDelegate(view.Tag.ToString());
                }
            }
        }

        private void DocClosed(object sender, EventArgs e)
        {
            FrameworkElement view = (FrameworkElement)((LayoutDocument)sender).Content;
            removeDesignerDelegate(view.Tag.ToString());
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
            else
            {
                throw new NotImplementedException();
            }
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
