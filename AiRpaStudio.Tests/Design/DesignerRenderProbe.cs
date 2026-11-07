using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Validation;
using System.Activities.Validation;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Xml;
using RPA.Core;
using RPA.Helpers;
using DesignerView = System.Activities.Presentation.View.DesignerView;
using ValidationError = System.Activities.Validation.ValidationError;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Проба отрисовки: загружает XAML в настоящий <c>WorkflowDesigner</c> на стенде и отвечает на вопросы,
/// которые и означают «XAML открывается в конструкторе» (п. 1.3 плана
/// конвейер генерации XAML):
/// <list type="number">
/// <item>файл — корректный XML и загрузился в дизайнер (<see cref="DesignerRenderReport.LoadSucceeded"/>);</item>
/// <item>поверхность дизайнера построена (<see cref="DesignerRenderReport.SurfaceReady"/>);</item>
/// <item>в дереве есть хотя бы одна активность (<see cref="DesignerRenderReport.RenderedActivities"/>);</item>
/// <item>нераспознанных активностей нет (<see cref="DesignerRenderReport.UnrecognizedActivities"/>);
/// WF не считает неизвестный тип ошибкой — подставляет «ErrorActivity», и без этой проверки сломанный
/// XAML выглядел бы открытым;</item>
/// <item>валидация WF и валидация самого дизайнера не дали ошибок;</item>
/// <item>модальных окон не появилось (<see cref="DesignerRenderReport.ModalWindows"/>).</item>
/// </list>
/// <para>
/// Проба ничего не сохраняет: путь студии (<c>LoadActivityToDesignerHelper</c>) пересохраняет файл после
/// загрузки, и эталон <c>RPA-1\Main.xaml</c> менялся бы при каждом прогоне тестов.
/// </para>
/// </summary>
internal static class DesignerRenderProbe
{
    /// <summary>Тип-заглушка WF для активности, которую не удалось разобрать.</summary>
    private const string UnrecognizedActivityType = "ErrorActivity";

    private const int SurfaceWidth = 1200;
    private const int SurfaceHeight = 900;

    private static readonly TimeSpan SurfaceTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Прогоняет файл через конструктор на стенде и возвращает отчёт.</summary>
    internal static DesignerRenderReport Render(string xamlPath)
    {
        if (string.IsNullOrWhiteSpace(xamlPath))
        {
            throw new ArgumentException("Путь к XAML не задан", nameof(xamlPath));
        }

        string fullPath = Path.GetFullPath(xamlPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Файл воркфлоу не найден: {fullPath}", fullPath);
        }

        return DesignerTestHost.Shared.Invoke(() => RenderOnHostThread(fullPath));
    }

    private static DesignerRenderReport RenderOnHostThread(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;
        var report = new DesignerRenderReport(xamlPath);

        // Битый XML проверяется до создания дизайнера: WF на таком файле не падает, а строит
        // повреждённый документ, и стенд ждал бы поверхность впустую десятки секунд.
        string parseError = CheckWellFormed(xamlPath);
        if (parseError != null)
        {
            report.LoadError = parseError;
            return report;
        }

        var validation = new RecordingValidationErrorService();
        HwndSource surface = null;

        using (DesignerModalWindowGuard guard = DesignerModalWindowGuard.Attach())
        {
            WorkflowDesignerEx designer = null;
            try
            {
                designer = new WorkflowDesignerEx();

                // Среда — та же, что у студии (WfDesignerEnvironment). Отличие одно: сервис ошибок
                // записывающий, иначе ошибки валидации WF показывает модальным окном и стенд виснет.
                WfDesignerEnvironment.Configure(designer, validation);
                designer.Load(xamlPath);

                report.LoadSucceeded = true;
            }
            catch (Exception ex)
            {
                report.LoadError = ErrorText.Of(ex);
            }

            if (report.LoadSucceeded)
            {
                try
                {
                    Grid root;
                    (surface, root) = AttachSurface(designer);

                    // Поверхность строится отложенно: шаблон DesignerView применяется после раскладки,
                    // а вьюхи активностей появляются уже внутри него.
                    report.SurfaceReady = host.PumpUntil(() => FindDesignerView(root) != null, SurfaceTimeout);

                    root.UpdateLayout();
                    guard.Poll();

                    host.Pump(SettleTimeout);

                    foreach (WorkflowViewElement element in XamlHelper.FindVisualChildren<WorkflowViewElement>(root))
                    {
                        string description = Describe(element);
                        report.RenderedActivities.Add(description);

                        if (IsUnrecognized(element))
                        {
                            report.UnrecognizedActivities.Add(description);
                        }
                    }

                    // Валидация самого дизайнера: результат приходит только через IValidationErrorService.
                    designer.Context.Services.GetService<ValidationService>()?.ValidateWorkflow();
                    host.PumpUntil(() => validation.RequestCount > 0, ValidationTimeout);
                }
                catch (Exception ex)
                {
                    report.Hints.Add($"Поверхность дизайнера не проверена: {ErrorText.Of(ex)}");
                }
            }

            CollectActivityValidation(xamlPath, report);

            guard.Poll();
            report.DesignerValidationErrors.AddRange(validation.Describe());
            report.ModalWindows.AddRange(guard.Windows);
        }

        CloseSurface(surface);
        return report;
    }

