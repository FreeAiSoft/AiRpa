using System;
using System.Activities.Presentation.Toolbox;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using RPA.Helpers;

namespace RPA.Controls
{
    /// <summary>
    /// Interaction logic for ToolboxPanel.xaml
    /// </summary>
    public partial class ToolboxPanel : UserControl
    {
        private ToolboxControl toolboxControl;

        /// <summary>
        /// Подписки на появление новых контейнеров: категории и активности материализует генератор
        /// элементов при разворачивании (и при прокрутке виртуализованного списка), то есть уже после
        /// применения темы. Без повторного прохода заголовок новой категории снова приходит со светлой
        /// плашкой WF-стиля. Подписка ставится один раз на контрол.
        /// </summary>
        private static readonly ConditionalWeakTable<ToolboxControl, ToolboxHook> ContainerHooks =
            new ConditionalWeakTable<ToolboxControl, ToolboxHook>();

        public ToolboxPanel()
        {
            InitializeComponent();
        }

        /// <summary>Показывает indeterminate-индикатор, пока палитра пересобирается в фоне.</summary>
        public void ShowLoading()
        {
            LoadingProgress.Visibility = Visibility.Visible;
            ToolboxContainer.Opacity = 0.35;
            ToolboxContainer.IsHitTestVisible = false;
        }

        /// <summary>Скрывает индикатор и возвращает панель в интерактивное состояние.</summary>
        public void HideLoading()
        {
            LoadingProgress.Visibility = Visibility.Collapsed;
            ToolboxContainer.Opacity = 1.0;
            ToolboxContainer.IsHitTestVisible = true;
        }

        /// <summary>
        /// Устанавливает ToolboxControl для отображения
        /// </summary>
        public void SetToolboxControl(ToolboxControl toolbox)
        {
            if (toolbox == null)
            {
                return;
            }

            toolboxControl = toolbox;

            toolbox.Resources ??= new ResourceDictionary();

            // Устанавливаем шрифт через стиль для TreeView
            Style treeViewStyle = new Style(typeof(TreeView));
            treeViewStyle.Setters.Add(new Setter(FontFamilyProperty, WfThemeStyles.Font));
            toolbox.Resources[typeof(TreeView)] = treeViewStyle;

            // Очищаем контейнер и добавляем ToolboxControl
            ToolboxContainer.Child = toolbox;

            // Применяем шаблон, чтобы создать визуальное дерево
            _ = toolbox.ApplyTemplate();

            // Пробуем получить TreeView через GetTemplateChild
            if (toolbox.Template?.FindName("PART_Tools", toolbox) is TreeView treeView)
            {
                // Устанавливаем margin 5 на верхней границе
                treeView.Margin = new Thickness(0, 5, 0, 0);

                // Применяем шаблон TreeView
                _ = treeView.ApplyTemplate();

                // Получаем первый дочерний Border через логическое или визуальное дерево
                Border firstBorder = GetFirstChildBorder(treeView);
                if (firstBorder != null)
                {
                    firstBorder.BorderThickness = new Thickness(0);
                }
            }

            WfVisualTreeThemer.ApplyWhenLoaded(toolbox);

            HookContainers(toolbox);
        }

        /// <summary>
        /// Повторно применяет палитру студии к нативному ToolboxControl (вызывается при смене темы).
        /// </summary>
        public void ApplyTheme()
        {
            if (toolboxControl != null)
            {
                WfVisualTreeThemer.ApplyWhenLoaded(toolboxControl);
            }
        }

        /// <summary>
        /// Повторная темизация при появлении новых элементов: контейнеры создаются после применения темы.
        /// </summary>
        private static void HookContainers(ToolboxControl toolbox)
        {
            ToolboxHook hook = ContainerHooks.GetValue(toolbox, key => new ToolboxHook());

            if (hook.Subscribed)
            {
                return;
            }

            hook.Subscribed = true;

            // Разворачивание категории — единственное действие пользователя, создающее новые элементы.
            toolbox.AddHandler(
                TreeViewItem.ExpandedEvent,
                new RoutedEventHandler((sender, args) => ScheduleApply(toolbox)),
                handledEventsToo: true);

            // Сохранение состояния категорий при разворачивании/сворачивании.
            toolbox.AddHandler(
                TreeViewItem.ExpandedEvent,
                new RoutedEventHandler((sender, args) => OnCategoryStateChanged(toolbox, args)),
                handledEventsToo: true);

            toolbox.AddHandler(
                TreeViewItem.CollapsedEvent,
                new RoutedEventHandler((sender, args) => OnCategoryStateChanged(toolbox, args)),
                handledEventsToo: true);

            // Дерево приходит из шаблона (PART_Tools): до загрузки контрола генератора ещё нет.
            // Повторная загрузка панели (переключение раскладки) пересоздаёт элементы — темизируем заново.
            toolbox.Loaded += (sender, args) =>
            {
                HookGenerator(toolbox, hook);
                ScheduleApply(toolbox);
                RestoreCategoryStates(toolbox, hook);
            };
            HookGenerator(toolbox, hook);
        }

