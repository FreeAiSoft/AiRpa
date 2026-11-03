// Приёмочный пробник №5 (вариант 1): вызывает НАСТОЯЩИЙ продакшн-код
// (RPA.Helpers.WfColorResourcesOverride / WfDesignerTheme из AiRpaStudio.exe, через reflection)
// на живом WorkflowDesigner. Два режима:
//   single (режим кэша): установка словаря палитры до DesignerMetadata().Register() →
//       первый кадр активности в палитре, кисти — клоны связанных кистей (есть выражение привязки),
//       проход WfDesignerTheme.Apply активности не трогает, поздние шаблоны (TryCatch/Switch) тоже в палитре,
//       смена темы = ApplyPalette без обхода дерева;
//   legacy (прежнее поведение, пост-покраска) — база для сравнения, чтобы увидеть, что именно
//       перестало краситься.
//
// Запуск: Run-Probe.ps1 -Probe probe5

using System;
using System.Collections;
using System.Collections.Generic;
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

internal static class CacheOverrideProbe5
{
    private const string RelativeUriText =
        "/System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

    private static readonly List<string> Report = new List<string>();

    // Дефолтные цвета WF: если такие цвета остались на элементах активности в тёмной теме — это и есть
    // «мерцание»/недокрас.
    private static readonly Dictionary<string, Color> WorkflowDefaults = new Dictionary<string, Color>(StringComparer.Ordinal)
    {
        ["плита WF"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["заголовок WF"] = Color.FromRgb(0xE9, 0xEC, 0xEE),
        ["рамка WF"] = Color.FromRgb(0xD0, 0xD7, 0xE2),
        ["подпись WF"] = Color.FromRgb(0x1B, 0x29, 0x3E)
    };

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

        _legacy = args.Length > 1 && string.Equals(args[1], "legacy", StringComparison.OrdinalIgnoreCase);

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
        Log("################ ПРОБНИК 5: продакшн-код на живом дизайнере, режим "
            + (_legacy ? "LEGACY (пост-покраска)" : "CACHE (словарь палитры)") + " ################");

        // Application создаётся до установки: pack-URI и диспетчер нужны так же, как в студии.
        _host = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        Assembly studio = Assembly.LoadFrom(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AiRpaStudio.exe"));
        _studio = studio;
        Type paletteType = studio.GetType("RPA.Helpers.StudioPalette", true);
        Type colorsType = studio.GetType("RPA.Helpers.StudioThemeColors", true);
        Type overrideType = studio.GetType("RPA.Helpers.WfColorResourcesOverride", true);

        object dark = paletteType.GetProperty("Dark", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        object light = paletteType.GetProperty("Light", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        Log("Surface тёмной палитры: " + colorsType.GetProperty("Surface").GetValue(dark));

        // Как в App.StartupCore: кисти/шрифты/ключи дизайнера объявляются ДО подмены словаря WF.
        InvokeStatic(studio, "RPA.Helpers.StudioBrushes", "ApplyToApplication");
        InvokeStatic(studio, "RPA.Helpers.StudioFonts", "ApplyToApplication");
        InvokeStatic(studio, "RPA.Helpers.WfDesignerTheme", "ApplyToApplication");

        MethodInfo tryInstall = overrideType.GetMethod(
            "TryInstall",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { colorsType },
            null);

        bool installed = !_legacy && (bool)tryInstall.Invoke(null, new[] { dark });
        Log("TryInstall(dark) = " + installed);

        var cacheField = (FieldInfo)overrideType
            .GetMethod("FindCacheField", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, null);

        var cache = (IDictionary)cacheField.GetValue(null);
        Log("ключей в кэше WF: " + cache.Count);
        foreach (DictionaryEntry entry in cache)
        {
            Log("  кэш: " + entry.Key);
        }

        var defaults = (ResourceDictionary)overrideType
            .GetMethod("LoadWorkflowDefaults", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, null);

        Log("ключи ColorResources.xaml (" + defaults.Count + "): " + string.Join(", ", defaults.Keys.Cast<object>()
            .Select(key => key.ToString()).OrderBy(key => key, StringComparer.Ordinal).ToArray()));

        new DesignerMetadata().Register();

        WorkflowDesigner designer = CreateDesigner(BuildSequence());
        FrameworkElement view = Show(designer);

        Log(string.Empty);
        Log("=== первый кадр, проход темы дизайнера ЕЩЁ НЕ вызывался ===");
        Dump(view);
        Detail(view, "первый кадр");

        // Тот же путь, что в студии: WfDesignerTheme.Apply(root).
        studio.GetType("RPA.Helpers.WfDesignerTheme", true)
            .GetMethod("Apply", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(FrameworkElement) }, null)
            .Invoke(null, new object[] { view });

        Pump(4);
        Log(string.Empty);
        Log("=== после WfDesignerTheme.Apply ===");
        Detail(view, "после Apply");

        Log(string.Empty);
        Log("=== эмуляция перетаскивания: у активности снят Foreground (как у только что созданной) ===");
        CheckTextHook(view);

        CheckCaptionStyle(view);

        Dump(view);

        Log(string.Empty);
        Log("=== поздние шаблоны: новый дизайнер с TryCatch/Switch, проход темы не вызывался ===");
        var late = new Sequence { DisplayName = "Late" };
        late.Activities.Add(new TryCatch { DisplayName = "TryCatch" });
        late.Activities.Add(new Switch<string> { DisplayName = "Switch" });
        Dump(Show(CreateDesigner(late)));

        Log(string.Empty);
        Log("=== смена темы: только ApplyPalette(light), дерево не обходится ===");
        overrideType
            .GetMethod("ApplyPalette", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new[] { light });

        Pump(3);
        Dump(Show(designer));

        _host.Shutdown();
    }

    private static object BuildSequence()
    {
        var sequence = new Sequence { DisplayName = "Sequence" };
        var branch = new If { DisplayName = "If" };
        branch.Then = new WriteLine { DisplayName = "Then" };
        sequence.Activities.Add(branch);
        sequence.Activities.Add(new WriteLine { DisplayName = "WriteLine" });
        sequence.Activities.Add(new Parallel { DisplayName = "Parallel" });
        return sequence;
    }

    private static void InvokeStatic(Assembly studio, string typeName, string methodName)
    {
        Type type = studio.GetType(typeName, true);
        MethodInfo method = type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            null,
            Type.EmptyTypes,
            null);

        Log("  " + typeName + "." + methodName + ": " + (method == null ? "НЕТ МЕТОДА" : "ок"));
        method?.Invoke(null, null);
    }

    private static WorkflowDesigner CreateDesigner(object root)
    {
        var designer = new WorkflowDesigner();
        designer.Load(root);
        return designer;
    }

    private static FrameworkElement Show(WorkflowDesigner designer)
    {
        var view = (FrameworkElement)designer.View;
        var window = new Window
        {
            Width = 1100,
            Height = 700,
            Left = -4000,
            Top = 80,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = view
        };

        window.Show();
        Pump(10);
        _windows.Add(window);
        return view;
    }

    private static void Dump(FrameworkElement view)
    {
        foreach (WorkflowViewElement activity in Walk(view).OfType<WorkflowViewElement>())
        {
            Log("  " + activity.GetType().Name
                + " bg=" + BrushText(activity.Background) + Bound(activity.Background)
                + " border=" + BrushText(activity.BorderBrush) + Bound(activity.BorderBrush)
                + " fg=" + BrushText(activity.Foreground));
        }

        var leftovers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DependencyObject element in Walk(view))
        {
            foreach (Brush brush in new[] { BackgroundOf(element), BorderBrushOf(element) })
            {
                if (!(brush is SolidColorBrush solid))
                {
                    continue;
                }

                foreach (KeyValuePair<string, Color> pair in WorkflowDefaults)
                {
                    if (pair.Value != solid.Color)
                    {
                        continue;
                    }

                    string key = pair.Key + " " + element.GetType().Name;
                    leftovers[key] = leftovers.TryGetValue(key, out int count) ? count + 1 : 1;
                }
            }
        }

        if (leftovers.Count == 0)
        {
            Log("  остатков дефолтных цветов WF нет");
            return;
        }

        Log("  дефолтные цвета WF в дереве:");
        foreach (KeyValuePair<string, int> pair in leftovers.OrderByDescending(p => p.Value).Take(12))
        {
            Log("    " + pair.Key + ": " + pair.Value);
        }
    }

