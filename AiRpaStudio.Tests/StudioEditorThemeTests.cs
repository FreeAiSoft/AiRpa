using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

using RPA.Helpers;
using Agent.Core.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 12 плана (docs\Vs2026_Theme_Plan.md, п.5): цвета редакторов кода и XAML как в Visual Studio.
/// <para>
/// Проверяется: палитра редактора заполнена в обеих темах и читается (контраст текста), XSHD не содержит
/// зашитых цветов, определения подсветки перекрашиваются по именам цветов (Green/Blue/Magenta из
/// AvalonEdit заменяются на схему VS), кэш отдаёт разные определения для разных тем.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class StudioEditorThemeTests
{
    [Test]
    public void Palettes_HaveAllTokens()
    {
        Assert.Multiple(() =>
        {
            foreach (EditorThemeColors colors in new[] { StudioEditorPalette.Dark, StudioEditorPalette.Light })
            {
                foreach (string token in StudioEditorPalette.TokenNames)
                {
                    Assert.That(colors.Tokens.ContainsKey(token), Is.True, $"в палитре редактора нет токена {token}");
                }
            }

            Assert.That(StudioEditorPalette.Dark.Background, Is.Not.EqualTo(StudioEditorPalette.Light.Background));
            Assert.That(StudioEditorPalette.For(StudioTheme.Dark), Is.SameAs(StudioEditorPalette.Dark));
            Assert.That(StudioEditorPalette.For(StudioTheme.Light), Is.SameAs(StudioEditorPalette.Light));
        });
    }

    [Test]
    public void TextAndHighlighting_AreReadableOnBackground()
    {
        foreach (EditorThemeColors colors in new[] { StudioEditorPalette.Dark, StudioEditorPalette.Light })
        {
            string theme = colors.IsDark ? "dark" : "light";

            Assert.That(Contrast(colors.Foreground, colors.Background), Is.GreaterThanOrEqualTo(4.5),
                $"{theme}: основной текст редактора не читается на фоне");

            foreach (string token in new[]
                     {
                         StudioEditorPalette.CommentToken,
                         StudioEditorPalette.StringToken,
                         StudioEditorPalette.KeywordToken,
                         StudioEditorPalette.TypeToken,
                         StudioEditorPalette.NumberToken,
                         StudioEditorPalette.XmlTagToken,
                         StudioEditorPalette.AttributeNameToken,
                         StudioEditorPalette.LinkToken
                     })
            {
                Assert.That(Contrast(colors.Get(token), colors.Background), Is.GreaterThanOrEqualTo(3d),
                    $"{theme}: токен {token} теряется на фоне редактора");
            }
        }
    }

    [Test]
    public void XamlXshd_HasNoHardcodedColors()
    {
        string path = Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "Resources", "Highlighting", "Xaml.xshd");
        string text = File.ReadAllText(path);

        MatchCollection colors = Regex.Matches(
            text, "#[0-9A-Fa-f]{6}", RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.That(colors.Count, Is.Zero,
            "цвета подсветки XAML задаются палитрой, а не в XSHD: " + string.Join(", ", colors));
    }

    [Test]
    public void ResolveHighlightToken_MapsAvalonEditNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StudioEditorPalette.ResolveHighlightToken("Comment"), Is.EqualTo(StudioEditorPalette.CommentToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("String"), Is.EqualTo(StudioEditorPalette.StringToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("Char"), Is.EqualTo(StudioEditorPalette.StringToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("NumberLiteral"), Is.EqualTo(StudioEditorPalette.NumberToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("Preprocessor"), Is.EqualTo(StudioEditorPalette.PreprocessorToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("TypeKeywords"), Is.EqualTo(StudioEditorPalette.TypeToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("Keywords"), Is.EqualTo(StudioEditorPalette.KeywordToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("ControlFlow"), Is.EqualTo(StudioEditorPalette.ControlKeywordToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("MethodCall"), Is.EqualTo(StudioEditorPalette.MethodToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("FieldName"), Is.EqualTo(StudioEditorPalette.FieldToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("XmlTag"), Is.EqualTo(StudioEditorPalette.XmlTagToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("AttributeName"), Is.EqualTo(StudioEditorPalette.AttributeNameToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("AttributeValue"), Is.EqualTo(StudioEditorPalette.AttributeValueToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("MarkupExtension"), Is.EqualTo(StudioEditorPalette.MarkupExtensionToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("XmlDeclaration"), Is.EqualTo(StudioEditorPalette.XmlDeclarationToken));
            Assert.That(StudioEditorPalette.ResolveHighlightToken("Punctuation"), Is.Null, "неизвестные цвета остаются дефолтными");
            Assert.That(StudioEditorPalette.ResolveHighlightToken(null), Is.Null);
        });
    }

    [Test]
    public void GetDefinition_RepaintsNamedColorsPerTheme()
    {
        IHighlightingDefinition dark = StudioEditorTheme.GetDefinition("C#", StudioTheme.Dark);
        IHighlightingDefinition light = StudioEditorTheme.GetDefinition("C#", StudioTheme.Light);

        Assert.That(dark, Is.Not.Null);
        Assert.That(light, Is.Not.Null);
        Assert.That(dark, Is.Not.SameAs(light), "каждая тема получает своё определение подсветки");

        Color darkComment = ColorOf(dark, "Comment");
        Color lightComment = ColorOf(light, "Comment");

        Assert.Multiple(() =>
        {
            Assert.That(darkComment, Is.EqualTo(StudioEditorPalette.Dark.Get(StudioEditorPalette.CommentToken)));
            Assert.That(lightComment, Is.EqualTo(StudioEditorPalette.Light.Get(StudioEditorPalette.CommentToken)));
            Assert.That(ColorOf(dark, "Keywords"), Is.EqualTo(StudioEditorPalette.Dark.Get(StudioEditorPalette.KeywordToken)),
                "ключевые слова C# перекрашиваются (в AvalonEdit это синий)");
        });
    }

    [Test]
    public void GetDefinition_XamlIsThemedAndCached()
    {
        IHighlightingDefinition first = StudioEditorTheme.GetDefinition(CodeLanguageProvider.XamlLanguage, StudioTheme.Dark);
        IHighlightingDefinition second = StudioEditorTheme.GetDefinition(CodeLanguageProvider.XamlLanguage, StudioTheme.Dark);

        Assert.That(first, Is.Not.Null);
        Assert.That(first, Is.SameAs(second), "определение кэшируется по паре «язык + тема»");
        Assert.That(first.Name, Is.EqualTo(CodeLanguageProvider.XamlLanguage));
        Assert.That(ColorOf(first, "XmlTag"), Is.EqualTo(StudioEditorPalette.Dark.Get(StudioEditorPalette.XmlTagToken)));
    }

    [Test]
    public void GetDefinition_UnknownLanguage_IsNull()
    {
        Assert.That(StudioEditorTheme.GetDefinition(null), Is.Null);
        Assert.That(StudioEditorTheme.GetDefinition(string.Empty), Is.Null);
    }

    [Test]
    public void ApplyChrome_SetsEditorColorsAndFont()
    {
        var editor = new TextEditor();

        StudioEditorTheme.ApplyChrome(editor, StudioTheme.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(editor.Background), Is.EqualTo(StudioEditorPalette.Dark.Background));
            Assert.That(ColorOf(editor.Foreground), Is.EqualTo(StudioEditorPalette.Dark.Foreground));
            Assert.That(ColorOf(editor.LineNumbersForeground), Is.EqualTo(StudioEditorPalette.Dark.LineNumber));
            Assert.That(editor.FontFamily.Source, Does.Contain("Roboto"));
            Assert.That(editor.Options.HighlightCurrentLine, Is.True);
            Assert.DoesNotThrow(() => StudioEditorTheme.ApplyChrome(null, StudioTheme.Dark));
        });
    }

    [Test]
    public void Apply_SetsThemedSyntaxHighlighting()
    {
        var editor = new TextEditor();

        StudioEditorTheme.Apply(editor, "Json");

        Assert.That(editor.SyntaxHighlighting, Is.Not.Null);
        Assert.That(ColorOf(editor.SyntaxHighlighting, "String"),
            Is.EqualTo(StudioEditorPalette.Dark.Get(StudioEditorPalette.StringToken)),
            "строки JSON перекрашиваются (в AvalonEdit это зелёный)");
    }

    [Test]
    public void ApplyChrome_SetsThemedLinkColor()
    {
        // #3794FF — цвет ссылок VS Code Dark+ (в AvalonEdit по умолчанию Brushes.Blue, т.е. #0000FF).
        Color expected = Color.FromRgb(0x37, 0x94, 0xFF);
        var editor = new TextEditor();

        Assert.That(ColorOf(editor.TextArea.TextView.LinkTextForegroundBrush), Is.EqualTo(Colors.Blue),
            "AvalonEdit по умолчанию красит ссылки в синий #0000FF — именно это и было не видно в XAML");

        StudioEditorTheme.ApplyChrome(editor, StudioTheme.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(StudioEditorPalette.Dark.Link, Is.EqualTo(expected), "ссылки в тёмной теме — ровно #3794FF");
            Assert.That(StudioEditorPalette.Light.Link, Is.EqualTo(expected), "ссылки в светлой теме — тот же #3794FF");
            Assert.That(ColorOf(editor.TextArea.TextView.LinkTextForegroundBrush), Is.EqualTo(expected));
            Assert.DoesNotThrow(() => StudioEditorTheme.ApplyLinkColor(null));
            Assert.DoesNotThrow(() => StudioEditorTheme.ApplyLinkColor(null, StudioEditorPalette.Dark));
        });
    }

    [Test]
    public void ApplyLinkColor_WorksWithoutFullChrome()
    {
        var editor = new TextEditor();

        StudioEditorTheme.ApplyLinkColor(editor, StudioEditorPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(editor.TextArea.TextView.LinkTextForegroundBrush),
                Is.EqualTo(StudioEditorPalette.Light.Link));
            Assert.That(editor.FontFamily.Source, Does.Not.Contain("Roboto"),
                "ApplyLinkColor не трогает шрифт и фон: редакторы выражений (VB/Roslyn) красят себя сами");
        });
    }

    [Test]
    public void ExpressionEditors_UseThemedLinkColor()
    {
        string root = FindRepositoryRoot();

        foreach (string relative in new[]
                 {
                     Path.Combine("AiRpaStudio", "VbExpressionEditor", "VbExpressionEditorInstance.cs"),
                     Path.Combine("AiRpaStudio", "CSharpExpressionEditor", "RoslynExpressionEditorInstance2.cs")
                 })
        {
            string text = File.ReadAllText(Path.Combine(root, relative));

            Assert.That(Regex.IsMatch(text, @"StudioEditorTheme\.ApplyLinkColor\s*\(\s*this\s*\)"), Is.True,
                relative + ": редактор должен брать цвет ссылок из палитры (StudioEditorTheme.ApplyLinkColor), " +
                "иначе AvalonEdit вернёт синий #0000FF");
        }
    }

    private static Color ColorOf(IHighlightingDefinition definition, string colorName)
    {
        HighlightingColor color = definition.GetNamedColor(colorName);
        Assert.That(color, Is.Not.Null, $"в определении {definition?.Name} нет цвета {colorName}");
        Assert.That(color.Foreground, Is.Not.Null, $"у цвета {colorName} нет foreground");

        Color? value = color.Foreground.GetColor(null);
        Assert.That(value.HasValue, Is.True, $"цвет {colorName} не является простым цветом");

        return value.Value;
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>());
        return ((SolidColorBrush)brush).Color;
    }

    /// <summary>Контраст по WCAG (1…21): текст на фоне ниже 4.5 читается плохо.</summary>
    private static double Contrast(Color first, Color second)
    {
        double a = RelativeLuminance(first);
        double b = RelativeLuminance(second);

        return (Math.Max(a, b) + 0.05d) / (Math.Min(a, b) + 0.05d);
    }

    private static double RelativeLuminance(Color color)
    {
        return (0.2126d * Channel(color.R)) + (0.7152d * Channel(color.G)) + (0.0722d * Channel(color.B));

        double Channel(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.03928d ? channel / 12.92d : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);
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