        private static void HookGenerator(ToolboxControl toolbox, ToolboxHook hook)
        {
            if (hook.GeneratorSubscribed || !(toolbox.Template?.FindName("PART_Tools", toolbox) is TreeView treeView))
            {
                return;
            }

            hook.GeneratorSubscribed = true;

            treeView.ItemContainerGenerator.StatusChanged += (sender, args) =>
            {
                if (sender is ItemContainerGenerator generator && generator.Status == GeneratorStatus.ContainersGenerated)
                {
                    ScheduleApply(toolbox);
                    RestoreCategoryStates(toolbox, hook);
                }
            };
        }

        /// <summary>
        /// Отложенный проход: контейнеры создаются в процессе раскладки, поэтому темизация выполняется на
        /// следующем цикле диспетчера и не чаще одного раза за цикл.
        /// </summary>
        private static void ScheduleApply(ToolboxControl toolbox)
        {
            if (!ContainerHooks.TryGetValue(toolbox, out ToolboxHook hook) || hook.Pending)
            {
                return;
            }

            hook.Pending = true;

            _ = toolbox.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    hook.Pending = false;

                    if (ContainerHooks.TryGetValue(toolbox, out ToolboxHook current) && current.Subscribed)
                    {
                        WfVisualTreeThemer.Apply(toolbox);
                    }
                }),
                DispatcherPriority.Background);
        }

        /// <summary>
        /// Сохраняет состояние узлов-категорий панели «Активности» (свёрнуто/развёрнуто) при
        /// пользовательском разворачивании/сворачивании категории. Во время восстановления
        /// (<see cref="ToolboxHook.Restoring"/>) и до его завершения (<see cref="ToolboxHook.StateReady"/>)
        /// события игнорируются, чтобы программное применение состояния и стартовая генерация
        /// контейнеров не перезаписывали настройку.
        /// </summary>
        private static void OnCategoryStateChanged(ToolboxControl toolbox, RoutedEventArgs args)
        {
            if (!ContainerHooks.TryGetValue(toolbox, out ToolboxHook hook) || !hook.StateReady || hook.Restoring)
            {
                return;
            }

            if (!(args.OriginalSource is TreeViewItem item) || !(item.Header is ToolboxCategory))
            {
                return;
            }

            SaveCategoryStates(toolbox);
        }

        /// <summary>Сериализует имена развёрнутых категорий в настройку <c>ActivityToolboxLayout</c>.</summary>
        private static void SaveCategoryStates(ToolboxControl toolbox)
        {
            TreeView treeView = FindToolsTreeView(toolbox);
            if (treeView == null)
            {
                return;
            }

            var expanded = new List<string>();
            foreach (TreeViewItem item in FindVisualChildren<TreeViewItem>(treeView))
            {
                if (item.IsExpanded && item.Header is ToolboxCategory category)
                {
                    expanded.Add(category.CategoryName);
                }
            }

            Properties.Settings.Default.ActivityToolboxLayout = Newtonsoft.Json.JsonConvert.SerializeObject(expanded);
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Планирует восстановление свёрнутости/развёрнутости узлов-категорий из настройки
        /// <c>ActivityToolboxLayout</c>. Работа откладывается на Background-приоритет диспетчера,
        /// чтобы не входить в генерацию контейнеров синхронно из <c>Loaded</c>/<c>StatusChanged</c>.
        /// </summary>
        private static void RestoreCategoryStates(ToolboxControl toolbox, ToolboxHook hook)
        {
            if (hook.StateRestored || hook.RestorePending)
            {
                return;
            }

            hook.RestorePending = true;
            _ = toolbox.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    hook.RestorePending = false;
                    ApplySavedCategoryStates(toolbox, hook);
                }),
                DispatcherPriority.Background);
        }


        /// <summary>
        /// Восстанавливает свёрнутость/развёрнутость узлов-категорий из настройки
        /// <c>ActivityToolboxLayout</c>. Вызывается после генерации контейнеров дерева (Loaded и
        /// <c>ItemContainerGenerator.StatusChanged</c>); если контейнеры ещё не созданы, попытка
        /// повторяется позже. Флаг <see cref="ToolboxHook.Restoring"/> подавляет сохранение состояния
        /// во время программного применения сохранённой свёрнутости/развёрнутости.
        /// </summary>
        private static void ApplySavedCategoryStates(ToolboxControl toolbox, ToolboxHook hook)
        {
            if (hook.StateRestored)
            {
                return;
            }

            TreeView treeView = FindToolsTreeView(toolbox);
            if (treeView == null)
            {
                return;
            }

            List<string> expanded = ReadExpandedCategories();
            if (expanded == null)
            {
                // Настройки нет — оставляем дефолтное состояние (все категории развёрнуты).
                hook.StateReady = true;
                hook.StateRestored = true;
                return;
            }

            List<TreeViewItem> categoryItems = FindVisualChildren<TreeViewItem>(treeView)
                .Where(item => item.Header is ToolboxCategory)
                .ToList();

            if (categoryItems.Count == 0)
            {
                // Контейнеры ещё не созданы генератором — повторим по StatusChanged/Loaded.
                return;
            }

            hook.Restoring = true;
            try
            {
                foreach (TreeViewItem item in categoryItems)
                {
                    string categoryName = ((ToolboxCategory)item.Header).CategoryName;
                    item.IsExpanded = expanded.Contains(categoryName);
                }
            }
            finally
            {
                hook.Restoring = false;
            }

            hook.StateReady = true;
            hook.StateRestored = true;
        }

        /// <summary>Читает список имён развёрнутых категорий; <c>null</c> — настройки нет.</summary>
        private static List<string> ReadExpandedCategories()
        {
            string json = Properties.Settings.Default.ActivityToolboxLayout;
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Находит <c>TreeView</c> (<c>PART_Tools</c>) нативного тулбокса.</summary>
        private static TreeView FindToolsTreeView(ToolboxControl toolbox)
        {
            return toolbox?.Template?.FindName("PART_Tools", toolbox) as TreeView;
        }


        private static Border GetFirstChildBorder(TreeView treeView)
        {
            // Сначала пробуем через логическое дерево
            IEnumerable<Border> logicalChildren = LogicalTreeHelper.GetChildren(treeView).OfType<Border>();
            Border border = logicalChildren.FirstOrDefault();

            if (border != null)
            {
                return border;
            }

            // Если не нашли в логическом, ищем в визуальном (обход через XamlHelper — без исключений).
            return XamlHelper.FindVisualChildren<Border>(treeView).FirstOrDefault();
        }

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseExpandAll(false);
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseExpandAll(true);
        }

        private void CollapseExpandAll(bool expand)
        {
            if (toolboxControl == null)
            {
                return;
            }

            // Находим TreeView с именем PART_Tools
            TreeView treeView = FindVisualChildByName<TreeView>(toolboxControl, "PART_Tools");

            if (treeView == null)
            {
                return;
            }

            // Получаем все TreeViewItem и устанавливаем IsExpanded
            IEnumerable<TreeViewItem> treeViewItems = FindVisualChildren<TreeViewItem>(treeView);
            foreach (TreeViewItem item in treeViewItems)
            {
                item.IsExpanded = expand;
            }
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            return FindVisualChildren<T>(parent).FirstOrDefault(element => element.Name == name);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            // Обход через XamlHelper: он пропускает не-визуальные узлы (RowDefinition/ColumnDefinition)
            // вместо ArgumentException.
            return XamlHelper.FindVisualChildren<T>(depObj);
        }

        /// <summary>Состояние подписок панели на контейнеры тулбокса.</summary>
        private sealed class ToolboxHook
        {
            public bool Subscribed { get; set; }

            /// <summary>Подписка на <see cref="ItemContainerGenerator.StatusChanged"/> поставлена.</summary>
            public bool GeneratorSubscribed { get; set; }

            /// <summary>Отложенный проход уже запланирован — защита от повторной постановки.</summary>
            public bool Pending { get; set; }

            /// <summary>Программное применение состояния категорий идёт — события не сохраняем.</summary>
            public bool Restoring { get; set; }

            /// <summary>Восстановление завершено (или настройки нет) — можно сохранять изменения.</summary>
            public bool StateReady { get; set; }

            /// <summary>Состояние категорий уже восстановлено для этого контрола.</summary>
            public bool StateRestored { get; set; }
            /// <summary>Отложенное восстановление уже запланировано — защита от повторной постановки.</summary>
            public bool RestorePending { get; set; }


        }
    }
}
