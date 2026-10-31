using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
// NB: System.Windows.Shapes не подключаем — Path конфликтует с System.IO.Path.
using System.Windows.Data;

using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 6 плана (docs\Vs2026_Theme_Plan.md, §8 п.6, F-01…F-03): темизация нативных WF-контролов.
/// <para>
/// Требования этапа: значения берутся только из <see cref="StudioPalette"/> (никакого акцента
/// MaterialDesign), кисти заморожены, состояние в <c>ToolboxControl</c>/<c>OutlineView</c> передаётся
/// триггерами стиля (<c>IsMouseOver</c> → Hover, <c>IsSelected</c> → Selected), шрифт — Segoe UI
/// вместо Consolas, а <see cref="PropertyWindowStyleHelper"/> ищет элементы по именам
/// (<c>_propertyToolBar</c>, <c>PART_ByCategoryViewButton</c>, <c>PART_AlphaViewButton</c>,
/// <c>PART_SearchClearButton</c>) и перекрашивается при смене темы без пересоздания контрола.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class WfThemeTests
{
    // --- Общие примитивы темизации. ---

    [Test]
    public void Font_IsSegoeUi()
    {
        Assert.That(WfThemeStyles.FontFamilyName, Is.EqualTo("Segoe UI"));
        Assert.That(WfThemeStyles.Font.Source, Is.EqualTo("Segoe UI"));
    }

    [Test]
    public void Brush_IsFrozen()
    {
        SolidColorBrush brush = WfThemeStyles.Brush(StudioPalette.Dark.Surface);

        Assert.That(brush.IsFrozen, Is.True);
        Assert.That(brush.Color, Is.EqualTo(StudioPalette.Dark.Surface));
    }

    [Test]
    public void ThemedStyle_MarkerIsInheritedByBasedOn()
    {
        var baseStyle = new Style(typeof(TreeViewItem));
        WfThemeStyles.MarkThemed(baseStyle);

        var derived = new Style(typeof(TreeViewItem), baseStyle);

        Assert.That(WfThemeStyles.IsThemed(baseStyle), Is.True);
        Assert.That(WfThemeStyles.IsThemed(derived), Is.True, "стиль, унаследованный от размеченного, тоже считается нашим");
        Assert.That(WfThemeStyles.IsThemed(new Style(typeof(TreeViewItem))), Is.False);
        Assert.That(WfThemeStyles.IsThemed(null), Is.False);
    }

    // --- F-01/F-02: обход визуального дерева. ---

    [Test]
    public void Apply_NullRootAndPalette_DoNotThrow()
    {
        Assert.DoesNotThrow(() => WfVisualTreeThemer.Apply(null));
        Assert.DoesNotThrow(() => WfVisualTreeThemer.Apply(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfVisualTreeThemer.Apply(new TreeView(), null));
    }

    [Test]
    public void Apply_SetsRootBackground()
    {
        var root = new Border();
        WfVisualTreeThemer.Apply(root, StudioPalette.Dark);

        Assert.That(ColorOf(root.Background), Is.EqualTo(StudioPalette.Dark.Surface));
        Assert.That(root.Background.IsFrozen, Is.True);
    }

    [Test]
    public void Apply_SetsTreeViewBrushes()
    {
        var tree = new TreeView();

        WfVisualTreeThemer.Apply(tree, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(tree.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(tree.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(ColorOf(tree.BorderBrush), Is.EqualTo(StudioPalette.Dark.Separator));
            Assert.That(tree.FontFamily.Source, Is.EqualTo(WfThemeStyles.FontFamilyName));
        });
    }

    [Test]
    public void Apply_SwitchesBrushesOnPaletteChange()
    {
        var tree = new TreeView();

        WfVisualTreeThemer.Apply(tree, StudioPalette.Dark);
        Assert.That(ColorOf(tree.Background), Is.EqualTo(StudioPalette.Dark.Surface));

        WfVisualTreeThemer.Apply(tree, StudioPalette.Light);
        Assert.That(ColorOf(tree.Background), Is.EqualTo(StudioPalette.Light.Surface));
    }

    [Test]
    public void Apply_SetsItemContainerStyleWithStateTriggers()
    {
        var tree = new TreeView();

        WfVisualTreeThemer.Apply(tree, StudioPalette.Dark);

        Style style = tree.ItemContainerStyle;

        Assert.That(style, Is.Not.Null);
        Assert.That(style.TargetType, Is.EqualTo(typeof(TreeViewItem)));

        Trigger selected = TriggersOf(style).OfType<Trigger>().FirstOrDefault(t => t.Property == TreeViewItem.IsSelectedProperty);
        Trigger hover = TriggersOf(style).OfType<Trigger>().FirstOrDefault(t => t.Property == UIElement.IsMouseOverProperty);

        Assert.That(selected, Is.Not.Null, "нет триггера IsSelected — выделение останется системным (акцентным)");
        Assert.That(hover, Is.Not.Null, "нет триггера IsMouseOver");

        Assert.That(SetterColor(selected, Control.BackgroundProperty), Is.EqualTo(StudioPalette.Dark.Selected));
        Assert.That(SetterColor(hover, Control.BackgroundProperty), Is.EqualTo(StudioPalette.Dark.Hover));
    }

    [Test]
    public void Apply_KeepsExistingItemContainerStyleAsBase()
    {
        var tree = new TreeView();
        var toolboxItemStyle = new Style(typeof(TreeViewItem));
        toolboxItemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 22d));
        tree.ItemContainerStyle = toolboxItemStyle;

        WfVisualTreeThemer.Apply(tree, StudioPalette.Dark);

        Assert.That(tree.ItemContainerStyle, Is.Not.SameAs(toolboxItemStyle), "стиль панели должен быть обёрнут, а не перезаписан");
        Assert.That(tree.ItemContainerStyle.BasedOn, Is.SameAs(toolboxItemStyle), "шаблон элемента Toolbox (иконка активити) должен сохраниться");
    }

    [Test]
    public void Apply_IsIdempotentForItemContainerStyle()
    {
        var tree = new TreeView();

        WfVisualTreeThemer.Apply(tree, StudioPalette.Dark);
        Style first = tree.ItemContainerStyle;

        WfVisualTreeThemer.Apply(tree, StudioPalette.Dark);

        Assert.That(tree.ItemContainerStyle, Is.SameAs(first), "повторная темизация не должна наращивать цепочку BasedOn");
    }

    [Test]
    public void Apply_ThemesAlreadyRealizedTreeViewItems()
    {
        var panel = new StackPanel();
        var item = new TreeViewItem();
        panel.Children.Add(item);

        WfVisualTreeThemer.Apply(panel, StudioPalette.Dark);

        Assert.That(item.Style, Is.Not.Null);
        Assert.That(ColorOf(item.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
    }

    [Test]
    public void Apply_SetsTextBoxBrushes()
    {
        var panel = new StackPanel();
        var textBox = new TextBox();
        panel.Children.Add(textBox);

        WfVisualTreeThemer.Apply(panel, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(textBox.Background), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(textBox.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(ColorOf(textBox.CaretBrush), Is.EqualTo(StudioPalette.Light.Foreground));
        });
    }

    [Test]
    public void ApplyWhenLoaded_AppliesImmediatelyAndStaysRepeatable()
    {
        var tree = new TreeView();

        WfVisualTreeThemer.ApplyWhenLoaded(tree, StudioPalette.Light);
        Assert.That(ColorOf(tree.Background), Is.EqualTo(StudioPalette.Light.Surface));

        // Повторный вызов (смена темы) не вешает второй обработчик, но перекрашивает дерево.
        WfVisualTreeThemer.ApplyWhenLoaded(tree, StudioPalette.Dark);
        Assert.That(ColorOf(tree.Background), Is.EqualTo(StudioPalette.Dark.Surface));
        Assert.That(WfVisualTreeThemer.LoadedHookCount(tree), Is.EqualTo(1));
    }

    [Test]
    public void Apply_DoesNotRewriteIncompatibleContainerStyle()
    {
        // Стиль элемента Toolbox может иметь TargetType наследника TreeViewItem: обёртка через BasedOn
        // невозможна (WPF бросает на несовпадении TargetType), поэтому стиль остаётся нетронутым.
        var tree = new TreeView();
        var toolboxStyle = new Style(typeof(CustomTreeItem));
        toolboxStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 22d));
        tree.ItemContainerStyle = toolboxStyle;

        Assert.DoesNotThrow(() => WfVisualTreeThemer.Apply(tree, StudioPalette.Dark));
        Assert.That(tree.ItemContainerStyle, Is.SameAs(toolboxStyle));
        Assert.That(ColorOf(tree.Background), Is.EqualTo(StudioPalette.Dark.Surface), "кисти самого дерева при этом применяются");
    }


    // --- F-03: PropertyInspectorView. ---

    [Test]
    public void PropertyHelper_FindsToolBarByName()
    {
        (Panel root, _, Border decoy, Border toolBar, _, _, _, _, _) = BuildInspectorTree();

        new PropertyWindowStyleHelper().SetPropertyStyles(root, StudioPalette.Dark);

        Assert.That(ColorOf(toolBar.Background), Is.EqualTo(StudioPalette.Dark.Surface));
        Assert.That(decoy.Background, Is.Null, "поиск позиционного «первого Border» запрещён — фон приманки не трогаем");
    }

    [Test]
    public void PropertyHelper_StylesButtonsByName()
    {
        (Panel root, ToggleButton byCategory, _, _, ToggleButton alpha, Button clear, TextBox search, Border separator, _) = BuildInspectorTree();

        new PropertyWindowStyleHelper().SetPropertyStyles(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(byCategory.Width, Is.EqualTo(PropertyWindowStyleHelper.ToolButtonSize));
            Assert.That(byCategory.Height, Is.EqualTo(PropertyWindowStyleHelper.ToolButtonSize));
            Assert.That(alpha.Width, Is.EqualTo(PropertyWindowStyleHelper.ToolButtonSize));
            Assert.That(clear.Width, Is.EqualTo(PropertyWindowStyleHelper.ClearButtonSize));

            Border byCategoryHost = byCategory.Content as Border;
            Assert.That(byCategoryHost, Is.Not.Null, "кнопка «по категориям» должна получить иконку в рамке");
            Assert.That(byCategoryHost.Child, Is.InstanceOf<Viewbox>());
            Assert.That(ColorOf(byCategoryHost.BorderBrush), Is.EqualTo(StudioPalette.Dark.Separator));

            // Состояния — триггеры стиля, а не обработчики: их ровно три (hover/pressed/checked).
            Assert.That(TriggersOf(byCategoryHost.Style), Has.Count.EqualTo(3));
            Assert.That(DataTriggerFor(byCategoryHost.Style, "IsChecked"), Is.Not.Null);
            Assert.That(DataTriggerFor(byCategoryHost.Style, "IsMouseOver"), Is.Not.Null);

            // Тонкий разделитель — hairline палитры.
            Assert.That(ColorOf(separator.Background), Is.EqualTo(StudioPalette.Dark.Separator));

            // Поле поиска: палитра + триггер фокуса.
            Assert.That(ColorOf(search.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(search.Height, Is.EqualTo(PropertyWindowStyleHelper.SearchTextBoxHeight));
            Assert.That(TriggersOf(search.Style).OfType<Trigger>().Any(t => t.Property == UIElement.IsKeyboardFocusWithinProperty), Is.True);
        });
    }

    [Test]
    public void PropertyHelper_IsIdempotent()
    {
        (Panel root, ToggleButton byCategory, _, _, _, _, _, _, _) = BuildInspectorTree();
        var helper = new PropertyWindowStyleHelper();

        helper.SetPropertyStyles(root, StudioPalette.Dark);
        helper.SetPropertyStyles(root, StudioPalette.Dark);

        Border host = (Border)byCategory.Content;
        Assert.That(host.Child, Is.InstanceOf<Viewbox>());
        Assert.That(TriggersOf(host.Style), Has.Count.EqualTo(3), "повторное применение не должно дублировать триггеры");
        Assert.That(TriggersOf(byCategory.Template.Triggers).Count, Is.Zero, "триггеры не должны переезжать в шаблон кнопки");
    }

    [Test]
    public void PropertyHelper_ReactsToPaletteChangeWithoutRecreatingControl()
    {
        (Panel root, _, _, Border toolBar, _, _, TextBox search, Border separator, _) = BuildInspectorTree();
        var helper = new PropertyWindowStyleHelper();

        helper.SetPropertyStyles(root, StudioPalette.Dark);
        helper.SetPropertyStyles(root, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(toolBar.Background), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(separator.Background), Is.EqualTo(StudioPalette.Light.Separator));
            Assert.That(ColorOf(search.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(helper.Palette, Is.SameAs(StudioPalette.Light));
        });
    }

    [Test]
    public void PropertyHelper_NullInput_DoesNotThrow()
    {
        var helper = new PropertyWindowStyleHelper();

        Assert.DoesNotThrow(() => helper.SetPropertyStyles(null));
        Assert.DoesNotThrow(() => helper.SetPropertyStyles(new Border(), null));
    }

    [Test]
    public void PropertyHelper_AppliesSegoeUiFont()
    {
        (Panel root, ToggleButton byCategory, _, _, _, _, _, _, TextBlock caption) = BuildInspectorTree();

        new PropertyWindowStyleHelper().SetPropertyStyles(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(byCategory.FontFamily.Source, Is.EqualTo(WfThemeStyles.FontFamilyName));
            Assert.That(caption.FontFamily.Source, Is.EqualTo(WfThemeStyles.FontFamilyName));
        });
    }

    [Test]
    public void PropertyHelper_ExtractsPathDataFromSvg()
    {
        Assert.That(PropertyWindowStyleHelper.ExtractPathData("<svg><path d=\"M0 0L4 4\"/></svg>"), Is.EqualTo("M0 0L4 4"));
        Assert.That(PropertyWindowStyleHelper.ExtractPathData("M0 0L4 4"), Is.EqualTo("M0 0L4 4"));
        Assert.That(PropertyWindowStyleHelper.ExtractPathData(null), Is.Null);
        Assert.That(PropertyWindowStyleHelper.ExtractPathData("   "), Is.Null);
    }

    // --- Стражи по исходникам: акцент и Consolas не должны вернуться. ---

    [Test]
    public void Sources_UseNoAccentPaletteAndNoConsolas()
    {
        foreach (string relativePath in new[]
                 {
                     Path.Combine("AiRpaStudio", "Helpers", "WfVisualTreeThemer.cs"),
                     Path.Combine("AiRpaStudio", "Helpers", "PropertyWindowStyleHelper.cs"),
                     Path.Combine("AiRpaStudio", "Controls", "ToolboxPanel.xaml.cs")
                 })
        {
            string source = ReadSource(relativePath);

            Assert.That(source, Does.Not.Contain("MaterialThemePalette"), $"{relativePath}: акцент MaterialDesign в WF-контролах запрещён");
            Assert.That(source, Does.Not.Contain("Consolas"), $"{relativePath}: шрифт должен быть Segoe UI");
            Assert.That(Regex.IsMatch(source, @"\bDebug\.WriteLine", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                $"{relativePath}: отладочные WriteLine не должны оставаться в темизации");
        }
    }

    [Test]
    public void Sources_AreWiredIntoStudio()
    {
        string mainView = ReadSource(Path.Combine("AiRpaStudio", "Views", "MainView.xaml.cs"));
        Assert.That(mainView, Does.Contain("ApplyWhenLoaded"), "OutlineView должен темизироваться после загрузки визуального дерева");
        Assert.That(mainView, Does.Contain("_propertyStyleHelper"), "PropertyInspectorView перекрашивается повторным применением, а не пересозданием");

        string docs = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "DocGroupHelper.cs"));
        Assert.That(docs, Does.Contain("SetPropertyStyles"));

        string toolbox = ReadSource(Path.Combine("AiRpaStudio", "Controls", "ToolboxPanel.xaml.cs"));
        Assert.That(toolbox, Does.Contain("ApplyWhenLoaded"));
    }

    [Test]
    public void ProjectFile_CompilesHelpers()
    {
        string csproj = ReadSource(Path.Combine("AiRpaStudio", "AiRpaStudio.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain("Helpers\\WfThemeStyles.cs"));
            Assert.That(csproj, Does.Contain("Helpers\\WfVisualTreeThemer.cs"));
            Assert.That(csproj, Does.Contain("Helpers\\PropertyWindowStyleHelper.cs"));
        });
    }

    // --- Построители и помощники. ---

    /// <summary>
    /// Синтетическое дерево PropertyInspectorView: первая приманка-Border (позиционный обход её темизировал),
    /// панель инструментов <c>_propertyToolBar</c>, три кнопки по именам, поле поиска, hairline-разделитель
    /// и TextBlock заголовка.
    /// </summary>
    private sealed class CustomTreeItem : TreeViewItem
    {
    }

    private static (Panel Root, ToggleButton ByCategory, Border Decoy, Border ToolBar, ToggleButton Alpha, Button Clear, TextBox Search, Border Separator, TextBlock Caption) BuildInspectorTree()
    {
        var root = new StackPanel();

        var decoy = new Border { Name = "Decoy", BorderThickness = new Thickness(1) };
        var toolBar = new Border { Name = PropertyWindowStyleHelper.PropertyToolBarName };
        var toolBarPanel = new StackPanel();
        toolBar.Child = toolBarPanel;

        var byCategory = new ToggleButton { Name = PropertyWindowStyleHelper.ByCategoryButtonName };
        var alpha = new ToggleButton { Name = PropertyWindowStyleHelper.AlphaViewButtonName };
        var clear = new Button { Name = PropertyWindowStyleHelper.SearchClearButtonName };
        var caption = new TextBlock { Text = "Activity" };
        var search = new TextBox();
        var separator = new Border { Name = "VerticalSeparator", Width = 1 };

        toolBarPanel.Children.Add(byCategory);
        toolBarPanel.Children.Add(alpha);
        toolBarPanel.Children.Add(clear);
        toolBarPanel.Children.Add(caption);

        root.Children.Add(decoy);
        root.Children.Add(toolBar);
        root.Children.Add(search);
        root.Children.Add(separator);

        return (root, byCategory, decoy, toolBar, alpha, clear, search, separator, caption);
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>());
        return ((SolidColorBrush)brush).Color;
    }

    private static IList<TriggerBase> TriggersOf(Style style)
    {
        Assert.That(style, Is.Not.Null);
        return style.Triggers;
    }

    private static IList<TriggerBase> TriggersOf(TriggerCollection triggers)
    {
        List<TriggerBase> copy = new();
        foreach (TriggerBase trigger in triggers)
        {
            copy.Add(trigger);
        }

        return copy;
    }

    private static Color SetterColor(Trigger trigger, DependencyProperty property)
    {
        Assert.That(trigger, Is.Not.Null);
        Setter setter = trigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == property);
        Assert.That(setter, Is.Not.Null, $"у триггера {property.Name} нет сеттера");
        return ColorOf((Brush)setter.Value);
    }

    private static DataTrigger DataTriggerFor(Style style, string path)
    {
        if (style == null)
        {
            return null;
        }

        foreach (TriggerBase trigger in style.Triggers)
        {
            if (trigger is DataTrigger dataTrigger && dataTrigger.Binding is Binding binding && binding.Path?.Path == path)
            {
                return dataTrigger;
            }
        }

        return null;
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
