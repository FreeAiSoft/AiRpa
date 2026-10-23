using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Activities.Flow;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using RPA.Core;
using RPA.Entities;
using RPA.Helpers;
using RPA.ViewModels;
using RPA.Models;

namespace RPA.Controls
{
    /// <summary>
    /// Interaction logic for ProjectManagerControl.xaml
    /// </summary>
    public partial class ProjectManagerControl : UserControl
    {
        public ProjectManagerControl()
        {
            InitializeComponent();
        }

        public ObservableCollection<FSNode> ItemsSource
        {
            get => (ObservableCollection<FSNode>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(ObservableCollection<FSNode>), typeof(ProjectManagerControl), new PropertyMetadata(null, new PropertyChangedCallback(OnItemsSourcePropertyChanged)));

        private static void OnItemsSourcePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ProjectManagerControl control)
            {
                control.OnItemsSourceChanged((ObservableCollection<FSNode>)e.OldValue, (ObservableCollection<FSNode>)e.NewValue);
            }
        }

        private void OnItemsSourceChanged(ObservableCollection<FSNode> oldValue, ObservableCollection<FSNode> newValue)
        {
            // Remove handler for oldValue.CollectionChanged
            if (oldValue is INotifyCollectionChanged oldValueINotifyCollectionChanged)
            {
                oldValueINotifyCollectionChanged.CollectionChanged -= new NotifyCollectionChangedEventHandler(NewValueINotifyCollectionChanged_CollectionChanged);
            }

            // Unsubscribe from PropertyChanged events on old nodes
            if (oldValue != null)
            {
                UnsubscribeFromNodeEvents(oldValue);
            }

            // Add handler for newValue.CollectionChanged (if possible)
            if (newValue is INotifyCollectionChanged newValueINotifyCollectionChanged)
            {
                newValueINotifyCollectionChanged.CollectionChanged += new NotifyCollectionChangedEventHandler(NewValueINotifyCollectionChanged_CollectionChanged);
            }

            // Subscribe to PropertyChanged events on new nodes
            if (newValue != null && newValue.Count > 0)
            {
                SubscribeToNodeEvents(newValue);
                RestoreTreeState();
            }
        }

