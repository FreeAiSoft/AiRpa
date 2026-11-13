using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RPA.Core.Services;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Р‘Р°Рі В«РѕС€РёР±РєР° РІРёРґРЅР° РєСЂР°СЃРЅРѕР№ СЂР°РјРєРѕР№/РїСЂСЏРјРѕСѓРіРѕР»СЊРЅРёРєРѕРј, РЅРѕ Р±РµР· РёРєРѕРЅРєРёВ».
/// <para>
/// Р”РІРµ С‚РѕС‡РєРё, РіРґРµ СЃС‚СѓРґРёСЏ СЂРёСЃСѓРµС‚ РїСЂРёР·РЅР°Рє РѕС€РёР±РєРё СЃР°РјР°:
/// <list type="number">
/// <item>РёРєРѕРЅРєР° РЅР° Р°РєС‚РёРІРЅРѕСЃС‚Рё РІ РґРёР·Р°Р№РЅРµСЂРµ (<c>DesignerValidator.SetValidationIconDirectly</c>
///       СЃС‚Р°РІРёР» <c>Border.Background</c> РІ СЃРїР»РѕС€РЅСѓСЋ РєСЂР°СЃРЅСѓСЋ РєРёСЃС‚СЊ РїР°Р»РёС‚СЂС‹ вЂ” РіР»РёС„ РїСЂРё СЌС‚РѕРј РёСЃС‡РµР·Р°Р»);</item>
/// <item>Р°РґР°РїС‚РµСЂ РѕС€РёР±РєРё WPF (<c>WfDesignerTheme.CreateValidationErrorTemplate</c>) вЂ” hairline-СЂР°РјРєР°
///       Р±РµР· Р·РЅР°С‡РєР° РѕС€РёР±РєРё.</item>
/// </list>
/// </para>
/// <para>
/// РџСЂР°РІРёР»Рѕ РїРѕСЃР»Рµ РёСЃРїСЂР°РІР»РµРЅРёСЏ: РёРєРѕРЅРєР° Р°РєС‚РёРІРЅРѕСЃС‚Рё вЂ” СЂРѕРґРЅРѕР№ СЂРёСЃСѓРЅРѕРє WF
/// (<c>WorkflowDesignerIcons.DesignerItems.ActivityErrorValidation</c>: РєСЂР°СЃРЅС‹Р№ РєСЂСѓРі СЃ Р±РµР»С‹Рј В«!В»),
/// СЃРїР»РѕС€РЅР°СЏ РєРёСЃС‚СЊ РїР°Р»РёС‚СЂС‹ РѕСЃС‚Р°С‘С‚СЃСЏ С‚РѕР»СЊРєРѕ СЂРµР·РµСЂРІРѕРј; Сѓ Р°РґР°РїС‚РµСЂР° РѕС€РёР±РєРё, РєСЂРѕРјРµ СЂР°РјРєРё, РµСЃС‚СЊ Р±РµР№РґР¶-РёРєРѕРЅРєР°.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class ValidationIconGlyphTests
{
    // --- РРєРѕРЅРєР° РѕС€РёР±РєРё РЅР° Р°РєС‚РёРІРЅРѕСЃС‚Рё. ---

    [Test]
    public void ActivityErrorIcon_IsGlyphDrawingNotSolidFill()
    {
        Brush brush = ValidationIconState.ErrorIconBrush();

        Assert.That(brush, Is.Not.Null);
        Assert.That(brush, Is.InstanceOf<DrawingBrush>(),
            "СЃРїР»РѕС€РЅР°СЏ РєРёСЃС‚СЊ РЅР° Border вЂ” СЌС‚Рѕ РєСЂР°СЃРЅС‹Р№ РїСЂСЏРјРѕСѓРіРѕР»СЊРЅРёРє Р±РµР· РёРєРѕРЅРєРё");
        Assert.That(((DrawingBrush)brush).Drawing, Is.Not.Null, "Сѓ РёРєРѕРЅРєРё РґРѕР»Р¶РµРЅ Р±С‹С‚СЊ СЂРёСЃСѓРЅРѕРє");
        Assert.That(CountGeometryDrawings(((DrawingBrush)brush).Drawing), Is.GreaterThanOrEqualTo(2),
            "РёРєРѕРЅРєР° РѕС€РёР±РєРё = Р·Р°Р»РёРІРєР° РєСЂСѓРіР° + Р±РµР»С‹Р№ РіР»РёС„ В«!В»");
    }

    [Test]
    public void ActivityErrorIcon_DrawsRedGlyph()
    {
        DrawingBrush brush = (DrawingBrush)ValidationIconState.ErrorIconBrush();

        Color[] colors = CollectColors(brush.Drawing).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(colors, Is.Not.Empty, "Сѓ РёРєРѕРЅРєРё РЅРµС‚ С†РІРµС‚РѕРІ вЂ” РЅРµС‡РµРіРѕ СЂРёСЃРѕРІР°С‚СЊ");
            Assert.That(colors.Any(c => c.R > c.G && c.R > c.B),
                "РєСЂР°СЃРЅС‹Р№ вЂ” СЃРёСЃС‚РµРјРЅР°СЏ СЃРµРјР°РЅС‚РёРєР° РѕС€РёР±РєРё; РєРёСЃС‚СЊ РѕР±СЏР·Р°РЅР° Р±С‹С‚СЊ РєСЂР°СЃРЅРѕР№");
            Assert.That(colors.Any(c => c.R > 240 && c.G > 240 && c.B > 240),
                "РіР»РёС„ В«!В» СЂРёСЃСѓРµС‚СЃСЏ СЃРІРµС‚Р»С‹Рј С†РІРµС‚РѕРј РїРѕРІРµСЂС… Р·Р°Р»РёРІРєРё");
        });
    }

    [Test]
    public void ActivityErrorIcon_IsStableAndFrozenForReuse()
    {
        Brush first = ValidationIconState.ErrorIconBrush();
        Brush second = ValidationIconState.ErrorIconBrush();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(second),
                "РєРёСЃС‚СЊ РёРєРѕРЅРєРё СЃС‚Р°РІРёС‚СЃСЏ РЅР° РґРµСЃСЏС‚РєРё Р°РєС‚РёРІРЅРѕСЃС‚РµР№: РѕР±С‰РёР№ РёР·РјРµРЅСЏРµРјС‹Р№ СЌРєР·РµРјРїР»СЏСЂ РЅРµ РЅСѓР¶РµРЅ");
            Assert.That(first.IsFrozen, Is.True, "Р·Р°РјРѕСЂРѕР¶РµРЅРЅСѓСЋ РєРёСЃС‚СЊ РјРѕР¶РЅРѕ СЃС‚Р°РІРёС‚СЊ РЅР° Р»СЋР±РѕР№ СЌР»РµРјРµРЅС‚");
        });
    }

    [Test]
    public void DesignerValidator_UsesGlyphIconForOwnErrors()
    {
        string source = ReadSource(@"AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ValidationIconState.ErrorIconBrush()"),
                "СЃРІРѕРё РѕС€РёР±РєРё СЃС‚СѓРґРёРё РґРѕР»Р¶РЅС‹ СЂРёСЃРѕРІР°С‚СЊСЃСЏ РёРєРѕРЅРєРѕР№, Р° РЅРµ СЃРїР»РѕС€РЅРѕР№ Р·Р°Р»РёРІРєРѕР№");
            Assert.That(source, Does.Not.Contain("border.Background = ValidationIconState.ErrorBrush()"),
                "СЃРїР»РѕС€РЅР°СЏ РєРёСЃС‚СЊ вЂ” СЌС‚Рѕ СЂРµРіСЂРµСЃСЃРёСЏ В«РєСЂР°СЃРЅС‹Р№ РїСЂСЏРјРѕСѓРіРѕР»СЊРЅРёРє РІРјРµСЃС‚Рѕ РёРєРѕРЅРєРёВ»");
        });
    }

    // --- РђРґР°РїС‚РµСЂ РѕС€РёР±РєРё WPF (СЂР°РјРєР° + Р·РЅР°С‡РѕРє). ---

    [Test]
    public void ErrorAdorner_DrawsFrameAndBadgeGlyph()
    {
        ControlTemplate template = WfDesignerTheme.CreateValidationErrorTemplate(StudioPalette.Dark);
        FrameworkElementFactory root = template.VisualTree;

        Assert.That(root, Is.Not.Null);
        Assert.That(root.Type, Is.EqualTo(typeof(Grid)), "РєРѕСЂРµРЅСЊ Р°РґР°РїС‚РµСЂР° вЂ” Grid: СЂР°РјРєР° + Р·РЅР°С‡РѕРє");

        FrameworkElementFactory[] rootChildren = Children(root).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rootChildren.Select(c => c.Type), Does.Contain(typeof(AdornedElementPlaceholder)),
                "Р°РґР°РїС‚РµСЂ РѕР±СЏР·Р°РЅ СЃРѕРґРµСЂР¶Р°С‚СЊ AdornedElementPlaceholder");
            Assert.That(rootChildren.Select(c => c.Type), Does.Contain(typeof(Rectangle)),
                "hairline-СЂР°РјРєР° РѕС€РёР±РєРё РѕСЃС‚Р°С‘С‚СЃСЏ");
        });

        FrameworkElementFactory badge = rootChildren.FirstOrDefault(c => c.Type == typeof(Grid));
        Assert.That(badge, Is.Not.Null, "СЂР°РјРєР° Р±РµР· Р·РЅР°С‡РєР° вЂ” СЌС‚Рѕ РґРµС„РµРєС‚ В«РєСЂР°СЃРЅР°СЏ СЂР°РјРєР° Р±РµР· РёРєРѕРЅРєРёВ»");

        Type[] badgeChildren = Children(badge).Select(c => c.Type).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(badgeChildren, Does.Contain(typeof(Ellipse)), "Р·РЅР°С‡РѕРє РѕС€РёР±РєРё вЂ” Р·Р°Р»РёС‚С‹Р№ РєСЂСѓРі");
            Assert.That(badgeChildren, Does.Contain(typeof(TextBlock)), "РІРЅСѓС‚СЂРё РєСЂСѓРіР° РѕР±СЏР·Р°С‚РµР»РµРЅ РіР»РёС„ В«!В»");
        });
    }

    // --- Р Р°Р·Р±РѕСЂ РіСЂР°С„РёРєРё. ---

    private static int CountGeometryDrawings(Drawing drawing)
    {
        switch (drawing)
        {
            case null:
                return 0;
            case DrawingGroup group:
                return group.Children.Sum(CountGeometryDrawings);
            case GlyphRunDrawing _:
                return 0;
            default:
                return 1;
        }
    }

    private static System.Collections.Generic.IEnumerable<Color> CollectColors(Drawing drawing)
    {
        switch (drawing)
        {
            case null:
                yield break;
            case DrawingGroup group:
                foreach (Drawing child in group.Children)
                {
                    foreach (Color color in CollectColors(child))
                    {
                        yield return color;
                    }
                }

                yield break;
            case GeometryDrawing geometryDrawing:
                foreach (Color color in BrushColors(geometryDrawing.Brush))
                {
                    yield return color;
                }
                // Красный круг иконки WF нарисован обводкой (Pen), а не заливкой:
                // без Pen.Brush цвет семантики ошибки терялся, и тест видел только белый глиф.
                if (geometryDrawing.Pen != null)
                {
                    foreach (Color color in BrushColors(geometryDrawing.Pen.Brush))
                    {
                        yield return color;
                    }
                }


                yield break;
            case GlyphRunDrawing glyphRun:
                foreach (Color color in BrushColors(glyphRun.ForegroundBrush))
                {
                    yield return color;
                }

                yield break;
        }
    }

    private static System.Collections.Generic.IEnumerable<Color> BrushColors(Brush brush)
    {
        switch (brush)
        {
            case SolidColorBrush solid when solid.Color.A > 0:
                yield return solid.Color;
                break;
            case GradientBrush gradient:
                foreach (GradientStop stop in gradient.GradientStops)
                {
                    if (stop.Color.A > 0)
                    {
                        yield return stop.Color;
                    }
                }

                break;
            case DrawingBrush drawingBrush:
                foreach (Color color in CollectColors(drawingBrush.Drawing))
                {
                    yield return color;
                }

                break;
        }
    }

    private static System.Collections.Generic.IEnumerable<FrameworkElementFactory> Children(FrameworkElementFactory factory)
    {
        for (FrameworkElementFactory child = factory?.FirstChild; child != null; child = child.NextSibling)
        {
            yield return child;
        }
    }

    private static string ReadSource(string relativePath)
    {
        string path = System.IO.Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"РЅРµ РЅР°Р№РґРµРЅ С„Р°Р№Р» {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(System.IO.Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "РЅРµ РЅР°Р№РґРµРЅ РєРѕСЂРµРЅСЊ СЂРµРїРѕР·РёС‚РѕСЂРёСЏ (AiRpa.sln)");
        return directory.FullName;
    }
}
