using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using AvalonDock.Themes;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 3 плана (docs\Vs2026_Theme_Plan.md, §7 M-02): мост кистей палитры в изолированные ресурсы
/// окон AvalonDock. <c>DockingManager</c> отдаёт тему в <c>Resources.MergedDictionaries</c> плавающих
/// окон, окна Ctrl+Tab и оверлея, поэтому их <c>Resources</c> не видят кисти студии и в тёмной теме
/// остаются светлыми.
/// </summary>
[Apartment(ApartmentState.STA)]
public class AvalonDockThemeBridgeTests
{
    [Test]
    public void Keys_AreDeclaredAndUnique()
    {
        Assert.That(ThemeBrushBridge.Keys, Is.Not.Empty);
        Assert.That(ThemeBrushBridge.Keys, Is.Unique);
        Assert.That(ThemeBrushBridge.Keys, Has.All.StartsWith("AvalonDock_"));

        foreach (string key in new[]
                 {
                     ThemeBrushBridge.SurfaceKey,
                     ThemeBrushBridge.SurfaceAltKey,
                     ThemeBrushBridge.ForegroundKey,
                     ThemeBrushBridge.ForegroundMutedKey,
                     ThemeBrushBridge.SeparatorKey,
                     ThemeBrushBridge.SelectionKey,
                     ThemeBrushBridge.OverlayFillKey,
                     ThemeBrushBridge.OverlayStrokeKey,
                     ThemeBrushBridge.NavigatorHoverKey,
                     ThemeBrushBridge.NavigatorSelectedKey
                 })
        {
            Assert.That(ThemeBrushBridge.Keys, Does.Contain(key), $"мост не прокидывает {key}");
        }
    }

    [Test]
    public void BridgeKeys_MatchStudioWriter()
    {
        // Ключи моста и ключи, которые пишет студия, должны совпадать, иначе окна AvalonDock
        // получат часть кистей из палитры, а часть — из fallback-значений.
        Assert.That(ThemeBrushBridge.Keys, Is.EquivalentTo(DockingThemeBrushes.PaletteKeys));
    }

    [Test]
    public void Apply_CopiesBrushesFromSource()
    {
        var source = new ResourceDictionary();
        var target = new ResourceDictionary();
        var expected = new Dictionary<string, Color>();

        byte channel = 0x30;
        foreach (string key in ThemeBrushBridge.Keys)
        {
            Color color = Color.FromRgb(channel, channel, channel);
            expected[key] = color;
            source[key] = new SolidColorBrush(color);
            channel = (byte)(channel + 1);
        }

        ThemeBrushBridge.Apply(target, source);

        foreach (string key in ThemeBrushBridge.Keys)
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнена кисть {key}");
            Assert.That(((SolidColorBrush)target[key]).Color, Is.EqualTo(expected[key]), $"{key} взял не то значение");
            Assert.That(((Brush)target[key]).IsFrozen, Is.True, $"{key} должен быть заморожен");
        }
    }

    [Test]
    public void Apply_WithoutSource_UsesNeutralFallbacks()
    {
        var target = new ResourceDictionary();

        ThemeBrushBridge.Apply(target, source: null);

        foreach (string key in ThemeBrushBridge.Keys)
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнена кисть {key}");
        }

        Color surface = ((SolidColorBrush)target[ThemeBrushBridge.SurfaceKey]).Color;
        Assert.That(StudioPalette.Luminance(surface), Is.LessThan(0.5), "fallback — тёмная палитра (тема по умолчанию)");
        Assert.That(StudioPalette.Chroma(surface), Is.LessThanOrEqualTo(12));

        // Акцента нет ни в одной кисти, кроме системного красного «закрыть».
        foreach (string key in ThemeBrushBridge.Keys)
        {
            if (key == ThemeBrushBridge.CloseHoverBackgroundKey)
            {
                continue;
            }

            Color color = ((SolidColorBrush)target[key]).Color;
            Assert.That(StudioPalette.Chroma(color), Is.LessThanOrEqualTo(12), $"кисть {key} должна быть нейтральной");
        }
    }

    [Test]
    public void Apply_ReplacesBrushes_SoThemeSwitchTakesEffect()
    {
        var target = new ResourceDictionary();
        var dark = new ResourceDictionary { [ThemeBrushBridge.SurfaceKey] = new SolidColorBrush(Colors.Black) };
        var light = new ResourceDictionary { [ThemeBrushBridge.SurfaceKey] = new SolidColorBrush(Colors.White) };

        ThemeBrushBridge.Apply(target, dark);
        Brush before = (Brush)target[ThemeBrushBridge.SurfaceKey];

        ThemeBrushBridge.Apply(target, light);
        Brush after = (Brush)target[ThemeBrushBridge.SurfaceKey];

        Assert.That(after, Is.Not.SameAs(before));
        Assert.That(((SolidColorBrush)after).Color, Is.EqualTo(Colors.White));
    }

    [Test]
    public void Apply_StoresNoDeferredResourceReferences()
    {
        var target = new ResourceDictionary();

        ThemeBrushBridge.Apply(target, source: null);

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
        Assert.DoesNotThrow(() => ThemeBrushBridge.Apply(null, null));
        Assert.DoesNotThrow(() => ThemeBrushBridge.Apply(null));
    }

    /// <summary>
    /// Окна AvalonDock создают собственную копию темы, поэтому мост обязан вызываться из их
    /// <c>UpdateThemeResources</c> — иначе новые ключи до них не доедут.
    /// </summary>
    [Test]
    public void CoreWindows_CallBridgeAfterMergingTheme()
    {
        string controls = Path.Combine(FindRepositoryRoot(), "AvalonDock", "Controls");

        foreach (string file in new[] { "LayoutFloatingWindowControl.cs", "NavigatorWindow.cs", "OverlayWindow.cs" })
        {
            string path = Path.Combine(controls, file);
            Assert.That(File.Exists(path), Is.True, $"не найден {path}");

            string text = File.ReadAllText(path);
            Match call = Regex.Match(text, @"ThemeBrushBridge\.Apply\(Resources\)", RegexOptions.None, TimeSpan.FromSeconds(2));

            Assert.That(call.Success, Is.True,
                $"{file}: в UpdateThemeResources нет вызова ThemeBrushBridge.Apply(Resources) — кисти палитры " +
                "не попадут в изолированные ресурсы окна");
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
