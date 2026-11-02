// Диагностический пробник №4 (вариант 1): полное покрытие — переопределяем ВСЕ ключи WF-словаря
// ColorResources.xaml (59), включая градиентные, кистями с привязкой. Проверяем:
//   * ни одного XamlParseException / отсутствующего ключа (шаблоны WF находят всё);
//   * первый кадр сразу в палитре на наборе разных активностей (Sequence/WriteLine/If/Parallel);
//   * смена только источника привязки перекрашивает ВСЁ, без обхода дерева.
//
// Запуск: Run-Probe.ps1 -Probe probe4. Аргумент [0] — файл отчёта.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Activities;
using System.Activities.Core.Presentation;
using System.Activities.Presentation;
using System.Activities.Presentation.View;
using System.Activities.Statements;

internal static class CacheOverrideProbe4
{
    private const string ColorResourcesRelative =
        "/System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private const string ColorResourcesPack =
        "pack://application:,,,/System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private const string ColorResourcesComponent =
        "System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private static readonly List<string> Report = new List<string>();
    private static readonly Dictionary<string, ThemeColor> Sources =
        new Dictionary<string, ThemeColor>(StringComparer.Ordinal);

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
        Log("################ ПРОБНИК 4: полное покрытие всех ключей ColorResources.xaml ################");

        _host = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        FieldInfo cacheField = typeof(CachedResourceDictionaryExtension).GetField(
            "Cache",
            BindingFlags.NonPublic | BindingFlags.Static);

        var cache = cacheField == null ? null : cacheField.GetValue(null) as IDictionary;

        if (cache == null)
        {
            Log("поле Cache недоступно");
            return;
        }

        ResourceDictionary wf = (ResourceDictionary)Application.LoadComponent(
            new Uri(ColorResourcesComponent, UriKind.Relative));

        var mine = new ResourceDictionary();
        int boundBrushes = 0;
        int copiedValues = 0;

        foreach (DictionaryEntry entry in wf)
        {
            object value = entry.Value;

            Color? colour = ExtractColour(value);
            if (colour.HasValue)
            {
                var source = new ThemeColor { Value = colour.Value };
                var brush = new SolidColorBrush(colour.Value);
                BindingOperations.SetBinding(
                    brush,
                    SolidColorBrush.ColorProperty,
                    new Binding(nameof(ThemeColor.Value)) { Source = source, Mode = BindingMode.OneWay });

                Sources[(string)entry.Key] = source;
                mine[entry.Key] = brush;
                boundBrushes++;
            }
            else
            {
                mine[entry.Key] = value;
                copiedValues++;
                Log("  ключ '" + entry.Key + "' скопирован как есть: "
                    + (value == null ? "null" : value.GetType().Name));
            }
        }

        Log("ключей всего: " + wf.Count + ", из них с привязкой на Color: " + boundBrushes
            + ", скопировано как есть: " + copiedValues);

        cache[new Uri(ColorResourcesRelative, UriKind.Relative)] = mine;
        cache[new Uri(ColorResourcesPack, UriKind.Absolute)] = mine;

        new DesignerMetadata().Register();

        var sequence = new Sequence { DisplayName = "Sequence" };
        var branch = new If { DisplayName = "If" };
        branch.Then = new WriteLine { DisplayName = "Then" };
        sequence.Activities.Add(branch);
        sequence.Activities.Add(new WriteLine { DisplayName = "WriteLine" });
        sequence.Activities.Add(new Parallel { DisplayName = "Parallel" });

        var designer = new WorkflowDesigner();
        designer.Load(sequence);

        var view = (FrameworkElement)designer.View;
        var window = new Window
        {
            Width = 1100,
            Height = 800,
            Left = -4000,
            Top = 80,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = view
        };

        window.Show();
        Pump(10);

        Log(string.Empty);
        Log("=== первый кадр: тёмные значения из источников, ни одного прохода темизации ===");
        Log("  словарь IsReadOnly=" + mine.IsReadOnly);
        Measure(view);

        Log(string.Empty);
        Log("--- меняем ТОЛЬКО источники (все ключи сразу), дерево не обходим ---");

        var light = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "WorkflowViewElementBackground", Color.FromRgb(0xE6, 0xE6, 0xE6) },
            { "WorkflowViewElementBorderBrush", Color.FromRgb(0xC8, 0x64, 0x00) },
            { "WorkflowViewElementCaptionBrush", Color.FromRgb(0x00, 0x33, 0x66) },
            { "DesignerViewBackground", Color.FromRgb(0xFA, 0xFA, 0xFA) },
        };

        foreach (KeyValuePair<string, ThemeColor> pair in Sources)
        {
            if (light.TryGetValue(pair.Key, out Color colour))
            {
                pair.Value.Value = colour;
                continue;
            }

            // Остальные ключи — просто сдвигаем в «светлую» сторону, чтобы увидеть, что они подхватились.
            pair.Value.Value = Lighten(pair.Value.Value);
        }

        Pump(6);
        Log(string.Empty);
        Log("=== после смены источников ===");
        Measure(view);

        window.Close();
        Pump(2);
    }

    private static Color Lighten(Color colour)
    {
        byte channel(byte value) => (byte)(0xC0 + (value / 8));
        return Color.FromArgb(colour.A, channel(colour.R), channel(colour.G), channel(colour.B));
    }

    private static Color? ExtractColour(object value)
    {
        if (value is SolidColorBrush solid)
        {
            return solid.Color;
        }

        if (value is LinearGradientBrush gradient && gradient.GradientStops.Count > 0)
        {
            // Градиентный ключ заменяем на однотонную кисть: значение ключа — просто Brush,
            // шаблоны WF не разбирают его на остановки.
            return gradient.GradientStops[0].Color;
        }

        return null;
    }

    private sealed class ThemeColor : INotifyPropertyChanged
    {
        private Color _value;

        public Color Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    private static void Measure(FrameworkElement view)
    {
        foreach (WorkflowViewElement activity in Walk(view).OfType<WorkflowViewElement>())
        {
            Log("  " + activity.GetType().Name + " bg=" + BrushText(activity.Background)
                + " border=" + BrushText(activity.BorderBrush) + " fg=" + BrushText(activity.Foreground));
        }

        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DependencyObject element in Walk(view))
        {
            string text = BrushText(BackgroundOf(element));
            histogram[text] = histogram.TryGetValue(text, out int count) ? count + 1 : 1;
        }

        Log("  гистограмма фонов (top6):");
        foreach (KeyValuePair<string, int> pair in histogram.OrderByDescending(p => p.Value).Take(6))
        {
            Log("    " + pair.Key + ": " + pair.Value);
        }
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

    private static Application _host;
}
