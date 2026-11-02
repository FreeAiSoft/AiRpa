using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Activities;
using System.Activities.Presentation;

internal static class Program
{
    private static readonly Color WfDesignerViewBackground = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color WfViewElementBackground = Color.FromRgb(0xE9, 0xEC, 0xEE);
    private static readonly Color WfViewElementBorder = Color.FromRgb(0xD0, 0xD7, 0xE2);
    private static readonly Color WfViewElementCaption = Color.FromRgb(0x1B, 0x29, 0x3E);

    private static readonly Color LightSurface = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color DarkSurface = Color.FromRgb(0x27, 0x27, 0x27);

    [STAThread]
    private static void Main()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED: " + ex);
        }
    }

    private static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var designer = new WorkflowDesigner();
        designer.Load(new ActivityBuilder());
        var view = (FrameworkElement)designer.View;

        var window = new Window { Width = 900, Height = 700, Content = view, Title = "repro" };
        window.Show();
        Pump(8);

        Console.WriteLine("=== FRESH STATE (before any theming) ===");
        DescribeFresh(view);

        Console.WriteLine("=== PASS 1: LIGHT (fresh, like opening a document in the light theme) ===");
        ApplyPass(view, LightSurface);
        Pump(4);
        Report(view);

        Console.WriteLine("=== PASS 2: DARK (theme toggle to dark) ===");
        ApplyPass(view, DarkSurface);
        Pump(4);
        Report(view);

        Console.WriteLine("=== PASS 3: LIGHT AGAIN (theme toggle back to light) ===");
        ApplyPass(view, LightSurface);
        Pump(4);
        Report(view);

        Console.WriteLine("=== FRESH DESIGNER, LIGHT ONLY (control) ===");
        var designer2 = new WorkflowDesigner();
        designer2.Load(new ActivityBuilder());
        var view2 = (FrameworkElement)designer2.View;
        window.Content = view2;
        Pump(8);
        ApplyPass(view2, LightSurface);
        Pump(4);
        Report(view2);

        window.Close();
        app.Shutdown();
    }

    /// <summary>Копия прохода WfDesignerTheme.ReplaceWorkflowDefaultColors (по значению дефолта WF).</summary>
    private static void ApplyPass(FrameworkElement root, Color surfaceColor)
    {
        var surface = new SolidColorBrush(surfaceColor);
        var separatorStrong = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF));
        var foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));

        if (root is Control c) { c.Background = surface; }
        else if (root is Panel p) { p.Background = surface; }

        Color[] designerBackgrounds = { WfDesignerViewBackground };
        Color[] elementBackgrounds = { WfViewElementBackground };
        Color[] elementBorders = { WfViewElementBorder };
        Color[] captions = { WfViewElementCaption };

        foreach (DependencyObject element in Walk(root))
        {
            Replace(element, Panel.BackgroundProperty, designerBackgrounds, surface);
            Replace(element, Panel.BackgroundProperty, elementBackgrounds, surface);
            Replace(element, Control.BackgroundProperty, designerBackgrounds, surface);
            Replace(element, Control.BackgroundProperty, elementBackgrounds, surface);
            Replace(element, Border.BackgroundProperty, designerBackgrounds, surface);
            Replace(element, Border.BackgroundProperty, elementBackgrounds, surface);
            Replace(element, Border.BorderBrushProperty, elementBorders, separatorStrong);
            Replace(element, Control.BorderBrushProperty, elementBorders, separatorStrong);
            Replace(element, Control.ForegroundProperty, captions, foreground);
            Replace(element, TextBlock.ForegroundProperty, captions, foreground);
        }
    }

    private static void Replace(DependencyObject element, DependencyProperty property, Color[] from, Brush to)
    {
        if (element == null || property == null || !(element.ReadLocalValue(property) is SolidColorBrush current))
        {
            return;
        }

        foreach (Color color in from)
        {
            if (current.Color == color)
            {
                try
                {
                    element.SetValue(property, to);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  set failed on " + element.GetType().Name + " : " + ex.GetType().Name);
                }

                return;
            }
        }
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            DependencyObject current = stack.Pop();
            yield return current;
            foreach (DependencyObject child in XamlChildren(current))
            {
                stack.Push(child);
            }
        }
    }

    private static IEnumerable<DependencyObject> XamlChildren(DependencyObject element)
    {
        var list = new List<DependencyObject>();

        if (!(element is Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
        {
            return list;
        }

        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            list.Add(VisualTreeHelper.GetChild(element, i));
        }

        return list;
    }

    private static void DescribeFresh(FrameworkElement root)
    {
        foreach (DependencyObject element in Walk(root))
        {
            foreach (string propertyName in new[] { "Background", "BorderBrush" })
            {
                DependencyProperty property = FindProperty(element.GetType(), propertyName);
                if (property == null)
                {
                    continue;
                }

                object local = element.ReadLocalValue(property);
                if (!(local is SolidColorBrush brush) || brush.Color.A == 0)
                {
                    continue;
                }

                var fe = element as FrameworkElement;
                double width = fe?.ActualWidth ?? 0;
                double height = fe?.ActualHeight ?? 0;

                if (width < 200 && height < 200)
                {
                    continue;
                }

                var source = DependencyPropertyHelper.GetValueSource(element, property);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0} '{1}'.{2} = #{3:X2}{4:X2}{5:X2} size={6:F0}x{7:F0} source={8} frozen={9}",
                    element.GetType().Name,
                    fe?.Name ?? string.Empty,
                    propertyName,
                    brush.Color.R,
                    brush.Color.G,
                    brush.Color.B,
                    width,
                    height,
                    source.BaseValueSource,
                    brush.IsFrozen));
            }
        }
    }

    private static void DescribeChain(DependencyObject element)
    {
        DependencyObject current = element;
        int depth = 0;
        while (current != null && depth < 12)
        {
            var fe = current as FrameworkElement;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "    ^ {0} '{1}' size={2:F0}x{3:F0}",
                current.GetType().Name,
                fe?.Name ?? string.Empty,
                fe?.ActualWidth ?? 0,
                fe?.ActualHeight ?? 0));
            current = fe?.Parent ?? System.Windows.Media.VisualTreeHelper.GetParent(current);
            depth++;
        }
    }

    private static void Report(FrameworkElement root)
    {
        int stale = 0;
        foreach (DependencyObject element in Walk(root))
        {
            foreach (string propertyName in new[] { "Background", "BorderBrush", "Fill", "Stroke", "Foreground" })
            {
                DependencyProperty property = FindProperty(element.GetType(), propertyName);
                if (property == null)
                {
                    continue;
                }

                object local = element.ReadLocalValue(property);
                if (!(local is SolidColorBrush brush))
                {
                    continue;
                }

                if (brush.Color == DarkSurface)
                {
                    stale++;
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  STALE {0} '{1}'.{2} = #{3:X2}{4:X2}{5:X2} size={6:F0}x{7:F0}",
                        element.GetType().Name,
                        (element as FrameworkElement)?.Name ?? string.Empty,
                        propertyName,
                        brush.Color.R,
                        brush.Color.G,
                        brush.Color.B,
                        (element as FrameworkElement)?.ActualWidth ?? 0,
                        (element as FrameworkElement)?.ActualHeight ?? 0));
                    DescribeChain(element);

                }
            }
        }

        Console.WriteLine("  total stale dark-surface brushes: " + stale);
    }

    private static DependencyProperty FindProperty(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name + "Property", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
            if (field != null && field.GetValue(null) is DependencyProperty property)
            {
                return property;
            }
        }

        return null;
    }

    private static void Pump(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
