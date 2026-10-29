using System.Threading;
using System.Windows;
using System.Windows.Media;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

public class MaterialThemePaletteTests
{
    [Test]
    public void ToHex_FormatsUppercaseRgb()
    {
        Assert.That(MaterialThemePalette.ToHex(Color.FromRgb(255, 0, 0)), Is.EqualTo("#FF0000"));
        Assert.That(MaterialThemePalette.ToHex(Color.FromRgb(0, 128, 255)), Is.EqualTo("#0080FF"));
    }

    [Test]
    public void TryParseHex_AcceptsCommonFormats()
    {
        Assert.That(MaterialThemePalette.TryParseHex("#FF0000", out Color c1), Is.True);
        Assert.That((c1.R, c1.G, c1.B), Is.EqualTo((255, 0, 0)));

        Assert.That(MaterialThemePalette.TryParseHex("FF0000", out Color c2), Is.True);
        Assert.That((c2.R, c2.G, c2.B), Is.EqualTo((255, 0, 0)));

        Assert.That(MaterialThemePalette.TryParseHex("#F00", out Color c3), Is.True);
        Assert.That((c3.R, c3.G, c3.B), Is.EqualTo((255, 0, 0)));

        // #AARRGGBB — альфа-канал отбрасывается.
        Assert.That(MaterialThemePalette.TryParseHex("#80FF0000", out Color c4), Is.True);
        Assert.That((c4.R, c4.G, c4.B), Is.EqualTo((255, 0, 0)));
    }

    [Test]
    public void TryParseHex_Invalid_ReturnsFalse()
    {
        Assert.That(MaterialThemePalette.TryParseHex("#GGGGGG", out _), Is.False);
        Assert.That(MaterialThemePalette.TryParseHex("", out _), Is.False);
        Assert.That(MaterialThemePalette.TryParseHex(null, out _), Is.False);
        Assert.That(MaterialThemePalette.TryParseHex("#12345", out _), Is.False);
    }

    [Test]
    public void Lighten_Darken_BlendCorrectly()
    {
        Assert.That(MaterialThemePalette.Lighten(Colors.Black, 1.0), Is.EqualTo(Colors.White));
        Assert.That(MaterialThemePalette.Darken(Colors.White, 1.0), Is.EqualTo(Colors.Black));
        Assert.That(MaterialThemePalette.Lighten(Colors.Black, 0.0), Is.EqualTo(Colors.Black));
        Assert.That(MaterialThemePalette.Lighten(Colors.Black, 0.5), Is.EqualTo(Color.FromRgb(128, 128, 128)));
        Assert.That(MaterialThemePalette.Darken(Colors.White, 0.5), Is.EqualTo(Color.FromRgb(128, 128, 128)));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Resolve_ReadsMaterialDesignBrushes()
    {
        var resources = new ResourceDictionary
        {
            ["MaterialDesign.Brush.Background"] = new SolidColorBrush(Color.FromRgb(1, 2, 3)),
            ["MaterialDesign.Brush.Foreground"] = new SolidColorBrush(Color.FromRgb(10, 20, 30)),
            ["MaterialDesign.Brush.Card.Border"] = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
            ["MaterialDesign.Brush.Primary"] = new SolidColorBrush(Color.FromRgb(70, 80, 90))
        };

        MaterialThemeColors colors = MaterialThemePalette.Resolve(resources);

        Assert.That(colors.Background, Is.EqualTo(Color.FromRgb(1, 2, 3)));
        Assert.That(colors.Foreground, Is.EqualTo(Color.FromRgb(10, 20, 30)));
        Assert.That(colors.Border, Is.EqualTo(Color.FromRgb(40, 50, 60)));
        Assert.That(colors.Accent, Is.EqualTo(Color.FromRgb(70, 80, 90)));
    }
}