using System.Activities.Presentation.Toolbox;
using System.Activities.Statements;
using System.Windows;
using System.Windows.Controls;
using RPA.Controls;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Контракт «активность можно перетащить из контрола активностей в дизайнер».
/// <para>
/// Перетаскивание целиком реализовано внутри <see cref="ToolboxControl"/> (обработчик
/// <c>OnToolMouseMove</c> и стили элементов <c>ToolItemStyle</c>/<c>CategoryItemStyle</c> из его темы).
/// Студия не имеет права подменять стили элементов тулбокса: вместе со стилем теряется шаблон
/// элемента и обработчики, из-за чего <c>DragDrop.DoDragDrop</c> больше не вызывается.
/// </para>
/// Темизация (<see cref="ToolboxPanel.SetToolboxControl"/>) обязана сохранять стили WF и не отключать
/// взаимодействие с элементами.
/// </summary>
[Apartment(ApartmentState.STA)]
public class ToolboxDragContractTests
{
    [Test]
    public void Toolbox_KeepsWfItemStylesAfterTheming()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeView tree = FindVisualChild<TreeView>(toolbox);
            Assert.That(tree, Is.Not.Null, "в ToolboxControl нет PART_Tools — WF не сможет начать перетаскивание");

            TreeViewItem category = FirstContainer(tree);
            Assert.That(category, Is.Not.Null, "категория тулбокса не материализована");
            category.IsExpanded = true;
            Pump(window);

            TreeViewItem tool = FirstContainer(category);
            Assert.That(tool, Is.Not.Null, "активность категории не материализована");

            Assert.Multiple(() =>
            {
                // Стили элементов тулбокса — стили WF (их выбирает ItemContainerStyleSelector).
                Assert.That(WfThemeStyles.IsThemed(category.Style), Is.False,
                    "темизатор подменил стиль категории: вместе с ним потерян шаблон WF");
                Assert.That(WfThemeStyles.IsThemed(tool.Style), Is.False,
                    "темизатор подменил стиль активности: вместе с ним потеряны шаблон и обработчики WF");
                Assert.That(tool.Template, Is.Not.Null, "у активности пропал шаблон WF");

                // Именно в стиле элемента живёт EventSetter WF, начинающий перетаскивание:
                // MouseMove -> OnToolMouseMove вызывает DragDrop.DoDragDrop, MouseDoubleClick -> создание.
                List<string> events = tool.Style.Setters
                    .OfType<EventSetter>()
                    .Select(setter => setter.Event.Name)
                    .ToList();

                Assert.That(events, Does.Contain("MouseMove"),
                    "у стиля активности пропал обработчик MouseMove (OnToolMouseMove) — перетаскивание не начнётся");
                Assert.That(events, Does.Contain("MouseDoubleClick"),
                    "у стиля активности пропал обработчик MouseDoubleClick (OnTreeViewDoubleClick)");

                // Темизация не имеет права отключать взаимодействие с элементами.
                Assert.That(category.IsHitTestVisible, Is.True, "по категории не попасть мышью");
                Assert.That(category.IsEnabled, Is.True, "категория выключена");
                Assert.That(tool.IsHitTestVisible, Is.True, "по активности не попасть мышью");
                Assert.That(tool.IsEnabled, Is.True, "активность выключена");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Toolbox_SelectedToolAppearsAfterSelection()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeView tree = FindVisualChild<TreeView>(toolbox);
            Assert.That(tree, Is.Not.Null);

            TreeViewItem category = FirstContainer(tree);
            Assert.That(category, Is.Not.Null);
            category.IsExpanded = true;
            Pump(window);

            TreeViewItem tool = FirstContainer(category);
            Assert.That(tool, Is.Not.Null);

            tool.IsSelected = true;
            Pump(window);

            // Предусловие WF: OnToolMouseMove вызывает DoDragDrop только при SelectedTool != null.
            Assert.That(toolbox.SelectedTool, Is.Not.Null,
                "выделение не доходит до ToolboxControl.SelectedTool — перетаскивание не начнётся");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Themer_DoesNotOverrideItemContainerStyleOfSelectorBasedTree()
    {
        // ItemContainerStyleSelector (тулбокс WF) сильнее ItemContainerStyle: подмена стиля
        // выбранного элемента лишает его шаблона WF. Поэтому темизатор обязан отступить,
        // если стили элементов выбирает selector.
        var tree = new TreeView();
        var selector = new FakeStyleSelector();
        tree.ItemContainerStyleSelector = selector;

        WfVisualTreeThemer.Apply(tree, StudioPalette.Dark);

        Assert.That(tree.ItemContainerStyle, Is.Null,
            "темизатор выставил ItemContainerStyle поверх ItemContainerStyleSelector — стили WF потеряются");
    }

    // --- Инфраструктура. ---

    private static (ToolboxControl Toolbox, Window Window) HostThemedToolbox()
    {
        ToolboxControl toolbox = BuildToolbox();
        ToolboxPanel panel = new ToolboxPanel();

        // Ровно тот путь, что и в студии (MainView): панель + SetToolboxControl (включает темизацию).
        panel.SetToolboxControl(toolbox);

        Window window = new Window
        {
            Width = 300,
            Height = 400,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = panel
        };

        window.Show();
        Pump(window);

        return (toolbox, window);
    }

    private static ToolboxControl BuildToolbox()
    {
        ToolboxControl toolbox = new ToolboxControl();

        ToolboxCategory category = new ToolboxCategory("Flow")
        {
            new ToolboxItemWrapper(typeof(Sequence), null, "Sequence"),
            new ToolboxItemWrapper(typeof(WriteLine), null, "WriteLine")
        };

        toolbox.Categories.Add(category);
        return toolbox;
    }

    /// <summary>Доводит раскладку и очередь диспетчера до состояния «контейнеры созданы».</summary>
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        window.UpdateLayout();
    }

    private static TreeViewItem FirstContainer(ItemsControl parent)
    {
        if (parent == null)
        {
            return null;
        }

        parent.UpdateLayout();

        // Предметные контейнеры создаются генератором при раскладке.
        return parent.ItemContainerGenerator
            .ContainerFromIndex(0) as TreeViewItem;
    }

    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        return XamlHelper.FindVisualChildren<T>(parent).FirstOrDefault();
    }

    private sealed class FakeStyleSelector : StyleSelector
    {
        public override Style SelectStyle(object item, DependencyObject container)
        {
            return new Style(typeof(TreeViewItem));
        }
    }
}
