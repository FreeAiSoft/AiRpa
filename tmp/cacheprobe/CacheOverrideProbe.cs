// Диагностический пробник варианта 1: «перехват кэша словаря цветов WF».
//
// Проверяет ровно то, что нужно знать ДО реализации, и ничего кроме:
//   1) поле CachedResourceDictionaryExtension.Cache — существует ли, статическое ли, записываемое,
//      какого типа и какие в нём URI-ключи (относительный и pack-вариант одного ColorResources.xaml);
//   2) можно ли ДО первого создания WF-контрола положить туда СВОЙ словарь (снимок WF-словаря как
//      MergedDictionaries + незамороженные кисти палитры) и получить тёмную плиту активности
//      БЕЗ единого прохода темизации (то есть без «второго прохода» и без мерцания);
//   3) замораживаются ли наши кисти после загрузки шаблонов — то есть работает ли смена темы
//      мутацией Color без обхода визуального дерева;
//   4) остаются ли в кэше только наши записи (WF не создал свой второй словарь);
//   5) precedence: наши ключи выигрывают, остальные приходят из merged WF-словаря (ключи не нужно
//      перечислять руками).
//
// Режимы: override — установка до загрузки дизайнера; control — без установки (эталон WF),
// после первого кадра установка «поздно», чтобы увидеть разницу.
//
// Запуск: Run-Probe.ps1 (см. рядом). Аргумент [0] — файл отчёта, [1] — режим.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Activities;
using System.Activities.Core.Presentation;
using System.Activities.Presentation;
using System.Activities.Presentation.View;
using System.Activities.Statements;

