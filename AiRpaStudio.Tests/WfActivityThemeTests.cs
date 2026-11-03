using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

using NUnit.Framework;

using RPA.Helpers;

using IOPath = System.IO.Path;
using Path = System.Windows.Shapes.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Тема <b>активностей</b> дизайнера WF (<see cref="WfActivityTheme"/>).
/// <para>
/// Регресс, ради которого появился проход: при первом включении тёмной темы активности оставались
/// светлыми. Шаблон WF (<c>WorkflowElementDesignerDefaultTemplate.xaml</c>) берёт цвета из
/// <c>ColorResources.xaml</c>, который WF подмешивает в <c>ControlTemplate.Resources</c> своего шаблона;
/// этот словарь ближе к элементу, чем словари приложения/дизайнера, поэтому ключи
/// <c>WorkflowViewElementBackground</c>/<c>DesignerViewBackground</c> до активностей не доходят.
/// Кисти WF заморожены, а сам словарь после загрузки шаблонов становится read-only — менять его нельзя.
/// Поэтому тема ставится локальными значениями на элементы шаблона.
/// </para>
/// <para>
/// Вторая часть — контролы внутри активности: неявные стили MaterialDesign из <c>App.xaml</c>
/// (<c>Button</c>/<c>CheckBox</c>/<c>TextBox</c>…) перебивали нативную форму WF (регресс: <c>CheckBox</c>
/// из <c>ActivityTop</c> 29×29, кнопки «Развернуть все» — <c>Width = 34</c> вместо подписи).
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class WfActivityThemeTests
{
    // --- «Нативные» стили контролов: гасят неявные стили MaterialDesign. ---

    [Test]
    public void UseNativeControlStyles_AddsEmptyStubsForControlsMaterialDesignHijacks()
    {
        ResourceDictionary resources = new ResourceDictionary();

        WfActivityTheme.UseNativeControlStyles(resources);

        foreach (Type type in new[]
        {
            typeof(Button),
            typeof(RepeatButton),
            typeof(ToggleButton),
            typeof(CheckBox),
            typeof(RadioButton),
            typeof(TextBox),
            typeof(ComboBox),
            typeof(ComboBoxItem)
        })
        {
            Assert.That(resources[type], Is.InstanceOf<Style>(), type.Name + ": нужен неявный стиль-заглушка");
            var style = (Style)resources[type];
            Assert.Multiple(() =>
            {
                Assert.That(style.TargetType, Is.EqualTo(type));
                Assert.That(style.Setters, Is.Empty,
                    "заглушка не задаёт ни формы, ни шрифта: шаблон остаётся нативным (WF/Aero2)");
            });
        }
    }

    [Test]
    public void UseNativeControlStyles_DoesNotTouchScrollBars()
    {
        // Плоские скроллы 10 px задаёт Themes\StudioScrollBar.xaml: заглушка вернула бы системную полосу.
        ResourceDictionary resources = new ResourceDictionary();
        WfActivityTheme.UseNativeControlStyles(resources);

        Assert.Multiple(() =>
        {
            Assert.That(resources.Contains(typeof(ScrollBar)), Is.False,
                "стиль скроллбара не должен появляться в словаре заглушек");
            Assert.That(FlatStyle(resources), Is.Null, "заглушка для ScrollBar не создаётся");
        });
    }

    [Test]
    public void UseNativeControlStyles_IsIdempotent()
    {
        ResourceDictionary resources = new ResourceDictionary();

        WfActivityTheme.UseNativeControlStyles(resources);
        object first = resources[typeof(Button)];
        WfActivityTheme.UseNativeControlStyles(resources);

        Assert.Multiple(() =>
        {
            Assert.That(resources.Contains(WfActivityTheme.NativeStylesMarker), Is.True);
            Assert.That(resources[typeof(Button)], Is.SameAs(first), "повторный вызов не пересоздаёт стили");
        });
    }

    [Test]
    public void UseNativeControlStyles_KeepsExplicitStyleOfDictionary()
    {
        ResourceDictionary resources = new ResourceDictionary();
        var ownStyle = new Style(typeof(Button));
        ownStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1)));
        resources[typeof(Button)] = ownStyle;

        WfActivityTheme.UseNativeControlStyles(resources);

        Assert.That(resources[typeof(Button)], Is.SameAs(ownStyle),
            "стиль, уже заданный словарём (тема WF или наш), не перебивается");
    }

    [Test]
    public void CreateNativeStyle_NullType_ReturnsNull()
    {
        Assert.That(WfActivityTheme.CreateNativeStyle(null), Is.Null);
    }

    // --- Покраска плиты активности, рамки и содержимого. ---

    [Test]
    public void RepaintActivity_DarkTheme_PaintsPlateOutlineAndText()
    {
        // Синтетическая копия структуры WorkflowElementDesignerDefaultTemplate.xaml:
        // Grid на DesignerViewBackground, полоса заголовка на WorkflowViewElementBackground,
        // скруглённый Rectangle — рамка активности, глиф раскрытия и подпись.
        (Grid activity, Grid plate, Border titleBar, Rectangle outline, Path glyph, TextBlock caption, TextBox field) =
            BuildActivityVisual();

        WfActivityTheme.RepaintActivity(activity, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(plate.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                "плита активности должна быть цветом поверхности");
            Assert.That(ColorOf(titleBar.Background), Is.EqualTo(StudioPalette.Dark.SurfaceAlt),
                "полоса заголовка — на тон светлее плиты");
            Assert.That(ColorOf(titleBar.BorderBrush), Is.EqualTo(StudioPalette.Dark.SeparatorStrong),
                "линия под заголовком должна быть видна в тёмной теме");
            Assert.That(ColorOf(outline.Fill), Is.EqualTo(StudioPalette.Dark.Surface),
                "под обводкой не должно оставаться светлой плиты");
            Assert.That(ColorOf(outline.Stroke), Is.EqualTo(StudioPalette.Dark.SeparatorStrong),
                "рамка активности — линия палитры");
            Assert.That(ColorOf(glyph.Fill), Is.EqualTo(StudioPalette.Dark.ForegroundMuted),
                "глиф раскрытия (системный чёрный) не читается на тёмной плите");
            Assert.That(ColorOf(caption.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground),
                "буквы подписи должны быть светлыми");
            Assert.That(ColorOf(field.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(field.BorderBrush), Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
            Assert.That(ColorOf(field.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
        });
    }

    [Test]
    public void RepaintActivity_ReturnsToLightTheme()
    {
        // Регресс round-trip: значения ставятся локально, поэтому обратный проход обязан их вернуть —
        // иначе на светлой теме остались бы тёмные плашки и белые буквы.
        (Grid activity, Grid plate, Border titleBar, Rectangle outline, Path glyph, TextBlock caption, TextBox field) =
            BuildActivityVisual();

        WfActivityTheme.RepaintActivity(activity, StudioPalette.Dark);
        WfActivityTheme.RepaintActivity(activity, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(plate.Background), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(titleBar.Background), Is.EqualTo(StudioPalette.Light.SurfaceAlt));
            Assert.That(ColorOf(outline.Stroke), Is.EqualTo(StudioPalette.Light.SeparatorStrong));
            Assert.That(ColorOf(caption.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(ColorOf(field.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
        });
    }

    [Test]
    public void RepaintActivity_KeepsDataBoundForeground()
    {
        // Подпись активности может красить конвертер модели: локальное значение разорвало бы привязку.
        (Grid activity, _, _, _, _, TextBlock caption, _) = BuildActivityVisual();
        var source = new CaptionSource { Colour = Brushes.Black };
        caption.ClearValue(TextBlock.ForegroundProperty);
        caption.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(CaptionSource.Colour)) { Source = source });

        WfActivityTheme.RepaintActivity(activity, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(BindingOperations.IsDataBound(caption, TextBlock.ForegroundProperty), Is.True,
                "привязка подписи не должна разрываться темизацией");
            Assert.That(ColorOf(caption.Foreground), Is.EqualTo(Colors.Black), "значение приходит из модели");
        });
    }

    [Test]
    public void RepaintActivity_NullArguments_DoNotThrow()
    {
        Assert.DoesNotThrow(() => WfActivityTheme.RepaintActivity(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfActivityTheme.RepaintActivity(new Grid(), null));
    }

    [Test]
    public void ThemeActivities_NullArguments_DoNotThrow()
    {
        Assert.DoesNotThrow(() => WfActivityTheme.ThemeActivities(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfActivityTheme.ThemeActivities(new Grid(), null));
        Assert.DoesNotThrow(() => WfActivityTheme.Apply(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfActivityTheme.Apply(new Grid(), null));
    }

    // --- Стражи по исходникам: правило «внутри активности — палитра, форма WF нативная». ---

    [Test]
    public void Sources_PaletteProtectionUsesNeutralAndTextTokensOnly()
    {
        // Регресс: защита «это уже палитра» по всем токенам (palette.Tokens.Values) считала белым
        // семантический CloseHoverForeground = #FFFFFF, и плита активности оставалась белой.
        string source = ReadSource(IOPath.Combine("AiRpaStudio", "Helpers", "WfActivityTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("StudioPalette.NeutralTokenNames"),
                "набор «уже покрашено» строится по нейтральным и текстовым токенам");
            Assert.That(source, Does.Not.Contain("palette.Tokens.Values"),
                "все токены включать нельзя: белый/красный семантики совпадают с системными цветами WF");
            Assert.That(source, Does.Contain("BindingOperations.IsDataBound"),
                "привязки модели не разрываются");
        });
    }

    [Test]
    public void Sources_DesignerThemeAppliesActivityTheme()
    {
        string source = ReadSource(IOPath.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("WfActivityTheme.Apply(root, palette);"),
                "активности должны темизироваться общим проходом дизайнера");
            Assert.That(source, Does.Contain("WfActivityTheme.UseNativeControlStyles(designerView.Resources, palette);"),
                "кнопки шапки дизайнера (expandAll/collapseAll) тоже не должны получать стиль MaterialDesign");
        });
    }

    [Test]
    public void Sources_ActivityDesignersHaveNoHardcodedTextBrushes()
    {
        // Чёрный/светло-серый цвет в конвертере давал чёрные буквы на тёмной плите.
        List<string> offenders = new List<string>();

        foreach (string directory in new[] { "ActivityLibraryBase", "Activities", "Activities.Debug", "Activities.Flow", "Activities.OS", "Activities.Browser", "Rpa.Activities.Primitives" })
        {
            foreach (string file in SourceFiles(IOPath.Combine(FindRepositoryRoot(), directory)))
            {
                string text = File.ReadAllText(file);

                if (Regex.IsMatch(text, @"Brushes\.(Black|LightGray|White|Gray)\b"))
                {
                    offenders.Add(file);
                }
            }
        }

        Assert.That(offenders, Is.Empty, "цвета только из палитры Studio.*: " + string.Join(", ", offenders));
    }

    [Test]
    public void Sources_ActivityTopCaptionIsNotColouredByConverter()
    {
        string xaml = ReadSource(IOPath.Combine("ActivityLibraryBase", "Controls", "ActivityTop.xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Not.Contain("BoolToColorConverter"),
                "цвет подписи задаёт темизатор дизайнера; конвертер возвращал зашитый чёрный");
            Assert.That(xaml, Does.Contain("Property=\"Opacity\""),
                "выключенная активность показывается прозрачностью, а не цветом");
        });
    }

    // --- Вспомогательное. ---

    private static Style FlatStyle(ResourceDictionary resources)
    {
        return resources[typeof(ScrollBar)] as Style;
    }

    private static (Grid Activity, Grid Plate, Border TitleBar, Rectangle Outline, Path Glyph, TextBlock Caption, TextBox Field)
        BuildActivityVisual()
    {
        var plate = new Grid { Background = Brushes.White };
        var titleBar = new Border { Background = FromHex("#E9ECEE"), BorderBrush = FromHex("#D0D7E2"), BorderThickness = new Thickness(0, 0, 0, 1) };
        var outline = new Rectangle { RadiusX = 3, RadiusY = 3, Fill = Brushes.White, Stroke = FromHex("#D0D7E2") };
        var glyph = new Path { Fill = Brushes.Black, Stroke = Brushes.Black };
        var caption = new TextBlock { Text = "Logger", Foreground = FromHex("#1B293E") };
        var field = new TextBox { Background = Brushes.White, BorderBrush = FromHex("#D0D7E2"), Foreground = Brushes.Black };

        var activity = new Grid();
        activity.Children.Add(plate);
        plate.Children.Add(outline);
        plate.Children.Add(titleBar);
        plate.Children.Add(glyph);
        plate.Children.Add(caption);
        plate.Children.Add(field);

        // Полоса заголовка в шаблоне WF тонкая — по этой высоте темизатор отличает её от плиты.
        titleBar.Measure(new Size(300, 22));
        titleBar.Arrange(new Rect(0, 0, 300, 22));

        return (activity, plate, titleBar, outline, glyph, caption, field);
    }

    private static Brush FromHex(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex);
    }

    private static Color? ColorOf(Brush brush)
    {
        return brush is SolidColorBrush solid ? solid.Color : (Color?)null;
    }

    private static IEnumerable<string> SourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains("\\obj\\") || file.Contains("\\bin\\"))
            {
                continue;
            }

            string extension = IOPath.GetExtension(file);

            if (extension == ".cs" || extension == ".xaml")
            {
                yield return file;
            }
        }
    }

    private static string ReadSource(string relativePath)
    {
        string path = IOPath.Combine(FindRepositoryRoot(), relativePath);

        Assert.That(File.Exists(path), Is.True, "не найден файл: " + path);

        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");

        return directory.FullName;
    }

    /// <summary>Источник значения для привязки подписи (модель активности в тесте).</summary>
    private sealed class CaptionSource
    {
        public Brush Colour { get; set; }
    }
}
