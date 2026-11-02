using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Регрессионные тесты на алиасы Studio.*: в словарь должны попадать реальные Brush,
/// иначе WPF падает с
/// XamlParseException: "System.Windows.ResourceReferenceExpression" не является допустимым
/// значением для свойства "Foreground" (StyleHelper.ApplyStyleOrTemplateValue).
/// </summary>
[Apartment(ApartmentState.STA)]
public class StudioBrushesTests
{
    private static ResourceDictionary MaterialDesignSource(bool dark = true)
    {
        byte background = dark ? (byte)0x2D : (byte)0xFF;
        byte toolbar = dark ? (byte)0x33 : (byte)0xF0;

        return new ResourceDictionary
        {
            ["MaterialDesign.Brush.Background"] = new SolidColorBrush(Color.FromRgb(background, background, background)),
            ["MaterialDesign.Brush.Foreground"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF1)),
            ["MaterialDesign.Brush.Card.Background"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
            ["MaterialDesign.Brush.Card.Border"] = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            ["MaterialDesign.Brush.Primary"] = new SolidColorBrush(Color.FromRgb(0x67, 0x3A, 0xB7)),
            ["MaterialDesign.Brush.Primary.Foreground"] = new SolidColorBrush(Colors.White),
            ["MaterialDesign.Brush.ToolBar.Background"] = new SolidColorBrush(Color.FromRgb(toolbar, toolbar, toolbar)),
            ["MaterialDesign.Brush.ToolBar.Item.Background"] = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            ["MaterialDesign.Brush.ToolBar.Item.Foreground"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF1)),
            ["MaterialDesign.Brush.ToggleButton.Background"] = new SolidColorBrush(Color.FromRgb(0x33, 0x64, 0xB5)),
            ["MaterialDesign.Brush.ListBoxItem.Selected"] = new SolidColorBrush(Color.FromRgb(0x4A, 0x2C, 0x82))
        };
    }

    [Test]
    public void AliasKeys_AreDeclared()
    {
        Assert.That(StudioBrushes.AliasKeys, Is.Not.Empty);
        Assert.That(StudioBrushes.AliasKeys, Is.Unique);
        Assert.That(StudioBrushes.AliasKeys, Has.All.StartsWith("Studio."));
    }

    [Test]
    public void Apply_FillsEveryAliasWithFrozenSolidBrush()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource());

        foreach (string key in StudioBrushes.AliasKeys)
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнен алиас {key}");
            Assert.That(target[key], Is.InstanceOf<SolidColorBrush>(), $"алиас {key} должен быть SolidColorBrush");
            Assert.That(((Brush)target[key]).IsFrozen, Is.True, $"алиас {key} должен быть заморожен");
        }
    }

    [Test]
    public void Apply_StoresNoDeferredResourceReferences()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource());

        foreach (object key in target.Keys)
        {
            object value = target[key];
            Assert.That(value, Is.Not.InstanceOf<MarkupExtension>(), $"{key} содержит markup extension");
            Assert.That(value.GetType().FullName, Does.Not.Contain("ResourceReference"),
                $"{key} содержит отложенную ссылку на ресурс — это и есть причина XamlParseException");
        }
    }

    [Test]
    public void Apply_FillsSystemBrushesUsedByNativeWfControls()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource());

        foreach (object key in StudioBrushes.SystemBrushKeys)
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнена системная кисть {key}");
            Assert.That(target[key], Is.InstanceOf<Brush>(), $"системная кисть {key} должна быть Brush");
        }

        Assert.That(target.Contains(SystemColors.MenuTextBrushKey), Is.True);
    }

    [Test]
    public void Apply_SystemBrushGradationsFollowPalette()
    {
        // Шаблоны Aero2 рисуют chrome системными градациями (ControlLight/ControlDark/ScrollBar/…):
        // раньше в тёмной теме из-за них появлялись светлые плашки (заголовок категории панели «Активности»).
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource(dark: true));

        foreach (object key in StudioBrushes.SystemBrushKeys)
        {
            Color colour = ((SolidColorBrush)target[key]).Color;

            if (key == SystemColors.InfoBrushKey)
            {
                Assert.That(colour, Is.EqualTo(StudioPalette.Dark.SurfaceAlt), "фон подсказки — поверхность палитры");
                continue;
            }

            Assert.That(colour, Is.AnyOf(
                    StudioPalette.Dark.Surface,
                    StudioPalette.Dark.SurfaceAlt,
                    StudioPalette.Dark.Separator,
                    StudioPalette.Dark.SeparatorStrong,
                    StudioPalette.Dark.Foreground,
                    StudioPalette.Dark.ForegroundMuted,
                    StudioPalette.Dark.Selected),
                $"системная кисть {key} взяла цвет вне палитры ({colour}) — в тёмной теме это светлая плашка");
        }
    }

    [Test]
    public void Apply_ReplacesExistingBrushes_SoThemeSwitchTakesEffect()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource(dark: false));
        Color before = ((SolidColorBrush)target[StudioBrushes.MenuBackgroundKey]).Color;

        StudioBrushes.Apply(target, MaterialDesignSource(dark: true));
        Color after = ((SolidColorBrush)target[StudioBrushes.MenuBackgroundKey]).Color;

        Assert.That(after, Is.Not.EqualTo(before));
        Assert.That(after, Is.EqualTo(StudioPalette.Dark.Surface)); // значение берётся из единой палитры
    }

    [Test]
    public void Apply_WithoutSource_UsesFallbacks()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, null);

        foreach (string key in StudioBrushes.AliasKeys)
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнен алиас {key}");
            Assert.That(target[key], Is.InstanceOf<SolidColorBrush>(), $"алиас {key} должен быть SolidColorBrush");
        }
    }

    [Test]
    public void Apply_NullTarget_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => StudioBrushes.Apply(null, MaterialDesignSource()));
    }

    [Test]
    public void Sources_DeclareNoDynamicResourceDictionaryEntries()
    {
        string repositoryRoot = FindRepositoryRoot();
        string studioRoot = Path.Combine(repositoryRoot, "AiRpaStudio");

        List<string> offenders = Directory
            .EnumerateFiles(studioRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => new { path, line, number = index + 1 }))
            .Where(entry => entry.line.Contains("<DynamicResource"))
            .Select(entry => $"{entry.path}:{entry.number}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "В ResourceDictionary нельзя объявлять <DynamicResource x:Key=.../>: в словарь попадает " +
            "ResourceReferenceExpression, и WPF падает с XamlParseException на Style-сеттерах.");
    }

    [Test]
    public void Apply_UsesStudioPalette_NotMaterialDesignAccent()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource());

        StudioThemeColors palette = StudioPalette.Dark;

        // Правило одного фона: окно, меню, тулбар и заголовок — один и тот же Surface.
        Assert.That(ColorOf(target, StudioBrushes.WindowBackgroundKey), Is.EqualTo(palette.Surface));
        Assert.That(ColorOf(target, StudioBrushes.MenuBackgroundKey), Is.EqualTo(palette.Surface));
        Assert.That(ColorOf(target, StudioBrushes.ToolbarBackgroundKey), Is.EqualTo(palette.Surface));
        Assert.That(ColorOf(target, StudioBrushes.TitleBarBackgroundKey), Is.EqualTo(palette.Surface));

        Assert.That(ColorOf(target, StudioBrushes.ToolbarBorderKey), Is.EqualTo(palette.Separator));
        Assert.That(ColorOf(target, StudioBrushes.ToolbarHoverBackgroundKey), Is.EqualTo(palette.Hover));
        Assert.That(ColorOf(target, StudioBrushes.ToolbarPressedBackgroundKey), Is.EqualTo(palette.Selected));
        Assert.That(ColorOf(target, StudioBrushes.ToolbarCheckedBackgroundKey), Is.EqualTo(palette.Selected));
        Assert.That(ColorOf(target, StudioBrushes.MenuForegroundKey), Is.EqualTo(palette.Foreground));

        // Акцент MaterialDesign (DeepPurple #673AB7) не должен просачиваться ни в один алиас.
        Color accent = Color.FromRgb(0x67, 0x3A, 0xB7);
        foreach (string key in StudioBrushes.AliasKeys)
        {
            Assert.That(ColorOf(target, key), Is.Not.EqualTo(accent), $"алиас {key} взял акцент MaterialDesign");
        }

        // Красный остаётся только у кнопки закрытия, все прочие алиасы нейтральны.
        Assert.That(ColorOf(target, StudioBrushes.CloseHoverBackgroundKey), Is.EqualTo(palette.CloseHoverBackground));
        foreach (string key in StudioBrushes.AliasKeys)
        {
            if (key == StudioBrushes.CloseHoverBackgroundKey)
            {
                continue;
            }

            Assert.That(StudioPalette.Chroma(ColorOf(target, key)), Is.LessThanOrEqualTo(12),
                $"алиас {key} должен быть нейтральным");
        }
    }

    [Test]
    public void Apply_SystemHighlight_IsNeutral()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource());

        Color highlight = ColorOf(target, SystemColors.HighlightBrushKey);

        Assert.That(StudioPalette.Chroma(highlight), Is.LessThanOrEqualTo(6), "выделение нативных контролов должно быть серым");
        // Холодный сдвиг допустим, но цвет обязан остаться нейтральным (см. токены палитры).
        Assert.That(highlight, Is.EqualTo(StudioPalette.Dark.Selected));
        Assert.That(ColorOf(target, SystemColors.HighlightTextBrushKey), Is.EqualTo(StudioPalette.Dark.Foreground));
        Assert.That(ColorOf(target, SystemColors.InactiveSelectionHighlightBrushKey),
            Is.EqualTo(StudioPalette.Dark.SurfaceAlt));
    }

    [Test]
    public void Apply_LightPalette_IsUsedForLightTheme()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource(dark: false));

        Assert.That(ColorOf(target, StudioBrushes.SurfaceKey), Is.EqualTo(StudioPalette.Light.Surface));
        Assert.That(ColorOf(target, StudioBrushes.ForegroundKey), Is.EqualTo(StudioPalette.Light.Foreground));
        Assert.That(ColorOf(target, StudioBrushes.SeparatorKey), Is.EqualTo(StudioPalette.Light.Separator));
    }

    [Test]
    public void Apply_OverlayAliases_ComeFromPalette()
    {
        var target = new ResourceDictionary();

        StudioBrushes.Apply(target, MaterialDesignSource());

        Color fill = ColorOf(target, StudioBrushes.OverlayFillKey);
        Assert.That(fill.A, Is.EqualTo(StudioPalette.OverlayFillAlpha));
        Assert.That(fill.R, Is.EqualTo(StudioPalette.Dark.Surface.R));
        Assert.That(ColorOf(target, StudioBrushes.OverlayStrokeKey), Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
    }

    private static Color ColorOf(ResourceDictionary target, object key)
    {
        Assert.That(target.Contains(key), Is.True, $"не заполнена кисть {key}");
        Assert.That(target[key], Is.InstanceOf<SolidColorBrush>(), $"{key} должен быть SolidColorBrush");

        var brush = (SolidColorBrush)target[key];
        Assert.That(brush.IsFrozen, Is.True, $"{key} должен быть заморожен");
        return brush.Color;
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
