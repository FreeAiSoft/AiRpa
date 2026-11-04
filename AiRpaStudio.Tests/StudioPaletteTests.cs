using System.Windows;
using System.Windows.Media;
using RPA.Helpers;
using Agent.Core.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 1 плана (docs\Vs2026_Theme_Plan.md, §2 и §8 п.1): единая палитра студии.
/// Проверяет, что токены заданы, монохромны, различаются только яркостью, что тем две
/// и что тема определяется по яркости фона.
/// </summary>
[Apartment(ApartmentState.STA)]
public class StudioPaletteTests
{
    private static readonly double[] RadiusOrder = { StudioPalette.TabCornerRadius, StudioPalette.ButtonCornerRadius };

    [Test]
    public void BothThemes_DefineEveryToken()
    {
        foreach (StudioThemeColors palette in new[] { StudioPalette.Dark, StudioPalette.Light })
        {
            string theme = palette.IsDark ? "dark" : "light";

            foreach (string token in StudioPalette.TokenNames)
            {
                Assert.That(palette.TryGet(token, out Color color), Is.True, $"{theme}: не задан токен {token}");
                Assert.That(color.A, Is.GreaterThan((byte)0), $"{theme}: токен {token} полностью прозрачный");
            }

            Assert.That(palette.Tokens.Count, Is.EqualTo(StudioPalette.TokenNames.Count),
                $"{theme}: в палитре лишние или недостающие токены");
            Assert.That(palette.Tokens.Keys, Is.EquivalentTo(StudioPalette.TokenNames));
        }
    }

    [Test]
    public void DarkTheme_BackgroundTokensAreDark()
    {
        foreach (string token in StudioPalette.NeutralTokenNames)
        {
            Color color = StudioPalette.Dark.Get(token);
            Assert.That(StudioPalette.Luminance(color), Is.LessThan(0.5), $"dark: токен {token} должен быть тёмным");
        }
    }

    [Test]
    public void LightTheme_BackgroundTokensAreLight()
    {
        foreach (string token in StudioPalette.NeutralTokenNames)
        {
            Color color = StudioPalette.Light.Get(token);
            Assert.That(StudioPalette.Luminance(color), Is.GreaterThan(0.5), $"light: токен {token} должен быть светлым");
        }
    }

    [Test]
    public void StateTokens_AreMonochrome()
    {
        foreach (StudioThemeColors palette in new[] { StudioPalette.Dark, StudioPalette.Light })
        {
            string theme = palette.IsDark ? "dark" : "light";

            foreach (string token in StudioPalette.NeutralTokenNames)
            {
                int chroma = StudioPalette.Chroma(palette.Get(token));
                Assert.That(chroma, Is.LessThanOrEqualTo(6),
                    $"{theme}: токен {token} должен быть серым без цветного сдвига (chroma {chroma})");
            }

            foreach (string token in StudioPalette.TextTokenNames)
            {
                int chroma = StudioPalette.Chroma(palette.Get(token));
                Assert.That(chroma, Is.LessThanOrEqualTo(12),
                    $"{theme}: токен {token} имеет слишком заметный цветной сдвиг (chroma {chroma})");
            }
        }
    }

    [Test]
    public void DarkTheme_StatesGrowInLuminance_WithoutAccent()
    {
        StudioThemeColors palette = StudioPalette.Dark;

        Assert.That(StudioPalette.Luminance(palette.Surface), Is.LessThan(StudioPalette.Luminance(palette.SurfaceAlt)));
        Assert.That(StudioPalette.Luminance(palette.SurfaceAlt), Is.LessThan(StudioPalette.Luminance(palette.Hover)));
        Assert.That(StudioPalette.Luminance(palette.Hover), Is.LessThan(StudioPalette.Luminance(palette.TabActive)),
            "активная вкладка светлее hover, но темнее Selected");
        Assert.That(StudioPalette.Luminance(palette.TabActive), Is.LessThan(StudioPalette.Luminance(palette.Selected)));
        Assert.That(StudioPalette.Luminance(palette.Foreground), Is.GreaterThan(StudioPalette.Luminance(palette.Selected)));
    }

    [Test]
    public void LightTheme_StatesGoDownInLuminance_WithoutAccent()
    {
        StudioThemeColors palette = StudioPalette.Light;

        Assert.That(StudioPalette.Luminance(palette.Surface), Is.GreaterThan(StudioPalette.Luminance(palette.SurfaceAlt)));
        Assert.That(StudioPalette.Luminance(palette.SurfaceAlt), Is.GreaterThan(StudioPalette.Luminance(palette.Hover)));
        Assert.That(StudioPalette.Luminance(palette.Surface), Is.GreaterThanOrEqualTo(StudioPalette.Luminance(palette.TabActive)));
        Assert.That(StudioPalette.Luminance(palette.TabActive), Is.GreaterThan(StudioPalette.Luminance(palette.SurfaceAlt)),
            "в светлой теме активная вкладка светлее неактивной");
        Assert.That(StudioPalette.Luminance(palette.Hover), Is.GreaterThan(StudioPalette.Luminance(palette.Selected)));
        Assert.That(StudioPalette.Luminance(palette.Foreground), Is.LessThan(StudioPalette.Luminance(palette.Selected)));
    }

