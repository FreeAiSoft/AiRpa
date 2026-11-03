using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using AvalonDock;
using RPA.Helpers;
using VibeCoder.Core.Models;
using RPA.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Кисти вкладок AvalonDock (<c>AvalonDock_Base</c>/<c>AvalonDock_Hover</c>/<c>AvalonDock_Active</c>).
/// Активная вкладка берётся из отдельного токена палитры и светлее неактивной.
/// Тема <c>AvalonDock.Themes.Metro\Theme.xaml</c> берёт их по DynamicResource, а в словаре темы они
/// объявлены светлыми (<c>#FAFAFA</c>/<c>#DDDDDD</c>/<c>#CCCCCC</c>) — из-за этого в тёмной теме
/// вкладки панелей «Проекты / Активности / Структура» и вкладки документов оставались светлыми.
/// </summary>
[Apartment(ApartmentState.STA)]
public class DockingThemeBrushesTests
{
    private static readonly Color ExpectedDarkTab = StudioPalette.Dark.SurfaceAlt;
    private static readonly Color ExpectedDarkActive = StudioPalette.Dark.TabActive;
    private static readonly Color ExpectedLightTab = StudioPalette.Light.SurfaceAlt;
    private static readonly Color ExpectedLightActive = StudioPalette.Light.TabActive;

    private static ResourceDictionary MaterialDesignSource(bool dark)
    {
        Color background = dark ? ExpectedDarkActive : ExpectedLightActive;
        Color toolbar = dark ? ExpectedDarkTab : ExpectedLightTab;

        return new ResourceDictionary
        {
            ["MaterialDesign.Brush.Background"] = new SolidColorBrush(background),
            ["MaterialDesign.Brush.Foreground"] = new SolidColorBrush(dark
                ? Color.FromRgb(0xF1, 0xF1, 0xF1)
                : Color.FromRgb(0x22, 0x22, 0x22)),
            ["MaterialDesign.Brush.Card.Background"] = new SolidColorBrush(dark
                ? Color.FromRgb(0x25, 0x25, 0x25)
                : Colors.White),
            ["MaterialDesign.Brush.ToolBar.Background"] = new SolidColorBrush(toolbar)
        };
    }

    private static Color ColorOf(ResourceDictionary target, string key)
    {
        Assert.That(target.Contains(key), Is.True, $"не заполнена кисть {key}");
        Assert.That(target[key], Is.InstanceOf<SolidColorBrush>(), $"{key} должен быть SolidColorBrush");

        var brush = (SolidColorBrush)target[key];
        Assert.That(brush.IsFrozen, Is.True, $"{key} должен быть заморожен");
        return brush.Color;
    }

    /// <summary>Воспринимаемая яркость 0..1 — по ней отличаем тёмные вкладки от светлых.</summary>
    private static double Luminance(Color color)
    {
        return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255d;
    }

    [Test]
    public void Keys_PointToAvalonDockTabBrushes()
    {
        Assert.That(DockingThemeBrushes.TabBackgroundKey, Is.EqualTo("AvalonDock_Base"));
        Assert.That(DockingThemeBrushes.TabHoverKey, Is.EqualTo("AvalonDock_Hover"));
        Assert.That(DockingThemeBrushes.TabActiveKey, Is.EqualTo("AvalonDock_Active"));

        Assert.That(DockingThemeBrushes.Keys, Is.Unique);
        Assert.That(DockingThemeBrushes.Keys, Is.EquivalentTo(new[]
        {
            DockingThemeBrushes.TabBackgroundKey,
            DockingThemeBrushes.TabHoverKey,
            DockingThemeBrushes.TabActiveKey
        }));
    }

    [Test]
    public void ThemeXaml_UsesExactlyTheseKeys()
    {
        string themeXaml = Path.Combine(FindRepositoryRoot(), "AvalonDock.Themes.Metro", "Theme.xaml");
        Assert.That(File.Exists(themeXaml), Is.True, $"не найден {themeXaml}");

        string text = File.ReadAllText(themeXaml);
        foreach (string key in DockingThemeBrushes.Keys)
        {
            Assert.That(text, Does.Contain($"DynamicResource {key}"),
                $"тема AvalonDock больше не использует {key} — проверьте DockingThemeBrushes");
        }
    }

