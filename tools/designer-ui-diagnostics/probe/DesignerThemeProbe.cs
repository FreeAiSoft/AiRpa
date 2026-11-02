// Диагностический пробник темы дизайнера WF.
//
// Зачем: баги темы дизайнера WF видны только на НАСТОЯЩЕМ WorkflowDesigner — синтетическое дерево
// в юнит-тестах не воспроизводит ни плитки, которые WF запекает через {StaticResource}, ни словари
// цветов, которые WF подмешивает в ресурсы СВОЕГО шаблона активности.
//
// Пробник воспроизводит хост студии (ресурсы App.xaml + MaterialDesign + переключение темы через
// AppSettings.ApplyTheme), открывает два сценария (пустая активность и workflow с вложенными
// активностями), прогоняет проходы темизации и печатает:
//   * плиту пустой активности и элементы, оставшиеся на цвете ЧУЖОЙ палитры;
//   * кнопки «Развернуть все»/«Свернуть все» (регресс P-10);
//   * вид каждой активности: фон/рамка/подпись плиты и текстов внутри (задача «тёмные активности»);
//   * контролы внутри активностей: тип, размер, шрифт и ОТКУДА пришёл их стиль (локальный,
//     MaterialDesign из App.xaml, WF-стиль) — чтобы видеть, чей стиль перебивает нативную форму WF;
//   * словари цветов WF (CachedResourceDictionaryExtension + WorkflowDesignerColors.DefaultColors):
//     какие кисти там лежат, заморожены ли они и можно ли их перекрасить. Без этого нельзя выбрать
//     механизм темизации активностей: WF берёт цвета из СВОЕЙ копии ColorResources.xaml, которая
//     подмешана в ControlTemplate.Resources шаблона активности и потому ближе к элементу, чем словарь
//     приложения (см. WorkflowElementDesignerDefaultTemplate.xaml).
//
// Запуск: Invoke-DesignerThemeProbe.ps1 (см. README.md рядом). Аргумент [0] — путь к файлу отчёта:
// консоль при перенаправлении вывода портит кириллицу, поэтому отчёт всегда пишется в UTF-8.
//
// Ссылается на AiRpaStudio.exe, WPF и WF; internal-члены студии вызываются по рефлексии,
// чтобы пробник не зависел от InternalsVisibleTo.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using System.Activities;
using System.Activities.Core.Presentation;
using System.Activities.Presentation;
using System.Activities.Presentation.View;
using System.Activities.Statements;

internal static class DesignerThemeProbe
{
    private static readonly List<string> Report = new List<string>();
    private static Assembly _studio;
    private static Type _paletteType;
    private static Type _colorsType;
    private static MethodInfo _applyTheme;
    private static Application _host;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (Exception)
        {
            // Консоль может не поддерживать смену кодировки — отчёт всё равно будет в файле.
        }

        int exitCode;
        try
        {
            exitCode = Run();
        }
        catch (Exception ex)
        {
            Log("FAILED: " + ex);
            exitCode = 2;
        }

        foreach (string line in Report)
        {
            Console.WriteLine(line);
        }

        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
        {
            System.IO.File.WriteAllLines(args[0], Report, new System.Text.UTF8Encoding(false));
        }