    /// <summary>
    /// Показывает <c>View</c> дизайнера в невидимом <see cref="HwndSource"/>: нужен настоящий источник
    /// представления, чтобы сработали <see cref="FrameworkElement.Loaded"/> и раскладка, но окно при
    /// этом не показывается (<c>WindowStyle = 0</c>).
    /// </summary>
    private static (HwndSource Surface, Grid Root) AttachSurface(WorkflowDesigner designer)
    {
        var root = new Grid();

        if (designer?.View is UIElement view)
        {
            root.Children.Add(view);
        }

        var parameters = new HwndSourceParameters("AiRpaStudio-DesignerTestHost")
        {
            Width = SurfaceWidth,
            Height = SurfaceHeight,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0
        };

        var surface = new HwndSource(parameters) { RootVisual = root };

        root.Measure(new Size(SurfaceWidth, SurfaceHeight));
        root.Arrange(new Rect(0, 0, SurfaceWidth, SurfaceHeight));
        root.UpdateLayout();

        return (surface, root);
    }

    private static void CloseSurface(HwndSource surface)
    {
        if (surface == null)
        {
            return;
        }

        try
        {
            surface.RootVisual = null;
            surface.Dispose();
        }
        catch (Exception ex)
        {
            // Диагностика, а не предмет теста: сбой закрытия не должен портить отчёт.
            System.Diagnostics.Debug.WriteLine("Designer test surface was not closed: " + ErrorText.Of(ex));
        }
    }