    [Test]
    public void Apply_Dark_MakesTabsDark()
    {
        var target = new ResourceDictionary();

        DockingThemeBrushes.Apply(target, StudioTheme.Dark, MaterialDesignSource(dark: true));

        Color tab = ColorOf(target, DockingThemeBrushes.TabBackgroundKey);
        Color active = ColorOf(target, DockingThemeBrushes.TabActiveKey);
        Color hover = ColorOf(target, DockingThemeBrushes.TabHoverKey);

        Assert.That(Luminance(tab), Is.LessThan(0.5), "неактивная вкладка должна быть тёмной");
        Assert.That(Luminance(active), Is.LessThan(0.5), "активная вкладка должна быть тёмной");
        Assert.That(tab, Is.EqualTo(ExpectedDarkTab));
        // Активная вкладка светлее неактивной — иначе переключатель вкладок выглядит «вдавленным».
        Assert.That(active, Is.EqualTo(ExpectedDarkActive));
        Assert.That(Luminance(active), Is.GreaterThan(Luminance(tab)), "активная вкладка светлее неактивной");
        Assert.That(Luminance(hover), Is.GreaterThan(Luminance(tab)), "hover в тёмной теме светлее базы");
    }

    [Test]
    public void Apply_Light_MakesTabsLight()
    {
        var target = new ResourceDictionary();

        DockingThemeBrushes.Apply(target, StudioTheme.Light, MaterialDesignSource(dark: false));

        Color tab = ColorOf(target, DockingThemeBrushes.TabBackgroundKey);
        Color active = ColorOf(target, DockingThemeBrushes.TabActiveKey);
        Color hover = ColorOf(target, DockingThemeBrushes.TabHoverKey);

        Assert.That(Luminance(tab), Is.GreaterThan(0.5), "неактивная вкладка должна быть светлой");
        Assert.That(tab, Is.EqualTo(ExpectedLightTab));
        Assert.That(active, Is.EqualTo(ExpectedLightActive));
        Assert.That(Luminance(active), Is.GreaterThan(Luminance(tab)), "активная вкладка светлее неактивной");
        Assert.That(Luminance(hover), Is.LessThan(Luminance(tab)), "hover в светлой теме темнее базы");
    }

    [Test]
    public void Apply_WithoutPalette_UsesFallbacks()
    {
        var dark = new ResourceDictionary();
        DockingThemeBrushes.Apply(dark, StudioTheme.Dark, paletteSource: null);

        Assert.That(ColorOf(dark, DockingThemeBrushes.TabBackgroundKey), Is.EqualTo(ExpectedDarkTab));
        Assert.That(ColorOf(dark, DockingThemeBrushes.TabActiveKey), Is.EqualTo(ExpectedDarkActive));

        var light = new ResourceDictionary();
        DockingThemeBrushes.Apply(light, StudioTheme.Light, paletteSource: null);

        Assert.That(ColorOf(light, DockingThemeBrushes.TabBackgroundKey), Is.EqualTo(ExpectedLightTab));
        Assert.That(ColorOf(light, DockingThemeBrushes.TabActiveKey), Is.EqualTo(ExpectedLightActive));
    }

    [Test]
    public void Apply_UsesTargetDictionaryAsPalette_WhenSourceNotGiven()
    {
        var target = MaterialDesignSource(dark: true);

        DockingThemeBrushes.Apply(target, StudioTheme.Dark);

        Assert.That(ColorOf(target, DockingThemeBrushes.TabBackgroundKey), Is.EqualTo(ExpectedDarkTab));
    }

    [Test]
    public void Apply_PrefersPaletteSourceOverTarget()
    {
        // В dockManager.Resources палитры MaterialDesign нет — она в ресурсах приложения.
        var target = new ResourceDictionary
        {
            ["MaterialDesign.Brush.ToolBar.Background"] = new SolidColorBrush(Colors.Magenta),
            ["MaterialDesign.Brush.Background"] = new SolidColorBrush(Colors.Magenta)
        };

        DockingThemeBrushes.Apply(target, StudioTheme.Dark, MaterialDesignSource(dark: true));

        Assert.That(ColorOf(target, DockingThemeBrushes.TabBackgroundKey), Is.EqualTo(ExpectedDarkTab));
        Assert.That(ColorOf(target, DockingThemeBrushes.TabActiveKey), Is.EqualTo(ExpectedDarkActive));
    }

