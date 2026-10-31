using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;

namespace AiRpaStudio.Tests;

/// <summary>
/// Плоский стиль студии (VS2026): все скроллы приложения — 10px, без стрелок, бегунок со скруглением;
/// выпадающие списки не наследуют фиксированные размеры неявного стиля <c>ToggleButton</c> из
/// MaterialDesign и растягиваются по ширине столбца, как текстовые поля.
/// <para>
/// Инварианты:
/// <list type="bullet">
/// <item>словарь <c>AiRpaStudio\Themes\StudioScrollBar.xaml</c> объявляет неявный <c>ScrollBar</c> шириной 10
/// (горизонтальный — высотой 10) и красится только кистями <c>Studio.*</c>;</item>
/// <item>в <c>App.xaml</c> словарь подключён последним — иначе неявный стиль MaterialDesign выигрывает по порядку;</item>
/// <item>локальные стили <c>RPA.Chat</c> (у чата своя палитра, он не ссылается на <c>RPA.Helpers</c>) тоже 10px;</item>
/// <item>внутренний <c>ToggleButton</c> шаблона <c>OutlinedComboBoxStyle</c> задаёт <c>Style</c> явно: иначе на него
/// ложится неявный стиль MaterialDesign (переключатель 34x18) и «коробка» списка сжимается по горизонтали.</item>
/// </list>
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class StudioScrollBarTests
{
    private const string ScrollBarDictionary = @"AiRpaStudio\Themes\StudioScrollBar.xaml";

    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    // ---------------------------------------------------------------- глобальные скроллы 10px

    [Test]
    public void ScrollBarDictionary_DefinesTenPixelStyle()
    {
        XElement style = KeyedStyle(ScrollBarDictionary, "StudioScrollBarStyle", "ScrollBar");

        Assert.Multiple(() =>
        {
            Assert.That(SetterValue(style, "Width"), Is.EqualTo(10d), "ширина вертикального скролла — 10");
            Assert.That(SetterValue(style, "MinWidth"), Is.EqualTo(10d), "MinWidth тоже 10");
            Assert.That(SetterValue(style, "MaxWidth"), Is.EqualTo(10d), "MaxWidth тоже 10");
        });

        Assert.That(SetterValue(style, "Background"), Is.Null,
            "фон не задаём локально: тёмная/светлая тема приходит кистью палитры");
    }

    [Test]
    public void ScrollBarDictionary_HorizontalOrientationIsTenPixelsTall()
    {
        XElement template = TemplateOf(KeyedStyle(ScrollBarDictionary, "StudioScrollBarStyle", "ScrollBar"));
        XElement horizontal = TemplateTrigger(template, "Orientation", "Horizontal");

        Assert.That(horizontal, Is.Not.Null, "горизонтальный скролл должен иметь собственные размеры");
        Assert.Multiple(() =>
        {
            Assert.That(TargetedSetterValue(horizontal, null, "Height"), Is.EqualTo(10d));
            Assert.That(TargetedSetterValue(horizontal, null, "MinHeight"), Is.EqualTo(10d));
            Assert.That(TargetedSetterValue(horizontal, null, "MaxHeight"), Is.EqualTo(10d));
            Assert.That(TargetedSetterValue(horizontal, null, "Width"), Is.EqualTo("Auto"),
                "ширину горизонтального скролла задаёт контейнер — Width остаётся Auto");
        });

        XElement track = template.Descendants(P + "Track").FirstOrDefault();

        Assert.That(track, Is.Not.Null, "шаблон обязан содержать PART_Track");
        Assert.That((string)track.Attribute(X + "Name"), Is.EqualTo("PART_Track"));
        Assert.That(TargetedSetterValue(horizontal, "PART_Track", "IsDirectionReversed"), Is.EqualTo(false).Or.EqualTo("False"),
            "у горизонтального скролла направление не инвертируется");
    }

    [Test]
    public void ScrollBarDictionary_DeclaresImplicitStyleSoWidthIsGlobal()
    {
        XElement implicitStyle = ImplicitStyle(ScrollBarDictionary, "ScrollBar");

        Assert.That(implicitStyle, Is.Not.Null,
            "без неявного стиля ширина 10 не станет глобальной: ScrollBar берут стиль по типу");
        Assert.That((string)implicitStyle.Attribute("BasedOn"), Does.Contain("StudioScrollBarStyle"));
    }

    [Test]
    public void ScrollBarDictionary_PageButtonsUseTransparentTemplateOnly()
    {
        // Если у RepeatButton не будет собственного Template, неявный стиль MaterialDesign
        // (raised button: паддинги, минимальные размеры) раздувает скролл.
        XElement style = KeyedStyle(ScrollBarDictionary, "StudioScrollBarPageButtonStyle", "RepeatButton");

        Assert.That(SetterValue(style, "OverridesDefaultStyle"), Is.EqualTo("True"));
        Assert.That(SetterValue(style, "IsTabStop"), Is.EqualTo("False"));
        Assert.That(SetterValue(style, "Focusable"), Is.EqualTo("False"));
        Assert.That(TemplateOf(style), Is.Not.Null);
    }

    [Test]
    public void ScrollBarDictionary_ThumbUsesPaletteBrushes()
    {
        XElement style = KeyedStyle(ScrollBarDictionary, "StudioScrollBarThumbStyle", "Thumb");
        XElement template = TemplateOf(style);

        Assert.That(SetterValue(style, "OverridesDefaultStyle"), Is.EqualTo("True"),
            "иначе неявный стиль MaterialDesign перерисует бегунок");

        var brushes = template.Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName is "Background" or "Fill")
            .Select(a => a.Value)
            .ToArray();

        Assert.That(brushes, Is.Not.Empty, "бегунок должен быть закрашен");
        Assert.That(brushes, Has.All.StartsWith("{DynamicResource Studio."),
            "цвет бегунка берётся только из палитры: " + string.Join(", ", brushes));
        Assert.That(template.ToString(), Does.Contain("CornerRadius=\"5\""), "бегунок 10px — скругление 5");
    }

    [Test]
    public void ScrollBarDictionary_LoadsAsResourceDictionary()
    {
        // XAML словаря разбирается целиком: ссылки StaticResource внутри него разрешаются сразу,
        // поэтому порядок объявления стилей (страница → бегунок → скролл → неявный) обязателен.
        string text = Read(ScrollBarDictionary);
        var dictionary = (ResourceDictionary)XamlReader.Parse(text);

        Assert.That(dictionary.Contains("StudioScrollBarStyle"), Is.True);
        var style = (Style)dictionary["StudioScrollBarStyle"];

        Assert.That(style.TargetType, Is.EqualTo(typeof(System.Windows.Controls.Primitives.ScrollBar)));
        Assert.That(style.Setters.OfType<Setter>().Any(s => s.Property.Name == "Width"), Is.True);
    }

    [Test]
    public void App_MergesScrollBarDictionaryLast()
    {
        string app = Read(@"AiRpaStudio\App.xaml");

        int defaults = app.IndexOf("MaterialDesign2.Defaults.xaml", StringComparison.Ordinal);
        int theme = app.IndexOf("Themes/StudioTheme.xaml", StringComparison.Ordinal);
        int scrollBars = app.IndexOf("Themes/StudioScrollBar.xaml", StringComparison.Ordinal);

        Assert.That(scrollBars, Is.GreaterThan(-1), "App.xaml обязан подключать словарь скроллов");
        Assert.Multiple(() =>
        {
            Assert.That(scrollBars, Is.GreaterThan(defaults),
                "после MaterialDesign2.Defaults.xaml: иначе неявный стиль MaterialDesign перекроет наш");
            Assert.That(scrollBars, Is.GreaterThan(theme), "словарь палитры подключается раньше");
        });
    }

    [Test]
    public void Project_CompilesScrollBarDictionary()
    {
        string project = Read(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.That(project, Does.Contain(@"<Page Include=""Themes\StudioScrollBar.xaml"">"),
            "проект не SDK-style: без Page словарь не попадёт в BAML");
        Assert.That(project, Does.Contain("MSBuild:Compile"));
    }

    // ---------------------------------------------------------------- чат: локальные стили тоже 10px

    [TestCase(@"RPA.Chat\Views\ChatPanel.xaml", "ThinScrollBarStyle")]
    [TestCase(@"RPA.Chat\Controls\ChatMessagesControl.xaml", null)]
    [TestCase(@"RPA.Chat\Controls\PinnedChatMessageControl.xaml", null)]
    public void ChatScrollBars_AreTenPixelsWide(string file, string styleKey)
    {
        XElement style = styleKey == null
            ? ImplicitStyle(file, "ScrollBar")
            : KeyedStyle(file, styleKey, "ScrollBar");

        Assert.That(style, Is.Not.Null, $"{file}: не найден стиль скролла");
        Assert.Multiple(() =>
        {
            Assert.That(SetterValue(style, "Width"), Is.EqualTo(10d), $"{file}: ширина скролла 10");
            Assert.That(SetterValue(style, "MinWidth"), Is.EqualTo(10d), $"{file}: MinWidth 10");
            Assert.That(SetterValue(style, "MaxWidth"), Is.EqualTo(10d), $"{file}: MaxWidth 10");
        });

        string thumb = style.Document.ToString();
        Assert.That(thumb, Does.Not.Contain("CornerRadius=\"2.5\""),
            $"{file}: скругление бегунка считается от ширины 10 — 5, а не 2.5");
    }

    [Test]
    public void ChatPanel_ScrollBarKeepsHorizontalOrientationSupport()
    {
        XElement style = KeyedStyle(@"RPA.Chat\Views\ChatPanel.xaml", "ThinScrollBarStyle", "ScrollBar");
        XElement template = TemplateOf(style);
        XElement track = template.Descendants(P + "Track").FirstOrDefault();

        Assert.That(track, Is.Not.Null);
        Assert.That(track.Attribute("Orientation")?.Value, Is.EqualTo("{TemplateBinding Orientation}"),
            "жёсткая Vertical не даёт отрисовать горизонтальный скролл");
        Assert.That(TemplateTrigger(template, "Orientation", "Horizontal"), Is.Not.Null,
            "для горизонтального скролла нужен триггер с высотой 10");
    }

    // ---------------------------------------------------------------- комбобоксы настроек агента

    [Test]
    public void OutlinedComboBox_InnerToggleButtonHasExplicitStyle()
    {
        XElement style = KeyedStyle(@"RPA.Chat\Views\ChatPanel.xaml", "OutlinedComboBoxStyle", "ComboBox");
        XElement template = TemplateOf(style);

        Assert.That((string)template.Attribute("TargetType"), Is.EqualTo("ComboBox"));

        XElement toggle = template.Descendants(P + "ToggleButton").FirstOrDefault();

        Assert.That(toggle, Is.Not.Null, "шаблон списка обязан содержать внутренний ToggleButton");
        Assert.That(toggle.Attribute("Style"), Is.Not.Null,
            "без явного Style на ToggleButton ложится неявный стиль MaterialDesign (переключатель 34x18) " +
            "и «коробка» списка сжимается по горизонтали, хотя сам ComboBox растянут");
        Assert.That(toggle.Attribute("Width"), Is.Null, "фиксированная ширина возвращает дефект");
        Assert.That(toggle.Attribute("Height"), Is.Null, "фиксированная высота возвращает дефект");
    }

    [Test]
    public void OutlinedComboBox_ToggleButtonStyleStretchesToColumn()
    {
        XElement style = KeyedStyle(@"RPA.Chat\Views\ChatPanel.xaml", "ComboBoxToggleButtonStyle", "ToggleButton");

        Assert.Multiple(() =>
        {
            Assert.That(SetterValue(style, "Width"), Is.Null.Or.EqualTo("Auto"), "ширину задаёт столбец");
            Assert.That(SetterValue(style, "MinWidth"), Is.Null.Or.EqualTo("Auto").Or.EqualTo(0d),
                "MinWidth=\"Auto\" недопустим (NaN в LengthConverter → падение Style.Seal), поэтому 0");
            Assert.That(SetterValue(style, "HorizontalAlignment"), Is.EqualTo("Stretch"));
            Assert.That(SetterValue(style, "VerticalAlignment"), Is.EqualTo("Stretch"));
            Assert.That(SetterValue(style, "Focusable"), Is.EqualTo("False"),
                "фокус остаётся у ComboBox");
        });

        XElement template = TemplateOf(style);

        Assert.That(template, Is.Not.Null, "у стиля обязан быть свой шаблон: рамка + стрелка списка");
        Assert.That(template.Descendants(P + "Path").Any(), Is.True, "стрелка списка рисуется шаблоном");
    }

    [Test]
    public void OutlinedComboBox_StyleIsUsedByEveryAgentSettingsCombo()
    {
        string xaml = Read(@"RPA.Chat\Views\ChatPanel.xaml");
        int combos = System.Text.RegularExpressions.Regex.Matches(xaml, "<ComboBox Style=\"\\{StaticResource OutlinedComboBoxStyle\\}\"")
            .Count;

        Assert.That(combos, Is.GreaterThanOrEqualTo(3),
            "окно настроек агента (API/General/Sound) — все списки идут через OutlinedComboBoxStyle");
    }

    [Test]
    public void ReasoningExpanderHeader_HasExplicitToggleButtonStyle()
    {
        // Тот же дефект: HeaderSite наследует неявный стиль MaterialDesign и не растягивается по ширине.
        XElement style = KeyedStyle(@"RPA.Chat\Controls\ChatMessagesControl.xaml", "ReasoningExpanderStyle", "Expander");
        XElement header = TemplateOf(style).Descendants(P + "ToggleButton")
            .FirstOrDefault(e => (string)e.Attribute(X + "Name") == "HeaderSite");

        Assert.That(header, Is.Not.Null);
        Assert.That(header.Attribute("Style"), Is.Not.Null,
            "HeaderSite обязан ссылаться на явный стиль, иначе заголовок «Reasoning» сжимается");
    }

    [Test]
    public void ImplicitScrollBarStyle_ShrinksRealScrollViewerToTenPixels()
    {
        // Не только текст XAML: неявный стиль реально применяется к ScrollBar в дереве,
        // иначе ширина осталась бы системной (17px) или стилем MaterialDesign (~8px).
        var dictionary = (ResourceDictionary)Application.LoadComponent(new Uri("/AiRpaStudio;component/Themes/StudioScrollBar.xaml", UriKind.Relative));
        var root = new System.Windows.Controls.Grid();
        root.Resources.MergedDictionaries.Add(dictionary);

        var scrollViewer = new System.Windows.Controls.ScrollViewer
        {
            Content = new System.Windows.Controls.Border { Width = 1000, Height = 1000 },
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        };

        root.Children.Add(scrollViewer);
        root.Measure(new Size(200, 200));
        root.Arrange(new Rect(0, 0, 200, 200));
        root.UpdateLayout();

        var vertical = FindAll<System.Windows.Controls.Primitives.ScrollBar>(root)
            .Where(s => s.Orientation == System.Windows.Controls.Orientation.Vertical)
            .ToArray();

        Assert.That(vertical, Is.Not.Empty, "ScrollViewer не построил вертикальный скролл — шаблон стиля сломан");
        Assert.That(vertical[0].ActualWidth, Is.EqualTo(10d), "глобальный стиль обязан сжать скролл до 10px");

        var horizontal = FindAll<System.Windows.Controls.Primitives.ScrollBar>(root)
            .Where(s => s.Orientation == System.Windows.Controls.Orientation.Horizontal)
            .ToArray();

        Assert.That(horizontal, Is.Not.Empty, "ScrollViewer не построил горизонтальный скролл — шаблон стиля сломан");
        Assert.That(horizontal[0].ActualHeight, Is.EqualTo(10d), "горизонтальный скролл тоже 10px");
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    // ---------------------------------------------------------------- помощники

    private static XElement KeyedStyle(string relativePath, string key, string targetType)
    {
        XElement style = XDocument.Parse(Read(relativePath), LoadOptions.PreserveWhitespace)
            .Descendants(P + "Style")
            .FirstOrDefault(e => (string)e.Attribute(X + "Key") == key);

        Assert.That(style, Is.Not.Null, $"{relativePath}: не найден Style x:Key=\"{key}\"");
        Assert.That((string)style.Attribute("TargetType"), Is.EqualTo(targetType), $"{relativePath}: TargetType {key}");
        return style;
    }

    private static XElement ImplicitStyle(string relativePath, string targetType)
    {
        return XDocument.Parse(Read(relativePath), LoadOptions.PreserveWhitespace)
            .Descendants(P + "Style")
            .FirstOrDefault(e => e.Attribute(X + "Key") == null && (string)e.Attribute("TargetType") == targetType);
    }

    private static XElement TemplateOf(XElement style)
    {
        XElement setter = style.Elements(P + "Setter").FirstOrDefault(e => (string)e.Attribute("Property") == "Template");

        Assert.That(setter, Is.Not.Null, "у стиля нет шаблона");
        return setter.Descendants(P + "ControlTemplate").FirstOrDefault();
    }

    private static XElement TemplateTrigger(XElement template, string property, string value)
    {
        return template.Elements(P + "ControlTemplate.Triggers")
            .Descendants(P + "Trigger")
            .FirstOrDefault(e => (string)e.Attribute("Property") == property && (string)e.Attribute("Value") == value);
    }

    private static object SetterValue(XElement style, string property)
    {
        XElement setter = style.Elements(P + "Setter").FirstOrDefault(e => (string)e.Attribute("Property") == property);
        if (setter == null)
        {
            return null;
        }

        string raw = (string)setter.Attribute("Value");
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : (object)raw;
    }

    private static object TargetedSetterValue(XElement triggersOwner, string targetName, string property)
    {
        XElement setter = triggersOwner.Elements(P + "Setter").FirstOrDefault(e =>
            (string)e.Attribute("Property") == property &&
            (targetName == null ? e.Attribute("TargetName") == null : (string)e.Attribute("TargetName") == targetName));

        if (setter == null)
        {
            return null;
        }

        string raw = (string)setter.Attribute("Value");
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : (object)raw;
    }

    private static string Read(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден {path}");
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
