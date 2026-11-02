using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Activities;
using System.Activities.Presentation;
using RPA.Helpers;

/// <summary>
/// Сквозная проверка на НАСТОЯЩЕМ WorkflowDesigner: три прохода темизации
/// (light → dark → light) и подсчёт элементов, оставшихся на цвете чужой палитры.
/// Собирается как AiRpaStudio.Tests, чтобы видеть internal WfDesignerTheme.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var designer = new WorkflowDesigner();
        designer.Load(new ActivityBuilder());
        var view = (FrameworkElement)designer.View;

        var window = new Window { Width = 900, Height = 700, Content = view, Title = "probe" };
        window.Show();
        Pump(8);

        Report(view, "FRESH (без темизации)", StudioPalette.Dark);

        WfDesignerTheme.Apply(view, StudioPalette.Light);
        Pump(4);
        Report(view, "LIGHT", StudioPalette.Dark);

        WfDesignerTheme.Apply(view, StudioPalette.Dark);
        Pump(4);
        Report(view, "DARK", StudioPalette.Light);

        WfDesignerTheme.Apply(view, StudioPalette.Light);
        Pump(4);
        Report(view, "LIGHT AGAIN", StudioPalette.Dark);

        window.Close();
        Pump(2);
    }

    private static void Report(FrameworkElement root, string pass, StudioThemeColors foreign)
    {
        List<string> staleSurface = new List<string>();
        List<string> staleBorder = new List<string>();
        List<string> staleText = new List<string>();
        string plate = "(плита не найдена)";

        foreach (DependencyObject element in Walk(root))
        {
            FrameworkElement fe = element as FrameworkElement;

            string size = fe != null
                ? string.Format("{0:0}x{1:0}", fe.ActualWidth, fe.ActualHeight)
                : "?";

            Brush background = BackgroundOf(element);
            if (ColorFrom(background) == foreign.Surface)
            {
                staleSurface.Add(element.GetType().Name + " " + size + " " + ColorFrom(background));
            }

            Brush border = BorderBrushOf(element);
            if (ColorFrom(border) == foreign.SeparatorStrong)
            {
                staleBorder.Add(element.GetType().Name + " " + size + " " + ColorFrom(border));
            }

            Brush text = ForegroundOf(element);
            if (ColorFrom(text) == foreign.Foreground)
            {
                staleText.Add(element.GetType().Name + " " + size + " " + ColorFrom(text));
            }

            // Плита пустой активности: крупная плашка внутри поверхности дизайнера.
            if (fe is Border && fe.ActualWidth > 380 && fe.ActualHeight > 380
                && ColorFrom(background).HasValue)
            {
                plate = size + " background=" + ColorFrom(background);
            }
        }

        Console.WriteLine("=== " + pass + " === (чужой цвет: Surface=" + foreign.Surface + ", SeparatorStrong="
            + foreign.SeparatorStrong + ", Foreground=" + foreign.Foreground + ")");
        Console.WriteLine("  плита: " + plate);
        Print("  фон чужой поверхности", staleSurface);
        Print("  рамка чужой палитры", staleBorder);
        Print("  текст чужой палитры", staleText);
        Console.WriteLine();
    }

    private static void Print(string title, List<string> items)
    {
        Console.WriteLine(title + ": " + items.Count);

        for (int i = 0; i < items.Count && i < 5; i++)
        {
            Console.WriteLine("    - " + items[i]);
        }
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

        return null;
    }

    private static Brush BorderBrushOf(DependencyObject element)
    {
        if (element is Border border)
        {
            return border.BorderBrush;
        }

        return element is Control control ? control.BorderBrush : null;
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
}