    private static void Detail(FrameworkElement view, string label)
    {
        WorkflowViewElement activity = Walk(view).OfType<WorkflowViewElement>().FirstOrDefault();
        if (activity == null)
        {
            return;
        }

        Log(string.Empty);
        Log("--- подробно: " + label + " (" + activity.GetType().Name + ") ---");

        foreach (string key in new[]
        {
            "WorkflowViewElementBackground",
            "WorkflowViewElementBorderBrush",
            "DesignerViewBackground",
            "DesignerViewForeground"
        })
        {
            object found = activity.TryFindResource(key);
            Log("  FindResource('" + key + "') = " + (found == null ? "(нет)" : BrushText(found as Brush))
                + Bound(found as Brush));
        }

        TextBlock caption = Walk(activity).OfType<TextBlock>().FirstOrDefault(item => item.Text.Length > 0);
        if (caption != null)
        {
            Log("  подпись: '" + caption.Text + "' fg=" + BrushText(caption.Foreground)
                + " стиль=" + (caption.Style == null ? "(нет)" : caption.Style.TargetType.Name)
                + " источник=" + DependencyPropertyHelper.GetValueSource(caption, TextBlock.ForegroundProperty).BaseValueSource);
        }
        {
        }


        int shown = 0;
        foreach (DependencyObject element in Walk(activity))
        {
            Brush background = BackgroundOf(element);
            if (background == null || shown >= 24)
            {
                continue;
            }

            shown++;
            DependencyProperty property = element is Control ? Control.BackgroundProperty : Panel.BackgroundProperty;
            ValueSource source = DependencyPropertyHelper.GetValueSource(element, property);
            string foreground = element is TextBlock text
                ? " fg=" + BrushText(text.Foreground)
                : element is Control control ? " fg=" + BrushText(control.Foreground) : string.Empty;
            string size = element is FrameworkElement fe
                ? " " + fe.ActualWidth.ToString("0") + "x" + fe.ActualHeight.ToString("0")
                  + (fe.IsVisible ? " видим" : " скрыт")
                : string.Empty;

            Log("  " + element.GetType().Name
                + " bg=" + BrushText(background) + Bound(background)
                + foreground
                + size
                + " источник=" + source.BaseValueSource);
        }
    }