        private void NewValueINotifyCollectionChanged_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            //Do your stuff here.
            throw new NotImplementedException();
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (object item in projectTree.Items)
            {
                TreeViewItem treeItem = projectTree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (treeItem != null)
                {
                    ExpandAll(treeItem, true);
                }

                treeItem.IsExpanded = true;
            }
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (object item in projectTree.Items)
            {
                TreeViewItem treeItem = projectTree.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (treeItem != null)
                {
                    ExpandAll(treeItem, false);
                }

                treeItem.IsExpanded = false;
            }
        }

        private void ExpandAll(ItemsControl items, bool expand)
        {
            foreach (object obj in items.Items)
            {
                ItemsControl childControl = items.ItemContainerGenerator.ContainerFromItem(obj) as ItemsControl;
                if (childControl != null)
                {
                    ExpandAll(childControl, expand);
                }
                if (childControl is TreeViewItem item)
                {
                    item.IsExpanded = true;
                }
            }
        }

        #region Context menu

        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem)
            {
                treeViewItem.IsSelected = true;
                e.Handled = false; // Allow the event to continue for context menu
            }
        }

        private void TreeViewItem_MouseDoubleClick(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem && treeViewItem.DataContext is FSNode fsNode)
            {
                // Only handle if this is the actual item that was double-clicked, not a parent item
                if (e.OriginalSource is DependencyObject originalSource)
                {
                    // Check if click was on the TreeViewItem itself (not on an expander or nested item)
                    TreeViewItem clickedItem = FindVisualParent<TreeViewItem>(originalSource);
                    if (clickedItem != treeViewItem)
                    {
                        return; // Click was on a child item, ignore
                    }
                }

                // Execute the command to open the file - use the same approach as OpenFileMenuItem_Click
                string runtimeFullPath = fsNode.RuntimeFullPath;
                MainViewModel mainViewModel = (MainViewModel)DataContext;
                Models.Main main = (Models.Main)mainViewModel.Model;
                main.CmdTryLoadFile(runtimeFullPath);

                e.Handled = true;
            }
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // Проверяем, открывается ли контекстное меню для Grid с редактируемым TextBox
            if (sender is ContextMenu contextMenu2 && contextMenu2.PlacementTarget is Grid grid)
            {
                // Проверяем, есть ли в Grid редактируемый TextBox
                if (grid.Tag is FSNode node && node.IsEditing)
                {
                    // Закрываем контекстное меню, чтобы не мешать редактированию
                    contextMenu2.IsOpen = false;
                    return;
                }
            }

            // Commit any active edits before opening context menu
            CommitAnyActiveEdits();

            // Проверяем, является ли это контекстное меню файла
            if (sender is ContextMenu contextMenu && contextMenu.PlacementTarget is FrameworkElement target)
            {
                if (target.Tag is FSNode fsNode)
                {
                    // Проверяем, является ли файл project.json
                    bool isProjectJson = fsNode.Name?.Equals("project.json", StringComparison.OrdinalIgnoreCase) == true;

                    // Находим элементы меню
                    if (contextMenu.Items.Count > 0)
                    {
                        foreach (object item in contextMenu.Items)
                        {
                            if (item is MenuItem menuItem)
                            {
                                // Если файл project.json, показываем только "Открыть содержащую папку"
                                if (isProjectJson)
                                {
                                    menuItem.Visibility = menuItem.Name == "OpenFolderContainingFileMenuItem"
                                        ? Visibility.Visible
                                        : Visibility.Collapsed;
                                }
                                else
                                {
                                    // Для всех остальных файлов показываем все пункты меню
                                    menuItem.Visibility = Visibility.Visible;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Находит и сохраняет любые активные редактирования в дереве
        /// </summary>
        private void CommitAnyActiveEdits()
        {
            if (ItemsSource == null || ItemsSource.Count == 0)
            {
                return;
            }

            // Рекурсивно ищем узлы в режиме редактирования
            FSNode editingNode = FindEditingNodeRecursive(ItemsSource[0]);
            if (editingNode != null)
            {
                // Сохраняем изменения
                CommitRename(editingNode, editingNode.Name);
            }
        }

        /// <summary>
        /// Рекурсивно ищет узел в режиме редактирования
        /// </summary>
        private FSNode FindEditingNodeRecursive(FSNode current)
        {
            if (current.IsEditing)
            {
                return current;
            }

            if (current.Childs != null)
            {
                foreach (FSNode child in current.Childs)
                {
                    FSNode result = FindEditingNodeRecursive(child);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        #region Context menu file

        private void DeleteFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode fSNode)
                {
                    Services.ProjectManager.DeleteFile(fSNode);
                }
            }
        }

        private void OpenFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode fSNode)
                {
                    string runtimeFullPath = fSNode.RuntimeFullPath;
                    MainViewModel mainViewModel = (MainViewModel)DataContext;
                    Models.Main main = (Models.Main)mainViewModel.Model;
                    main.CmdTryLoadFile(runtimeFullPath);
                }
            }
        }

        private void OpenFolderContainingFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode fSNode)
                {
                    string path = fSNode.RuntimeFullPath;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        // Open Explorer and select the file
                        _ = Process.Start(new ProcessStartInfo()
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{path}\"",
                            UseShellExecute = true
                        });
                    }
                    else if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        // If it's a directory, just open it
                        _ = Process.Start(new ProcessStartInfo()
                        {
                            FileName = path,
                            UseShellExecute = true,
                            Verb = "open"
                        });
                    }
                }
            }
        }

        #endregion Context menu file

        #region Context menu project

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode parentNode)
                {
                    // Генерируем уникальное имя для новой папки
                    string uniqueFolderName = GenerateUniqueFolderName(parentNode, "Новая папка");

                    // Создаем временный узел для новой папки
                    FSNode newNode = new FSNode(Path.Combine(parentNode.RuntimeFullPath, uniqueFolderName), Enums.FSNodeType.Dir)
                    {
                        IsNew = true,
                        IsEditing = true,
                        Childs = new ObservableCollection<FSNode>()
                    };

                    // Добавляем в дочерние элементы
                    parentNode.Childs.Add(newNode);

                    // Sort children after adding new folder
                    SortChildren(parentNode);

                    // Разворачиваем родительский узел, чтобы новая папка была видна
                    parentNode.IsExpanded = true;

                    // Устанавливаем фокус на TextBox новой папки
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (projectTree.ItemContainerGenerator.ContainerFromItem(parentNode) is TreeViewItem parentContainer)
                        {
                            parentContainer.UpdateLayout();
                            if (parentContainer.ItemContainerGenerator.ContainerFromItem(newNode) is TreeViewItem newNodeContainer)
                            {
                                TextBox textBox = FindVisualChild<TextBox>(newNodeContainer);
                                if (textBox != null && textBox.Tag == newNode)
                                {
                                    _ = textBox.Focus();
                                    textBox.SelectAll();
                                }
                            }
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void AddFolderToFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode parentNode)
                {
                    Debug.WriteLine($"[AddFolderToFolder] Parent: {parentNode.Name}, Path: {parentNode.RuntimeFullPath}");

                    // Генерируем уникальное имя для новой папки
                    string uniqueFolderName = GenerateUniqueFolderName(parentNode, "Новая папка");
                    Debug.WriteLine($"[AddFolderToFolder] Unique folder name: {uniqueFolderName}");

                    // Создаем временный узел для новой папки
                    FSNode newNode = new FSNode(Path.Combine(parentNode.RuntimeFullPath, uniqueFolderName), Enums.FSNodeType.Dir)
                    {
                        IsNew = true,
                        IsEditing = true,
                        Childs = new ObservableCollection<FSNode>()
                    };

                    // Добавляем в дочерние элементы
                    parentNode.Childs.Add(newNode);
                    Debug.WriteLine($"[AddFolderToFolder] Node added to parent.Childs, count: {parentNode.Childs.Count}");

                    // Sort children after adding new folder
                    SortChildren(parentNode);

                    // Разворачиваем родительский узел, чтобы новая папка была видна
                    parentNode.IsExpanded = true;
                    Debug.WriteLine($"[AddFolderToFolder] Parent expanded: {parentNode.IsExpanded}");

                    // Устанавливаем фокус на TextBox новой папки
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Debug.WriteLine($"[AddFolderToFolder] Dispatcher callback started");

                        // Ищем TreeViewItem для родительского узла рекурсивно
                        TreeViewItem parentContainer = FindTreeViewItemForNode(projectTree, parentNode);
                        Debug.WriteLine($"[AddFolderToFolder] ParentContainer found: {parentContainer != null}");

                        if (parentContainer != null)
                        {
                            parentContainer.UpdateLayout();
                            Debug.WriteLine($"[AddFolderToFolder] ParentContainer.UpdateLayout() called");

                            TreeViewItem newNodeContainer = parentContainer.ItemContainerGenerator.ContainerFromItem(newNode) as TreeViewItem;
                            Debug.WriteLine($"[AddFolderToFolder] NewNodeContainer: {newNodeContainer != null}");

                            if (newNodeContainer != null)
                            {
                                TextBox textBox = FindVisualChild<TextBox>(newNodeContainer);
                                Debug.WriteLine($"[AddFolderToFolder] TextBox found: {textBox != null}, Tag matches: {textBox?.Tag == newNode}");

                                if (textBox != null && textBox.Tag == newNode)
                                {
                                    bool focusResult = textBox.Focus();
                                    Debug.WriteLine($"[AddFolderToFolder] Focus result: {focusResult}");
                                    textBox.SelectAll();
                                }
                            }
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode fSNode)
                {
                    // Включаем режим редактирования
                    fSNode.IsEditing = true;

                    // Назначаем фокус текстбоксу после включения режима редактирования
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // Ищем TextBox в визуальном дереве
                        TextBox textBox = FindVisualChild<TextBox>(target);
                        if (textBox != null && textBox.Tag == fSNode)
                        {
                            _ = textBox.Focus();
                            textBox.SelectAll();
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void AddRefence_Click(object sender, RoutedEventArgs e)
        {

        }

        #endregion Context menu project

        #region Context menu folder

        /// <summary>
        /// Рекурсивно ищет TreeViewItem для заданного узла в дереве
        /// </summary>
        private TreeViewItem FindTreeViewItemForNode(ItemsControl container, FSNode node)
        {
            if (container == null || node == null)
            {
                return null;
            }

            // Проверяем элементы на текущем уровне
            foreach (object item in container.Items)
            {
                if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem)
                {
                    // Проверяем, является ли этот элемент искомым узлом
                    if (item == node)
                    {
                        return treeViewItem;
                    }

                    // Рекурсивно ищем в дочерних элементах
                    TreeViewItem foundItem = FindTreeViewItemForNode(treeViewItem, node);
                    if (foundItem != null)
                    {
                        return foundItem;
                    }
                }
            }

            return null;
        }

        private T FindVisualChild<T>(FrameworkElement target) where T : DependencyObject
        {
            if (target == null)
            {
                return null;
            }

            // First check if the target itself is of the desired type
            if (target is T typedTarget)
            {
                return typedTarget;
            }

            // Then check all children recursively
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(target); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(target, i);
                if (child is FrameworkElement frameworkElement)
                {
                    T result = FindVisualChild<T>(frameworkElement);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Tag is FSNode fSNode)
            {
                if (e.Key == Key.Enter)
                {
                    CommitRename(fSNode, textBox.Text);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // Отменяем редактирование
                    fSNode.IsEditing = false;
                    e.Handled = true;
                }
            }
        }

        public async void CommitRename(FSNode fSNode, string newName)
        {
            // Выходим из режима редактирования
            fSNode.IsEditing = false;

            // Если имя пустое, удаляем временный узел
            if (string.IsNullOrWhiteSpace(newName))
            {
                if (fSNode.IsNew)
                {
                    // Находим родителя и удаляем новый узел
                    FSNode parent = FindParentInTree(fSNode);
                    _ = (parent?.Childs.Remove(fSNode));
                }
                return;
            }

            // Если это новый узел, создаем папку или файл
            if (fSNode.IsNew)
            {
                FSNode parent = FindParentInTree(fSNode);
                if (parent != null)
                {
                    try
                    {
                        // Проверяем тип узла: папка или файл (активность)
                        if (fSNode.FSNodeType == Enums.FSNodeType.Dir)
                        {
                            string newFolderPath = Path.Combine(parent.RuntimeFullPath, newName);

                            if (Directory.Exists(newFolderPath))
                            {
                                // Удаляем временный узел при ошибке
                                _ = parent.Childs.Remove(fSNode);

                                if (Window.GetWindow(this) is MetroWindow window2)
                                {
                                    _ = await window2.ShowMessageAsync("Ошибка", $"Папка '{newName}' уже существует");
                                }
                                return;
                            }

                            _ = Directory.CreateDirectory(newFolderPath);

                            // Обновляем существующий узел вместо создания нового
                            fSNode.RuntimeFullPath = newFolderPath;
                            fSNode.Name = newName;
                            fSNode.IsNew = false;
                        }
                        else if (fSNode.FSNodeType == Enums.FSNodeType.File)
                        {
                            // Добавляем расширение .xaml, если его нет
                            string activityName = newName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                                ? newName
                                : newName + ".xaml";

                            string newFilePath = Path.Combine(parent.RuntimeFullPath, activityName);

                            if (File.Exists(newFilePath))
                            {
                                // Удаляем временный узел при ошибке
                                _ = parent.Childs.Remove(fSNode);

                                if (Window.GetWindow(this) is MetroWindow window2)
                                {
                                    _ = await window2.ShowMessageAsync("Ошибка", $"Файл '{activityName}' уже существует");
                                }
                                return;
                            }

                            // Создаем файл активности через команду
                            if (Window.GetWindow(this) is MetroWindow window3)
                            {
                                if (window3.DataContext != null)
                                {
                                    dynamic dc = window3.DataContext;
                                    dc.Cmd?.Execute(new object[] { "CmdAddNewActivity", newFilePath });

                                    // Обновляем существующий узел
                                    fSNode.RuntimeFullPath = newFilePath;
                                    fSNode.Name = activityName;
                                    fSNode.IsNew = false;
                                }
                            }
                        }

                        // Sort children after renaming
                        SortChildren(parent);
                    }
                    catch (Exception ex)
                    {
                        // Удаляем временный узел при ошибке
                        _ = parent.Childs.Remove(fSNode);

                        if (Window.GetWindow(this) is MetroWindow window2)
                        {
                            _ = await window2.ShowMessageAsync("Ошибка", $"Ошибка при создании: {ex.Message}");
                        }
                    }
                }
                return;
            }

            // Если имя не изменилось, ничего не делаем
            if (newName == fSNode.OriginalName)
            {
                return;
            }

            // Выполняем переименование в зависимости от типа узла
            RpaCommon.CallResult renameResult;

            if (fSNode.FSNodeType == Enums.FSNodeType.Dir)
            {
                renameResult = Services.ProjectManager.RenameFolder(fSNode, newName);
            }
            else if (fSNode.FSNodeType == Enums.FSNodeType.File)
            {
                renameResult = Services.ProjectManager.RenameFile(fSNode, newName);
            }
            else if (fSNode.FSNodeType == Enums.FSNodeType.Project)
            {
                renameResult = Services.ProjectManager.RenameProject(fSNode, newName);
            }
            else
            {
                return;
            }

            if (renameResult.Error != null && Window.GetWindow(this) is MetroWindow window)
            {
                _ = await window.ShowMessageAsync("Ошибка", renameResult.Error.Message);
                fSNode.Name = fSNode.OriginalName;
            }
            else
            {
                // Sort children after successful rename
                FSNode parent = FindParentInTree(fSNode);
                if (parent != null)
                {
                    SortChildren(parent);
                }
            }
        }

        /// <summary>
        /// Находит родительский узел для указанного узла в дереве
        /// </summary>
        private FSNode FindParentInTree(FSNode targetNode)
        {
            return ItemsSource == null || ItemsSource.Count == 0 ? null : FindParentRecursive(ItemsSource[0], targetNode);
        }

        /// <summary>
        /// Рекурсивно ищет родительский узел
        /// </summary>
        private FSNode FindParentRecursive(FSNode current, FSNode target)
        {
            if (current.Childs == null)
            {
                return null;
            }

            if (current.Childs.Contains(target))
            {
                return current;
            }

            foreach (FSNode child in current.Childs)
            {
                FSNode result = FindParentRecursive(child, target);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private async void DeleteFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode fSNode)
                {
                    if (Window.GetWindow(this) is MetroWindow window)
                    {
                        MessageDialogResult result = await window.ShowMessageAsync(
                            "Подтверждение удаления",
                            $"Вы действительно хотите удалить папку '{fSNode.Name}' и всё её содержимое?",
                            MessageDialogStyle.AffirmativeAndNegative,
                            new MetroDialogSettings
                            {
                                AffirmativeButtonText = "Да",
                                NegativeButtonText = "Нет"
                            });

                        if (result == MessageDialogResult.Affirmative)
                        {
                            RpaCommon.CallResult deleteResult = Services.ProjectManager.DeleteFolder(fSNode);
                            if (deleteResult.Error != null)
                            {
                                _ = await window.ShowMessageAsync("Ошибка", deleteResult.Error.Message);
                            }
                        }
                    }
                }
            }
        }

        private void AddActivityToFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu)
            {
                FrameworkElement target = ((ContextMenu)mnu.Parent).PlacementTarget as FrameworkElement;
                if (target?.Tag is FSNode parentNode)
                {
                    // Добавляем в дочерние элементы
                    string folderPath = parentNode.RuntimeFullPath;
                    MainViewModel mainViewModel = (MainViewModel)DataContext;
                    Models.Main main = (Models.Main)mainViewModel.Model;
                    main.CmdAddNewActivityToProjectFolder(folderPath);

                    // Sort children after adding new activity
                    SortChildren(parentNode);

                    // Разворачиваем родительский узел, чтобы новая активность была видна
                    parentNode.IsExpanded = true;
                }
            }
        }

        #endregion Context menu folder

        #endregion Context menu

        /// <summary>
        /// Находит родительский визуальный элемент указанного типа
        /// </summary>
        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            return parentObject == null ? null : parentObject is T parent ? parent : FindVisualParent<T>(parentObject);
        }

        private void OpenProjectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _ = Process.Start(new ProcessStartInfo()
            {
                FileName = Services.ProjectManager.ProjectFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        /// <summary>
        /// Генерирует уникальное имя папки, которое не конфликтует с существующими папками в узле
        /// </summary>
        /// <param name="parentNode">Родительский узел</param>
        /// <param name="baseName">Базовое имя (например, "Новая папка")</param>
        /// <returns>Уникальное имя папки</returns>
        private string GenerateUniqueFolderName(FSNode parentNode, string baseName)
        {
            if (parentNode?.Childs == null)
            {
                return baseName;
            }

            // Получаем все имена папок в текущем узле
            HashSet<string> existingFolderNames = new HashSet<string>(
                parentNode.Childs
                    .Where(n => n.FSNodeType == Enums.FSNodeType.Dir)
                    .Select(n => n.Name),
                StringComparer.OrdinalIgnoreCase);

            // Если базовое имя не используется, возвращаем его
            if (!existingFolderNames.Contains(baseName))
            {
                return baseName;
            }

            // Ищем первый доступный вариант с номером (начиная с 1)
            int counter = 1;
            string candidateName;
            do
            {
                candidateName = $"{baseName} {counter}";
                counter++;
            }
            while (existingFolderNames.Contains(candidateName));

            return candidateName;
        }

        /// <summary>
        /// Генерирует уникальное имя активности, которое не конфликтует с существующими файлами в узле
        /// </summary>
        /// <param name="parentNode">Родительский узел</param>
        /// <param name="baseName">Базовое имя (например, "NewActivity")</param>
        /// <returns>Уникальное имя активности (без расширения .xaml)</returns>
        private string GenerateUniqueActivityName(FSNode parentNode, string baseName)
        {
            if (parentNode?.Childs == null)
            {
                return baseName;
            }

            // Получаем все имена файлов в текущем узле (без расширений для .xaml)
            HashSet<string> existingFileNames = new HashSet<string>(
                parentNode.Childs
                    .Where(n => n.FSNodeType == Enums.FSNodeType.File)
                    .Select(n => n.Name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileNameWithoutExtension(n.Name)
                        : n.Name),
                StringComparer.OrdinalIgnoreCase);

            // Если базовое имя не используется, возвращаем его
            if (!existingFileNames.Contains(baseName))
            {
                return baseName;
            }

            // Ищем первый доступный вариант с номером (начиная с 1)
            int counter = 1;
            string candidateName;
            do
            {
                candidateName = $"{baseName}{counter}";
                counter++;
            }
            while (existingFileNames.Contains(candidateName));

            return candidateName;
        }

        #region TreeView State Persistence

        /// <summary>
        /// Подписывается на события PropertyChanged всех узлов для отслеживания изменений IsExpanded
        /// </summary>
        private void SubscribeToNodeEvents(ObservableCollection<FSNode> nodes)
        {
            foreach (FSNode node in nodes)
            {
                node.PropertyChanged += Node_PropertyChanged;
                if (node.Childs != null && node.Childs.Count > 0)
                {
                    SubscribeToNodeEvents(node.Childs);
                }
            }
        }

        /// <summary>
        /// Отписывается от событий PropertyChanged всех узлов
        /// </summary>
        private void UnsubscribeFromNodeEvents(ObservableCollection<FSNode> nodes)
        {
            foreach (FSNode node in nodes)
            {
                node.PropertyChanged -= Node_PropertyChanged;
                if (node.Childs != null && node.Childs.Count > 0)
                {
                    UnsubscribeFromNodeEvents(node.Childs);
                }
            }
        }

        /// <summary>
        /// Обработчик изменения свойств узла
        /// </summary>
        private void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FSNode.IsExpanded))
            {
                SaveTreeState();
            }
        }

        /// <summary>
        /// Сохраняет состояние дерева (развёрнутые узлы) в настройках
        /// </summary>
        private void SaveTreeState()
        {
            if (ItemsSource == null || ItemsSource.Count == 0)
            {
                return;
            }

            List<string> expandedPaths = new List<string>();
            CollectExpandedPaths(ItemsSource, expandedPaths);

            // Сохраняем в настройки как JSON-строку
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(expandedPaths);
            Properties.Settings.Default.ProjectTreeLayout = json;
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Рекурсивно собирает пути всех развёрнутых узлов
        /// </summary>
        private void CollectExpandedPaths(ObservableCollection<FSNode> nodes, List<string> expandedPaths)
        {
            foreach (FSNode node in nodes)
            {
                if (node.IsExpanded && !string.IsNullOrEmpty(node.RuntimeFullPath))
                {
                    expandedPaths.Add(node.RuntimeFullPath);
                }

                if (node.Childs != null && node.Childs.Count > 0)
                {
                    CollectExpandedPaths(node.Childs, expandedPaths);
                }
            }
        }

        /// <summary>
        /// Восстанавливает состояние дерева из настроек
        /// </summary>
        private void RestoreTreeState()
        {
            if (ItemsSource == null || ItemsSource.Count == 0)
            {
                return;
            }

            string json = Properties.Settings.Default.ProjectTreeLayout;
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                List<string> expandedPaths = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
                if (expandedPaths != null && expandedPaths.Count > 0)
                {
                    RestoreExpandedPaths(ItemsSource, expandedPaths);
                }
            }
            catch
            {
                // Игнорируем ошибки десериализации
            }
        }

        /// <summary>
        /// Рекурсивно восстанавливает состояние развёрнутых узлов
        /// </summary>
        private void RestoreExpandedPaths(ObservableCollection<FSNode> nodes, List<string> expandedPaths)
        {
            foreach (FSNode node in nodes)
            {
                if (expandedPaths.Contains(node.RuntimeFullPath))
                {
                    node.IsExpanded = true;
                }

                if (node.Childs != null && node.Childs.Count > 0)
                {
                    RestoreExpandedPaths(node.Childs, expandedPaths);
                }
            }
        }

        #endregion TreeView State Persistence

        /// <summary>
        /// Sorts the children of a node: folders first (alphabetically), then files (alphabetically)
        /// </summary>
        private void SortChildren(FSNode node)
        {
            if (node?.Childs == null || node.Childs.Count <= 1)
            {
                return;
            }

            List<FSNode> sortedList = node.Childs.OrderBy(n => n, new FSNodeComparer()).ToList();

            // Move items to their correct positions instead of clearing and re-adding
            for (int i = 0; i < sortedList.Count; i++)
            {
                FSNode item = sortedList[i];
                int currentIndex = node.Childs.IndexOf(item);

                if (currentIndex != i)
                {
                    node.Childs.Move(currentIndex, i);
                }
            }
        }

        private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            TextBlock textBlock = (TextBlock)sender;
            FSNode node = (FSNode)textBlock.Tag;
            if (Path.GetFileName(node.RuntimeFullPath) == "project.json")
            {
                return;
            }
            string relativePath = node.RuntimeFullPath
                .Replace(ProjectManager.GetInstance().ProjectFolder, "")
                .TrimStart('\\')
                .Replace("\\", "\\\\");
            System.Activities.Activity activity = new RunScript { PathToXamlActivity = relativePath, DisplayName = "Run" };
            CustomWfDesigner customWfDesigner = Services.CustomWfDesigner;

            ModelItem mi = customWfDesigner.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelTreeManager>().CreateModelItem(null, activity);
            DataObject data = new DataObject(DragDropHelper.ModelItemDataFormat, mi);
            _ = DragDrop.DoDragDrop(textBlock, data, DragDropEffects.Copy);
        }
    }
}
