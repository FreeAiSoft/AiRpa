// Диагностический пробник №3 (вариант 1): можно ли получить МГНОВЕННУЮ смену темы без обхода дерева
// и без пост-покраски — если кисти в нашем словаре сделать ЗАВИСИМЫМИ (Binding на Color).
//
// Логика: WPF замораживает Freezable из Style/Template, но только если CanFreeze == true. Кисть
// с привязкой заморозить нельзя, поэтому StyleHelper делает на каждый элемент КЛОН (per-instance
// storage), а клон копирует и выражение привязки — то есть все клоны слушают один источник.
// Проверяем: (1) кисти не заморожены, (2) первый кадр сразу в палитре, (3) смена свойства источника
// меняет цвет уже созданных элементов БЕЗ прохода по дереву.
//
// Запуск: Run-Probe.ps1 -Probe probe3. Аргумент [0] — файл отчёта.

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

internal static class CacheOverrideProbe3
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
    private static readonly Dictionary<string, SolidColorBrush> Brushes =
        new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal);

    private static Application _host;
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
        Log("################ ПРОБНИК 3: кисти с привязкой (смена темы без обхода дерева) ################");

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

        _mine = new ResourceDictionary();
        _mine.MergedDictionaries.Add(wf);

        foreach (KeyValuePair<string, Color> pair in Dark())
        {
            var source = new ThemeColor { Value = pair.Value };
            var brush = new SolidColorBrush(pair.Value);
            BindingOperations.SetBinding(
                brush,
                SolidColorBrush.ColorProperty,
                new Binding(nameof(ThemeColor.Value)) { Source = source, Mode = BindingMode.OneWay });

            Sources[pair.Key] = source;
            Brushes[pair.Key] = brush;
            _mine[pair.Key] = brush;
        }

        Log("кисти с привязкой: " + string.Join(", ", Brushes.Select(p =>
            p.Key + " CanFreeze=" + p.Value.CanFreeze + " IsFrozen=" + p.Value.IsFrozen)));

        cache[new Uri(ColorResourcesRelative, UriKind.Relative)] = _mine;
        cache[new Uri(ColorResourcesPack, UriKind.Absolute)] = _mine;

        new DesignerMetadata().Register();

        var sequence = new Sequence { DisplayName = "Sequence" };
        sequence.Activities.Add(new WriteLine { DisplayName = "WriteLine" });
        sequence.Activities.Add(new WriteLine { DisplayName = "WriteLine 2" });

        var designer = new WorkflowDesigner();
        designer.Load(sequence);

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

        Log(string.Empty);
        Log("=== после первого кадра (проходов темизации не было) ===");
        Log("  кисти в словаре: " + string.Join(", ", Brushes.Select(p =>
            p.Key + " IsFrozen=" + p.Value.IsFrozen)));
        Log("  наш словарь IsReadOnly=" + _mine.IsReadOnly);
        Measure(view, "тёмная палитра (из источника)");

        Log(string.Empty);
        Log("--- меняем ТОЛЬКО свойство источника (ни одного обхода дерева) ---");

        foreach (KeyValuePair<string, Color> pair in Light())
        {
            Sources[pair.Key].Value = pair.Value;
            Log("  источник '" + pair.Key + "' -> " + pair.Value);
        }

        Pump(6);
        Measure(view, "светлая палитра (после смены источника)");

        window.Close();
        Pump(2);
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

    private static Dictionary<string, Color> Light()
    {
        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "WorkflowViewElementBackground", Color.FromRgb(0xFF, 0xFF, 0xFF) },
            { "WorkflowViewElementBorderBrush", Color.FromRgb(0xD0, 0xD7, 0xE2) },
            { "WorkflowViewElementCaptionBrush", Color.FromRgb(0x1B, 0x29, 0x3E) },
            { "DesignerViewBackground", Color.FromRgb(0xFF, 0xFF, 0xFF) },
        };
    }

    private static void Measure(FrameworkElement view, string title)
    {
        Log("=== ИЗМЕРЕНИЕ: " + title + " ===");

        foreach (WorkflowViewElement activity in Walk(view).OfType<WorkflowViewElement>().Take(6))
        {
            Log("  " + activity.GetType().Name + " bg=" + BrushText(activity.Background)
                + " border=" + BrushText(activity.BorderBrush));
        }

        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DependencyObject element in Walk(view))
        {
            string text = BrushText(BackgroundOf(element));
            histogram[text] = histogram.TryGetValue(text, out int count) ? count + 1 : 1;
        }

        foreach (KeyValuePair<string, int> pair in histogram.OrderByDescending(p => p.Value).Take(6))
        {
            Log("  фон " + pair.Key + ": " + pair.Value + " эл.");
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
}