internal static class CacheOverrideProbe
{
    private const string ColorResourcesRelative =
        "/System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private const string ColorResourcesPack =
        "pack://application:,,,/System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private const string ColorResourcesComponent =
        "System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private static readonly List<string> Report = new List<string>();
    private static readonly Dictionary<string, SolidColorBrush> OurBrushes =
        new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal);

    private static Application _host;
    private static FieldInfo _cacheField;
    private static IDictionary _cache;
    private static ResourceDictionary _mine;

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

        string mode = args.Length > 1 ? args[1] : "override";

        try
        {
            Run(mode);
        }
        catch (Exception ex)
        {
            Log("FAILED: " + ex);
        }

        foreach (string line in Report)
        {
            Console.WriteLine(line);
        }

        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
        {
            System.IO.File.WriteAllLines(args[0], Report, new System.Text.UTF8Encoding(false));
        }

        return 0;
    }

    private static void Run(string mode)
    {
        Log("################ РЕЖИМ: " + mode + " ################");

        _host = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        ReportCache("1. на старте процесса (ни один WF-контрол ещё не создан)");

        bool installed = false;
        if (mode == "override")
        {
            installed = InstallOverride();
            ReportCache("2. сразу после установки своего словаря");
        }
        else
        {
            Log("2. КОНТРОЛЬ: свой словарь до загрузки дизайнера НЕ ставим (эталон поведения WF)");
        }

        new DesignerMetadata().Register();

        Window first = RunScenario("пустая активность (ActivityBuilder)", new ActivityBuilder());

        if (!installed && mode == "control")
        {
            // Поздняя установка: словарь WF уже разобран шаблонами, менять поздно — проверяем это.
            InstallOverride();
            ReportCache("3. поздняя установка (после первого кадра)");

            Measure((FrameworkElement)first.Content, "тот же дизайнер ПОСЛЕ поздней установки");
        }

        FlipTheme(ToLight(), "смена темы мутацией кистей: DARK → LIGHT");
        FlipTheme(ToDark(), "смена темы мутацией кистей: LIGHT → DARK");
        first.Close();
        Pump(2);

        Window second = RunScenario("Sequence + WriteLine (новые шаблоны)", BuildWorkflow());
        second.Close();
        Pump(2);

        Window third = RunScenario("поздняя активность: If/Parallel (шаблоны впервые после старта)", BuildLateWorkflow());
        third.Close();
        Pump(2);

        ReportCache("итог: состояние кэша после всех сценариев");
    }

    // ------------------------------------------------------------------ кэш

    private static void ReportCache(string title)
    {
        Log(string.Empty);
        Log("--- КЭШ: " + title + " ---");

        _cacheField = typeof(CachedResourceDictionaryExtension).GetField(
            "Cache",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (_cacheField == null)
        {
            Log("  поле CachedResourceDictionaryExtension.Cache НЕ найдено");
            return;
        }

        Log("  поле: " + _cacheField.DeclaringType.FullName + "." + _cacheField.Name
            + " IsStatic=" + _cacheField.IsStatic
            + " IsInitOnly=" + _cacheField.IsInitOnly
            + " Type=" + _cacheField.FieldType.FullName);

        if (_cache == null)
        {
            _cache = _cacheField.GetValue(null) as IDictionary;
        }

        if (_cache == null)
        {
            Log("  значение поля не IDictionary");
            return;
        }

        Log("  записей: " + _cache.Count);

        foreach (DictionaryEntry entry in _cache)
        {
            var uri = entry.Key as Uri;
            var dictionary = entry.Value as ResourceDictionary;
            string text = uri == null ? "(ключ не Uri)" : uri.OriginalString
                + (uri.IsAbsoluteUri ? "   [absolute]" : "   [relative]");

            bool colours = uri != null
                && uri.OriginalString.IndexOf("ColorResources.xaml", StringComparison.OrdinalIgnoreCase) >= 0;

            Log("   - " + text
                + "  keys=" + (dictionary == null ? "?" : dictionary.Count.ToString())
                + (colours ? "   [ЦВЕТА WF]" : string.Empty)
                + (ReferenceEquals(dictionary, _mine) ? "   <== НАШ СЛОВАРЬ" : string.Empty));
        }
    }

    // ------------------------------------------------------------------ установка

    private static bool InstallOverride()
    {
        Log(string.Empty);
        Log("*** УСТАНОВКА своего словаря цветов в кэш WF ***");

        if (_cache == null || _cache.Count > 0)
        {
            Log("  ВНИМАНИЕ: кэш не пуст — установка поздняя, часть шаблонов уже разобрана");
        }

        ResourceDictionary wf;
        try
        {
            wf = (ResourceDictionary)Application.LoadComponent(new Uri(ColorResourcesComponent, UriKind.Relative));
        }
        catch (Exception ex)
        {
            Log("  НЕ удалось загрузить WF ColorResources.xaml: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }

        Log("  снимок WF-словаря: ключей " + wf.Count
            + " (идёт в MergedDictionaries — перечислять ключи руками не нужно)");

        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in wf)
        {
            object value = entry.Value;
            string kind = value == null ? "null" : value.GetType().Name;
            kinds[kind] = kinds.TryGetValue(kind, out int count) ? count + 1 : 1;
        }

        foreach (KeyValuePair<string, int> pair in kinds.OrderByDescending(p => p.Value))
        {
            Log("    тип значения " + pair.Key + ": " + pair.Value);
        }

        var frozen = wf.Values.OfType<Freezable>().Count(f => f.IsFrozen);
        Log("    замороженных Freezable в снимке: " + frozen + " из " + wf.Values.OfType<Freezable>().Count());

        _mine = new ResourceDictionary();
        _mine.MergedDictionaries.Add(wf);

        foreach (KeyValuePair<string, Color> pair in ToDark())
        {
            var brush = new SolidColorBrush(pair.Value);
            _mine[pair.Key] = brush;
            OurBrushes[pair.Key] = brush;
        }

        Log("  наших ключей: " + OurBrushes.Count
            + " (кисти НЕ заморожены и одни на все шаблоны)");

        bool relative = Put(ColorResourcesRelative, UriKind.Relative);
        bool pack = Put(ColorResourcesPack, UriKind.Absolute);

        return relative && pack;
    }

    private static bool Put(string uri, UriKind kind)
    {
        try
        {
            _cache[new Uri(uri, kind)] = _mine;
            Log("  кэш[" + uri + "] <- наш словарь");
            return true;
        }
        catch (Exception ex)
        {
            Log("  НЕ удалось положить " + uri + ": " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ палитры

    private static Dictionary<string, Color> ToDark()
    {
        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "WorkflowViewElementBackground", Color.FromRgb(0x27, 0x27, 0x27) },
            { "WorkflowViewElementBorderBrush", Color.FromRgb(0x48, 0x48, 0x4F) },
            { "WorkflowViewElementCaptionBrush", Color.FromRgb(0xF1, 0xF1, 0xF1) },
            { "DesignerViewBackground", Color.FromRgb(0x1E, 0x1E, 0x1E) },
        };
    }

    private static Dictionary<string, Color> ToLight()
    {
        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "WorkflowViewElementBackground", Color.FromRgb(0xFF, 0xFF, 0xFF) },
            { "WorkflowViewElementBorderBrush", Color.FromRgb(0xD0, 0xD7, 0xE2) },
            { "WorkflowViewElementCaptionBrush", Color.FromRgb(0x1B, 0x29, 0x3E) },
            { "DesignerViewBackground", Color.FromRgb(0xFF, 0xFF, 0xFF) },
        };
    }

    // ------------------------------------------------------------------ сценарии

    private static Activity BuildWorkflow()
    {
        var sequence = new Sequence { DisplayName = "Sequence" };
        sequence.Activities.Add(new WriteLine { DisplayName = "WriteLine" });

        var nested = new Sequence { DisplayName = "Nested" };
        nested.Activities.Add(new WriteLine { DisplayName = "Inner" });
        sequence.Activities.Add(nested);

        return sequence;
    }

    private static Activity BuildLateWorkflow()
    {
        var sequence = new Sequence { DisplayName = "Late" };

        var branch = new If { DisplayName = "If" };
        branch.Then = new WriteLine { DisplayName = "Then" };
        sequence.Activities.Add(branch);

        var parallel = new Parallel { DisplayName = "Parallel" };
        sequence.Activities.Add(parallel);

        return sequence;
    }

    private static Window RunScenario(string title, object activity)
    {
        Log(string.Empty);
        Log("################ СЦЕНАРИЙ: " + title + " ################");

        var designer = new WorkflowDesigner();
        designer.Load(activity);

        var view = (FrameworkElement)designer.View;

        var window = new Window
        {
            Width = 1000,
            Height = 760,
            Left = -4000,
            Top = 80,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = view,
            Title = title
        };

        window.Show();
        Pump(10);

        Measure(view, "первый кадр (ни одного прохода темизации не было)");
        return window;
    }

    /// <summary>Меняет только Color у наших кистей — ни одного обхода дерева.</summary>
    private static void FlipTheme(Dictionary<string, Color> colors, string title)
    {
        Log(string.Empty);
        Log("--- " + title + " ---");

        foreach (KeyValuePair<string, Color> pair in colors)
        {
            if (!OurBrushes.TryGetValue(pair.Key, out SolidColorBrush brush))
            {
                continue;
            }

            try
            {
                brush.Color = pair.Value;
                Log("  кисть '" + pair.Key + "' -> " + pair.Value);
            }
            catch (Exception ex)
            {
                Log("  кисть '" + pair.Key + "' НЕ перекрашивается: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        Pump(4);
        Log("  (после мутации — только повторное чтение цветов у уже созданных элементов)");
    }

    // ------------------------------------------------------------------ измерения

    private static void Measure(FrameworkElement view, string title)
    {
        Log("=== ИЗМЕРЕНИЕ: " + title + " ===");

        foreach (KeyValuePair<string, SolidColorBrush> pair in OurBrushes)
        {
            Log("  наша кисть '" + pair.Key + "': color=" + pair.Value.Color + " IsFrozen=" + pair.Value.IsFrozen);
        }

        List<WorkflowViewElement> activities = Walk(view).OfType<WorkflowViewElement>().ToList();
        Log("  активностей: " + activities.Count);

        foreach (WorkflowViewElement activity in activities.Take(4))
        {
            Log("    " + activity.GetType().Name + " \"" + CaptionOf(activity) + "\" " + Size(activity)
                + " bg=" + BrushText(activity.Background) + Own(activity.Background)
                + " border=" + BrushText(activity.BorderBrush) + Own(activity.BorderBrush)
                + " fg=" + BrushText(activity.Foreground) + Own(activity.Foreground));
        }

        string plate = "(не найдена)";
        int wfLight = 0;
        int ours = 0;
        var samples = new List<string>();

        foreach (DependencyObject element in Walk(view))
        {
            Brush background = BackgroundOf(element);
            string text = BrushText(background);

            if (text == "#FFE9ECEE" || text == "#FFFFFFFF")
            {
                wfLight++;
            }

            if (Own(background) == "  <== НАША")
            {
                ours++;
            }

            var fe = element as FrameworkElement;
            double area = fe == null ? 0 : fe.ActualWidth * fe.ActualHeight;

            if (fe is Border && fe.ActualWidth > 380 && fe.ActualHeight > 380)
            {
                plate = Size(fe) + " background=" + text + Own(background);
            }

            if (samples.Count < 8 && area > 400)
            {
                string bg = BrushText(BackgroundOf(element));
                string fill = BrushText(FillOf(element));
                if (bg != "(нет)" || fill != "(нет)")
                {
                    samples.Add("      " + element.GetType().Name + " " + Size(fe)
                        + " bg=" + bg + " fill=" + fill);
                }
            }
        }

        Log("  плита: " + plate);
        Log("  элементов на светлом цвете WF (#E9ECEE/#FFFFFF): " + wfLight);
        Log("  элементов на НАШЕЙ кисти (тот же инстанс из словаря): " + ours);
        Log("  выборка элементов с фоном:");
        foreach (string sample in samples)
        {
            Log(sample);
        }

        Log(string.Empty);
    }

    private static string Own(Brush brush)
    {
        if (brush == null)
        {
            return string.Empty;
        }

        return OurBrushes.Values.Any(b => ReferenceEquals(b, brush)) ? "  <== НАША" : string.Empty;
    }

    private static string BrushText(Brush brush)
    {
        if (brush == null)
        {
            return "(нет)";
        }

        return brush is SolidColorBrush solid ? solid.Color.ToString() : brush.GetType().Name;
    }

    private static string CaptionOf(WorkflowViewElement element)
    {
        try
        {
            return element.ModelItem == null ? "?" : Convert.ToString(element.ModelItem.Properties["DisplayName"].Value);
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string Size(FrameworkElement element)
    {
        return element == null ? "?" : string.Format("{0:0}x{1:0}", element.ActualWidth, element.ActualHeight);
    }

    private static Brush BackgroundOf(DependencyObject element)
    {
        if (element is Control control)
        {
            return control.Background;
        }

        if (element is Panel panel)
        {
            return panel.Background;
        }

        if (element is Border border)
        {
            return border.Background;
        }

        if (element is Shape shape)
        {
            return shape.Fill;
        }

        return null;
    }

    private static Brush FillOf(DependencyObject element)
    {
        return element is Shape shape ? shape.Fill : null;
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            DependencyObject current = queue.Dequeue();
            yield return current;

            int count;
            try
            {
                count = VisualTreeHelper.GetChildrenCount(current);
            }
            catch (Exception)
            {
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = null;
                try
                {
                    child = VisualTreeHelper.GetChild(current, i);
                }
                catch (Exception)
                {
                    // RowDefinition/ColumnDefinition не Visual — VisualTreeHelper на них бросает.
                }

                if (child != null)
                {
                    queue.Enqueue(child);
                }
            }
        }
    }

    private static void Pump(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);
        }

        Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
    }

    private static void Log(string line)
    {
        Report.Add(line);
    }
}