    [Test]
    public void Apply_ReplacesBrushes_SoThemeSwitchTakesEffect()
    {
        var target = new ResourceDictionary();

        DockingThemeBrushes.Apply(target, StudioTheme.Dark, MaterialDesignSource(dark: true));
        Brush darkBrush = (Brush)target[DockingThemeBrushes.TabBackgroundKey];

        DockingThemeBrushes.Apply(target, StudioTheme.Light, MaterialDesignSource(dark: false));
        Brush lightBrush = (Brush)target[DockingThemeBrushes.TabBackgroundKey];

        // Замена экземпляра кисти гарантирует, что DynamicResource-ссылки перерисуются.
        Assert.That(lightBrush, Is.Not.SameAs(darkBrush));
        Assert.That(((SolidColorBrush)lightBrush).Color, Is.EqualTo(ExpectedLightTab));
    }

    [Test]
    public void Apply_StoresNoDeferredResourceReferences()
    {
        var target = new ResourceDictionary();

        DockingThemeBrushes.Apply(target, StudioTheme.Dark, MaterialDesignSource(dark: true));

        foreach (object key in target.Keys)
        {
            object value = target[key];
            Assert.That(value, Is.Not.InstanceOf<MarkupExtension>(), $"{key} содержит markup extension");
            Assert.That(value.GetType().FullName, Does.Not.Contain("ResourceReference"),
                $"{key} содержит отложенную ссылку на ресурс — это причина XamlParseException");
        }
    }

