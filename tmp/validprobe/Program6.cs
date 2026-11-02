using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Validation;
using RPA.Core;

/// <summary>
/// Пробник «иконки валидации»: открывает workflow как студия и печатает состояние
/// Border'ов x:Name="validationVisuals" в покое после загрузки (без единой правки).
/// </summary>
internal static class Program
{
    private static readonly Type ViewElementType =
        typeof(WorkflowDesigner).Assembly.GetType("System.Activities.Presentation.WorkflowViewElement");

    private static FieldInfo _modelItemsField;

    [STAThread]
    private static void Main(string[] args)
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(120));
            Log("WATCHDOG: принудительный выход");
            Environment.Exit(3);
        })
        { IsBackground = true };
        watchdog.Start();

        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            Log("FAILED: " + ex);
        }
    }

    private static void Run(string[] args)
    {
        string workflow = args[0];
        bool simulateStudioDocumentHosting = args.Length > 1 && args[1] == "late";

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        Assembly studio = typeof(WorkflowDesignerEx).Assembly;

        var designer = new WorkflowDesignerEx();
        DesignerConfigurationService configuration = designer.Context.Services.GetService<DesignerConfigurationService>();
        configuration.TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
        configuration.LoadingFromUntrustedSourceEnabled = true;

        Type debugServiceType = studio.GetType("RPA.Core.DebugValidationErrorService", true);
        var debugService = (IValidationErrorService)Activator.CreateInstance(debugServiceType, true);
        designer.Context.Services.Publish<IValidationErrorService>(debugService);

        try
        {
            designer.Load(workflow);
            Log("загружено: " + workflow);
        }
        catch (Exception ex)
        {
            Log("LOAD FAILED: " + ex);
            return;
        }

        if (simulateStudioDocumentHosting)
        {
            // Как в студии: документ сначала создаётся (дизайнер живёт без окна), и только потом
            // его вьюха попадает в панель. Значит первый проход валидации идёт до построения поверхности.
            Log("фаза 1: дизайнер без окна, 2 с (первый проход валидации)");
            PumpFor(TimeSpan.FromSeconds(2));
            Report(designer, null);
        }

        var view = (FrameworkElement)designer.View;
        var window = new Window { Width = 1200, Height = 900, Content = view, Title = "validprobe" };
        window.Show();
        Log("окно показано");

        PumpFor(TimeSpan.FromSeconds(4));
        Log("");
        Log("=== ПОКОЙ ПОСЛЕ ЗАГРУЗКИ (никаких изменений) ===");
        Report(designer, view);

        window.Close();
        app.Shutdown();
    }

    private static void Log(string text)
    {
        Console.WriteLine(text);
        Console.Out.Flush();
    }

    private static void Report(WorkflowDesigner designer, FrameworkElement view)
    {
        ModelService modelService = designer.Context.Services.GetService<ModelService>();
        ModelItem root = modelService?.Root;
        Log("  model root: " + (root == null ? "null" : root.ItemType.FullName));
        if (root != null)
        {
            Log("  root properties: " + string.Join(", ", root.Properties.Select(p => p.Name).Take(30)));
        }

        List<ModelItem> items = GetModelItems(designer);
        Log("  model items: " + (items == null ? "null" : items.Count.ToString()));

        if (view != null)
        {
            List<string> heads = new List<string>();
            CollectViewElements(view, heads);
            Log("  вьюх активностей в дереве: " + heads.Count);
            foreach (string head in heads)
            {
                Log("    * " + head);
            }

            List<Border> borders = new List<Border>();
            CollectValidationBorders(view, borders);
            Log("  validationVisuals найдено: " + borders.Count);
            foreach (Border border in borders)
            {
                Log("    " + DescribeOwner(border)
                    + " vis=" + border.Visibility
                    + " bgLocal=" + DescribeLocalValue(border, Border.BackgroundProperty)
                    + " bg=" + DescribeBrush(border.Background)
                    + " autoId=" + System.Windows.Automation.AutomationProperties.GetAutomationId(border)
                    + " tip=" + DescribeTip(border.ToolTip));
            }
        }

        if (items != null)
        {
            foreach (ModelItem item in items)
            {
                object itemView = null;
                try { itemView = item.View; } catch (Exception ex) { Log("    View сбой: " + ex.GetType().Name); }
                Log("    model: " + item.ItemType.Name + " view=" + (itemView == null ? "null" : itemView.GetType().Name));
            }
        }
    }

    private static List<ModelItem> GetModelItems(WorkflowDesigner designer)
    {
        try
        {
            if (_modelItemsField == null)
            {
                FieldInfo validatorField = designer.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(f => f.FieldType.Name == "DesignerValidator");
                if (validatorField == null)
                {
                    return null;
                }

                object validator = validatorField.GetValue(designer);
                _modelItemsField = validator?.GetType().GetMethod(
                    "GetModelItems",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (_modelItemsField == null)
                {
                    return null;
                }
            }

            FieldInfo field = designer.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType.Name == "DesignerValidator");
            object instance = field?.GetValue(designer);
            return (List<ModelItem>)_modelItemsField.Invoke(instance, null);
        }
        catch (Exception ex)
        {
            Log("  GetModelItems сбой: " + ex.GetType().Name + " " + ex.Message);
            return null;
        }
    }

    private static void CollectViewElements(DependencyObject root, List<string> result)
    {
        if (root == null) { return; }
        Type type = root.GetType();
        if (ViewElementType != null && ViewElementType.IsAssignableFrom(type))
        {
            object modelItem = type.GetProperty("ModelItem")?.GetValue(root);
            string text = modelItem is ModelItem mi ? mi.ItemType.Name : "?";
            FrameworkElement element = root as FrameworkElement;
            result.Add(text + " (" + type.Name + " " + element?.ActualWidth.ToString("0") + "x" + element?.ActualHeight.ToString("0") + ")");
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            CollectViewElements(VisualTreeHelper.GetChild(root, i), result);
        }
    }

    private static void CollectValidationBorders(DependencyObject root, List<Border> result)
    {
        if (root == null) { return; }
        if (root is Border border && string.Equals(border.Name, "validationVisuals", StringComparison.Ordinal))
        {
            result.Add(border);
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            CollectValidationBorders(VisualTreeHelper.GetChild(root, i), result);
        }
    }

    private static string DescribeOwner(DependencyObject border)
    {
        DependencyObject current = border;
        for (int i = 0; i < 20 && current != null; i++)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current == null) { break; }
            if (ViewElementType != null && ViewElementType.IsAssignableFrom(current.GetType()))
            {
                object modelItem = current.GetType().GetProperty("ModelItem")?.GetValue(current);
                return modelItem is ModelItem mi ? mi.ItemType.Name : "?";
            }
        }
        return "<владелец?>";
    }

    private static string DescribeLocalValue(DependencyObject element, DependencyProperty property)
    {
        object local = element.ReadLocalValue(property);
        if (local == DependencyProperty.UnsetValue) { return "unset"; }
        return DescribeBrush(local as Brush);
    }

    private static string DescribeBrush(Brush brush)
    {
        if (brush == null) { return "null"; }
        if (brush is SolidColorBrush solid) { return "#" + solid.Color.ToString(); }
        return brush.GetType().Name;
    }

    private static string DescribeTip(object tip)
    {
        if (tip == null) { return "null"; }
        string text = tip.ToString().Replace(Environment.NewLine, " | ");
        return text.Length > 200 ? text.Substring(0, 200) + "..." : text;
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Normal, (sender, args) =>
        {
            ((DispatcherTimer)sender).Stop();
            frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
