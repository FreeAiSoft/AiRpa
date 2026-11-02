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

            // Дерево приходит из шаблона (PART_Tools): до загрузки контрола генератора ещё нет.
            // Повторная загрузка панели (переключение раскладки) пересоздаёт элементы — темизируем заново.
            toolbox.Loaded += (sender, args) =>
            {
                HookGenerator(toolbox, hook);
                ScheduleApply(toolbox);
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
        }
    }
}