    [Test]
    public void Apply_NullTarget_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => DockingThemeBrushes.Apply(null, StudioTheme.Dark, MaterialDesignSource(dark: true)));
    }

    /// <summary>
    /// Интеграция: AppSettings кладёт кисти вкладок именно в DockingManager.Resources
    /// (собственные записи словаря перекрывают merged-словарь темы Metro).
    /// </summary>
    [Test]
    public void AppSettings_ReapplyDockManagerResources_FillsDockingManagerResources()
    {
        AppSettings settings = AppSettings.GetInstance();
        var manager = new DockingManager();
        settings.SetDockingManager(manager);

        try
        {
            settings.ReapplyDockManagerResources();

            Color tab = ColorOf(manager.Resources, DockingThemeBrushes.TabBackgroundKey);
            Color active = ColorOf(manager.Resources, DockingThemeBrushes.TabActiveKey);

            Assert.That(Luminance(tab), Is.LessThan(0.5), "по умолчанию тема тёмная — вкладки должны быть тёмными");
            Assert.That(Luminance(active), Is.LessThan(0.5));
            Assert.That(manager.Resources.Contains("AvalonDock_ThemeMetro_BaseColor1"), Is.True,
                "кисти темы Metro (BaseColor1..7) должны заполняться тем же проходом");
        }
        finally
        {
            settings.SetDockingManager(null);
        }
    }

    /// <summary>
    /// Текст вкладки документа не должен брать цвет из BaseColor5 (фон тулбара): после темизации
    /// вкладок в тёмный цвет такой текст становится невидимым. Правильный источник — BaseColor2 (foreground).
    /// </summary>
    [Test]
    public void ThemeXaml_TabForegroundIsNotBoundToToolbarBackgroundColor()
    {
        string themeXaml = Path.Combine(FindRepositoryRoot(), "AvalonDock.Themes.Metro", "Theme.xaml");
        string text = File.ReadAllText(themeXaml);

        Match offender = Regex.Match(text,
            "Property=\"Foreground\"\\s+Value=\"\\{DynamicResource AvalonDock_ThemeMetro_BaseColor[157]\\}\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.That(offender.Success, Is.False,
            $"Foreground вкладки привязан к цвету фона ({offender.Value}) — на тёмной вкладке текст пропадёт");
    }

    [Test]
    public void Apply_FillsEveryPaletteKey()
    {
        var target = new ResourceDictionary();

        DockingThemeBrushes.Apply(target, StudioTheme.Dark);

        Assert.That(DockingThemeBrushes.AllKeys, Is.Unique);
        foreach (string key in DockingThemeBrushes.AllKeys)
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнена кисть {key}");
            Assert.That(target[key], Is.InstanceOf<SolidColorBrush>(), $"{key} должен быть SolidColorBrush");
            Assert.That(((Brush)target[key]).IsFrozen, Is.True, $"{key} должен быть заморожен");
        }
    }

    [Test]
    public void Apply_PaletteKeys_AreNeutralAndComeFromStudioPalette()
    {
        var target = new ResourceDictionary();

        DockingThemeBrushes.Apply(target, StudioTheme.Dark);

        StudioThemeColors palette = StudioPalette.Dark;

        // Один фон: поверхность и legacy BaseColor1 — одно значение; у вкладки свой токен TabActive.
        Assert.That(ColorOf(target, DockingThemeBrushes.SurfaceKey), Is.EqualTo(palette.Surface));
        Assert.That(ColorOf(target, DockingThemeBrushes.TabActiveKey), Is.EqualTo(palette.TabActive));
        Assert.That(ColorOf(target, DockingThemeBrushes.LegacyBaseColor1Key), Is.EqualTo(palette.Surface));

        Assert.That(ColorOf(target, DockingThemeBrushes.TabBackgroundKey), Is.EqualTo(palette.SurfaceAlt));
        Assert.That(ColorOf(target, DockingThemeBrushes.TabHoverKey), Is.EqualTo(palette.Hover));
        Assert.That(ColorOf(target, DockingThemeBrushes.SeparatorKey), Is.EqualTo(palette.Separator));
        Assert.That(ColorOf(target, DockingThemeBrushes.SeparatorStrongKey), Is.EqualTo(palette.SeparatorStrong));
        Assert.That(ColorOf(target, DockingThemeBrushes.SelectionKey), Is.EqualTo(palette.Selected));
        Assert.That(ColorOf(target, DockingThemeBrushes.ForegroundKey), Is.EqualTo(palette.Foreground));
        Assert.That(ColorOf(target, DockingThemeBrushes.OverlayFillKey), Is.EqualTo(palette.OverlayFill));
        Assert.That(ColorOf(target, DockingThemeBrushes.OverlayStrokeKey), Is.EqualTo(palette.OverlayStroke));

        // Ни одного акцента: красный — только у кнопки закрытия.
        foreach (string key in DockingThemeBrushes.AllKeys)
        {
            if (key == DockingThemeBrushes.CloseHoverBackgroundKey)
            {
                continue;
            }

            Assert.That(StudioPalette.Chroma(ColorOf(target, key)), Is.LessThanOrEqualTo(12),
                $"кисть {key} должна быть нейтральной");
        }
    }

    [Test]
    public void Apply_LightTheme_UsesLightPalette()
    {
        var target = new ResourceDictionary();

        DockingThemeBrushes.Apply(target, StudioTheme.Light);

        Assert.That(ColorOf(target, DockingThemeBrushes.SurfaceKey), Is.EqualTo(StudioPalette.Light.Surface));
        Assert.That(ColorOf(target, DockingThemeBrushes.ForegroundKey), Is.EqualTo(StudioPalette.Light.Foreground));
        Assert.That(ColorOf(target, DockingThemeBrushes.TabBackgroundKey), Is.EqualTo(StudioPalette.Light.SurfaceAlt));
    }

    [Test]
    public void AppSettings_ReapplyDockManagerResources_FillsPaletteKeys()
    {
        AppSettings settings = AppSettings.GetInstance();
        var manager = new DockingManager();
        settings.SetDockingManager(manager);

        try
        {
            settings.ReapplyDockManagerResources();

            foreach (string key in DockingThemeBrushes.AllKeys)
            {
                ColorOf(manager.Resources, key);
            }

            Assert.That(ColorOf(manager.Resources, DockingThemeBrushes.SurfaceKey), Is.EqualTo(StudioPalette.Dark.Surface));
        }
        finally
        {
            settings.SetDockingManager(null);
        }
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
