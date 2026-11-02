// Диагностический пробник №2 (вариант 1): что происходит с НАШИМИ кистями и словарём ПОСЛЕ того,
// как шаблоны WF их прочитали. Пробник №1 доказал, что первый кадр уже в палитре; здесь проверяется,
// чем можно переключать тему дальше:
//   * когда именно кисть становится IsFrozen (после установки / Register / создания дизайнера / кадра);
//   * остаётся ли наш ResourceDictionary записываемым (IsReadOnly) — можно ли подменить запись;
//   * обновляются ли элементы, если подменить запись в словаре (DynamicResource) и что остаётся
//     на старой кисти (StaticResource запечён при разборе шаблона);
//   * precedence: ключ, который мы НЕ переопределяли, приходит из merged WF-словаря.
//
// Запуск: Run-Probe.ps1 -Probe probe2. Аргумент [0] — файл отчёта.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Activities;
using System.Activities.Core.Presentation;
using System.Activities.Presentation;
using System.Activities.Presentation.View;
using System.Activities.Statements;

internal static class CacheOverrideProbe2
{
    private const string ColorResourcesRelative =
        "/System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private const string ColorResourcesPack =
        "pack://application:,,,/System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private const string ColorResourcesComponent =
        "System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private static readonly List<string> Report = new List<string>();
    private static readonly Dictionary<string, SolidColorBrush> Brushes =
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
            // Отчёт всё равно пишется в файл.
        }

        try
        {
            Run();
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

    private static void Run()
    {
        Log("################ ПРОБНИК 2: заморозка и подмена записей ################");

        _host = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        _cacheField = typeof(CachedResourceDictionaryExtension).GetField(
            "Cache",
            BindingFlags.NonPublic | BindingFlags.Static);

        _cache = _cacheField == null ? null : _cacheField.GetValue(null) as IDictionary;

        if (_cache == null)
        {
            Log("поле Cache недоступно — дальше проверять нечего");
            return;
        }

        ResourceDictionary wf = (ResourceDictionary)Application.LoadComponent(
            new Uri(ColorResourcesComponent, UriKind.Relative));

        Log("WF-словарь как MergedDictionaries: ключей " + wf.Count);

        _mine = new ResourceDictionary();
        _mine.MergedDictionaries.Add(wf);

        foreach (KeyValuePair<string, Color> pair in Dark())
        {
            var brush = new SolidColorBrush(pair.Value);
            _mine[pair.Key] = brush;
            Brushes[pair.Key] = brush;
        }

        State("A. после создания словаря (в кэш ещё не положен)");

        _cache[new Uri(ColorResourcesRelative, UriKind.Relative)] = _mine;
        _cache[new Uri(ColorResourcesPack, UriKind.Absolute)] = _mine;
        State("B. после установки в кэш");

        new DesignerMetadata().Register();
        State("C. после DesignerMetadata.Register()");

        var sequence = new Sequence { DisplayName = "Sequence" };
        sequence.Activities.Add(new WriteLine { DisplayName = "WriteLine" });

        var designer = new WorkflowDesigner();
        designer.Load(sequence);
        State("D. после new WorkflowDesigner() + Load()");

        var view = (FrameworkElement)designer.View;
        var window = new Window
        {
            Width = 1000,
            Height = 760,
            Left = -4000,
            Top = 80,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = view
        };

        window.Show();
        Pump(10);
        State("E. после первого кадра");
        Measure(view, "первый кадр");

        // --- подмена записи в словаре (сценарий смены темы) ---
        Log(string.Empty);
        Log("--- подмена записи 'WorkflowViewElementBackground' на светлую кисть ---");

        SolidColorBrush light = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        try
        {
            _mine["WorkflowViewElementBackground"] = light;
            Log("  запись подменена: словарь записываемый (IsReadOnly=" + _mine.IsReadOnly + ")");
        }
        catch (Exception ex)
        {
            Log("  запись НЕ подменяется: " + ex.GetType().Name + ": " + ex.Message
                + " (IsReadOnly=" + _mine.IsReadOnly + ")");
        }

        Pump(6);
        MeasureAgainst(view, "после подмены записи", light);

        window.Close();
        Pump(2);
    }

    private static Dictionary<string, Color> Dark()
    {
        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "WorkflowViewElementBackground", Color.FromRgb(0x27, 0x27, 0x27) },
            { "WorkflowViewElementBorderBrush", Color.FromRgb(0x48, 0x48, 0x4F) },
            { "WorkflowViewElementCaptionBrush", Color.FromRgb(0xF1, 0xF1, 0xF1) },
            { "DesignerViewBackground", Color.FromRgb(0x1E, 0x1E, 0x1E) },
        };
    }

    private static void State(string title)
    {
        Log(string.Empty);
        Log("=== " + title + " ===");

        string brushes = string.Join(", ", Brushes.Select(p => p.Key + "=" + p.Value.Color + "/frozen:" + p.Value.IsFrozen));
        Log("  наши кисти: " + brushes);
        Log("  наш словарь: keys=" + _mine.Count + " IsReadOnly=" + _mine.IsReadOnly
            + " merged=" + _mine.MergedDictionaries.Count);

        // precedence: переопределённый ключ — наш, непереопределённый — из merged WF-словаря.
        ReportValue("  переопределённый ключ WorkflowViewElementBackground", _mine["WorkflowViewElementBackground"]);
        ReportValue("  непереопределённый ключ ContextMenuBorderBrush", _mine["ContextMenuBorderBrush"]);
        ReportValue("  непереопределённый ключ AnnotationBorderBrush", _mine["AnnotationBorderBrush"]);
        ReportValue("  градиент ShellBarBackgroundBrush", _mine["ShellBarBackgroundBrush"]);
    }

    private static void ReportValue(string title, object value)
    {
        if (value is SolidColorBrush solid)
        {
            Log(title + ": SolidColorBrush " + solid.Color
                + " IsFrozen=" + solid.IsFrozen
                + (Brushes.Values.Any(b => ReferenceEquals(b, solid)) ? "  <== НАША" : string.Empty));
            return;
        }

        if (value is LinearGradientBrush gradient)
        {
            string stops = string.Join(";", gradient.GradientStops.Select(s => s.Color.ToString()));
            Log(title + ": LinearGradientBrush [" + stops + "] IsFrozen=" + gradient.IsFrozen);
            return;
        }

        Log(title + ": " + (value == null ? "нет значения" : value.GetType().Name));
    }

    private static void Measure(FrameworkElement view, string title)
    {
        Log(string.Empty);
        Log("=== ИЗМЕРЕНИЕ: " + title + " ===");

        foreach (WorkflowViewElement activity in Walk(view).OfType<WorkflowViewElement>().Take(6))
        {
            Log("  " + activity.GetType().Name + " bg=" + BrushText(activity.Background)
                + (activity.Background is SolidColorBrush sb && Brushes.Values.Any(b => ReferenceEquals(b, sb))
                    ? "  <== НАША (тёмная)"
                    : string.Empty));
        }

        int ours = 0;
        int frozenOurs = 0;
        foreach (DependencyObject element in Walk(view))
        {
            Brush background = BackgroundOf(element);
            if (background is SolidColorBrush solid && Brushes.Values.Any(b => ReferenceEquals(b, solid)))
            {
                ours++;
                if (solid.IsFrozen)
                {
                    frozenOurs++;
                }
            }
        }

        Log("  элементов на наших кистях: " + ours + " (из них замороженных: " + frozenOurs + ")");
    }

    private static void MeasureAgainst(FrameworkElement view, string title, SolidColorBrush expected)
    {
        Log(string.Empty);
        Log("=== ИЗМЕРЕНИЕ: " + title + " ===");

        int onNew = 0;
        int onOld = 0;

        foreach (DependencyObject element in Walk(view))
        {
            Brush background = BackgroundOf(element);
            var solid = background as SolidColorBrush;

            if (solid == null)
            {
                continue;
            }

            if (ReferenceEquals(solid, expected))
            {
                onNew++;
            }
            else if (Brushes.Values.Any(b => ReferenceEquals(b, solid)))
            {
                onOld++;
            }
        }

        foreach (WorkflowViewElement activity in Walk(view).OfType<WorkflowViewElement>().Take(6))
        {
            Log("  " + activity.GetType().Name + " bg=" + BrushText(activity.Background)
                + (ReferenceEquals(activity.Background, expected) ? "  <== НОВАЯ кисть (DynamicResource обновился)" : string.Empty));
        }

        Log("  элементов на НОВОЙ кисти: " + onNew + ", осталось на СТАРЫХ кистях: " + onOld);
        Log("  новая кисть IsFrozen=" + expected.IsFrozen);
    }

    private static string BrushText(Brush brush)
    {
        if (brush == null)
        {
            return "(нет)";
        }

        return brush is SolidColorBrush solid ? solid.Color.ToString() : brush.GetType().Name;
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

        return null;
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
                    // RowDefinition/ColumnDefinition не Visual.
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