    [Test]
    public void BothThemes_KeepSingleSurfaceForEveryZone()
    {
        // Правило одного фона: заголовки, панели, вкладки и поверхность дизайнера берут тот же Surface.
        Assert.That(StudioPalette.Dark.OverlayStroke, Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
        Assert.That(StudioPalette.Light.OverlayStroke, Is.EqualTo(StudioPalette.Light.SeparatorStrong));

        Color darkOverlay = StudioPalette.Dark.OverlayFill;
        Assert.That(darkOverlay.A, Is.EqualTo(StudioPalette.OverlayFillAlpha), "заливка оверлея — Surface @15%");
        Assert.That(darkOverlay.R, Is.EqualTo(StudioPalette.Dark.Surface.R));
        Assert.That(darkOverlay.G, Is.EqualTo(StudioPalette.Dark.Surface.G));
        Assert.That(darkOverlay.B, Is.EqualTo(StudioPalette.Dark.Surface.B));
    }

    [Test]
    public void CloseHover_IsTheOnlyNonNeutralColor()
    {
        foreach (StudioThemeColors palette in new[] { StudioPalette.Dark, StudioPalette.Light })
        {
            Assert.That(palette.CloseHoverBackground, Is.EqualTo(Color.FromRgb(0xE8, 0x11, 0x23)));
            Assert.That(palette.CloseHoverForeground, Is.EqualTo(Colors.White));
        }
    }

    [Test]
    public void CornerRadii_FollowTheDesignRules()
    {
        Assert.That(StudioPalette.WindowCornerRadius, Is.EqualTo(8d));
        Assert.That(StudioPalette.TabCornerRadius, Is.EqualTo(4d));
        Assert.That(StudioPalette.ButtonCornerRadius, Is.EqualTo(4d));
        Assert.That(StudioPalette.MenuCornerRadius, Is.EqualTo(6d));
        Assert.That(RadiusOrder, Has.All.LessThan(StudioPalette.WindowCornerRadius));
    }

    [Test]
    public void For_ReturnsPaletteByTheme()
    {
        Assert.That(StudioPalette.For(StudioTheme.Dark), Is.SameAs(StudioPalette.Dark));
        Assert.That(StudioPalette.For(StudioTheme.Light), Is.SameAs(StudioPalette.Light));
        Assert.That(StudioPalette.Dark.Theme, Is.EqualTo(StudioTheme.Dark),
            "палитра обязана знать свою тему: иначе проходы темизации не найдут противоположную");
        Assert.That(StudioPalette.Light.Theme, Is.EqualTo(StudioTheme.Light));
    }

    [Test]
    public void DetectDark_UsesBackgroundLuminance()
    {
        Assert.That(StudioPalette.DetectDark(DictionaryWithBackground(Color.FromRgb(0x1E, 0x1E, 0x1E))), Is.True);
        Assert.That(StudioPalette.DetectDark(DictionaryWithBackground(Color.FromRgb(0xFF, 0xFF, 0xFF))), Is.False);
        Assert.That(StudioPalette.DetectDark(DictionaryWithBackground(Color.FromRgb(0x2D, 0x2D, 0x30))), Is.True);
        Assert.That(StudioPalette.DetectDark(DictionaryWithBackground(Color.FromRgb(0xF3, 0xF3, 0xF3))), Is.False);
    }

    [Test]
    public void DetectDark_WithoutPalette_FallsBackToDarkTheme()
    {
        Assert.That(StudioPalette.DetectDark(new ResourceDictionary()), Is.True, "по умолчанию студия стартует в dark");
        Assert.That(StudioPalette.DetectDark(null), Is.True);
        Assert.That(StudioPalette.DetectTheme(null), Is.EqualTo(StudioTheme.Dark));
    }

    [Test]
    public void Get_UnknownToken_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StudioPalette.Dark.Get("NoSuchToken"));
        Assert.That(StudioPalette.Dark.TryGet("NoSuchToken", out _), Is.False);
    }

    private static ResourceDictionary DictionaryWithBackground(Color background)
    {
        return new ResourceDictionary
        {
            ["MaterialDesign.Brush.Background"] = new SolidColorBrush(background)
        };
    }
}