        return exitCode;
    }

    private static int Run()
    {
        _studio = LoadStudio();
        _paletteType = _studio.GetType("RPA.Helpers.StudioPalette", throwOnError: true);
        _colorsType = _studio.GetType("RPA.Helpers.StudioThemeColors", throwOnError: true);
        Type themeType = _studio.GetType("RPA.Helpers.WfDesignerTheme", throwOnError: true);

        MethodInfo apply = themeType.GetMethod(
            "Apply",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(FrameworkElement), _colorsType },
            null);

        if (apply == null)
        {
            Log("FAILED: не найден WfDesignerTheme.Apply(FrameworkElement, StudioThemeColors)");
            return 2;
        }

        Log("студия: " + _studio.Location);

        StartHostApp();
        new DesignerMetadata().Register();

        ReportHostResources();

        object light = Palette("Light");
        object dark = Palette("Dark");

        RunScenario(apply, "ПУСТАЯ АКТИВНОСТЬ (ActivityBuilder)", new ActivityBuilder(), light, dark);
        RunScenario(apply, "WORKFLOW С АКТИВНОСТЯМИ (Sequence + WriteLine)", BuildWorkflow(), light, dark);
        RunScenario(apply, "АКТИВНОСТЬ С КАСТОМНЫМ ДИЗАЙНЕРОМ (Activities.Debug.Logger)", BuildCustomWorkflow(), light, dark);

        ReportWfColourDictionaries();

        return 0;
    }

    /// <summary>Поднимает ресурсы студии так же, как это делает приложение (App.xaml + тема).</summary>
    private static void StartHostApp()
    {
        // Ресурсы хоста собираются вручную: App.xaml студии не поднимаем — его Startup создаёт MainView.
        _host = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        _host.Resources.MergedDictionaries.Add(new BundledTheme
        {
            BaseTheme = BaseTheme.Dark,
            PrimaryColor = PrimaryColor.Grey,
            SecondaryColor = SecondaryColor.Inherit
        });

        AddPack("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml");
        AddPack("pack://application:,,,/AiRpaStudio;component/Themes/StudioTheme.xaml");
        AddPack("pack://application:,,,/AiRpaStudio;component/Themes/StudioScrollBar.xaml");

        Type settingsType = _studio.GetType("RPA.Models.AppSettings", throwOnError: true);
        _applyTheme = settingsType.GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic);

        ApplyHostTheme(dark: true);
    }

    private static void AddPack(string uri)
    {
        try
        {
            _host.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
        }
        catch (Exception ex)
        {
            Log("  (словарь не загружен: " + uri + " - " + ex.GetType().Name + ")");
        }
    }

    private static void ApplyHostTheme(bool dark)
    {
        try
        {
            _applyTheme.Invoke(GetSettingsInstance(), new object[] { dark });
        }
        catch (TargetInvocationException ex)
        {
            string reason = ex.InnerException == null
                ? ex.Message
                : ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
            Log("  (тема хоста не применилась: " + reason + ")");
        }
    }

    private static object GetSettingsInstance()
    {
        Type settingsType = _studio.GetType("RPA.Models.AppSettings", throwOnError: true);
        return settingsType.GetMethod("GetInstance").Invoke(null, null);
    }

    private static Activity BuildWorkflow()
    {
        var sequence = new Sequence { DisplayName = "Sequence" };
        sequence.Activities.Add(new WriteLine { DisplayName = "WriteLine" });

        var nested = new Sequence { DisplayName = "Nested" };
        nested.Activities.Add(new WriteLine { DisplayName = "Inner" });
        sequence.Activities.Add(nested);

        return sequence;
    }

    private static void RunScenario(
        MethodInfo apply,
        string title,
        object activity,
        object light,
        object dark)
    {
        var designer = new WorkflowDesigner();
        designer.Load(activity);
        var view = (FrameworkElement)designer.View;

        var window = new Window { Width = 1000, Height = 760, Content = view, Title = "designer theme probe" };
        window.Show();
        Pump(8);

        Log("################ " + title + " ################");
        ReportPass(view, "FRESH (до темизации)", light);

        // «Первое включение тёмной темы» — главный сценарий задачи.
        Pass(apply, view, dark, "DARK (первое включение)", light, true);
        Pass(apply, view, light, "LIGHT", dark, false);
        Pass(apply, view, dark, "DARK AGAIN", light, true);

        window.Close();
        Pump(2);
    }

    private static void Pass(
        MethodInfo apply,
        FrameworkElement view,
        object palette,
        string name,
        object foreign,
        bool hostDark)
    {
        ApplyHostTheme(hostDark);

        try
        {
            _ = apply.Invoke(null, new object[] { view, palette });
        }
        catch (TargetInvocationException ex)
        {
            Log("FAILED на проходе " + name + ": " + ex.InnerException);
            return;
        }

        Pump(4);
        ReportPass(view, name, foreign);
    }

    /// <summary>Проход отчёта: плита пустой активности и элементы на цвете чужой палитры.</summary>
    private static void ReportPass(FrameworkElement root, string pass, object foreign)
    {
        Color foreignSurface = ColorOf(foreign, "Surface");
        Color foreignStrong = ColorOf(foreign, "SeparatorStrong");
        Color foreignText = ColorOf(foreign, "Foreground");

        var staleSurface = new List<string>();
        var staleBorder = new List<string>();
        var staleText = new List<string>();
        string plate = "(плита пустой активности не найдена)";

        foreach (DependencyObject element in Walk(root))
        {
            var fe = element as FrameworkElement;
            string size = fe != null ? string.Format("{0:0}x{1:0}", fe.ActualWidth, fe.ActualHeight) : "?";

            Color? background = ColorFrom(BackgroundOf(element));
            Color? border = ColorFrom(BorderBrushOf(element));
            Color? text = ColorFrom(ForegroundOf(element));

            if (background == foreignSurface)
            {
                staleSurface.Add(element.GetType().Name + " " + size);
            }

            if (border == foreignStrong)
            {
                staleBorder.Add(element.GetType().Name + " " + size);
            }

            if (text == foreignText)
            {
                staleText.Add(element.GetType().Name + " " + size);
            }

            // Плита пустой активности — крупная непрозрачная плашка внутри поверхности дизайнера.
            if (fe is Border && fe.ActualWidth > 380 && fe.ActualHeight > 380 && background.HasValue)
            {
                plate = size + " background=" + background;
            }
        }

        Log("=== " + pass + " === (чужой цвет: Surface=" + foreignSurface
            + ", SeparatorStrong=" + foreignStrong + ", Foreground=" + foreignText + ")");
        Log("  плита: " + plate);
        LogItems("  фон чужой поверхности", staleSurface);
        LogItems("  рамка чужой палитры", staleBorder);
        ReportHeaderButtons(root);
        LogItems("  текст чужой палитры", staleText);
        ReportActivities(root);
        Log(string.Empty);
    }

    /// <summary>
    /// Кнопки «Развернуть все»/«Свернуть все» в шапке дизайнера: размер, ширина подписи и отступ от
    /// правого края. Регресс P-10: неявный стиль MaterialDesign задаёт <c>Width=34</c>, а подпись
    /// «Развернуть все» — 83 px, поэтому в шапке читались только «разв» и «свер».
    /// </summary>
    private static void ReportHeaderButtons(FrameworkElement root)
    {
        foreach (string name in new[] { "expandAllButton", "collapseAllButton" })
        {
            var button = FindByName(root, name) as ButtonBase;
            if (button == null)
            {
                Log("  кнопка " + name + ": не найдена");
                continue;
            }

            var caption = button.Template == null
                ? null
                : button.Template.FindName("collapseAllText", button) as TextBlock;

            string captionText = caption == null
                ? "(нет)"
                : string.Format("{0:0.#}x{1:0.#}", caption.ActualWidth, caption.ActualHeight);

            bool fits = caption == null
                || caption.ActualWidth + caption.Margin.Left + caption.Margin.Right <= button.ActualWidth + 0.5;

            Log(string.Format(
                "  кнопка {0}: {1:0.#}x{2:0.#} Width={3} MinWidth={4:0.#} Margin.Right={5:0.#} подпись={6} влезает={7}",
                name,
                button.ActualWidth,
                button.ActualHeight,
                double.IsNaN(button.Width) ? "NaN" : button.Width.ToString("0.#"),
                button.MinWidth,
                button.Margin.Right,
                captionText,
                fits ? "да" : "НЕТ"));
        }
    }

    /// <summary>
    /// Вид активностей: плита, рамка, подписи и контролы внутри. Печатается на каждом проходе, чтобы
    /// видеть, ЧТО именно не перекрашивается при первом включении тёмной темы.
    /// </summary>
    private static void ReportActivities(FrameworkElement root)
    {
        List<WorkflowViewElement> activities = Walk(root).OfType<WorkflowViewElement>().ToList();
        Log("  активности: " + activities.Count);

        for (int i = 0; i < activities.Count && i < 4; i++)
        {
            WorkflowViewElement activity = activities[i];
            Log("    #" + (i + 1) + " " + activity.GetType().Name
                + " \"" + CaptionOf(activity) + "\" " + Size(activity)
                + " bg=" + BrushText(activity.Background)
                + " border=" + BrushText(activity.BorderBrush)
                + " fg=" + BrushText(activity.Foreground));

            int plates = 0;
            foreach (DependencyObject element in Walk(activity))
            {
                if (!(element is FrameworkElement) && !(element is Shape))
                {
                    continue;
                }

                var fe = element as FrameworkElement;
                string bg = BrushText(BackgroundOf(element));
                string border = BrushText(BorderBrushOf(element));
                string fill = BrushText(FillOf(element));
                string stroke = BrushText(StrokeOf(element));

                if (bg == "(нет)" && border == "(нет)" && fill == "(нет)" && stroke == "(нет)")
                {
                    continue;
                }

                double area = fe == null ? 0 : fe.ActualWidth * fe.ActualHeight;
                if (area < 400)
                {
                    continue;
                }

                if (plates++ >= 6)
                {
                    break;
                }

                Log("        вид " + element.GetType().Name + NameOf(element) + " " + Size(fe)
                    + " bg=" + bg + " border=" + border + " fill=" + fill + " stroke=" + stroke);
            }

            int texts = 0;
            foreach (TextBlock textBlock in Walk(activity).OfType<TextBlock>())
            {
                if (texts++ >= 6)
                {
                    break;
                }

                Log("        текст \"" + textBlock.Text + "\" fg=" + BrushText(textBlock.Foreground)
                    + " " + textBlock.FontFamily.Source + " " + textBlock.FontSize);
            }

            int controls = 0;
            foreach (DependencyObject element in Walk(activity))
            {
                if (!IsInteractive(element))
                {
                    continue;
                }

                if (controls++ >= 10)
                {
                    break;
                }

                var fe = (FrameworkElement)element;
                var control = element as Control;
                Log("        контрол " + element.GetType().Name + NameOf(element) + " " + Size(fe)
                    + " шрифт=" + (control == null ? "?" : control.FontFamily.Source + " " + control.FontSize)
                    + " " + StyleInfo(fe)
                    + " шаблон=" + TemplateOf(control));
            }
        }
    }

    private static bool IsInteractive(DependencyObject element)
    {
        return element is ButtonBase
            || element is CheckBox
            || element is RadioButton
            || element is TextBox
            || element is ComboBox
            || element is ExpressionTextBox;
    }

    /// <summary>Откуда пришёл стиль элемента: локальный, словарь приложения (MaterialDesign/студия) или WF.</summary>
    private static string StyleInfo(FrameworkElement element)
    {
        bool local = element.ReadLocalValue(FrameworkElement.StyleProperty) != DependencyProperty.UnsetValue;
        Style style = element.Style;
        string origin;

        if (style == null)
        {
            origin = "нет";
        }
        else
        {
            origin = FindStyleIn(_host == null ? null : _host.Resources, style);
            if (origin == null)
            {
                origin = "(не из ресурсов приложения: WF/система)";
            }
        }

        return "стиль=" + origin + (local ? " [локальный]" : string.Empty);
    }

    private static string FindStyleIn(ResourceDictionary dictionary, Style style)
    {
        if (dictionary == null)
        {
            return null;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            if (ReferenceEquals(entry.Value, style))
            {
                return Describe(dictionary);
            }
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            string found = FindStyleIn(merged, style);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static string Describe(ResourceDictionary dictionary)
    {
        return dictionary.Source == null ? "(встроенный словарь)" : dictionary.Source.ToString();
    }

    private static string TemplateOf(Control control)
    {
        if (control == null || control.Template == null)
        {
            return "нет";
        }

        return control.Template.VisualTree == null
            ? control.Template.GetType().Name
            : control.Template.VisualTree.Type == null
                ? control.Template.GetType().Name
                : control.Template.VisualTree.Type.Name;
    }

    /// <summary>
    /// Словари цветов WF: <c>CachedResourceDictionaryExtension</c> (кэш на процесс, из него WF берёт
    /// <c>ColorResources.xaml</c> для шаблонов активностей) и <c>WorkflowDesignerColors.DefaultColors</c>.
    /// Печатает, какие кисти там лежат, заморожены ли они и можно ли их перекрасить.
    /// </summary>
    private static void ReportWfColourDictionaries()
    {
        Log("################ СЛОВАРИ ЦВЕТОВ WF ################");

        FieldInfo cache = typeof(CachedResourceDictionaryExtension)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static);

        if (cache == null)
        {
            Log("  CachedResourceDictionaryExtension.Cache не найден (изменилась реализация WF)");
        }
        else if (!(cache.GetValue(null) is IDictionary map))
        {
            Log("  кэш словарей пуст");
        }
        else
        {
            Log("  закэшированных словарей: " + map.Count);
            foreach (DictionaryEntry entry in map)
            {
                var dictionary = entry.Value as ResourceDictionary;
                if (dictionary == null)
                {
                    continue;
                }

                string source = dictionary.Source == null ? "(inline)" : dictionary.Source.ToString();
                bool colours = source.IndexOf("ColorResources.xaml", StringComparison.OrdinalIgnoreCase) >= 0;
                Log("  - " + source + (colours ? "   [ЦВЕТА WF]" : string.Empty));

                if (!colours)
                {
                    continue;
                }

                foreach (string key in new[]
                {
                    "WorkflowViewElementBackground",
                    "WorkflowViewElementBorderBrush",
                    "WorkflowViewElementCaptionBrush",
                    "WorkflowViewElementSelectedBackground",
                    "WorkflowViewElementSelectedBorder",
                    "DesignerViewBackground",
                    "DesignerViewStatusBarBackground",
                    "ParallelSpacerColor",
                    "FlowchartConnectorColor"
                })
                {
                    ReportBrush(dictionary, key);
                }

                // Проба совместимости: меняется ли кисть на месте (её видят все, кто взял её
                // через {StaticResource} — то есть уже загруженные шаблоны WF).
                TryRecolour(dictionary, "WorkflowViewElementBackground", Color.FromRgb(0x27, 0x27, 0x27));
            }
        }

        PropertyInfo defaults = typeof(WorkflowDesignerColors)
            .GetProperty("DefaultColors", BindingFlags.NonPublic | BindingFlags.Static);

        if (defaults == null)
        {
            Log("  WorkflowDesignerColors.DefaultColors не найден");
            return;
        }

        if (defaults.GetValue(null, null) is ResourceDictionary defaultColours)
        {
            Log("  DefaultColors: ключей " + defaultColours.Count);
            ReportBrush(defaultColours, WorkflowDesignerColors.WorkflowViewElementBackgroundColorKey);
            ReportBrush(defaultColours, WorkflowDesignerColors.DesignerViewBackgroundColorKey);
            TryRecolour(
                defaultColours,
                WorkflowDesignerColors.WorkflowViewElementBackgroundColorKey,
                Color.FromRgb(0x27, 0x27, 0x27));
        }
    }

    private static void ReportBrush(ResourceDictionary dictionary, string key)
    {
        object value = dictionary[key];
        var brush = value as SolidColorBrush;

        if (brush == null)
        {
            Log("      " + key + ": " + (value == null ? "нет значения" : value.GetType().Name));
            return;
        }

        Log("      " + key + ": " + brush.Color + " IsFrozen=" + brush.IsFrozen);
    }

    private static void TryRecolour(ResourceDictionary dictionary, string key, Color colour)
    {
        var brush = dictionary[key] as SolidColorBrush;
        if (brush == null)
        {
            Log("      проба " + key + ": не SolidColorBrush");
            return;
        }

        try
        {
            brush.Color = colour;
            Log("      проба " + key + ": кисть перекрашена на месте -> " + colour);
        }
        catch (Exception ex)
        {
            Log("      проба " + key + ": перекрасить НЕЛЬЗЯ (" + ex.GetType().Name + ")");
        }
    }

    private static void LogItems(string title, List<string> items)
    {
        Log(title + ": " + items.Count);

        for (int i = 0; i < items.Count && i < 5; i++)
        {
            Log("    - " + items[i]);
        }
    }

    private static void Log(string line)
    {
        Report.Add(line);
    }

    private static Assembly LoadStudio()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i].GetType("RPA.Helpers.WfDesignerTheme", throwOnError: false) != null)
            {
                return loaded[i];
            }
        }

        string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AiRpaStudio.exe");
        return Assembly.LoadFrom(path);
    }

    private static object Palette(string name)
    {
        return _paletteType.GetProperty(name, BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
    }

    private static Color ColorOf(object palette, string token)
    {
        return (Color)_colorsType.GetProperty(token, BindingFlags.Public | BindingFlags.Instance)
            .GetValue(palette, null);
    }

    private static Brush BackgroundOf(DependencyObject element)
    {
        if (element is Border border)
        {
            return border.Background;
        }

        if (element is Control control)
        {
            return control.Background;
        }

        if (element is Panel panel)
        {
            return panel.Background;
        }

        return (element as Shape)?.Fill;
    }

    private static Brush BorderBrushOf(DependencyObject element)
    {
        if (element is Border border)
        {
            return border.BorderBrush;
        }

        return element is Control control ? control.BorderBrush : null;
    }

    private static Brush FillOf(DependencyObject element)
    {
        return (element as Shape)?.Fill;
    }

    private static Brush StrokeOf(DependencyObject element)
    {
        return (element as Shape)?.Stroke;
    }

    private static Brush ForegroundOf(DependencyObject element)
    {
        if (element is TextBlock textBlock)
        {
            return textBlock.Foreground;
        }

        return element is Control control ? control.Foreground : null;
    }

    private static Color? ColorFrom(Brush brush)
    {
        return brush is SolidColorBrush solid ? solid.Color : (Color?)null;
    }

    private static string BrushText(Brush brush)
    {
        if (brush == null)
        {
            return "(нет)";
        }

        return brush is SolidColorBrush solid ? solid.Color.ToString() : brush.GetType().Name;
    }

    private static string Size(FrameworkElement element)
    {
        return element == null ? "?" : string.Format("{0:0}x{1:0}", element.ActualWidth, element.ActualHeight);
    }

    private static string NameOf(DependencyObject element)
    {
        var fe = element as FrameworkElement;
        return fe == null || string.IsNullOrEmpty(fe.Name) ? string.Empty : "#" + fe.Name;
    }

    private static string CaptionOf(WorkflowViewElement activity)
    {
        try
        {
            if (activity.ModelItem == null)
            {
                return string.Empty;
            }

            var model = activity.ModelItem.GetCurrentValue() as Activity;
            return model == null ? string.Empty : model.DisplayName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static FrameworkElement FindByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement self && self.Name == name)
        {
            return self;
        }

        foreach (DependencyObject element in Walk(root))
        {
            if (element is FrameworkElement found && found.Name == name)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        if (root == null)
        {
            yield break;
        }

        yield return root;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            foreach (DependencyObject descendant in Walk(VisualTreeHelper.GetChild(root, i)))
            {
                yield return descendant;
            }
        }
    }

    private static void Pump(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            System.Threading.Thread.Sleep(30);
        }
    }
    private static Activity BuildCustomWorkflow()
    {
        var sequence = new Sequence { DisplayName = "Sequence" };

        try
        {
            Assembly assembly = Assembly.LoadFrom(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Activities.Debug.dll"));
            Type loggerType = assembly.GetType("Activities.Debug.Main.Logger", throwOnError: true);
            sequence.Activities.Add((Activity)Activator.CreateInstance(loggerType));
        }
        catch (Exception ex)
        {
            Log("  (кастомная активность не создана: " + ex.GetType().Name + ")");
        }

        return sequence;
    }

    /// <summary>Проверка, что ресурсы хоста (MaterialDesign + студия) действительно поднялись.</summary>
    private static void ReportHostResources()
    {
        ResourceDictionary resources = _host == null ? null : _host.Resources;
        Log("  словарей приложения: " + (resources == null ? 0 : resources.MergedDictionaries.Count));

        if (resources == null)
        {
            return;
        }

        object style = resources[typeof(Button)];
        string source = style == null ? "нет" : FindStyleIn(resources, style as Style);
        Log("  неявный стиль Button: " + (source ?? "(не найден в словарях)"));
    }

}