    private static void CheckTextHook(FrameworkElement view)
    {
        WorkflowViewElement activity = Walk(view).OfType<WorkflowViewElement>().FirstOrDefault();
        if (activity == null)
        {
            return;
        }

        _studio.GetType("RPA.Helpers.WfActivityTheme", true)
            .GetMethod("ForgetThemeMarks", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, null);

        activity.ClearValue(Control.ForegroundProperty);
        Pump(2);

        Log("  активность " + activity.GetType().Name + " fg=" + BrushText(activity.Foreground) + Bound(activity.Foreground)
            + " источник=" + DependencyPropertyHelper.GetValueSource(activity, Control.ForegroundProperty).BaseValueSource);

        TextBlock caption = Walk(activity).OfType<TextBlock>()
            .FirstOrDefault(item => item.Text != null && item.Text.Length > 0);

        if (caption != null)
        {
            Log("  подпись '" + caption.Text + "' fg=" + BrushText(caption.Foreground) + Bound(caption.Foreground)
                + " источник=" + DependencyPropertyHelper.GetValueSource(caption, TextBlock.ForegroundProperty).BaseValueSource);
        }
    }

    private static void DropActivity(WorkflowDesigner designer, FrameworkElement view)
    {
        EditingContext context = designer.Context;
        var modelService = context.Services.GetService(
            typeof(System.Activities.Presentation.Services.ModelService)) as System.Activities.Presentation.Services.ModelService;

        if (modelService == null)
        {
            Log("  ModelService недоступен");
            return;
        }

        System.Activities.Presentation.Model.ModelItemCollection activities =
            modelService.Root.Properties["Activities"].Collection;

        activities.Add(System.Activities.Presentation.Model.ModelFactory.CreateItem(
            context,
            new WriteLine { DisplayName = "Dropped" }));

        Pump(6);

        foreach (WorkflowViewElement activity in Walk(view).OfType<WorkflowViewElement>())
        {
            Log("  " + activity.GetType().Name
                + " bg=" + BrushText(activity.Background) + Bound(activity.Background)
                + " border=" + BrushText(activity.BorderBrush) + Bound(activity.BorderBrush)
                + " fg=" + BrushText(activity.Foreground) + Bound(activity.Foreground));
        }

        TextBlock caption = Walk(view).OfType<TextBlock>()
            .FirstOrDefault(item => item.Text != null && item.Text.IndexOf("Dropped", StringComparison.Ordinal) >= 0);

        Log(caption == null
            ? "  подпись новой активности не найдена"
            : "  подпись новой активности: fg=" + BrushText(caption.Foreground) + Bound(caption.Foreground)
              + " источник=" + DependencyPropertyHelper.GetValueSource(caption, TextBlock.ForegroundProperty).BaseValueSource);
    }

    private static void CheckCaptionStyle(FrameworkElement view)
    {
        // Эмуляция «перетаскивания»: у подписи нет локального цвета (так её создаёт шаблон), и он должен
        // прийти из неявного стиля TextBlock в ресурсах документа — без пост-покраски и без прохода темы.
        TextBlock caption = Walk(view).OfType<TextBlock>().FirstOrDefault(item => item.Text.Length > 0);
        if (caption == null)
        {
            Log("  подпись не найдена");
            return;
        }

        caption.ClearValue(TextBlock.ForegroundProperty);
        Pump(2);

        Log(string.Empty);
        Log("=== подписьactivityбез локального цвета (как у только что созданной) ===");
        Log("  '" + caption.Text + "' fg=" + BrushText(caption.Foreground) + Bound(caption.Foreground)
            + " стиль=" + (caption.Style == null ? "(нет)" : caption.Style.TargetType.Name)
            + " источник=" + DependencyPropertyHelper.GetValueSource(caption, TextBlock.ForegroundProperty).BaseValueSource);
    }

    private static string Bound(Brush brush)
    {
        return brush is SolidColorBrush solid
               && BindingOperations.GetBindingExpression(solid, SolidColorBrush.ColorProperty) != null
            ? " [связанная]"
            : string.Empty;
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

        return element is Border border ? border.Background : null;
    }

    private static Brush BorderBrushOf(DependencyObject element)
    {
        if (element is Control control)
        {
            return control.BorderBrush;
        }

        return element is Border border ? border.BorderBrush : null;
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

            for (int index = 0; index < count; index++)
            {
                DependencyObject child = null;
                try
                {
                    child = VisualTreeHelper.GetChild(current, index);
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
        for (int index = 0; index < frames; index++)
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
    private static bool _legacy;
    private static Assembly _studio;
    private static readonly List<Window> _windows = new List<Window>();
}