    /// <summary>
    /// Проверяет, что файл — корректный XML. Возвращает <c>null</c> для корректного файла и полный текст
    /// ошибки (<see cref="ErrorText"/>) для битого: сам конструктор на таком файле не падает, поэтому
    /// проверка нужна до создания дизайнера.
    /// </summary>
    private static string CheckWellFormed(string xamlPath)
    {
        try
        {
            using (XmlReader reader = XmlReader.Create(xamlPath))
            {
                while (reader.Read())
                {
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return ErrorText.Of(ex);
        }
    }

    private static DesignerView FindDesignerView(DependencyObject root)
    {
        return XamlHelper.FindVisualChild<DesignerView>(root);
    }

    /// <summary>
    /// Неизвестный тип в XAML не считается ошибкой загрузки: WF подставляет активность-заглушку
    /// (<c>ErrorActivity</c>) и показывает её на диаграмме. Для контракта «валидный XAML» это провал.
    /// </summary>
    private static bool IsUnrecognized(WorkflowViewElement element)
    {
        try
        {
            return string.Equals(element.ModelItem?.ItemType?.Name, UnrecognizedActivityType, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Валидация WF по загруженному XAML (<c>ActivityValidationServices</c>) — то, что этап 2 плана
    /// оформит как <c>WorkflowValidationGate</c>. Ошибки обязательны к показу, предупреждения — нет.
    /// </summary>
    private static void CollectActivityValidation(string xamlPath, DesignerRenderReport report)
    {
        try
        {
            Activity activity = ActivityXamlServices.Load(
                xamlPath,
                new ActivityXamlServicesSettings { CompileExpressions = false });

            ValidationResults results = ActivityValidationServices.Validate(activity);

            report.ValidationErrors.AddRange(results.Errors.Select(Describe));
            report.ValidationWarnings.AddRange(results.Warnings.Select(Describe));
        }
        catch (Exception ex)
        {
            report.ValidationErrors.Add($"Валидация WF не выполнена: {ErrorText.Of(ex)}");
        }
    }

    private static string Describe(ValidationError error)
    {
        if (error == null)
        {
            return "(пустая ошибка валидации)";
        }

        string source = error.Source == null ? string.Empty : $" ({error.Source.GetType().Name})";
        string id = string.IsNullOrWhiteSpace(error.Id) ? string.Empty : $" [{error.Id}]";

        return $"{error.Message}{source}{id}";
    }

    private static string Describe(WorkflowViewElement element)
    {
        string name;
        try
        {
            name = element.ModelItem?.ItemType?.Name;
        }
        catch (Exception ex)
        {
            name = "модель недоступна: " + ex.GetType().Name;
        }

        return $"{element.GetType().Name} \"{name}\" {element.ActualWidth:0}x{element.ActualHeight:0}";
    }
}

/// <summary>
/// Отчёт пробы отрисовки. <see cref="Ok"/> — «XAML открывается в конструкторе» целиком;
/// остальные поля объясняют, какой именно пункт контракта нарушен.
/// </summary>
internal sealed class DesignerRenderReport
{
    internal DesignerRenderReport(string filePath)
    {
        FilePath = filePath;
    }

    internal string FilePath { get; }

    /// <summary>Файл загрузился в дизайнер.</summary>
    internal bool LoadSucceeded { get; set; }

    /// <summary>Полный текст ошибки разбора/загрузки со стеком (<see cref="ErrorText"/>).</summary>
    internal string LoadError { get; set; }

    /// <summary>Поверхность дизайнера (<c>DesignerView</c>) построена — диаграмма видна.</summary>
    internal bool SurfaceReady { get; set; }

    /// <summary>Визуальные элементы активностей, найденные в дереве дизайнера.</summary>
    internal List<string> RenderedActivities { get; } = new List<string>();

    /// <summary>Активности-заглушки WF: тип из XAML не распознан, на диаграмме «ErrorActivity».</summary>
    internal List<string> UnrecognizedActivities { get; } = new List<string>();

    /// <summary>Ошибки валидации WF (<c>ActivityValidationServices</c>).</summary>
    internal List<string> ValidationErrors { get; } = new List<string>();

    /// <summary>Предупреждения валидации WF.</summary>
    internal List<string> ValidationWarnings { get; } = new List<string>();

    /// <summary>Ошибки, найденные самим дизайнером (<c>IValidationErrorService</c>).</summary>
    internal List<string> DesignerValidationErrors { get; } = new List<string>();

    /// <summary>Модальные окна, появившиеся во время прогона.</summary>
    internal List<string> ModalWindows { get; } = new List<string>();

    /// <summary>Диагностические замечания стенда (не влияют на <see cref="Ok"/>).</summary>
    internal List<string> Hints { get; } = new List<string>();

    /// <summary>Контракт «валидный XAML сразу открывается в конструкторе» выполнен.</summary>
    internal bool Ok => LoadSucceeded
                        && SurfaceReady
                        && RenderedActivities.Count > 0
                        && UnrecognizedActivities.Count == 0
                        && ModalWindows.Count == 0
                        && ValidationErrors.Count == 0
                        && DesignerValidationErrors.Count == 0;

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    internal string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine($"файл: {FilePath}");
        text.AppendLine($"загрузка: {(LoadSucceeded ? "ок" : "ОШИБКА")}");
        text.AppendLine($"поверхность дизайнера: {(SurfaceReady ? "построена" : "НЕ построена")}");
        text.AppendLine($"активностей в дереве: {RenderedActivities.Count}");
        text.AppendLine($"нераспознанных активностей: {UnrecognizedActivities.Count}");
        text.AppendLine($"модальных окон: {ModalWindows.Count}");
        text.AppendLine($"ошибок валидации WF: {ValidationErrors.Count}");
        text.AppendLine($"ошибок валидации дизайнера: {DesignerValidationErrors.Count}");
        text.AppendLine($"предупреждений валидации WF: {ValidationWarnings.Count}");

        AppendBlock(text, "ошибка загрузки", string.IsNullOrWhiteSpace(LoadError)
            ? new List<string>()
            : new List<string> { LoadError });
        AppendBlock(text, "активности", RenderedActivities);
        AppendBlock(text, "нераспознанные активности", UnrecognizedActivities);
        AppendBlock(text, "модальные окна", ModalWindows);
        AppendBlock(text, "ошибки валидации WF", ValidationErrors);
        AppendBlock(text, "ошибки валидации дизайнера", DesignerValidationErrors);
        AppendBlock(text, "предупреждения валидации WF", ValidationWarnings);
        AppendBlock(text, "замечания стенда", Hints);

        return text.ToString();
    }

    private static void AppendBlock(StringBuilder text, string title, IList<string> items)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        text.AppendLine($"{title}:");
        foreach (string item in items)
        {
            text.AppendLine("  - " + item?.Replace("\r\n", "\r\n    "));
        }
    }
}
