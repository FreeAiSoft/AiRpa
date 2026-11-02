using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;

/// <summary>
/// РџСЂРѕР±РЅРёРє В«РёРєРѕРЅРєРё РІР°Р»РёРґР°С†РёРё РїСЂРё Р·Р°РіСЂСѓР·РєРµВ»: РѕС‚РєСЂС‹РІР°РµС‚ workflow С‚Р°Рє Р¶Рµ, РєР°Рє СЃС‚СѓРґРёСЏ
/// (RPA.Helpers.LoadActivityToDesignerHelper.LoadActivityToDesigner), РґРµСЂР¶РёС‚ РґРёР·Р°Р№РЅРµСЂ РІ РѕРєРЅРµ
/// РІ СЃРѕСЃС‚РѕСЏРЅРёРё РїРѕРєРѕСЏ Рё РїРµС‡Р°С‚Р°РµС‚ СЃРѕСЃС‚РѕСЏРЅРёРµ Border'РѕРІ x:Name="validationVisuals".
/// Р—Р°С‚РµРј РїСЂРёРЅСѓРґРёС‚РµР»СЊРЅРѕ РІС‹Р·С‹РІР°РµС‚ DesignerValidator.Validate Рё РїРµС‡Р°С‚Р°РµС‚ СЃРѕСЃС‚РѕСЏРЅРёРµ СЃРЅРѕРІР° вЂ”
/// СЌС‚Рѕ A/B В«РїРѕСЃР»Рµ Р·Р°РіСЂСѓР·РєРё РІ РїРѕРєРѕРµВ» РїСЂРѕС‚РёРІ В«РїРѕСЃР»Рµ Р»СЋР±РѕРіРѕ РёР·РјРµРЅРµРЅРёСЏВ».
/// РљРѕРґ СЃС‚СѓРґРёРё РЅРµ РјРµРЅСЏРµС‚.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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

        Assembly studio = typeof(RPA.Core.WorkflowDesignerEx).Assembly;
        Type helper = studio.GetType("RPA.Helpers.LoadActivityToDesignerHelper", true);
        MethodInfo load = helper.GetMethod(
            "LoadActivityToDesigner",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        object loaded = load.Invoke(null, new object[] { workflow });
        var designer = (WorkflowDesigner)loaded;
        Console.WriteLine("designer = " + (designer == null ? "null" : designer.GetType().FullName));

        var view = (FrameworkElement)designer.View;
        var window = new Window { Width = 1200, Height = 900, Content = view, Title = "validprobe" };
        window.Show();

        PumpFor(TimeSpan.FromSeconds(6));
        Console.WriteLine();
        Console.WriteLine("=== A: РџРћРљРћР™ РџРћРЎР›Р• Р—РђР“Р РЈР—РљР (РЅРёРєР°РєРёС… РёР·РјРµРЅРµРЅРёР№) ===");
        Report(designer, view);

        Console.WriteLine();
        Console.WriteLine("=== B: РџР РРќРЈР”РРўР•Р›Р¬РќР«Р™ РџР РћРҐРћР” DesignerValidator.Validate ===");
        bool invoked = ForceValidate(designer);
        Console.WriteLine("Validate() РІС‹Р·РІР°РЅ: " + invoked);
        PumpFor(TimeSpan.FromSeconds(2));
        Report(designer, view);

        window.Close();
        app.Shutdown();
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
                Console.WriteLine("  РїРѕР»Рµ DesignerValidator РЅРµ РЅР°Р№РґРµРЅРѕ");
                return false;
            }

            object validator = field.GetValue(designer);
            MethodInfo validate = validator?.GetType().GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic);
            validate?.Invoke(validator, null);
            return validate != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  СЃР±РѕР№ РІС‹Р·РѕРІР°: " + ex);
            return false;
        }
    }

    private static void Report(WorkflowDesigner designer, FrameworkElement view)
    {
        List<Border> borders = new List<Border>();
        CollectValidationBorders(view, borders);
        Console.WriteLine("  validationVisuals РЅР°Р№РґРµРЅРѕ: " + borders.Count);
        foreach (Border border in borders)
        {
            Console.WriteLine(
                "    " + DescribeOwner(border)
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
            object view2 = null;
            try { view2 = item.View; } catch (Exception ex) { Console.WriteLine("    View СЃР±РѕР№: " + ex.GetType().Name); }
            bool hasView = view2 != null;
            if (!hasView) { noView++; }
            Console.WriteLine(
                "    model: " + item.ItemType.Name
                + " view=" + (hasView ? ((FrameworkElement)view2).GetType().Name : "null")
                + " validationState=" + TryReadValidationState(item)
                + " validationMessage=" + TryReadValidationMessage(item));
        }
        Console.WriteLine("  model items: " + items.Count + ", Р±РµР· View: " + noView);

        ReportValidationService(designer);
    }

    private static string TryReadValidationState(ModelItem item)
    {
        try
        {
            object value = typeof(ModelItem).GetProperty("ValidationState")?.GetValue(item);
            if (value != null) { return value.ToString(); }

            ModelPropertyCollection properties = item.Properties;
            foreach (ModelProperty property in properties)
            {
                if (string.Equals(property.Name, "ValidationState", StringComparison.Ordinal))
                {
                    return property.Value?.GetCurrentValue()?.ToString() ?? "null";
                }
            }
            return "<РЅРµС‚>";
        }
        catch (Exception ex)
        {
            return "<СЃР±РѕР№ " + ex.GetType().Name + ">";
        }
    }

    private static string TryReadValidationMessage(ModelItem item)
    {
        try
        {
            foreach (ModelProperty property in item.Properties)
            {
                if (string.Equals(property.Name, "ValidationMessage", StringComparison.Ordinal))
                {
                    return property.Value?.GetCurrentValue()?.ToString() ?? "null";
                }
            }
            return "<РЅРµС‚>";
        }
        catch (Exception ex)
        {
            return "<СЃР±РѕР№ " + ex.GetType().Name + ">";
        }
    }

    private static void ReportValidationService(WorkflowDesigner designer)
    {
        try
        {
            object service = designer.Context.Services.GetService(
                Type.GetType("System.Activities.Presentation.Validation.ValidationService, System.Activities.Presentation"));
            if (service == null)
            {
                Console.WriteLine("  ValidationService: null");
                return;
            }

            Console.WriteLine("  IValidationErrorService: " + (designer.Context.Services.GetService(
                Type.GetType("System.Activities.Presentation.Validation.IValidationErrorService, System.Activities.Presentation"))?.GetType().FullName ?? "null (WF default)"));

            foreach (FieldInfo field in service.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                object value = null;
                try { value = field.GetValue(service); } catch (Exception) { }
                string text;
                if (value == null)
                {
                    text = "null";
                }
                else if (value is System.Collections.ICollection collection)
                {
                    text = value.GetType().Name + "[" + collection.Count + "]";
                }
                else
                {
                    text = value.GetType().Name;
                }
                Console.WriteLine("    ValidationService." + field.Name + " = " + text);
            }

            PropertyInfo errors = service.GetType().GetProperty(
                "ValidationErrors", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object errorsValue = errors?.GetValue(service);
            if (errorsValue is System.Collections.ICollection errorsCollection)
            {
                Console.WriteLine("    ValidationErrors.Count = " + errorsCollection.Count);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ValidationService СЃР±РѕР№: " + ex.GetType().Name + " " + ex.Message);
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
        for (int i = 0; i < 12 && current != null; i++)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current == null) { break; }
            if (current.GetType().Name.Contains("WorkflowViewElement"))
            {
                object modelItem = current.GetType().GetProperty("ModelItem")?.GetValue(current);
                string text = modelItem is ModelItem mi ? mi.ItemType.Name : "?";
                return text + "(" + current.GetType().Name + ")";
            }
        }
        return "<РІР»Р°РґРµР»РµС†?>";
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
        string text = tip.ToString();
        text = text.Replace(Environment.NewLine, " | ");
        return text.Length > 160 ? text.Substring(0, 160) + "..." : text;
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Background, (sender, args) =>
        {
            ((DispatcherTimer)sender).Stop();
            frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}

