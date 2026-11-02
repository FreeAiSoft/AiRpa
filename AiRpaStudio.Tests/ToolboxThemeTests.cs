using System.Activities.Presentation.Toolbox;
using System.Activities.Statements;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RPA.Controls;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Панель «Активности» (WF <see cref="ToolboxControl"/>) в тёмной теме: под названиями категорий
/// оставалась светлая плашка.
/// <para>
/// Причина: WF-стиль категории задаёт контейнеру светлый <c>Background</c> (#E9ECEE), а шаблон элемента
/// повторяет его через <c>TemplateBinding</c> (<c>Border#Bd</c>). Дополнение системных кистей
/// (<see cref="StudioBrushes"/>) на такое значение не влияет — цвет зашит в стиль, а не взят из
/// <c>SystemColors</c>. Состояния (hover/выделение) рисует тема Aero2 светло-жёлтой плашкой (#FFE8A6).
/// Стиль подменять нельзя (в нём шаблон элемента и обработчики перетаскивания,
/// см. <see cref="ToolboxDragContractTests"/>), поэтому светлые кисти гасятся по эффективным значениям
/// (<see cref="WfThemeStyles.NeutralizeLightBrushes"/>), а состояния рисуются палитрой.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class ToolboxThemeTests
{
    [Test]
    public void CategoryHeader_HasNoLightPlates_DarkTheme()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            Assert.That(category, Is.Not.Null, "категория тулбокса не материализована");

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(category.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "фон заголовка категории — светлая плашка WF-стиля (#E9ECEE), а не поверхность палитры");
                Assert.That(LightBrushesIn(category, StudioPalette.Dark), Is.Empty,
                    "светлые кисти под названием категории: " + string.Join(", ", LightBrushesIn(category, StudioPalette.Dark)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CategoryHeader_NameIsReadableOnDarkSurface()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            List<TextBlock> captions = XamlHelper.FindVisualChildren<TextBlock>(category)
                .Where(text => !string.IsNullOrWhiteSpace(text.Text))
                .ToList();

            Assert.That(captions, Is.Not.Empty, "заголовок категории не найден");

            foreach (TextBlock caption in captions)
            {
                Color colour = ColorOf(caption.Foreground);
                Assert.That(colour, Is.EqualTo(StudioPalette.Dark.Foreground),
                    $"текст «{caption.Text}» не из палитры");
                Assert.That(Luminance(colour), Is.GreaterThan(Luminance(StudioPalette.Dark.Surface) + 0.1),
                    $"текст «{caption.Text}» не читается на поверхности тёмной темы");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CategoryHeader_ExpanderGlyphIsVisible()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            Assert.That(category, Is.Not.Null);

            // Стрелка раскрытия категории закрашена системным цветом шаблона (#595959/#262626):
            // на тёмной поверхности её не видно, поэтому она обязана получать кисть палитры.
            List<System.Windows.Shapes.Shape> glyphs = XamlHelper
                .FindVisualChildren<System.Windows.Shapes.Shape>(category)
                .Where(shape => shape.ActualWidth > 0 || shape.Width > 0)
                .ToList();

            Assert.That(glyphs, Is.Not.Empty, "стрелка раскрытия не найдена");

            foreach (System.Windows.Shapes.Shape glyph in glyphs)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(IsPaletteColour(glyph.Fill, StudioPalette.Dark), Is.True,
                        $"заливка стрелки раскрытия не из палитры: {Describe(glyph.Fill)}");
                    Assert.That(IsPaletteColour(glyph.Stroke, StudioPalette.Dark), Is.True,
                        $"обводка стрелки раскрытия не из палитры: {Describe(glyph.Stroke)}");
                });
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ToolItems_GetPaletteStateInsteadOfSystemHover()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            category.IsExpanded = true;
            Pump(window);

            TreeViewItem tool = FirstContainer(category);
            Assert.That(tool, Is.Not.Null, "активность категории не материализована");

            Assert.Multiple(() =>
            {
                Assert.That(LightBrushesIn(tool, StudioPalette.Dark), Is.Empty,
                    "светлые кисти в строке активности: " + string.Join(", ", LightBrushesIn(tool, StudioPalette.Dark)));
                Assert.That(ColorOf(tool.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            });

            // Стиль элемента выбрал WF, триггеров палитры в нём нет: состояние (hover/выделение) рисовала
            // системная тема светло-жёлтой плашкой. Теперь состояние берётся из палитры.
            tool.IsSelected = true;
            Pump(window);

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(tool.Background), Is.EqualTo(StudioPalette.Dark.Selected),
                    "выделение активности осталось системным (#FFE8A6) вместо палитры");
                Assert.That(LightBrushesIn(tool, StudioPalette.Dark), Is.Empty,
                    "под выделенной активностью светлая плашка: " + string.Join(", ", LightBrushesIn(tool, StudioPalette.Dark)));
            });

            tool.IsSelected = false;
            Pump(window);

            Assert.That(((SolidColorBrush)tool.Background).Color.A, Is.EqualTo(0),
                "нейтральная строка активности должна быть прозрачной, а не залитой фоном");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SelectedCategory_HasNoLightPlateAndUsesPalette()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);

            category.IsSelected = true;
            Pump(window);

            Assert.Multiple(() =>
            {
                Assert.That(LightBrushesIn(category, StudioPalette.Dark), Is.Empty,
                    "под выделенным заголовком категории светлая плашка: "
                    + string.Join(", ", LightBrushesIn(category, StudioPalette.Dark)));
                Assert.That(ColorOf(category.Background), Is.EqualTo(StudioPalette.Dark.Selected),
                    "выделение категории рисуется системным цветом, а не палитрой");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ThemeSwitch_RepaintsPlatesInBothDirections()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            Assert.That(ColorOf(category.Background), Is.EqualTo(StudioPalette.Dark.Surface), "тёмная тема");

            WfVisualTreeThemer.Apply(toolbox, StudioPalette.Light);
            Pump(window);
            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(category.Background), Is.EqualTo(StudioPalette.Light.Surface),
                    "светлая тема: на заголовке осталась тёмная плашка предыдущей темы");
                Assert.That(LightBrushesIn(category, StudioPalette.Light), Is.Empty,
                    "светлая тема: " + string.Join(", ", LightBrushesIn(category, StudioPalette.Light)));
            });

            WfVisualTreeThemer.Apply(toolbox, StudioPalette.Dark);
            Pump(window);
            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(category.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "возврат к тёмной теме: плашка не перекрасилась");
                Assert.That(LightBrushesIn(category, StudioPalette.Dark), Is.Empty,
                    "возврат к тёмной теме: " + string.Join(", ", LightBrushesIn(category, StudioPalette.Dark)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ApplyTheme_IsIdempotentForLoadedHook()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            var panel = (ToolboxPanel)window.Content;

            panel.ApplyTheme();
            panel.ApplyTheme();
            Pump(window);

            Assert.That(WfVisualTreeThemer.LoadedHookCount(toolbox), Is.EqualTo(1),
                "повторное применение темы не должно вешать второй обработчик Loaded");
            Assert.That(ColorOf(FirstCategory(toolbox).Background), Is.EqualTo(StudioPalette.Dark.Surface));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Theming_KeepsWfItemStylesAndDragHandlers()
    {
        (ToolboxControl toolbox, Window window) = HostThemedToolbox();

        try
        {
            TreeViewItem category = FirstCategory(toolbox);
            category.IsExpanded = true;
            Pump(window);

            TreeViewItem tool = FirstContainer(category);
            Assert.That(tool, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(WfThemeStyles.IsThemed(category.Style), Is.False,
                    "стиль категории подменён: вместе с ним потерян шаблон WF");
                Assert.That(WfThemeStyles.IsThemed(tool.Style), Is.False,
                    "стиль активности подменён: перетаскивание в дизайнер перестанет начинаться");

                List<string> events = tool.Style.Setters.OfType<System.Windows.EventSetter>()
                    .Select(setter => setter.Event.Name)
                    .ToList();

                Assert.That(events, Does.Contain("MouseMove"), "потерян EventSetter MouseMove (начало перетаскивания)");
                Assert.That(events, Does.Contain("MouseDoubleClick"), "потерян EventSetter MouseDoubleClick");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Sources_NeutralizePlatesWithoutWhiteLiterals()
    {
        foreach (string relativePath in new[]
                 {
                     Path.Combine("AiRpaStudio", "Helpers", "WfVisualTreeThemer.cs"),
                     Path.Combine("AiRpaStudio", "Helpers", "WfThemeStyles.cs"),
                     Path.Combine("AiRpaStudio", "Controls", "ToolboxPanel.xaml.cs")
                 })
        {
            string source = ReadSource(relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Not.Contain("Brushes.White"), $"{relativePath}: белая кисть литералом запрещена");
                Assert.That(source, Does.Not.Contain("Colors.White"), $"{relativePath}: белая кисть литералом запрещена");
                Assert.That(Regex.IsMatch(source, @"#FFFFFF|#ffffff"), Is.False,
                    $"{relativePath}: цвет литералом запрещён — только палитра");
            });
        }

        string themer = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfVisualTreeThemer.cs"));
        Assert.That(themer, Does.Contain("NeutralizeLightBrushes"),
            "контейнеры тулбокса обязаны гасить светлые кисти WF-шаблона (плашка категории)");
        Assert.That(themer, Does.Contain("HookItemStates"),
            "состояния элементов с WF-стилем обязаны рисоваться палитрой, а не темой Aero2");
    }

    // --- Инфраструктура. ---

    private static (ToolboxControl Toolbox, Window Window) HostThemedToolbox()
    {
        ToolboxControl toolbox = new ToolboxControl();
        ToolboxCategory category = new ToolboxCategory("Flow")
        {
            new ToolboxItemWrapper(typeof(Sequence), null, "Sequence"),
            new ToolboxItemWrapper(typeof(WriteLine), null, "WriteLine")
        };
        toolbox.Categories.Add(category);

        ToolboxPanel panel = new ToolboxPanel();
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

    private static TreeViewItem FirstCategory(ToolboxControl toolbox)
    {
        TreeView tree = XamlHelper.FindVisualChildren<TreeView>(toolbox).FirstOrDefault();
        Assert.That(tree, Is.Not.Null, "в ToolboxControl нет PART_Tools");
        return FirstContainer(tree);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        window.UpdateLayout();
    }

    private static TreeViewItem FirstContainer(ItemsControl parent)
    {
        parent?.UpdateLayout();
        return parent?.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
    }

    /// <summary>
    /// Светлые артефакты поддерева: кисть не из палитры и заметно светлее поверхности.
    /// <c>Foreground</c> не проверяется — текст темизируется отдельно; прозрачные кисти не рисуют плашку.
    /// </summary>
    private static List<string> LightBrushesIn(DependencyObject root, StudioThemeColors palette)
    {
        var violations = new List<string>();
        double surface = Luminance(palette.Surface);
        HashSet<Color> paletteColors = new(palette.Tokens.Values);

        foreach (DependencyObject element in SelfAndDescendants(root))
        {
            foreach (DependencyProperty property in WfDesignerTheme.BrushPropertiesOf(element.GetType()))
            {
                if (property.Name == "Foreground" || !(element.GetValue(property) is Brush brush))
                {
                    continue;
                }

                if (brush is SolidColorBrush solid)
                {
                    if (IsLightArtifact(solid.Color, surface, paletteColors))
                    {
                        violations.Add($"{element.GetType().Name}.{property.Name}={solid.Color}");
                    }

                    continue;
                }

                if (brush is GradientBrush gradient
                    && gradient.GradientStops.Any(stop => IsLightArtifact(stop.Color, surface, paletteColors)))
                {
                    violations.Add($"{element.GetType().Name}.{property.Name}=gradient");
                }
            }
        }

        return violations;
    }

    private static bool IsLightArtifact(Color colour, double surfaceLuminance, HashSet<Color> paletteColors)
    {
        return colour.A != 0
            && !paletteColors.Contains(colour)
            && Luminance(colour) > surfaceLuminance + 0.25;
    }

    private static bool IsPaletteColour(Brush brush, StudioThemeColors palette)
    {
        return brush is SolidColorBrush solid
            && solid.Color.A != 0
            && palette.Tokens.Values.Contains(solid.Color);
    }

    private static string Describe(Brush brush)
    {
        return brush is SolidColorBrush solid ? solid.Color.ToString() : brush?.GetType().Name ?? "<null>";
    }

    private static IEnumerable<DependencyObject> SelfAndDescendants(DependencyObject root)
    {
        if (root == null)
        {
            yield break;
        }

        yield return root;

        foreach (DependencyObject descendant in XamlHelper.FindVisualChildren<DependencyObject>(root))
        {
            yield return descendant;
        }
    }

    /// <summary>Относительная яркость цвета (WCAG): 0 — чёрный, 1 — белый.</summary>
    private static double Luminance(Color colour)
    {
        return (0.2126 * LinearChannel(colour.R)) + (0.7152 * LinearChannel(colour.G)) + (0.0722 * LinearChannel(colour.B));
    }

    private static double LinearChannel(byte value)
    {
        double channel = value / 255d;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>(), "ожидалась solid-кисть палитры");
        return ((SolidColorBrush)brush).Color;
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
