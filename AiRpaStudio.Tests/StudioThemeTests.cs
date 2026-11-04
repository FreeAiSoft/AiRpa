using System;
using System.Collections.Generic;
using System.Linq;
using RPA.Helpers;
using Agent.Core.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Тема студии — перечисление <see cref="StudioTheme"/> и его единственная точка правды
/// <see cref="StudioPalette.Current"/>.
/// <para>
/// Почему это отдельный набор тестов: раньше тема ходила по коду как <c>bool dark</c> и как «угадывание»
/// по яркости <c>MaterialDesign.Brush.Background</c>. Из-за этого подсистемы расходились на один
/// переключатель (чат перекрашивался, а поверхность дизайнера оставалась в палитре предыдущей темы).
/// Здесь зафиксировано: тема — только перечисление, палитра всегда знает свою тему, а «прошлая» тема
/// вычисляется как <see cref="StudioThemes.Other(StudioTheme)"/>, а не эвристикой.
/// </para>
/// </summary>
public class StudioThemeTests
{
    [Test]
    public void EnumHasExactlyTwoThemes()
    {
        StudioTheme[] themes = Enum.GetValues(typeof(StudioTheme)).Cast<StudioTheme>().ToArray();

        Assert.That(themes, Is.EqualTo(new[] { StudioTheme.Dark, StudioTheme.Light }),
            "третьей, «смешанной» темы нет: расширение перечисления обязано быть осознанным решением");
    }

    [Test]
    public void Default_IsDark()
    {
        Assert.That(default(StudioTheme), Is.EqualTo(StudioTheme.Dark),
            "default(StudioTheme) обязан совпадать с дефолтом студии (App.xaml BaseTheme=Dark): " +
            "иначе неинициализированное поле молча даёт светлую тему");
    }

    [Test]
    public void Other_ReturnsOppositeTheme()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StudioTheme.Dark.Other(), Is.EqualTo(StudioTheme.Light));
            Assert.That(StudioTheme.Light.Other(), Is.EqualTo(StudioTheme.Dark));
        });
    }

    [Test]
    public void Names_RoundTripThroughParse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StudioTheme.Dark.ToName(), Is.EqualTo("Dark"));
            Assert.That(StudioTheme.Light.ToName(), Is.EqualTo("Light"));
            Assert.That(StudioThemes.Parse("Dark"), Is.EqualTo(StudioTheme.Dark));
            Assert.That(StudioThemes.Parse("light"), Is.EqualTo(StudioTheme.Light), "регистр не важен");
            Assert.That(StudioThemes.Parse(" Light "), Is.EqualTo(StudioTheme.Light));
            Assert.That(StudioThemes.Parse(null), Is.EqualTo(StudioTheme.Dark), "неизвестное имя — дефолт студии");
            Assert.That(StudioThemes.Parse("Dark", StudioTheme.Light), Is.EqualTo(StudioTheme.Dark));
            Assert.That(StudioThemes.TryParse("nonsense", out _), Is.False);
            Assert.That(StudioThemes.TryParse("", out _), Is.False);
        });
    }

    [Test]
    public void Palettes_KnowTheirTheme()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StudioPalette.Dark.Theme, Is.EqualTo(StudioTheme.Dark));
            Assert.That(StudioPalette.Light.Theme, Is.EqualTo(StudioTheme.Light));
            Assert.That(StudioPalette.Dark.IsDark, Is.True);
            Assert.That(StudioPalette.Light.IsDark, Is.False);
            Assert.That(StudioPalette.For(StudioTheme.Dark), Is.SameAs(StudioPalette.Dark));
            Assert.That(StudioPalette.For(StudioTheme.Light), Is.SameAs(StudioPalette.Light));
        });
    }

    [Test]
    public void ApplyCurrent_SwitchesActivePalette()
    {
        StudioTheme original = StudioPalette.Current;

        try
        {
            StudioPalette.ApplyCurrent(StudioTheme.Light);

            Assert.Multiple(() =>
            {
                Assert.That(StudioPalette.Current, Is.EqualTo(StudioTheme.Light));
                Assert.That(StudioPalette.Active, Is.SameAs(StudioPalette.Light),
                    "палитра активной темы обязана следовать за перечислением, а не за словарём MaterialDesign");
            });

            StudioPalette.ApplyCurrent(StudioTheme.Dark);

            Assert.Multiple(() =>
            {
                Assert.That(StudioPalette.Current, Is.EqualTo(StudioTheme.Dark));
                Assert.That(StudioPalette.Active, Is.SameAs(StudioPalette.Dark));
            });
        }
        finally
        {
            StudioPalette.ApplyCurrent(original);
        }
    }

    [Test]
    public void DetectTheme_ReadsForeignDictionaryLuminance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StudioPalette.DetectTheme(WithBackground(0x1E)), Is.EqualTo(StudioTheme.Dark));
            Assert.That(StudioPalette.DetectTheme(WithBackground(0xFF)), Is.EqualTo(StudioTheme.Light));
            Assert.That(StudioPalette.DetectTheme(new System.Windows.ResourceDictionary()), Is.EqualTo(StudioTheme.Dark),
                "без кисти MaterialDesign считается тёмная тема: с неё стартует студия");
            Assert.That(StudioPalette.DetectTheme(null), Is.EqualTo(StudioTheme.Dark));
        });
    }

    [Test]
    public void TokensOfOppositeThemes_DoNotOverlapExceptSurface()
    {
        // Проходы темизации узнают «работу предыдущей темы» по цвету токена противоположной палитры:
        // если цвета совпадают, узнавание невозможно и элемент остаётся в чужой палитре.
        HashSet<string> shared = StudioPalette.Dark.Tokens.Keys
            .Where(token => StudioPalette.Dark.Tokens[token] == StudioPalette.Light.Tokens[token])
            .ToHashSet();

        Assert.That(shared, Is.SubsetOf(new HashSet<string> { StudioPalette.CloseHoverBackgroundToken,
                StudioPalette.CloseHoverForegroundToken }),
            "различие тем должно быть в цветах токенов: совпадать могут только системные (красный hover, белый текст)");
    }

    private static System.Windows.ResourceDictionary WithBackground(byte channel)
    {
        return new System.Windows.ResourceDictionary
        {
            ["MaterialDesign.Brush.Background"] =
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(channel, channel, channel))
        };
    }
}
