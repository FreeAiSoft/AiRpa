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
/// Пробник «иконки валидации при загрузке»: открывает workflow как студия
/// (WorkflowDesignerEx + те же публикации сервисов), держит дизайнер в окне в покое
/// и печатает состояние Border'ов x:Name="validationVisuals".
/// Блок A — покой после загрузки, блок B — после принудительного DesignerValidator.Validate.
/// </summary>
internal static class Program
{
    private static readonly Type ViewElementType =
        typeof(WorkflowDesigner).Assembly.GetType("System.Activities.Presentation.WorkflowViewElement");

    [STAThread]
    private static void Main(string[] args)
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(120));
            Console.WriteLine("WATCHDOG: принудительный выход");
            Console.Out.Flush();
            Environment.Exit(3);
        });
        watchdog.IsBackground = true;
        watchdog.Start();

        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED: " + ex);
        }
    }

    private static void Run(string[] args)
    {
        string workflow = args[0];
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        Assembly studio = typeof(WorkflowDesignerEx).Assembly;

        var designer = new WorkflowDesignerEx();
        DesignerConfigurationService configuration = designer.Context.Services.GetService<DesignerConfigurationService>();
        configuration.TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
        configuration.LoadingFromUntrustedSourceEnabled = true;

        Type debugServiceType = studio.GetType("RPA.Core.DebugValidationErrorService", true);
        var debugService = (IValidationErrorService)Activator.CreateInstance(debugServiceType, true);
        designer.Context.Services.Publish<IValidationErrorService>(debugService);

        designer.Load(workflow);
        Log("загружено: " + workflow);

        var view = (FrameworkElement)designer.View;
        var window = new Window { Width = 1200, Height = 900, Content = view, Title = "validprobe" };
        window.Show();
        Log("окно показано");

        PumpFor(TimeSpan.FromSeconds(6));
        Log("");
        Log("=== A: ПОКОЙ ПОСЛЕ ЗАГРУЗКИ (никаких изменений) ===");
        Report(designer, view);

        Log("");
        Log("=== B: ПРИНУДИТЕЛЬНЫЙ ПРОХОД DesignerValidator.Validate ===");
        Log("Validate() вызван: " + ForceValidate(designer));
        PumpFor(TimeSpan.FromSeconds(2));
        Report(designer, view);

        window.Close();
        app.Shutdown();
    }

    private static void Log(string text)
    {
        Console.WriteLine(text);
        Console.Out.Flush();
    }

    private static bool ForceValidate(WorkflowDesigner designer)
    {
        try
        {
            FieldInfo field = designer.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType.Name == "DesignerValidator");
            if (field == null)
            {
                Log("  поле DesignerValidator не найдено");
                return false;
            }

            object validator = field.GetValue(designer);
            MethodInfo validate = validator?.GetType().GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic);
            validate?.Invoke(validator, null);
            return validate != null;
        }
        catch (Exception ex)
        {
            Log("  сбой вызова: " + ex);
            return false;
        }
    }

    private static void Report(WorkflowDesigner designer, FrameworkElement view)
    {
        Log("  view: " + view.GetType().Name + " size=" + view.ActualWidth.ToString("0") + "x" + view.ActualHeight.ToString("0")
            + " IsLoaded=" + view.IsLoaded);

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

        ModelService modelService = designer.Context.Services.GetService<ModelService>();
        ModelItem root = modelService?.Root;
        ModelItem implementation = root?.Properties["Implementation"]?.Value;
        List<ModelItem> items = new List<ModelItem>();
        CollectModelItems(implementation, items);
        int noView = 0;
        foreach (ModelItem item in items)
        {
            object itemView = null;
            try { itemView = item.View; } catch (Exception ex) { Log("    View сбой: " + ex.GetType().Name); }
            bool hasView = itemView != null;
            if (!hasView) { noView++; }
            Log("    model: " + item.ItemType.Name
                + " (DisplayName=" + SafeDisplayName(item) + ")"
                + " view=" + (hasView ? itemView.GetType().Name : "null"));
        }
        Log("  model items: " + items.Count + ", без View: " + noView);
    }

    private static void CollectViewElements(DependencyObject root, List<string> result)
    {
        if (root == null) { return; }
        Type type = root.GetType();
        if (ViewElementType != null && ViewElementType.IsAssignableFrom(type))
        {
            object modelItem = type.GetProperty("ModelItem")?.GetValue(root);
            string text = modelItem is ModelItem mi ? mi.ItemType.Name + "/" + SafeDisplayName(mi) : "?";
            FrameworkElement element = root as FrameworkElement;
            result.Add(text + " (" + type.Name + " " + element?.ActualWidth.ToString("0") + "x" + element?.ActualHeight.ToString("0") + ")");
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            CollectViewElements(VisualTreeHelper.GetChild(root, i), result);
        }
    }

    private static string SafeDisplayName(ModelItem item)
    {
        try
        {
            return item.Properties["DisplayName"]?.Value?.GetCurrentValue()?.ToString() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static void CollectModelItems(ModelItem item, List<ModelItem> result)
    {
        if (item == null) { return; }
        result.Add(item);
        foreach (ModelProperty property in item.Properties)
        {
            if (property.Value != null && typeof(System.Activities.Activity).IsAssignableFrom(property.Value.ItemType))
            {
                CollectModelItems(property.Value, result);
            }
            if (property.Collection != null)
            {
                foreach (ModelItem child in property.Collection)
                {
                    if (typeof(System.Activities.Activity).IsAssignableFrom(child.ItemType))
                    {
                        CollectModelItems(child, result);
                    }
                }
            }
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
                string text = modelItem is ModelItem mi ? mi.ItemType.Name + "/" + SafeDisplayName(mi) : "?";
                return text + "(" + current.GetType().Name + ")";
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
