using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Linq;
using RPA.Core.Generation;
using RPA.Helpers;
using RPA.Chat.Services.Workflow;
using RPA.Core.Workflow;
using System.Activities.Presentation.Model;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Hosting;
using AiRpaStudio.Tests.Generation;


using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Controls;
using RPA.Core;
using RPA.CSharpExpressionEditor;

using RPA.Core.Services;
using System.Activities.Statements;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Мост к стенду конструктора, работающий в отдельном домене приложения.
/// <para>
/// Зачем домен: отрисовка настоящего <c>WorkflowDesigner</c> заполняет статические кэши WF
/// (словари шаблонов и цветов) объектами <see cref="System.Windows.Freezable"/>, созданными на потоке
/// стенда. Кэши общие на процесс, поэтому после отрисовки 11 тестов темы панели «Активности» падали с
/// <see cref="InvalidOperationException"/> «вызывающий поток не может получить доступ…»: они читали
/// кисти, принадлежащие другому потоку. Домен изолирует эти кэши — вместе с ним они и удаляются.
/// </para>
/// <para>
/// Мост возвращает только сериализуемые отчёты: тесты утверждают по данным, а не по живым объектам WPF.
/// </para>
/// </summary>
public sealed class DesignerBenchProxy : MarshalByRefObject
{
    private const string DomainName = "AiRpaStudio-DesignerBench";

    private static readonly object DomainGate = new object();

    private static DesignerBenchProxy _proxy;

    /// <summary>Мост процесса: домен создаётся при первом обращении и живёт до конца прогона.</summary>
    internal static DesignerBenchProxy Shared
    {
        get
        {
            lock (DomainGate)
            {
                if (_proxy != null)
                {
                    return _proxy;
                }

                var setup = new AppDomainSetup
                {
                    ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                    ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
                };

                AppDomain domain = AppDomain.CreateDomain(DomainName, null, setup);
                _proxy = (DesignerBenchProxy)domain.CreateInstanceAndUnwrap(
                    typeof(DesignerBenchProxy).Assembly.FullName,
                    typeof(DesignerBenchProxy).FullName);

                return _proxy;
            }
        }
    }

    /// <summary>Проверки стенда: STA-поток, маршалинг вызовов, жизнь потока.</summary>
    public DesignerBenchHostInfo HostInfo()
    {
        DesignerTestHost host = DesignerTestHost.Shared;
        int callerThreadId = Thread.CurrentThread.ManagedThreadId;

        (ApartmentState state, int threadId) = host.Invoke(() =>
            (Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.ManagedThreadId));

        return new DesignerBenchHostInfo
        {
            ApartmentState = state.ToString(),
            HostThreadId = threadId,
            KnownHostThreadId = host.ThreadId,
            CallerThreadId = callerThreadId,
            IsAlive = host.IsAlive
        };
    }

    /// <summary>Прогоняет файл через конструктор на стенде.</summary>
    /// <summary>
    /// Шаги 0 и 5 плана <c>dev-dev-docs\UI02_Background_Validation_Plan.md</c> на живом конструкторе:
    /// открыть нагрузочный документ (100+ активностей, ошибки внутри <c>ActivityAction</c>,
    /// <c>PickBranch</c> и <c>Then</c>), дождаться прохода валидации и снять цифры.
    /// <para>
    /// Что измеряется и проверяется:
    /// </para>
    /// <list type="bullet">
    /// <item>проход выполняется <b>сам, без правки</b> документа (баг «в покое ошибок нет»);</item>
    /// <item>время каждой фазы: обход модели, снимок, <c>ValidateWorkflow()</c>, фоновая компиляция,
    /// применение иконок — из этого складывается «было/стало» для UI-потока;</item>
    /// <item>наибольшая пауза диспетчера за время прохода (отзывчивость конструктора);</item>
    /// <item>на каких адресах иконка действительно видна — включая вложенные активности;</item>
    /// <item>правка модели запускает новый проход, а закрытие вкладки прекращает тики.</item>
    /// </list>
    /// </summary>
    public DesignerBenchValidationReport ValidationPassScenario(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var report = new DesignerBenchValidationReport();
            var designerHost = new BenchDesignerHost();
            DesignerTab tab = null;

            try
            {
                tab = designerHost.Open(xamlPath);
                designerHost.Activate(tab);

                WorkflowDesignerEx designer = tab.Designer;
                DesignerValidator validator = designer.Validation;
                if (validator == null)
                {
                    report.Errors.Add("у дизайнера нет валидатора: проход невозможен");
                    return report;
                }

                report.InspectionActivityCount = designerHost.CountActiveActivities();

                // Шаг 0: паузы диспетчера за время прохода (тик пробника — 10 мс).
                using (var gap = new DispatcherGapProbe(host.Dispatcher, TimeSpan.FromMilliseconds(10)))
                {
                    gap.Start();
                    report.FirstPassSeen = host.PumpUntil(
                        () => validator.LastMetrics != null,
                        TimeSpan.FromSeconds(60));
                    gap.Stop();

                    report.MaxDispatcherGapMs = gap.MaxGapMs;
                    report.DispatcherTicks = gap.TickCount;
                }

                DesignerValidationMetrics metrics = validator.LastMetrics;
                if (metrics == null)
                {
                    report.Description = "проход валидации не выполнился за 60 с; "
                                         + designerHost.ActivityCountInfo + "; " + validator.PassState;
                    return report;
                }

                report.Generation = metrics.Generation;
                report.ScopeActivityCount = metrics.ActivityCount;
                report.CheckCount = metrics.CheckCount;
                report.ErrorCount = metrics.ErrorCount;
                foreach (string errorAddress in metrics.ErrorAddresses)
                {
                    report.ErrorAddresses.Add(errorAddress);
                }
                report.ModelWalkMs = metrics.ModelWalkMs;
                report.SnapshotMs = metrics.SnapshotMs;
                report.ValidateWorkflowMs = metrics.ValidateWorkflowMs;
                report.ExpressionsMs = metrics.ExpressionsMs;
                report.ApplyMs = metrics.ApplyMs;
                report.UiMs = metrics.UiMs;
                report.OldUiMs = metrics.OldUiMs;
                report.PassState = validator.PassState;

                // Иконки: обход модели независимо от валидатора, состояние Border'а читается из вьюхи.
                ModelService modelService = designer.Context.Services.GetService<ModelService>();
                ModelItem implementation = modelService?.Root?.Properties["Implementation"]?.Value;
                IReadOnlyList<DesignerScopeEntry> scope = implementation == null
                    ? null
                    : DesignerValidationScope.Collect(new ModelItemScopeNode(implementation));

                if (scope != null)
                {
                    foreach (DesignerScopeEntry entry in scope)
                    {
                        ModelItem modelItem = (entry.Node as ModelItemScopeNode)?.ModelItem;
                        if (modelItem == null)
                        {
                            continue;
                        }

                        FrameworkElement view = null;
                        try
                        {
                            view = modelItem.View as FrameworkElement;
                        }
                        catch (Exception ex)
                        {
                            report.Errors.Add("View недоступен для " + entry.Address + ": " + ErrorText.Of(ex));
                        }

                        if (view == null)
                        {
                            report.NoViewAddresses.Add(entry.Address);
                            continue;
                        }

                        var icon = XamlHelper.FindVisualChildByTypeAndName(
                            view, "WorkflowDesignerIconBorder", "validationVisuals") as Border;

                        if (icon == null)
                        {
                            report.NoIconAddresses.Add(entry.Address);
                            continue;
                        }

                        string toolTip = icon.ToolTip as string;
                        if (!string.IsNullOrEmpty(toolTip))
                        {
                            report.AddressToolTips[entry.Address] = toolTip;
                        }

                        if (icon.Visibility == Visibility.Visible)
                        {
                            report.IconVisibleAddresses.Add(entry.Address);
                        }
                    }
                }

                // Правка модели: «правка во время/после прохода → новый проход нового поколения».
                int generationBeforeEdit = validator.PassGeneration;
                report.ModelEdited = TryAppendActivity(designer.Context, modelService?.Root);
                if (report.ModelEdited)
                {
                    report.EditStartedNewPass = host.PumpUntil(
                        () => validator.PassGeneration > generationBeforeEdit
                              && validator.LastMetrics != null
                              && validator.LastMetrics.Generation == validator.PassGeneration,
                        TimeSpan.FromSeconds(30));
                }

                report.GenerationAfterEdit = validator.PassGeneration;

                // Закрытие вкладки: после DetachValidation тиков быть не должно.
                designer.DetachValidation();
                int generationAfterDetach = validator.PassGeneration;
                host.Pump(TimeSpan.FromSeconds(3));

                report.GenerationAfterDetach = validator.PassGeneration;
                report.PassesAfterDetach = validator.PassGeneration - generationAfterDetach;

                report.Ok = true;
                report.Description = metrics.Describe()
                                     + Environment.NewLine
                                     + "документ: " + designerHost.ActivityCountInfo
                                     + Environment.NewLine
                                     + "пауза диспетчера: " + report.MaxDispatcherGapMs.ToString("0.0")
                                     + " мс за " + report.DispatcherTicks + " тиков"
                                     + Environment.NewLine
                                     + "иконка видна на " + report.IconVisibleAddresses.Count
                                     + " адрес(ах), без вьюхи " + report.NoViewAddresses.Count
                                     + ", без validationVisuals " + report.NoIconAddresses.Count
                                     + Environment.NewLine
                                     + "правка модели: " + (report.ModelEdited ? "выполнена" : "не выполнена")
                                     + ", новый проход: " + (report.EditStartedNewPass ? "запущен" : "не запущен")
                                     + Environment.NewLine
                                     + "после закрытия вкладки новых проходов: " + report.PassesAfterDetach;

                // Журнал шага 0: цифры прохода сохраняются в файл, чтобы их можно было внести
                // в план без повторного прогона стенда.
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AiRpaStudio-validation-baseline.txt"),
                        report.Description);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Журнал замеров не сохранён: " + ErrorText.Of(ex));
                }

                return report;
            }
            catch (Exception ex)
            {
                report.Errors.Add(ErrorText.Of(ex));
                report.Description = "сценарий валидации на стенде не выполнен";
                return report;
            }
            finally
            {
                // Уборка: закрытие вкладок останавливает таймер валидации (иначе его тик при выгрузке
                // домена пытается показать модальное окно WF — AccessViolation при инициализации шрифтов).
                foreach (DesignerTab openTab in designerHost.GetOpenTabs())
                {
                    designerHost.Close(openTab);
                }
            }
        });
    }

    /// <summary>
    /// Добавляет активность в корневую реализацию — настоящая правка модели, а не «дёрнуть событие»:
    /// только она проверяет, что новый проход запускается по изменению документа.
    /// </summary>
    private static bool TryAppendActivity(System.Activities.Presentation.EditingContext editingContext, ModelItem root)
    {
        try
        {
            ModelItem implementation = root?.Properties["Implementation"]?.Value;
            ModelItemCollection activities = implementation?.Properties["Activities"]?.Collection;
            if (editingContext == null || implementation == null || activities == null)
            {
                return false;
            }

            using (ModelEditingScope edit = implementation.BeginEdit())
            {
                activities.Add(ModelFactory.CreateItem(editingContext, new WriteLine { DisplayName = "Правка стенда" }));
                edit.Complete();
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Правка модели стенда не выполнена: " + ErrorText.Of(ex));
            return false;
        }
    }

    /// <summary>
    /// Баг «ошибка валидации поля выражения видна только после клика по полю»: замер состояния полей
    /// <c>RoslynExpressionEditor</c> в четырёх точках и <b>без кликов</b> — загрузка → фоновый проход
    /// → покой → контрольный фокус.
    /// <para>
    /// Почему стенд, а не юнит-тест: «в поле виден красный значок» — факт о живом визуальном дереве
    /// конструктора (поле создаёт WF по шаблону активности), синтетическая модель его не воспроизводит.
    /// Замер сразу после <c>Open</c> отвечает на вопрос карточки: есть ли проверка при загрузке, или
    /// единственные триггеры — изменение текста и фокус.
    /// </para>
    /// <para>
    /// <c>brokenText</c> — текст невалидного выражения из фикстуры: по нему поле опознаётся в дереве
    /// (пустые поля документа о состоянии проверки ничего не говорят).
    /// </para>
    /// </summary>
    public DesignerBenchFieldBadgeReport ExpressionFieldBadgeScenario(string xamlPath, string brokenText)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var report = new DesignerBenchFieldBadgeReport { BrokenText = brokenText ?? string.Empty };
            var designerHost = new BenchDesignerHost();
            var clock = Stopwatch.StartNew();
            DesignerTab tab = null;
            UIElement inspector = null;
            System.Windows.Interop.HwndSource inspectorSurface = null;
            ModelItem brokenActivity = null;

            try
            {
                tab = designerHost.Open(xamlPath);
                designerHost.Activate(tab);

                WorkflowDesignerEx designer = tab.Designer;
                UIElement surface = designer.View as UIElement;
                ModelService modelService = designer.Context.Services.GetService<ModelService>();
                ModelItem implementation = modelService?.Root?.Properties["Implementation"]?.Value;
                IReadOnlyList<DesignerScopeEntry> scope = implementation == null
                    ? null
                    : DesignerValidationScope.Collect(new ModelItemScopeNode(implementation));
                report.ScopeActivityCount = scope == null ? 0 : scope.Count;

                // Активность с невалидным выражением: единственное поле, состояние которого важно.
                // Берётся <b>самая вложенная</b> подходящая активность: вьюха корня содержит поля детей,
                // и окно «Свойства» для корня показало бы свойства процесса, а не аргументы Assign.
                foreach (DesignerScopeEntry entry in scope ?? new List<DesignerScopeEntry>())
                {
                    ModelItem modelItem = (entry.Node as ModelItemScopeNode)?.ModelItem;
                    if (modelItem == null || !(modelItem.GetCurrentValue() is System.Activities.Activity))
                    {
                        continue;
                    }

                    if (FindField(modelItem, report.BrokenText) != null)
                    {
                        brokenActivity = modelItem;
                        report.BrokenAddress = entry.Address;
                }
                }

                if (brokenActivity == null)
                {
                    report.BrokenAddress = "(активность с полем «" + report.BrokenText + "» не найдена)";
                }

                // Точка 1 — загрузка: сразу после открытия и до первого прохода валидатора.
                report.Stages.Add(CaptureStage("загрузка", clock, surface, null));


                // Окно «Свойства» на той же активности: поле аргумента обязано вести себя так же.
                if (brokenActivity != null)
                {
                    try
                    {
                        System.Activities.Presentation.View.Selection.SelectOnly(designer.Context, brokenActivity);

                        inspector = designer.PropertyInspectorView;
                        if (inspector == null)
                        {
                            report.InspectorState = "PropertyInspectorView == null";
                        }
                        else
                        {
                            inspectorSurface = HostInWindow(inspector, "AiRpaStudio-BenchInspector");
                            host.Pump(TimeSpan.FromMilliseconds(700));
                            System.Activities.Presentation.View.Selection.SelectOnly(designer.Context, brokenActivity);
                            host.Pump(TimeSpan.FromMilliseconds(700));
                            report.InspectorState = "ок";
                            report.InspectorDump.AddRange(DescribeInspector(inspector));

                        }
                    }
                    catch (Exception ex)
                    {
                        report.InspectorState = "ошибка: " + ErrorText.Of(ex);
                    }
                }

                // Точка 2 — фоновый проход студии отработал (иконки на активностях уже нарисованы).
                bool passSeen = host.PumpUntil(
                    () => designer.Validation == null || designer.Validation.LastMetrics != null,
                    TimeSpan.FromSeconds(60));
                if (!passSeen)
                {
                    report.Errors.Add("фоновый проход валидации не выполнился за 60 с");
                }

                report.Stages.Add(CaptureStage("после прохода", clock, surface, inspector));

                // Точка 3 — покой: ожидаемое «появляется само через непродолжительное время».
                host.Pump(TimeSpan.FromSeconds(3));
                report.Stages.Add(CaptureStage("покой", clock, surface, inspector));

                // Чья иконка на активности: своя (студии) с текстом ошибки выражения или WF.
                if (brokenActivity != null)
                {
                    object activityView = brokenActivity.View;
                    var icon = activityView == null
                        ? null
                        : XamlHelper.FindVisualChildByTypeAndName(
                            (DependencyObject)activityView, "WorkflowDesignerIconBorder", "validationVisuals") as Border;
                    if (icon?.ToolTip is string toolTip && !string.IsNullOrEmpty(toolTip))
                    {
                        report.BrokenAddressMessages.Add(toolTip);
                    }
                }

                // Точка 4 — контроль: программный фокус на поле (сегодняшнее поведение «только после клика»).
                RoslynExpressionEditor field = brokenActivity == null
                    ? null
                    : FindField(brokenActivity, report.BrokenText);
                if (field != null)
                {
                    FocusField(field);
                    host.PumpUntil(() => field.HasValidationError, TimeSpan.FromSeconds(15));
                }

                report.Stages.Add(CaptureStage("после клика", clock, surface, inspector));

                report.Ok = report.Errors.Count == 0;
                report.Description =
                    "активность с ошибкой: " + report.BrokenAddress
                    + "; текст поля: «" + report.BrokenText + "»"
                    + "; активностей в области: " + report.ScopeActivityCount.ToString()
                    + "; окно «Свойства»: " + report.InspectorState
                    + "; подсказка иконки: "
                    + (report.BrokenAddressMessages.Count == 0
                        ? "(нет)"
                        : string.Join(" | ", report.BrokenAddressMessages))
                    + Environment.NewLine
                    + Describe(report);

                return report;
            }
            catch (Exception ex)
            {
                report.Ok = false;
                report.Errors.Add(ErrorText.Of(ex));
                report.Description = "сценарий замера бейджа не выполнен: " + ErrorText.Of(ex);
                return report;
            }
            finally
            {
                // Фоновый цикл проверки поля (стартует по фокусу) не должен переживать сценарий:
                // он тикает на диспетчере стенда и мешает следующим тестам.
                StopFieldValidators(inspector);
                StopFieldValidators(brokenActivity);

                try
                {
                    if (inspectorSurface != null)
                    {
                        inspectorSurface.RootVisual = null;
                        inspectorSurface.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Поверхность инспектора не освобождена: " + ErrorText.Of(ex));
                }

                designerHost.Close(tab);
            }
        });
    }

    /// <summary>Снимок одной точки замера: состояние поля в конструкторе и в окне «Свойства».</summary>
    private static FieldBadgeStage CaptureStage(
        string name,
        Stopwatch clock,
        UIElement surface,
        UIElement inspector)
    {
        var stage = new FieldBadgeStage
        {
            Name = name,
            ElapsedMs = clock.Elapsed.TotalMilliseconds
        };

        if (surface != null)
        {
            stage.Fields.AddRange(CaptureFieldsFrom(surface, "конструктор"));
        }

        if (inspector != null)
        {
            stage.InspectorFields.AddRange(CaptureFieldsFrom(inspector, "свойства"));
        }

        return stage;
    }

    private static List<FieldBadgeEntry> CaptureFieldsFrom(DependencyObject root, string owner)
    {
        var entries = new List<FieldBadgeEntry>();
        if (root == null)
        {
            return entries;
        }

        try
        {
            foreach (RoslynExpressionEditor editor in XamlHelper.FindVisualChildren<RoslynExpressionEditor>(root))
            {
                entries.Add(CaptureField(editor, owner));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Обход полей выражений не выполнен: " + ErrorText.Of(ex));
        }

        return entries;
    }

    private static FieldBadgeEntry CaptureField(RoslynExpressionEditor editor, string owner)
    {
        var entry = new FieldBadgeEntry { Owner = owner };

        try
        {
            entry.FieldName = string.IsNullOrEmpty(editor.Name) ? "(без имени)" : editor.Name;
            entry.ArgumentName = MatchArgument(editor);

            entry.Text = editor.Text ?? string.Empty;
            entry.Template = TemplateName(editor);
            entry.HasBadge = editor.HasValidationError;
            entry.Message = editor.ValidationErrorMessage ?? string.Empty;
            entry.BadgeVisibility = BadgeVisibility(editor);
            entry.WpfHasError = System.Windows.Controls.Validation.GetHasError(editor);
            entry.EditingState = editor.EditingState.ToString();
            entry.ExpressionItemType = editor.Expression == null
                ? "(null)"
                : editor.Expression.ItemType?.Name ?? "(null)";
        }
        catch (Exception ex)
        {
            entry.Message = "ошибка чтения поля: " + ErrorText.Of(ex);
        }

        return entry;
    }

    /// <summary>
    /// К какому аргументу активности относится поле: сопоставление по модели, а не по тексту
    /// (<c>RoslynExpressionEditor.Name</c> у полей пустой). Проверяются оба уровня — сам аргумент и
    /// вложенный элемент <c>Expression</c>.
    /// </summary>
    private static string MatchArgument(RoslynExpressionEditor editor)
    {
        var matches = new List<string>();

        try
        {
            ModelItem activity = editor.OwnerActivity;
            ModelItem expression = editor.Expression;
            if (activity == null || expression == null)
            {
                return "(нет модели)";
            }

            foreach (System.Activities.Presentation.Model.ModelProperty property in activity.Properties)
            {
                ModelItem value;
                try
                {
                    value = property.Value;
                }
                catch (Exception)
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                if (ReferenceEquals(value, expression))
                {
                    matches.Add(property.Name);
                    continue;
                }

                try
                {
                    if (ReferenceEquals(value.Properties["Expression"]?.Value, expression))
                    {
                        matches.Add(property.Name + "(Expression)");
                    }
                }
                catch (Exception)
                {
                    // У свойства нет вложенного Expression — это не аргумент выражения.
                }
            }
        }
        catch (Exception ex)
        {
            return "(ошибка сопоставления: " + ErrorText.Of(ex) + ")";
        }

        return matches.Count == 0 ? "(не сопоставлено)" : string.Join(",", matches);
    }

    /// <summary>
    /// Что построило окно «Свойства»: типы его полей и их тексты. Без этого нельзя отличить «поля ещё
    /// нет» (WF создаёт его только по клику) от «поле есть, а бейджа нет» (это наш дефект).
    /// </summary>
    private static List<string> DescribeInspector(DependencyObject root)
    {
        var lines = new List<string>();

        try
        {
            foreach (FrameworkElement child in XamlHelper.FindVisualChildren<FrameworkElement>(root))
            {
                string typeName = child.GetType().Name;
                string text = child is TextBox box ? box.Text : child is TextBlock label ? label.Text : null;
                lines.Add(typeName
                          + (string.IsNullOrEmpty(child.Name) ? string.Empty : "#" + child.Name)
                          + (text == null ? string.Empty : " = «" + text + "»"));

                if (lines.Count >= 80)
                {
                    lines.Add("…(обрезано)");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add("дамп окна «Свойства» не выполнен: " + ErrorText.Of(ex));
        }

        if (lines.Count == 0)
        {
            lines.Add("(в окне «Свойства» нет ни одного элемента полей)");
        }

        return lines;
    }


    /// <summary>Какой шаблон показан в поле: <c>textblock</c> (не в правке) или <c>expressioneditor</c>.</summary>
    private static string TemplateName(RoslynExpressionEditor editor)
    {
        try
        {
            if (ReferenceEquals(editor.ContentTemplate, editor.FindResource("textblock")))
            {
                return "textblock";
            }

            if (ReferenceEquals(editor.ContentTemplate, editor.FindResource("expressioneditor")))
            {
                return "expressioneditor";
            }

            return editor.ContentTemplate == null ? "(null)" : "другой";
        }
        catch (Exception)
        {
            return "(неизвестен)";
        }
    }

    /// <summary>Видимость самого глифа: пользователь видит иконку, а не флаг привязки.</summary>
    private static string BadgeVisibility(RoslynExpressionEditor editor)
    {
        try
        {
            foreach (FrameworkElement child in XamlHelper.FindVisualChildren<FrameworkElement>(editor))
            {
                if (child.GetType().Name == "MaterialIcon")
                {
                    return child.Visibility.ToString();
                }
            }

            return "(глиф не найден)";
        }
        catch (Exception)
        {
            return "(ошибка обхода)";
        }
    }
    /// <summary>Первое поле активности с заданным текстом (по нему опознаётся фикстура).</summary>
    private static RoslynExpressionEditor FindField(ModelItem activity, string text)
    {
        object view = null;
        try
        {
            view = activity.View;
        }
        catch (Exception)
        {
            return null;
        }

        if (view == null)
        {
            return null;
        }

        foreach (RoslynExpressionEditor editor in XamlHelper.FindVisualChildren<RoslynExpressionEditor>(
                     (DependencyObject)view))
        {
            if (string.Equals(
                    (editor.Text ?? string.Empty).Trim(),
                    (text ?? string.Empty).Trim(),
                    StringComparison.Ordinal))
            {
                return editor;
            }
        }

        return null;
    }

    /// <summary>
    /// Программный «клик» по полю: тот же путь, что у пользователя (<c>GotKeyboardFocus</c> на текстовом
    /// блоке поля). Поверхность конструктора в стенде живёт без активного окна, поэтому
    /// <see cref="System.Windows.Input.Keyboard.Focus"/> может не сработать — тогда событие поднимается
    /// вручную на том же элементе.
    /// </summary>
    private static void FocusField(RoslynExpressionEditor editor)
    {
        var block = XamlHelper.FindVisualChildByTypeAndName(
            editor, "TextBlock", "expresionTextBlock") as TextBlock;

        if (block == null)
        {
            return;
        }

        if (System.Windows.Input.Keyboard.Focus(block) != null)
        {
            return;
        }

        block.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            Environment.TickCount,
            null,
            block)
        {
            RoutedEvent = System.Windows.Input.Keyboard.GotKeyboardFocusEvent
        });
    }

    /// <summary>
    /// Останавливает фоновые проверки полей: цикл стартует по фокусу и тикает на диспетчере стенда,
    /// а после сценария проверять уже нечего.
    /// </summary>
    private static void StopFieldValidators(DependencyObject root)
    {
        if (root == null)
        {
            return;
        }

        try
        {
            System.Reflection.MethodInfo kill = typeof(RoslynExpressionEditor).GetMethod(
                "KillValidator",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            foreach (RoslynExpressionEditor editor in XamlHelper.FindVisualChildren<RoslynExpressionEditor>(root))
            {
                kill?.Invoke(editor, null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Фоновые проверки полей не остановлены: " + ErrorText.Of(ex));
        }
    }

    private static void StopFieldValidators(ModelItem activity)
    {
        try
        {
            StopFieldValidators(activity?.View as DependencyObject);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Фоновые проверки полей активности не остановлены: " + ErrorText.Of(ex));
        }
    }

    /// <summary>Показывает элемент в невидимом окне: без этого окно «Свойства» не строит поля.</summary>
    private static System.Windows.Interop.HwndSource HostInWindow(UIElement element, string title)
    {
        var root = new Grid();
        root.Children.Add(element);

        var parameters = new System.Windows.Interop.HwndSourceParameters(title)
        {
            Width = 420,
            Height = 900,
            WindowStyle = 0
        };

        var surface = new System.Windows.Interop.HwndSource(parameters) { RootVisual = root };
        root.Measure(new Size(420, 900));
        root.Arrange(new Rect(0, 0, 420, 900));
        root.UpdateLayout();

        return surface;
    }

    /// <summary>Таблица «поле → точка замера → бейдж → текст» для сообщений об ошибках тестов.</summary>
    private static string Describe(DesignerBenchFieldBadgeReport report)
    {
        var text = new System.Text.StringBuilder();

        foreach (FieldBadgeStage stage in report.Stages)
        {
            text.AppendLine(stage.Name + " (" + stage.ElapsedMs.ToString("0") + " мс):");

            foreach (FieldBadgeEntry field in stage.Fields)
            {
                text.AppendLine("    конструктор/" + field.FieldName
                                + " (аргумент: " + field.ArgumentName + ")"
                                + " = «" + field.Text + "»"
                                + ", шаблон: " + field.Template
                                + ", состояние: " + field.EditingState
                                + ", бейдж: " + (field.HasBadge ? "есть" : "нет")
                                + ", глиф: " + field.BadgeVisibility
                                + ", WPF: " + (field.WpfHasError ? "ошибка" : "нет")
                                + ", модель: " + field.ExpressionItemType
                                + (string.IsNullOrEmpty(field.Message) ? string.Empty : ", текст: " + field.Message));
            }
            text.AppendLine("    поля окна «Свойства»: "
                            + string.Join(" | ", report.InspectorDump));





            foreach (FieldBadgeEntry field in stage.InspectorFields)
            {
                text.AppendLine("    свойства/" + field.FieldName
                                + " = «" + field.Text + "»"
                                + ", шаблон: " + field.Template
                                + ", бейдж: " + (field.HasBadge ? "есть" : "нет")
                                + ", глиф: " + field.BadgeVisibility
                                + (string.IsNullOrEmpty(field.Message) ? string.Empty : ", текст: " + field.Message));
            }
        }

        return text.ToString();
    }


    public DesignerBenchReport Render(string xamlPath)
    {
        DesignerRenderReport report = DesignerRenderProbe.Render(xamlPath);
        return DesignerBenchReport.From(report);
    }

    public LooseOpenReport LooseOpen(string mainXaml, string[] projectXamlFiles)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var report = new LooseOpenReport();

            try
            {
                WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(projectXamlFiles);
                var designer = new WorkflowDesignerEx();
                WfDesignerEnvironment.Configure(designer);
                LooseXamlDesignerLoader.Open(designer, mainXaml, registry);

                report.LoadSucceeded = true;
                report.WorkflowFileItem = designer.Context.Items.GetValue<WorkflowFileItem>()?.LoadedFile;

                ModelService modelService = designer.Context.Services.GetService<ModelService>();
                var builder = modelService?.Root?.GetCurrentValue() as ActivityBuilder;
                if (builder?.Implementation != null)
                {
                    report.FacadeNames = WorkflowInspectionServices.GetActivities(builder.Implementation)
                        .OfType<DynamicActivity>()
                        .Select(activity => activity.Name)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList();
                }

                report.Description = "loose-загрузка выполнена; фасадов: " + report.FacadeNames.Count;
            }
            catch (Exception ex)
            {
                report.Error = ErrorText.Of(ex);
                report.Description = "loose-загрузка упала: " + report.Error;
            }

            return report;
        });
    }

    /// <summary>
    /// Отслеживание правок в loose-XAML конструкторе: публикуется <c>IDocumentPersistenceService</c>,
    /// из-за которого <c>WorkflowDesigner.NotifyModelChanged()</c> не поднимает <c>ModelChanged</c>.
    /// Проверяет, что вкладка чистая после загрузки и становится «изменённой» после реальной правки
    /// модели, а пустой <c>ModelEditingScope</c> её не помечает.
    /// </summary>
    public DesignerEditTrackingReport EditTrackingScenario(string mainXaml, string[] projectXamlFiles)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var report = new DesignerEditTrackingReport();

            try
            {
                WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(projectXamlFiles);
                var designer = new WorkflowDesignerEx();
                WfDesignerEnvironment.Configure(designer);
                LooseXamlDesignerLoader.Open(designer, mainXaml, registry);

                report.LoadSucceeded = true;
                report.CleanAfterLoad = !designer.IsEdited;

                ModelService modelService = designer.Context.Services.GetService<ModelService>();
                ModelItem root = modelService?.Root;
                ModelItem implementation = root?.Properties["Implementation"]?.Value;
                ModelItemCollection activities = implementation?.Properties["Activities"]?.Collection;

                if (implementation == null || activities == null)
                {
                    report.Description += "реализация/коллекция Activities недоступны; ";
                }
                else
                {
                    using (ModelEditingScope edit = implementation.BeginEdit())
                    {
                        activities.Add(ModelFactory.CreateItem(designer.Context, new WriteLine { DisplayName = "Правка стенда" }));
                        edit.Complete();
                    }

                    report.EditedAfterModelChange = designer.IsEdited;

                    // Пустой scope не должен помечать вкладку «изменённой».
                    designer.IsEdited = false;
                    using (ModelEditingScope empty = implementation.BeginEdit())
                    {
                        empty.Complete();
                    }

                    report.EditedAfterEmptyScope = designer.IsEdited;
                }

                report.Description += "cleanAfterLoad: " + report.CleanAfterLoad
                    + ", editedAfterModelChange: " + report.EditedAfterModelChange
                    + ", editedAfterEmptyScope: " + report.EditedAfterEmptyScope;
            }
            catch (Exception ex)
            {
                report.Error = ErrorText.Of(ex);
                report.Description = "сценарий отслеживания правок упал: " + report.Error;
            }

            return report;
        });
    }


    /// <summary>
    /// Специфические фичи контекстного меню WF-дизайнера: аннотации и точки останова. Проверяет, что
    /// после настройки среды опубликованы <c>IDesignerDebugView</c>/<c>ICommandService</c> и включены
    /// аннотации — иначе пункты меню «Точка останова»/«Аннотации» в <c>DesignerView</c> схлопываются.
    /// </summary>
    public DesignerFeaturesReport DesignerFeaturesScenario(string mainXaml, string[] projectXamlFiles)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var report = new DesignerFeaturesReport();

            try
            {
                WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(projectXamlFiles);
                var designer = new WorkflowDesignerEx();
                WfDesignerEnvironment.Configure(designer);
                LooseXamlDesignerLoader.Open(designer, mainXaml, registry);

                // Студия публикует ICommandService в LoadActivityToDesignerHelper сразу после загрузки
                // (общий шаг для loose и non-loose путей). Стенд повторяет его, чтобы live-проверка
                // покрывала полный рецепт пункта «Точка останова»: IDesignerDebugView + ICommandService.
                designer.Context.Services.Publish<ICommandService>(new CommandService(designer));

                report.LoadSucceeded = true;

                DesignerConfigurationService configuration =
                    designer.Context.Services.GetService<DesignerConfigurationService>();
                report.AnnotationEnabled = configuration?.AnnotationEnabled == true;

                report.DebugViewAvailable =
                    designer.Context.Services.GetService<IDesignerDebugView>() != null;
                report.CommandServiceAvailable =
                    designer.Context.Services.GetService<ICommandService>() != null;
                report.BreakpointServicesEnabled =
                    report.DebugViewAvailable && report.CommandServiceAvailable;

                report.Description =
                    "annotationEnabled: " + report.AnnotationEnabled
                    + ", debugView: " + report.DebugViewAvailable
                    + ", commandService: " + report.CommandServiceAvailable
                    + ", breakpointServicesEnabled: " + report.BreakpointServicesEnabled;
            }
            catch (Exception ex)
            {
                report.Error = ErrorText.Of(ex);
                report.Description = "сценарий фич меню упал: " + report.Error;
            }

            return report;
        });
    }


    /// <summary>
    /// Показывает окно с заданным заголовком и возвращает то, что заметил страж модальных окон.
    /// </summary>
    public List<string> WindowsSeenWhileShowing(string title)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            using (DesignerModalWindowGuard guard = DesignerModalWindowGuard.Attach())
            {
                var window = new Window
                {
                    Title = title,
                    Width = 200,
                    Height = 120,
                    ShowInTaskbar = false
                };

                try
                {
                    window.Show();
                    host.Pump(TimeSpan.FromMilliseconds(300));
                    guard.Poll();
                }
                finally
                {
                    window.Close();
                }

                return new List<string>(guard.Windows);
            }
        });
    }

    /// <summary>
    /// Сценарий этапа 3 конвейера генерации XAML на живом дизайнере:
    /// открытие файла сервисом (<c>DesignerOpenService</c>) → повторное открытие того же файла →
    /// проверка перезаписи открытого файла → попытка генерации в него через <c>WorkflowBuildService</c>.
    /// </summary>
    public DesignerBenchOpenReport OpenTabScenario(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();
            DesignerOpenService service = designerHost.CreateService();

            DesignerOpenResult first = service.OpenOrActivate(xamlPath);
            int activityCount = designerHost.CountActiveActivities();
            DesignerOpenResult second = service.OpenOrActivate(xamlPath);
            DesignerWriteCheck writeCheck = service.CheckWrite(xamlPath);

            WorkflowBuildResult regeneration = new WorkflowBuildService(
                    WorkflowTestSamples.Options(),
                    gate: null,
                    writeCheck: service)
                .Save(WorkflowTestSamples.NestedWorkflow(), xamlPath);

            var report = new DesignerBenchOpenReport
            {
                FilePath = first.FilePath,
                WorkflowId = first.WorkflowId,
                FirstOk = first.Ok,
                Activated = first.Activated,
                ActivityCount = activityCount,
                ActivityCountInfo = designerHost.ActivityCountInfo,
                SecondOk = second.Ok,
                AlreadyOpenOnSecondCall = second.AlreadyOpen,
                SecondActivated = second.Activated,
                OpenCount = designerHost.OpenCount,
                CloseCount = designerHost.CloseCount,
                WriteBlocked = !writeCheck.CanWrite,
                WriteBlockedError = writeCheck.Error,
                RegenerationOk = regeneration.Ok,
                Errors = first.Errors.Concat(second.Errors).ToList(),
                RegenerationErrors = new List<string>(regeneration.Errors)
            };

            report.Ok = report.FirstOk
                         && report.SecondOk
                         && report.Activated
                         && report.SecondActivated
                         && report.AlreadyOpenOnSecondCall
                         && report.OpenCount == 1
                         && report.ActivityCount > 0
                         && report.WriteBlocked
                         && !report.RegenerationOk;

            // Уборка после сценария: закрываем вкладки стенда. Иначе фоновая валидация продолжает
            // тикать на диспетчере, а при выгрузке домена её тик пытается показать модальное окно WF
            // (наблюдалось как AccessViolation при инициализации шрифтов на остановке процесса).
            foreach (DesignerTab tab in designerHost.GetOpenTabs())
            {
                designerHost.Close(tab);
            }


            return report;
        });
    }


    /// <summary>Домен не должен выгружаться по аренде, пока идёт прогон.</summary>
    /// <summary>
    /// Сценарий этапа 4 конвейера генерации XAML на живом дизайнере:
    /// палитра открывает файл, вставляет активность через <c>ModelService</c>, сохраняет её штатным
    /// сериализатором конструктора и задаёт аргумент. Проверяется контракт п. 1.3: вкладка открыта
    /// и активна, активность видна в дереве, гейт валидации не нашёл ошибок.
    /// </summary>
    public DesignerBenchPaletteReport PaletteScenario(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();

            var service = new PaletteWorkflowService(
                ActivityPaletteRegistry.Default,
                designerHost,
                new DispatcherDesignerUiThread(host.Dispatcher),
                tab => tab?.Designer == null ? null : new DesignerModelSession(tab.Designer));

            var report = new DesignerBenchPaletteReport { FilePath = xamlPath };

            try
            {
                WorkflowSnapshot before = WorkflowModelReader.Read(xamlPath);
                report.ActivityCountBefore = before.ActivityCount;

                PaletteResult added = service.AddActivity(new PaletteAddActivityRequest
                {
                    FilePath = xamlPath,
                    Activity = new PaletteActivitySpec("WriteLine")
                        .WithArgument("Text", PaletteValue.FromExpression("\"добавлено палитрой\""))
                });

                report.AddOk = added.Ok;
                report.AddErrors = new List<string>(added.Errors);
                report.AddHints = new List<string>(added.Hints);
                report.ActivityId = added.ActivityId;
                report.WorkflowId = added.WorkflowId;
                report.ActivityCountAfterAdd = added.Snapshot?.ActivityCount ?? 0;
                report.TabOpen = designerHost.GetOpenTabs().Count > 0;
                report.TabActive = designerHost.ActiveTab != null;
                report.ActivityCountInDesigner = designerHost.CountActiveActivities();
                ModelService modelService = designerHost.ActiveTab?.Designer?.Context?.Services?.GetService<ModelService>();
                report.ModelActivityCount = modelService?.Root == null ? 0 : CountModelActivities(modelService.Root);
                report.DesignerInfo = designerHost.ActivityCountInfo;

                if (added.Ok && !string.IsNullOrWhiteSpace(added.ActivityId))
                {
                    PaletteResult changed = service.SetArgument(new PaletteSetArgumentRequest
                    {
                        FilePath = xamlPath,
                        ActivityId = added.ActivityId,
                        ArgumentName = "Text",
                        Value = PaletteValue.FromExpression("\"текст после set_argument\"")
                    });

                    report.SetArgumentOk = changed.Ok;
                    report.SetArgumentErrors = new List<string>(changed.Errors);
                    report.ExpressionInFile = File.ReadAllText(xamlPath).Contains("текст после set_argument");
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add(ErrorText.Describe("сценарий палитры не выполнен", ex));
            }
            finally
            {
                // Уборка: закрываем вкладки стенда, иначе фоновая валидация WF продолжает тикать
                // на диспетчере стенда и при выгрузке домена показывает модальное окно.
                foreach (DesignerTab tab in designerHost.GetOpenTabs())
                {
                    designerHost.Close(tab);
                }
            }

            report.Ok = report.Errors.Count == 0
                        && report.AddOk
                        && report.TabOpen
                        && report.TabActive
                        && report.ActivityCountAfterAdd == report.ActivityCountBefore + 1
                        && report.ModelActivityCount > 0
                        && report.SetArgumentOk
                        && report.ExpressionInFile;

            return report;
        });
    }

    /// <summary>
    /// Сколько активностей в дереве модели конструктора. Считается по <c>ModelItem</c>, а не по
    /// объектной модели: именно модель показывает пользователю диаграмму, и вставка «в файл, но не
    /// в дерево» — это и есть дефект, который ловит эта проверка.
    /// </summary>
    /// <summary>
    /// Сколько активностей в дереве модели конструктора. Считается по <c>ModelItem</c>, а не по
    /// объектной модели: именно модель показывает пользователю диаграмму, и вставка «в файл, но не
    /// в дерево» — это и есть дефект, который ловит эта проверка.
    /// <para>
    /// Выражения (<c>CSharpValue&lt;T&gt;</c> — тоже активности) не считаются, а посещённые объекты
    /// запоминаются: у модели есть общие узлы, и обход без защиты от повторов уходит в бесконечность.
    /// </para>
    /// </summary>
    private static int CountModelActivities(ModelItem item, HashSet<object> visited = null, int depth = 0)
    {
        if (item == null || depth > 32)
        {
            return 0;
        }

        visited ??= new HashSet<object>();

        object value = item.GetCurrentValue();
        bool isBuilder = value is System.Activities.ActivityBuilder;
        bool isActivity = value is System.Activities.Activity
                          && !(value is System.Activities.ActivityWithResult);

        if (!isBuilder && !isActivity)
        {
            return 0;
        }

        if (value != null && !visited.Add(value))
        {
            return 0;
        }

        int count = isActivity ? 1 : 0;

        foreach (ModelProperty property in item.Properties)
        {
            if (property.IsCollection && property.Collection != null)
            {
                foreach (ModelItem child in property.Collection)
                {
                    count += CountModelActivities(child, visited, depth + 1);
                }

                continue;
            }

            count += CountModelActivities(property.Value, visited, depth + 1);
        }

        return count;
    }

    /// <summary>
    /// Сценарий моста правок (карточка <c>AG-02</c>) на живом конструкторе: пачка вставок, <c>complete</c>,
    /// <c>undo</c>/<c>redo</c>, откат пачки при ошибке, удаление и перенос, структура несохранённого документа.
    /// <para>
    /// Без живого дизайнера это не проверить: <c>ModelEditingScope</c>, история отмены и
    /// <c>ModelService</c> существуют только внутри загруженной вкладки.
    /// </para>
    /// </summary>
    public DesignerBenchEditReport WorkflowEditScenario(string xamlPath, string unopenedPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();
            var report = new DesignerBenchEditReport { FilePath = xamlPath, UnopenedPath = unopenedPath };

            try
            {
                var service = new StudioWorkflowEditingService(
                    designerHost,
                    designer => new DispatcherDesignerUiThread(host.Dispatcher));

                report.OpenBeforeAnyTab = service.IsOpenInDesigner(xamlPath);

                DesignerTab tab = designerHost.Open(xamlPath);
                designerHost.Activate(tab);
                host.Pump(TimeSpan.FromMilliseconds(200));

                report.OpenInDesigner = service.IsOpenInDesigner(xamlPath);
                report.ActivityCountBefore = CountActivities(service, xamlPath);

                using (IWorkflowEditSession session = service.BeginEdit(xamlPath, "правка теста"))
                {
                    report.SessionActive = session.IsActive;
                    report.InactiveReason = (session as UnavailableWorkflowEditSession)?.Reason;

                    var start = session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine", Index = 0 });
                    report.InsertStartOk = start.Ok;
                    report.InsertStartId = start.NewElementId;
                    report.InsertStartErrors = IssueTexts(start);

                    var middle = session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine", Index = 1 });
                    report.InsertMiddleOk = middle.Ok;
                    report.InsertMiddleId = middle.NewElementId;

                    var end = session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine" });
                    report.InsertEndOk = end.Ok;
                    report.InsertEndId = end.NewElementId;

                    report.BatchActiveDuringEdits = session.IsActive;
                    report.ActivityCountDuringBatch = CountActivities(service, xamlPath);
                    report.OrderDuringBatch = OrderOf(service, xamlPath);

                    var complete = session.Complete();
                    report.CompleteOk = complete.Ok;
                    report.SessionActiveAfterComplete = complete.SessionActive;
                    report.CanUndoAfterComplete = complete.CanUndo;
                    report.ActivityCountAfterComplete = complete.Outline?.ActivityCount ?? -1;
                }

                // Структура несохранённого документа: правки живут в модели, файл на диске не менялся.
                report.LiveOutlineBeforeSave = CountActivities(service, xamlPath);

                var undo = service.Undo(xamlPath);
                report.UndoOk = undo.Ok;
                report.CanRedoAfterUndo = undo.CanRedo;
                host.Pump(TimeSpan.FromMilliseconds(100));
                report.ActivityCountAfterUndo = CountActivities(service, xamlPath);

                var redo = service.Redo(xamlPath);
                report.RedoOk = redo.Ok;
                host.Pump(TimeSpan.FromMilliseconds(100));
                report.ActivityCountAfterRedo = CountActivities(service, xamlPath);

                // Откат пачки: каждая правка — своя запись истории, поэтому revert отменяет их все.
                report.ActivityCountBeforeRevert = CountActivities(service, xamlPath);

                using (IWorkflowEditSession session = service.BeginEdit(xamlPath, "пачка на откат"))
                {
                    bool firstRevertInsert = session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine", Index = 0 }).Ok;
                    bool secondRevertInsert = session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine", Index = 0 }).Ok;
                    report.RevertInsertOk = firstRevertInsert && secondRevertInsert;
                    report.ActivityCountDuringRevertBatch = CountActivities(service, xamlPath);

                    var revert = session.Revert();
                    report.RevertOk = revert.Ok;
                    report.RevertSessionActive = revert.SessionActive;
                    report.RevertErrors = IssueTexts(revert);
                }

                report.ActivityCountAfterRevert = CountActivities(service, xamlPath);

                // Ошибка в пачке откатывает всю пачку: «половины правок» на диаграмме не остаётся.
                report.ActivityCountBeforeFailure = CountActivities(service, xamlPath);

                using (IWorkflowEditSession session = service.BeginEdit(xamlPath, "пачка с ошибкой"))
                {
                    var failed = session.Insert(new WorkflowInsertRequest
                    {
                        ActivityType = "WriteLine",
                        ParentId = "root/NoSuchElement"
                    });

                    report.FailedInsertOk = failed.Ok;
                    report.FailedInsertErrors = IssueTexts(failed);
                    report.SessionActiveAfterFailure = session.IsActive;
                }

                report.ActivityCountAfterFailure = CountActivities(service, xamlPath);

                using (IWorkflowEditSession session = service.BeginEdit(xamlPath, "удаление и перенос"))
                {
                    WorkflowOutline outline = session.Outline();
                    string firstId = outline?.Root?.Children.FirstOrDefault()?.Id;
                    report.DeletedId = firstId;

                    var delete = session.Delete(firstId);
                    report.DeleteOk = delete.Ok;
                    report.DeleteErrors = IssueTexts(delete);
                    report.ActivityCountAfterDelete = CountActivities(service, xamlPath);

                    WorkflowOutline afterDelete = session.Outline();
                    string moveId = afterDelete?.Root?.Children.LastOrDefault()?.Id;
                    report.MovedId = moveId;
                    report.OrderBeforeMove = OrderOf(service, xamlPath);

                    var move = session.Move(new WorkflowMoveRequest { ElementId = moveId, ParentId = "root", Index = 0 });
                    report.MoveOk = move.Ok;
                    report.MoveNewId = move.NewElementId;
                    report.MoveErrors = IssueTexts(move);
                    report.OrderAfterMove = OrderOf(service, xamlPath);

                    var missing = session.Delete("root/Activities[99]");
                    report.DeleteMissingOk = missing.Ok;

                    var complete = session.Complete();
                    report.SecondBatchCompleteOk = complete.Ok;
                    report.ActivityCountAfterSecondBatch = complete.Outline?.ActivityCount ?? -1;
                }

                // Файл, не открытый вкладкой: структурированный отказ, а не исключение.
                using (IWorkflowEditSession session = service.BeginEdit(unopenedPath, "не открыт"))
                {
                    report.NotOpenSessionActive = session.IsActive;
                    report.NotOpenReason = (session as UnavailableWorkflowEditSession)?.Reason;

                    var insert = session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine" });
                    report.NotOpenInsertOk = insert.Ok;
                    report.NotOpenInsertErrors = IssueTexts(insert);
                }

                // AG-06: валидация несохранённого документа — тем же путём, что у workflow_validate в студии:
                // служба структуры читает живую модель через LiveOutlineSource, как MainView в студии.
                // Ошибку даёт активность без обязательного аргумента — Assign без To.
                using (IWorkflowEditSession session = service.BeginEdit(xamlPath, "валидация"))
                {
                    // Тип уточнён явно: в этом файле уже есть RPA.Core.Generation.WorkflowEditResult
                    // (результат правки через палитру), имена совпадают.
                    RPA.Chat.Services.Workflow.WorkflowEditResult invalid =
                        session.Insert(new WorkflowInsertRequest { ActivityType = "Assign", Index = 0 });
                    report.ValidateInsertOk = invalid.Ok;
                    report.ValidateInsertId = invalid.NewElementId;
                    report.ValidateInsertCompleteOk = session.Complete().Ok;
                }

                StudioWorkflowOutlineService.LiveOutlineSource =
                    (path, depth, includeArguments) => service.GetLiveOutline(path, depth, includeArguments);

                try
                {
                    report.ValidateUndoDepthBefore = UndoDepthOf(designerHost);

                    WorkflowOutline live = StudioWorkflowOutlineService.Default.GetOutline(xamlPath);

                    // Полное имя намеренно: в этом файле есть ещё RPA.Core.Generation.WorkflowValidationReport
                    // (отчёт гейта валидного XAML) — это разные сущности с разным назначением.
                    var validate = RPA.Chat.Services.Workflow.WorkflowValidationReport.FromOutline(live);

                    report.ValidateUndoDepthAfter = UndoDepthOf(designerHost);
                    report.ValidateAvailable = validate.Available;
                    report.ValidateSource = validate.SourceName;
                    report.ValidateErrors = validate.Errors;
                    report.ValidateAddresses = validate.Issues.Select(issue => issue.ElementId).ToList();
                    report.ValidateText = WorkflowValidationText.Describe(validate);
                }
                finally
                {
                    StudioWorkflowOutlineService.LiveOutlineSource = null;
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add(ErrorText.Describe("сценарий правок не выполнен", ex));
            }
            finally
            {
                // Уборка: закрываем вкладки стенда, иначе фоновая валидация продолжает тикать
                // на диспетчере стенда и при выгрузке домена показывает модальное окно.
                foreach (DesignerTab tab in designerHost.GetOpenTabs())
                {
                    designerHost.Close(tab);
                }
            }

            report.Ok = report.Errors.Count == 0
                        && !report.OpenBeforeAnyTab
                        && report.OpenInDesigner
                        && report.SessionActive
                        && report.InsertStartOk
                        && report.InsertMiddleOk
                        && report.InsertEndOk
                        && report.ActivityCountDuringBatch == report.ActivityCountBefore + 3
                        && report.CompleteOk
                        && !report.SessionActiveAfterComplete
                        && report.CanUndoAfterComplete
                        && report.ActivityCountAfterComplete == report.ActivityCountBefore + 3
                        && report.LiveOutlineBeforeSave == report.ActivityCountBefore + 3
                        && report.UndoOk
                        && report.ActivityCountAfterUndo == report.ActivityCountBefore + 2
                        && report.RedoOk
                        && report.ActivityCountAfterRedo == report.ActivityCountBefore + 3
                        && report.RevertInsertOk
                        && report.ActivityCountDuringRevertBatch == report.ActivityCountBefore + 5
                        && report.RevertOk
                        && !report.RevertSessionActive
                        && report.ActivityCountAfterRevert == report.ActivityCountBefore + 3
                        && !report.FailedInsertOk
                        && report.ActivityCountAfterFailure == report.ActivityCountBefore + 3
                        && report.DeleteOk
                        && report.MoveOk
                        && !report.DeleteMissingOk
                        && report.SecondBatchCompleteOk
                        && !report.NotOpenSessionActive
                        && !report.NotOpenInsertOk
                        && report.ValidateInsertOk
                        && report.ValidateAvailable
                        && report.ValidateErrors > 0
                        && report.ValidateUndoDepthBefore == report.ValidateUndoDepthAfter;

            return report;
        });
    }

    /// <summary>
    /// Глубина истории отмены вкладки: сколько правок лежит в стеке <c>UndoEngine</c>. Нужна, чтобы
    /// доказать, что чтение структуры и валидация не оставляют в модели ни одной правки.
    /// </summary>
    private static int UndoDepthOf(BenchDesignerHost host)
    {
        try
        {
            RPA.Core.WorkflowDesignerEx designer = host?.ActiveTab?.Designer;
            var engine = designer?.Context?.Services
                .GetService(typeof(System.Activities.Presentation.UndoEngine))
                as System.Activities.Presentation.UndoEngine;

            var actions = engine?.GetUndoActions();

            return actions == null ? -1 : actions.Count();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Глубина истории отмены не прочитана: " + ErrorText.Of(ex));
            return -1;
        }
    }

    /// <summary>
    /// Сценарий обёртки активности (карточка <c>AG-04</c>) на живом конструкторе: обёртка занимает место
    /// элемента, элемент становится её первым ребёнком, адреса совпадают с outline, неоднозначный и
    /// несуществующий запросы отклоняются, <c>undo</c> возвращает структуру.
    /// <para>
    /// Без живого конструктора это не проверить: перестановка узлов идёт через <c>ModelItem</c>, а
    /// «обёртка внутри обёртки» ломается именно на пересоздании узлов, а не на фейковой модели.
    /// </para>
    /// </summary>
    public DesignerBenchWrapReport WorkflowWrapScenario(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();
            var report = new DesignerBenchWrapReport { FilePath = xamlPath };

            try
            {
                var service = new StudioWorkflowEditingService(
                    designerHost,
                    designer => new DispatcherDesignerUiThread(host.Dispatcher));

                DesignerTab tab = designerHost.Open(xamlPath);
                designerHost.Activate(tab);
                host.Pump(TimeSpan.FromMilliseconds(200));

                report.OpenInDesigner = service.IsOpenInDesigner(xamlPath);
                report.ActivityCountBefore = CountActivities(service, xamlPath);

                using (IWorkflowEditSession session = service.BeginEdit(xamlPath, "обёртка теста"))
                {
                    report.SessionActive = session.IsActive;
                    report.InactiveReason = (session as UnavailableWorkflowEditSession)?.Reason;

                    var sequenceWrap = session.Wrap(new WorkflowWrapRequest
                    {
                        ElementId = "root/Activities[0]",
                        WrapperType = "Sequence",
                        DisplayName = "guarded"
                    });

                    report.WrapInSequenceOk = sequenceWrap.Ok;
                    // NewElementId — адрес обёрнутой активности (по нему агент работает дальше),
                    // адрес самой обёртки — первый в списке адресов.
                    report.WrapInSequenceWrapperId = sequenceWrap.NewElementId;
                    report.WrapInSequenceWrapperAddress = sequenceWrap.Addresses.FirstOrDefault();
                    report.WrapInSequenceErrors = IssueTexts(sequenceWrap);
                    report.ActivityCountAfterSequenceWrap = CountActivities(service, xamlPath);
                    report.OrderAfterSequenceWrap = OrderOf(service, xamlPath);
                    report.FirstChildOfWrapper = ChildrenOf(service, xamlPath, "root/Activities[0]");

                    // У If несколько контейнеров: без container выбор не угадывается.
                    var ambiguous = session.Wrap(new WorkflowWrapRequest
                    {
                        ElementId = "root/Activities[1]",
                        WrapperType = "If"
                    });

                    report.WrapWithoutContainerOk = ambiguous.Ok;
                    report.WrapWithoutContainerErrors = IssueTexts(ambiguous);

                    var ifWrap = session.Wrap(new WorkflowWrapRequest
                    {
                        ElementId = "root/Activities[1]",
                        WrapperType = "If",
                        Container = "Then"
                    });

                    report.WrapInIfThenOk = ifWrap.Ok;
                    report.WrapInIfWrapperId = ifWrap.NewElementId;
                    report.WrapInIfWrapperAddress = ifWrap.Addresses.FirstOrDefault();
                    report.WrapInIfThenErrors = IssueTexts(ifWrap);
                    report.OrderAfterIfWrap = OrderOf(service, xamlPath);
                    report.ThenChild = ChildrenOf(service, xamlPath, "root/Activities[1]");

                    var unknown = session.Wrap(new WorkflowWrapRequest
                    {
                        ElementId = "root/Activities[0]",
                        WrapperType = "NoSuchActivity"
                    });

                    report.WrapUnknownTypeOk = unknown.Ok;
                    report.WrapUnknownTypeErrors = IssueTexts(unknown);

                    var complete = session.Complete();
                    report.CompleteOk = complete.Ok;
                    report.ActivityCountAfterComplete = complete.Outline?.ActivityCount ?? -1;
                }

                var undo = service.Undo(xamlPath);
                report.UndoOk = undo.Ok;
                host.Pump(TimeSpan.FromMilliseconds(100));
                report.ActivityCountAfterUndo = CountActivities(service, xamlPath);
                report.OrderAfterUndo = OrderOf(service, xamlPath);
            }
            catch (Exception ex)
            {
                report.Errors.Add(ErrorText.Describe("сценарий обёртки не выполнен", ex));
            }
            finally
            {
                // Уборка: закрываем вкладки стенда, иначе фоновая валидация продолжает тикать
                // на диспетчере стенда и при выгрузке домена показывает модальное окно.
                foreach (DesignerTab tab in designerHost.GetOpenTabs())
                {
                    designerHost.Close(tab);
                }
            }

            report.Ok = report.Errors.Count == 0
                        && report.OpenInDesigner
                        && report.SessionActive
                        && report.WrapInSequenceOk
                        && report.ActivityCountAfterSequenceWrap == report.ActivityCountBefore + 1
                        && report.WrapWithoutContainerOk == false
                        && report.WrapInIfThenOk
                        && report.WrapInIfWrapperId == "root/Activities[1]"
                        && report.WrapUnknownTypeOk == false
                        && report.CompleteOk
                        && report.ActivityCountAfterComplete == report.ActivityCountBefore + 2
                        && report.UndoOk
                        && report.ActivityCountAfterUndo == report.ActivityCountBefore + 1;

            return report;
        });
    }

    /// <summary>
    /// Сценарий аргументов и переменных (карточка <c>AG-05</c>) на живом конструкторе: тип аргумента,
    /// литерал и выражение, отказы на неизвестном аргументе, битом выражении и неверном типе значения,
    /// объявление и удаление переменной.
    /// <para>
    /// Только живая модель показывает то, что важно здесь: <c>InArgument&lt;T&gt;</c> с
    /// <c>CSharpValue&lt;T&gt;</c> принимается конструктором, тип аргумента берётся из реального
    /// дескриптора, а переменная сразу появляется в коллекции <c>Variables</c> активности.
    /// </para>
    /// </summary>
    public DesignerBenchArgumentReport WorkflowArgumentScenario(string xamlPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();
            var report = new DesignerBenchArgumentReport { FilePath = xamlPath };

            try
            {
                var service = new StudioWorkflowEditingService(
                    designerHost,
                    designer => new DispatcherDesignerUiThread(host.Dispatcher));

                DesignerTab tab = designerHost.Open(xamlPath);
                designerHost.Activate(tab);
                host.Pump(TimeSpan.FromMilliseconds(200));

                report.OpenInDesigner = service.IsOpenInDesigner(xamlPath);

                using (IWorkflowEditSession session = service.BeginEdit(xamlPath, "аргументы теста"))
                {
                    report.SessionActive = session.IsActive;
                    report.InactiveReason = (session as UnavailableWorkflowEditSession)?.Reason;

                    // Литерал в WriteLine.Text: значение видно в структуре живой модели.
                    var literal = session.SetArgument(new WorkflowArgumentSetRequest
                    {
                        ElementId = "root/Activities[0]",
                        Argument = "Text"
                    }.SetValue("hello from the agent"));

                    report.LiteralOk = literal.Ok;
                    report.LiteralMessage = literal.Message;
                    report.LiteralErrors = IssueTexts(literal);
                    report.TextAfterLiteral = ArgumentOf(service, xamlPath, "root/Activities[0]", "Text");

                    // Выражение: CSharpValue<string> конструктор принимает так же, как палитра.
                    var expression = session.SetArgument(new WorkflowArgumentSetRequest
                    {
                        ElementId = "root/Activities[0]",
                        Argument = "Text",
                        Expression = "\"a\" + \"b\""
                    });

                    report.ExpressionOk = expression.Ok;
                    report.ExpressionMessage = expression.Message;
                    report.ExpressionErrors = IssueTexts(expression);
                    report.TextAfterExpression = ArgumentOf(service, xamlPath, "root/Activities[0]", "Text");

                    // Неизвестный аргумент — отказ: имена даёт activity_describe.
                    var unknown = session.SetArgument(new WorkflowArgumentSetRequest
                    {
                        ElementId = "root/Activities[0]",
                        Argument = "NoSuchArgument"
                    }.SetValue("x"));

                    report.UnknownArgumentOk = unknown.Ok;
                    report.UnknownArgumentErrors = IssueTexts(unknown);

                    // Литерал и выражение одновременно — ошибка запроса, а не «последнее выиграло».
                    var bothResult = session.SetArgument(new WorkflowArgumentSetRequest
                    {
                        ElementId = "root/Activities[0]",
                        Argument = "Text",
                        Expression = "1"
                    }.SetValue("x"));

                    report.BothOk = bothResult.Ok;
                    report.BothErrors = IssueTexts(bothResult);

                    // Битое выражение ловится до правки модели.
                    var broken = session.SetArgument(new WorkflowArgumentSetRequest
                    {
                        ElementId = "root/Activities[0]",
                        Argument = "Text",
                        Expression = "if"
                    });

                    report.BrokenExpressionOk = broken.Ok;
                    report.BrokenExpressionErrors = IssueTexts(broken);

                    // Переменная в корне процесса: тип Int32 и значение по умолчанию 0.
                    var variable = session.AddVariable(new WorkflowVariableAddRequest
                    {
                        ElementId = "root",
                        Name = "counter",
                        TypeName = "Int32"
                    }.SetValue("0"));

                    report.VariableAddOk = variable.Ok;
                    report.VariableAddMessage = variable.Message;
                    report.VariableAddErrors = IssueTexts(variable);
                    report.VariablesAfterAdd = VariablesOf(service, xamlPath, "root");

                    var duplicate = session.AddVariable(new WorkflowVariableAddRequest
                    {
                        ElementId = "root",
                        Name = "counter",
                        TypeName = "Int32"
                    });

                    report.DuplicateVariableOk = duplicate.Ok;
                    report.DuplicateVariableErrors = IssueTexts(duplicate);

                    var badType = session.AddVariable(new WorkflowVariableAddRequest
                    {
                        ElementId = "root",
                        Name = "amount",
                        TypeName = "Money"
                    });

                    report.UnknownTypeOk = badType.Ok;
                    report.UnknownTypeErrors = IssueTexts(badType);

                    var badValue = session.AddVariable(new WorkflowVariableAddRequest
                    {
                        ElementId = "root",
                        Name = "amount",
                        TypeName = "Int32"
                    }.SetValue("не число"));

                    report.BadValueOk = badValue.Ok;
                    report.BadValueErrors = IssueTexts(badValue);

                    // У активности без коллекции Variables объявлять переменную негде.
                    var noVariables = session.AddVariable(new WorkflowVariableAddRequest
                    {
                        ElementId = "root/Activities[0]",
                        Name = "innerCounter",
                        TypeName = "Int32"
                    });

                    report.NoVariablesOk = noVariables.Ok;
                    report.NoVariablesErrors = IssueTexts(noVariables);

                    var remove = session.RemoveVariable(new WorkflowVariableRemoveRequest
                    {
                        ElementId = "root",
                        Name = "counter"
                    });

                    report.VariableRemoveOk = remove.Ok;
                    report.VariableRemoveErrors = IssueTexts(remove);
                    report.VariablesAfterRemove = VariablesOf(service, xamlPath, "root");

                    var removeMissing = session.RemoveVariable(new WorkflowVariableRemoveRequest
                    {
                        ElementId = "root",
                        Name = "counter"
                    });

                    report.RemoveMissingOk = removeMissing.Ok;
                    report.RemoveMissingErrors = IssueTexts(removeMissing);

                    var complete = session.Complete();
                    report.CompleteOk = complete.Ok;
                }

                var undo = service.Undo(xamlPath);
                report.UndoOk = undo.Ok;
                report.TextAfterUndo = ArgumentOf(service, xamlPath, "root/Activities[0]", "Text");
            }
            catch (Exception ex)
            {
                report.Errors.Add(ErrorText.Describe("сценарий аргументов не выполнен", ex));
            }
            finally
            {
                // Уборка: закрываем вкладки стенда, иначе фоновая валидация продолжает тикать
                // на диспетчере стенда и при выгрузке домена показывает модальное окно.
                foreach (DesignerTab tab in designerHost.GetOpenTabs())
                {
                    designerHost.Close(tab);
                }
            }

            report.Ok = report.Errors.Count == 0
                        && report.OpenInDesigner
                        && report.SessionActive
                        && report.LiteralOk
                        && report.TextAfterLiteral != null
                        && report.TextAfterLiteral.Contains("hello from the agent")
                        && report.ExpressionOk
                        && report.TextAfterExpression != null
                        && report.TextAfterExpression.Contains("a")
                        && !report.UnknownArgumentOk
                        && !report.BothOk
                        && !report.BrokenExpressionOk
                        && report.VariableAddOk
                        && report.VariablesAfterAdd.Contains("counter")
                        && !report.DuplicateVariableOk
                        && !report.UnknownTypeOk
                        && !report.BadValueOk
                        && !report.NoVariablesOk
                        && report.VariableRemoveOk
                        && !report.VariablesAfterRemove.Contains("counter")
                        && !report.RemoveMissingOk
                        && report.CompleteOk
                        && report.UndoOk;

            return report;
        });
    }

    /// <summary>Значение аргумента в живой структуре процесса (по адресу и имени аргумента).</summary>
    private static string ArgumentOf(IWorkflowEditingService service, string path, string elementId, string argument)
    {
        WorkflowOutline outline = service.GetLiveOutline(path);
        WorkflowOutlineNode node = outline?.Find(elementId);

        if (node == null)
        {
            return null;
        }

        foreach (WorkflowOutlineArgument item in node.Arguments)
        {
            if (string.Equals(item.Name, argument, StringComparison.OrdinalIgnoreCase))
            {
                return item.Expression;
            }
        }

        return null;
    }

    /// <summary>Имена переменных, объявленных в активности (по адресу).</summary>
    private static List<string> VariablesOf(IWorkflowEditingService service, string path, string elementId)
    {
        var names = new List<string>();
        WorkflowOutline outline = service.GetLiveOutline(path);
        WorkflowOutlineNode node = outline?.Find(elementId);

        if (node == null)
        {
            return names;
        }

        foreach (WorkflowOutlineVariable variable in node.Variables)
        {
            names.Add(variable.Name);
        }

        return names;
    }

    /// <summary>Активностей в живой модели открытой вкладки (-1 — структура не прочитана).</summary>
    private static int CountActivities(IWorkflowEditingService service, string path)
    {
        WorkflowOutline outline = service.GetLiveOutline(path);
        return outline?.ActivityCount ?? -1;
    }

    /// <summary>Порядок детей корня: тип и подпись — так видно и вставку, и перенос.</summary>
    private static string OrderOf(IWorkflowEditingService service, string path)
    {
        WorkflowOutline outline = service.GetLiveOutline(path);

        if (outline?.Root == null)
        {
            return string.Empty;
        }

        return string.Join(
            " | ",
            outline.Root.Children.Select(child => child.ShortTypeName + ":" + child.DisplayName));
    }

    /// <summary>Дети узла по его адресу: «тип:подпись | …» — так видно и обёртку, и вложенный элемент.</summary>
    private static string ChildrenOf(IWorkflowEditingService service, string path, string nodeId)
    {
        WorkflowOutline outline = service.GetLiveOutline(path);

        if (outline?.Root == null)
        {
            return string.Empty;
        }

        WorkflowOutlineNode node = outline.All().FirstOrDefault(item =>
            string.Equals(item.Id, nodeId, StringComparison.OrdinalIgnoreCase));

        if (node == null)
        {
            return "(нет узла " + nodeId + ")";
        }

        return string.Join(
            " | ",
            node.Children.Select(child => child.ShortTypeName + ":" + child.DisplayName));
    }

    /// <summary>Замечания правки в виде строк — отчёт обязан быть сериализуемым.</summary>
    private static List<string> IssueTexts(RPA.Chat.Services.Workflow.WorkflowEditResult result)
    {
        return result == null
            ? new List<string>()
            : result.Issues.Select(issue => issue.ToString()).ToList();
    }

    /// <summary>
    /// Сценарий «текстовая правка XAML» (карточка <c>AG-07</c>) на живом конструкторе: инструмент
    /// <c>editor</c> обязан отказать файлу, открытому вкладкой конструктора, и нормализовать +
    /// провалидировать файл, который в конструкторе не открыт. Гейт валидации — настоящий студийный,
    /// а не null-object: иначе тест доказывал бы работу заглушки.
    /// </summary>
    public AiRpaStudio.Tests.Workflow.DesignerBenchXamlReport WorkflowXamlScenario(string openPath, string unopenedPath)
    {
        DesignerTestHost host = DesignerTestHost.Shared;

        return host.Invoke(() =>
        {
            var designerHost = new BenchDesignerHost();
            var report = new AiRpaStudio.Tests.Workflow.DesignerBenchXamlReport { OpenPath = openPath, UnopenedPath = unopenedPath };

            try
            {
                var editing = new StudioWorkflowEditingService(
                    designerHost,
                    designer => new DispatcherDesignerUiThread(host.Dispatcher));

                RPA.Chat.Services.ChatEnvironment.WorkflowEditing = editing;
                RPA.Chat.Services.ChatEnvironment.WorkflowXamlGate = StudioWorkflowXamlGate.Default;

                DesignerTab tab = designerHost.Open(openPath);
                designerHost.Activate(tab);
                host.Pump(TimeSpan.FromMilliseconds(200));

                report.OpenInDesigner = editing.IsOpenInDesigner(openPath);

                var factory = new RPA.Chat.Services.AgentTools.AgentToolFactory(
                    new Agent.Core.Services.StubAiProvider(),
                    () => System.IO.Path.GetDirectoryName(openPath),
                    RPA.Chat.Services.NullStudioDocumentService.Instance,
                    new SilentWorkflowXamlBuildFeedback(),
                    (question, options, ct) => System.Threading.Tasks.Task.FromResult("answer"),
                    (systemPrompt, task, ct) => System.Threading.Tasks.Task.FromResult("result"),
                    enableSubagents: false,
                    subagentMaxDepth: 2);

                Agent.Core.AgentTools.AgentToolRegistry registry = factory.CreateRegistry();

                // 1. Файл открыт конструктором: текстовая запись отклоняется, файл на диске не меняется.
                string before = System.IO.File.ReadAllText(openPath);
                report.RefusedMessage = RunWorkflowXamlTool(registry, "editor", new Newtonsoft.Json.Linq.JObject
                {
                    ["path"] = openPath,
                    ["content"] = before
                });
                report.OpenFileUnchanged = string.Equals(System.IO.File.ReadAllText(openPath), before, StringComparison.Ordinal);

                // 2. Файл не открыт: запись проходит, нормализация и живая валидация видны в ответе.
                string unopened = System.IO.File.ReadAllText(unopenedPath);
                report.WriteMessage = RunWorkflowXamlTool(registry, "editor", new Newtonsoft.Json.Linq.JObject
                {
                    ["path"] = unopenedPath,
                    ["content"] = unopened
                });
                report.WrittenXaml = System.IO.File.ReadAllText(unopenedPath);
            }
            catch (Exception ex)
            {
                report.Errors.Add(ex.ToString());
            }

            report.Ok = report.Errors.Count == 0;
            return report;
        });
    }

    private static string RunWorkflowXamlTool(
        Agent.Core.AgentTools.AgentToolRegistry registry,
        string name,
        Newtonsoft.Json.Linq.JObject arguments)
    {
        Agent.Core.AgentTools.IAgentTool tool = registry.Find(name);
        if (tool == null)
        {
            return "(no tool " + name + ")";
        }

        return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Сборка не нужна сценарию: инструменты XAML её не вызывают.</summary>
    private sealed class SilentWorkflowXamlBuildFeedback : Agent.Core.Contracts.IBuildFeedbackService
    {
        public Task BuildAsync(string configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<Agent.Core.Models.BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Agent.Core.Models.BuildError>>(new List<Agent.Core.Models.BuildError>());

        public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    public override object InitializeLifetimeService()
    {
        return null;
    }
}

/// <summary>Сериализуемые данные о среде стенда.</summary>
[Serializable]
public sealed class DesignerBenchHostInfo
{
    public string ApartmentState { get; set; }

    public int HostThreadId { get; set; }

    public int KnownHostThreadId { get; set; }

    public int CallerThreadId { get; set; }

    public bool IsAlive { get; set; }
}

/// <summary>Сериализуемый отчёт пробы отрисовки (см. <c>DesignerRenderReport</c>).</summary>
[Serializable]
public sealed class DesignerBenchReport
{
    public string FilePath { get; set; }

    public bool LoadSucceeded { get; set; }

    public string LoadError { get; set; }

    public bool SurfaceReady { get; set; }

    public List<string> RenderedActivities { get; set; } = new List<string>();

    public List<string> UnrecognizedActivities { get; set; } = new List<string>();

    public List<string> ValidationErrors { get; set; } = new List<string>();

    public List<string> ValidationWarnings { get; set; } = new List<string>();

    public List<string> DesignerValidationErrors { get; set; } = new List<string>();

    public List<string> ModalWindows { get; set; } = new List<string>();

    public List<string> Hints { get; set; } = new List<string>();

    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description { get; set; }

    internal static DesignerBenchReport From(DesignerRenderReport report)
    {
        return new DesignerBenchReport
        {
            FilePath = report.FilePath,
            LoadSucceeded = report.LoadSucceeded,
            LoadError = report.LoadError,
            SurfaceReady = report.SurfaceReady,
            RenderedActivities = new List<string>(report.RenderedActivities),
            UnrecognizedActivities = new List<string>(report.UnrecognizedActivities),
            ValidationErrors = new List<string>(report.ValidationErrors),
            ValidationWarnings = new List<string>(report.ValidationWarnings),
            DesignerValidationErrors = new List<string>(report.DesignerValidationErrors),
            ModalWindows = new List<string>(report.ModalWindows),
            Hints = new List<string>(report.Hints),
            Ok = report.Ok,
            Description = report.Describe()
        };
    }
}

/// <summary>
/// Сериализуемый отчёт сценария открытия вкладки (<c>DesignerOpenRealDesignerTests</c>):
/// каждый флаг — отдельный пункт контракта п. 1.3 плана.
/// </summary>
[Serializable]
public sealed class DesignerBenchOpenReport
{
    public string FilePath { get; set; }

    public string WorkflowId { get; set; }

    /// <summary>Файл открылся сервисом в настоящем конструкторе.</summary>
    public bool FirstOk { get; set; }

    /// <summary>Вкладка стала активной.</summary>
    public bool Activated { get; set; }

    /// <summary>Активностей в дереве дизайнера: ноль означал бы «пустую» диаграмму.</summary>
    public int ActivityCount { get; set; }

    /// <summary>Диагностика подсчёта активностей (тип корня модели дизайнера).</summary>
    public string ActivityCountInfo { get; set; }

    /// <summary>Повторный вызов сервиса прошёл успешно.</summary>
    public bool SecondOk { get; set; }

    /// <summary>Повторный вызов узнал уже открытую вкладку.</summary>
    public bool AlreadyOpenOnSecondCall { get; set; }

    /// <summary>Повторный вызов активировал существующую вкладку.</summary>
    public bool SecondActivated { get; set; }

    /// <summary>Сколько конструкторов создано (обязано быть ровно один).</summary>
    public int OpenCount { get; set; }

    public int CloseCount { get; set; }

    /// <summary>Перезапись открытого файла запрещена.</summary>
    public bool WriteBlocked { get; set; }

    public string WriteBlockedError { get; set; }

    /// <summary>Генерация в открытый файл отклонена (обязано быть <c>false</c>).</summary>
    public bool RegenerationOk { get; set; }

    public List<string> RegenerationErrors { get; set; } = new List<string>();

    public List<string> Errors { get; set; } = new List<string>();

    /// <summary>Все пункты контракта выполнены.</summary>
    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"файл: {FilePath ?? "(нет)"}");
            text.AppendLine($"workflowId: {WorkflowId ?? "(не определён)"}");
            text.AppendLine($"открытие: {FirstOk}, активно: {Activated}, активностей: {ActivityCount}");
            text.AppendLine($"повторное открытие: {SecondOk}, уже открыт: {AlreadyOpenOnSecondCall}, активно: {SecondActivated}");
            text.AppendLine($"конструкторов создано: {OpenCount}, закрыто: {CloseCount}");
            text.AppendLine($"перезапись запрещена: {WriteBlocked} ({WriteBlockedError ?? "нет ошибки"})");
            text.AppendLine($"генерация в открытый файл: {(RegenerationOk ? "разрешена (ошибка!)" : "отклонена")}");

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка: " + error);
            }

            foreach (string error in RegenerationErrors)
            {
                text.AppendLine("  отказ генерации: " + error);
            }

            return text.ToString();
        }
    }
}

/// <summary>
/// Сериализуемый отчёт сценария моста правок (<c>WorkflowEditBridgeTests</c>, карточка <c>AG-02</c>):
/// каждый флаг — отдельный пункт контракта «агент правит открытый процесс, отмена штатная».
/// </summary>
[Serializable]
public sealed class DesignerBenchEditReport
{
    public string FilePath { get; set; }

    /// <summary>Файл, который не открывается вкладкой (проверка структурированного отказа).</summary>
    public string UnopenedPath { get; set; }

    /// <summary>До открытия вкладки служба обязана считать файл неоткрытым.</summary>
    public bool OpenBeforeAnyTab { get; set; }

    public bool OpenInDesigner { get; set; }

    public int ActivityCountBefore { get; set; }

    public bool SessionActive { get; set; }

    /// <summary>Причина неактивной пачки (если она не открылась).</summary>
    public string InactiveReason { get; set; }

    public bool InsertStartOk { get; set; }

    public string InsertStartId { get; set; }

    public List<string> InsertStartErrors { get; set; } = new List<string>();

    public bool InsertMiddleOk { get; set; }

    public string InsertMiddleId { get; set; }

    public bool InsertEndOk { get; set; }

    public string InsertEndId { get; set; }

    /// <summary>Пачка ещё открыта до <c>complete</c>.</summary>
    public bool BatchActiveDuringEdits { get; set; }

    public int ActivityCountDuringBatch { get; set; }

    public string OrderDuringBatch { get; set; }

    public bool CompleteOk { get; set; }

    public bool SessionActiveAfterComplete { get; set; }

    /// <summary>После <c>complete</c> в конструкторе есть что отменить.</summary>
    public bool CanUndoAfterComplete { get; set; }

    public int ActivityCountAfterComplete { get; set; }

    /// <summary>Структура несохранённого документа (правки ещё не в файле).</summary>
    public int LiveOutlineBeforeSave { get; set; }

    public bool UndoOk { get; set; }

    public bool CanRedoAfterUndo { get; set; }

    public int ActivityCountAfterUndo { get; set; }

    public bool RedoOk { get; set; }

    public int ActivityCountAfterRedo { get; set; }

    public int ActivityCountBeforeRevert { get; set; }

    /// <summary>Обе вставки пачки, которую затем отменяют целиком, применились.</summary>
    public bool RevertInsertOk { get; set; }

    public int ActivityCountDuringRevertBatch { get; set; }

    public bool RevertOk { get; set; }

    public bool RevertSessionActive { get; set; }

    public List<string> RevertErrors { get; set; } = new List<string>();

    public int ActivityCountAfterRevert { get; set; }

    public int ActivityCountBeforeFailure { get; set; }

    public bool FailedInsertOk { get; set; }

    public List<string> FailedInsertErrors { get; set; } = new List<string>();

    public bool SessionActiveAfterFailure { get; set; }

    public int ActivityCountAfterFailure { get; set; }

    public string DeletedId { get; set; }

    public bool DeleteOk { get; set; }

    public List<string> DeleteErrors { get; set; } = new List<string>();

    public int ActivityCountAfterDelete { get; set; }

    public string MovedId { get; set; }

    public bool MoveOk { get; set; }

    public string MoveNewId { get; set; }

    public List<string> MoveErrors { get; set; } = new List<string>();

    public string OrderBeforeMove { get; set; }

    public string OrderAfterMove { get; set; }

    /// <summary>Удаление несуществующего адреса — отказ, а не исключение.</summary>
    public bool DeleteMissingOk { get; set; }

    public bool SecondBatchCompleteOk { get; set; }

    public int ActivityCountAfterSecondBatch { get; set; }

    public bool NotOpenSessionActive { get; set; }

    public string NotOpenReason { get; set; }

    public bool NotOpenInsertOk { get; set; }

    public List<string> NotOpenInsertErrors { get; set; } = new List<string>();

    /// <summary>Вставка активности без обязательного аргумента (<c>Assign</c> без <c>To</c>) — материал для валидации.</summary>
    public bool ValidateInsertOk { get; set; }

    public string ValidateInsertId { get; set; }

    public bool ValidateInsertCompleteOk { get; set; }

    /// <summary>Валидация WF выполнена на живой модели конструктора (AG-06).</summary>
    public bool ValidateAvailable { get; set; }

    public string ValidateSource { get; set; }

    public int ValidateErrors { get; set; }

    /// <summary>Адреса замечаний валидации: те же, что отдаёт workflow_outline.</summary>
    public List<string> ValidateAddresses { get; set; } = new List<string>();

    /// <summary>Глубина истории отмены до и после валидации: чтение не должно менять модель.</summary>
    public int ValidateUndoDepthBefore { get; set; }

    public int ValidateUndoDepthAfter { get; set; }

    public string ValidateText { get; set; }

    public List<string> Errors { get; set; } = new List<string>();

    /// <summary>Все пункты контракта выполнены.</summary>
    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"файл: {FilePath ?? "(нет)"}");
            text.AppendLine($"открыт в конструкторе: {OpenInDesigner} (до открытия вкладки: {OpenBeforeAnyTab})");
            text.AppendLine($"пачка открыта: {SessionActive}, причина: {InactiveReason ?? "нет"}");
            text.AppendLine($"вставки: начало {InsertStartOk} ({InsertStartId ?? "-"}), середина {InsertMiddleOk} ({InsertMiddleId ?? "-"}), конец {InsertEndOk} ({InsertEndId ?? "-"})");
            text.AppendLine($"активностей: было {ActivityCountBefore}, в пачке {ActivityCountDuringBatch}, после complete {ActivityCountAfterComplete}, в живой модели {LiveOutlineBeforeSave}");
            text.AppendLine($"порядок в пачке: {OrderDuringBatch ?? "(нет)"}");
            text.AppendLine($"complete: {CompleteOk}, пачка открыта после: {SessionActiveAfterComplete}, можно отменить: {CanUndoAfterComplete}");
            text.AppendLine($"undo: {UndoOk} (активностей {ActivityCountAfterUndo}, можно вернуть: {CanRedoAfterUndo}), redo: {RedoOk} (активностей {ActivityCountAfterRedo})");
            text.AppendLine($"откат пачки: вставки {RevertInsertOk}, активностей в пачке {ActivityCountDuringRevertBatch} (до {ActivityCountBeforeRevert}), revert {RevertOk}, после {ActivityCountAfterRevert}");
            text.AppendLine($"ошибка в пачке: вставка {FailedInsertOk}, активностей до {ActivityCountBeforeFailure} / после {ActivityCountAfterFailure}");
            text.AppendLine($"удаление: {DeleteOk} ({DeletedId ?? "-"}), активностей {ActivityCountAfterDelete}, несуществующий адрес: {DeleteMissingOk}");
            text.AppendLine($"перенос: {MoveOk} ({MovedId ?? "-"} -> {MoveNewId ?? "-"})");
            text.AppendLine($"порядок до переноса: {OrderBeforeMove ?? "(нет)"}");
            text.AppendLine($"порядок после переноса: {OrderAfterMove ?? "(нет)"}");
            text.AppendLine($"вторая пачка: complete {SecondBatchCompleteOk}, активностей {ActivityCountAfterSecondBatch}");
            text.AppendLine($"не открыт в конструкторе: пачка {NotOpenSessionActive}, вставка {NotOpenInsertOk}, причина: {NotOpenReason ?? "нет"}");
            text.AppendLine($"валидация: вставка {ValidateInsertOk} ({ValidateInsertId ?? "-"}), выполнена {ValidateAvailable}, источник {ValidateSource ?? "-"}, ошибок {ValidateErrors}");
            text.AppendLine($"адреса замечаний: {(ValidateAddresses == null || ValidateAddresses.Count == 0 ? "(нет)" : string.Join(", ", ValidateAddresses))}");
            text.AppendLine($"история отмены при валидации: до {ValidateUndoDepthBefore}, после {ValidateUndoDepthAfter}");

            Append(text, "ошибка вставки", InsertStartErrors);
            Append(text, "ошибка вставки в несуществующий родитель", FailedInsertErrors);
            Append(text, "ошибка удаления", DeleteErrors);
            Append(text, "ошибка переноса", MoveErrors);
            Append(text, "ошибка вставки в неоткрытый файл", NotOpenInsertErrors);
            Append(text, "ошибка сценария", Errors);

            return text.ToString();
        }
    }

    private static void Append(System.Text.StringBuilder text, string title, List<string> values)
    {
        if (values == null)
        {
            return;
        }

        foreach (string value in values)
        {
            text.AppendLine("  " + title + ": " + value);
        }
    }
}

/// <summary>
/// Сериализуемый отчёт сценария обёртки (карточка <c>AG-04</c>): каждый флаг — отдельный пункт контракта
/// (обёртка заняла место элемента, элемент внутри, адреса совпали с outline, ошибочные запросы отклонены,
/// <c>undo</c> вернул структуру).
/// </summary>
[Serializable]
public sealed class DesignerBenchWrapReport
{
    public string FilePath { get; set; }

    public bool OpenInDesigner { get; set; }

    public int ActivityCountBefore { get; set; }

    public bool SessionActive { get; set; }

    public string InactiveReason { get; set; }

    public bool WrapInSequenceOk { get; set; }

    public string WrapInSequenceWrapperId { get; set; }

    /// <summary>Адрес самой обёртки (первый в списке адресов ответа).</summary>
    public string WrapInSequenceWrapperAddress { get; set; }

    public List<string> WrapInSequenceErrors { get; set; } = new List<string>();

    public int ActivityCountAfterSequenceWrap { get; set; }

    public string OrderAfterSequenceWrap { get; set; }

    /// <summary>Дети обёртки: обёрнутая активность обязана быть внутри.</summary>
    public string FirstChildOfWrapper { get; set; }

    public bool WrapWithoutContainerOk { get; set; }

    public List<string> WrapWithoutContainerErrors { get; set; } = new List<string>();

    public bool WrapInIfThenOk { get; set; }

    public string WrapInIfWrapperId { get; set; }

    /// <summary>Адрес обёртки If.</summary>
    public string WrapInIfWrapperAddress { get; set; }

    public List<string> WrapInIfThenErrors { get; set; } = new List<string>();

    public string OrderAfterIfWrap { get; set; }

    /// <summary>Дети If: обёрнутая активность лежит в <c>Then</c>.</summary>
    public string ThenChild { get; set; }

    public bool WrapUnknownTypeOk { get; set; }

    public List<string> WrapUnknownTypeErrors { get; set; } = new List<string>();

    public bool CompleteOk { get; set; }

    public int ActivityCountAfterComplete { get; set; }

    public bool UndoOk { get; set; }

    public int ActivityCountAfterUndo { get; set; }

    public string OrderAfterUndo { get; set; }

    public List<string> Errors { get; set; } = new List<string>();

    /// <summary>Все пункты контракта выполнены.</summary>
    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"файл: {FilePath ?? "(нет)"}");
            text.AppendLine($"открыт в конструкторе: {OpenInDesigner}, пачка открыта: {SessionActive} ({InactiveReason ?? "нет"})");
            text.AppendLine($"обёртка в Sequence: {WrapInSequenceOk} ({WrapInSequenceWrapperId ?? "-"}), активностей {ActivityCountBefore} -> {ActivityCountAfterSequenceWrap}");
            text.AppendLine($"порядок после обёртки: {OrderAfterSequenceWrap ?? "(нет)"}");
            text.AppendLine($"дети обёртки: {FirstChildOfWrapper ?? "(нет)"}");
            text.AppendLine($"If без container: {WrapWithoutContainerOk}");
            text.AppendLine($"обёртка в If.Then: {WrapInIfThenOk} ({WrapInIfWrapperId ?? "-"})");
            text.AppendLine($"порядок после If: {OrderAfterIfWrap ?? "(нет)"}");
            text.AppendLine($"дети If: {ThenChild ?? "(нет)"}");
            text.AppendLine($"неизвестная обёртка: {WrapUnknownTypeOk}");
            text.AppendLine($"complete: {CompleteOk}, активностей {ActivityCountAfterComplete}");
            text.AppendLine($"undo: {UndoOk}, активностей {ActivityCountAfterUndo}");
            text.AppendLine($"порядок после undo: {OrderAfterUndo ?? "(нет)"}");

            Append(text, "ошибка обёртки в Sequence", WrapInSequenceErrors);
            Append(text, "ошибка обёртки без container", WrapWithoutContainerErrors);
            Append(text, "ошибка обёртки в If.Then", WrapInIfThenErrors);
            Append(text, "ошибка неизвестной обёртки", WrapUnknownTypeErrors);
            Append(text, "ошибка сценария", Errors);

            return text.ToString();
        }
    }

    private static void Append(System.Text.StringBuilder text, string title, List<string> values)
    {
        if (values == null)
        {
            return;
        }

        foreach (string value in values)
        {
            text.AppendLine("  " + title + ": " + value);
        }
    }
}

/// <summary>
/// Сериализуемый отчёт сценария палитры (<c>PaletteDesignerTests</c>): каждый флаг — отдельный пункт
/// контракта п. 1.3 плана (вкладка открыта и активна, активность в дереве, гейт без ошибок).
/// </summary>
[Serializable]
public sealed class DesignerBenchPaletteReport
{
    public string FilePath { get; set; }

    public string WorkflowId { get; set; }

    /// <summary>Вставка активности через <c>ModelService</c> прошла и гейт её принял.</summary>
    public bool AddOk { get; set; }

    /// <summary>Идентификатор добавленной активности (путь в дереве).</summary>
    public string ActivityId { get; set; }

    public int ActivityCountBefore { get; set; }

    public int ActivityCountAfterAdd { get; set; }

    /// <summary>Активностей в дереве живого дизайнера (ноль означал бы «пустую» диаграмму).</summary>
    public int ActivityCountInDesigner { get; set; }

    /// <summary>Активностей в дереве модели конструктора (ModelItem) — то, что видит пользователь.</summary>
    public int ModelActivityCount { get; set; }

    public string DesignerInfo { get; set; }

    /// <summary>Вкладка конструктора открыта.</summary>
    public bool TabOpen { get; set; }

    /// <summary>Вкладка активна.</summary>
    public bool TabActive { get; set; }

    public bool SetArgumentOk { get; set; }

    /// <summary>Новое выражение аргумента попало в сохранённый файл процесса.</summary>
    public bool ExpressionInFile { get; set; }

    public List<string> Errors { get; set; } = new List<string>();

    public List<string> AddErrors { get; set; } = new List<string>();

    public List<string> AddHints { get; set; } = new List<string>();

    public List<string> SetArgumentErrors { get; set; } = new List<string>();

    /// <summary>Все пункты контракта выполнены.</summary>
    public bool Ok { get; set; }

    /// <summary>Текст отчёта для сообщения об ошибке теста.</summary>
    public string Description
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"файл: {FilePath ?? "(нет)"}");
            text.AppendLine($"workflowId: {WorkflowId ?? "(не определён)"}");
            text.AppendLine($"вставка: {AddOk}, активность: {ActivityId ?? "(нет)"}");
            text.AppendLine($"активностей было: {ActivityCountBefore}, стало: {ActivityCountAfterAdd}, в дереве: {ActivityCountInDesigner}");
            text.AppendLine($"дерево дизайнера: {DesignerInfo}");
            text.AppendLine($"вкладка открыта: {TabOpen}, активна: {TabActive}");
            text.AppendLine($"set_argument: {SetArgumentOk}, выражение в файле: {ExpressionInFile}");

            foreach (string error in AddErrors)
            {
                text.AppendLine("  ошибка вставки: " + error);
            }

            foreach (string error in SetArgumentErrors)
            {
                text.AppendLine("  ошибка set_argument: " + error);
            }

            foreach (string hint in AddHints)
            {
                text.AppendLine("  подсказка: " + hint);
            }

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка сценария: " + error);
            }

            return text.ToString();
        }
    }
}


/// <summary>
/// Отчёт сценария аргументов и переменных (<c>WorkflowArgumentBridgeTests</c>, карточка <c>AG-05</c>):
/// каждый флаг — отдельный пункт контракта «агент задаёт аргументы и переменные в живой модели».
/// </summary>
[Serializable]
public sealed class DesignerBenchArgumentReport
{
    public string FilePath { get; set; }

    public bool OpenInDesigner { get; set; }

    public bool SessionActive { get; set; }

    public string InactiveReason { get; set; }

    public bool LiteralOk { get; set; }

    public string LiteralMessage { get; set; }

    public List<string> LiteralErrors { get; set; } = new List<string>();

    /// <summary>Значение аргумента в живой структуре после литерала.</summary>
    public string TextAfterLiteral { get; set; }

    public bool ExpressionOk { get; set; }

    public string ExpressionMessage { get; set; }

    public List<string> ExpressionErrors { get; set; } = new List<string>();

    public string TextAfterExpression { get; set; }

    public bool UnknownArgumentOk { get; set; }

    public List<string> UnknownArgumentErrors { get; set; } = new List<string>();

    public bool BothOk { get; set; }

    public List<string> BothErrors { get; set; } = new List<string>();

    public bool BrokenExpressionOk { get; set; }

    public List<string> BrokenExpressionErrors { get; set; } = new List<string>();

    public bool VariableAddOk { get; set; }

    public string VariableAddMessage { get; set; }

    public List<string> VariableAddErrors { get; set; } = new List<string>();

    public List<string> VariablesAfterAdd { get; set; } = new List<string>();

    public bool DuplicateVariableOk { get; set; }

    public List<string> DuplicateVariableErrors { get; set; } = new List<string>();

    public bool UnknownTypeOk { get; set; }

    public List<string> UnknownTypeErrors { get; set; } = new List<string>();

    public bool BadValueOk { get; set; }

    public List<string> BadValueErrors { get; set; } = new List<string>();

    public bool NoVariablesOk { get; set; }

    public List<string> NoVariablesErrors { get; set; } = new List<string>();

    public bool VariableRemoveOk { get; set; }

    public List<string> VariableRemoveErrors { get; set; } = new List<string>();

    public List<string> VariablesAfterRemove { get; set; } = new List<string>();

    public bool RemoveMissingOk { get; set; }

    public List<string> RemoveMissingErrors { get; set; } = new List<string>();

    public bool CompleteOk { get; set; }

    public bool UndoOk { get; set; }

    public string TextAfterUndo { get; set; }

    public bool Ok { get; set; }

    public List<string> Errors { get; set; } = new List<string>();

    public string Description
    {
        get
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"файл: {FilePath ?? "(нет)"}, открыт вкладкой: {OpenInDesigner}, пачка: {SessionActive}");
            text.AppendLine($"литерал: {LiteralOk} → «{TextAfterLiteral}» ({LiteralMessage})");
            text.AppendLine($"выражение: {ExpressionOk} → «{TextAfterExpression}» ({ExpressionMessage})");
            text.AppendLine($"неизвестный аргумент отклонён: {!UnknownArgumentOk}; value+expression отклонены: {!BothOk}; битое выражение отклонено: {!BrokenExpressionOk}");
            text.AppendLine($"переменные после добавления: [{string.Join(", ", VariablesAfterAdd)}] ({VariableAddMessage})");
            text.AppendLine($"дубль отклонён: {!DuplicateVariableOk}; неизвестный тип отклонён: {!UnknownTypeOk}; неверное значение отклонено: {!BadValueOk}; без Variables отклонено: {!NoVariablesOk}");
            text.AppendLine($"переменные после удаления: [{string.Join(", ", VariablesAfterRemove)}]; повторное удаление отклонено: {!RemoveMissingOk}");
            text.AppendLine($"complete: {CompleteOk}, undo: {UndoOk}, значение после undo: «{TextAfterUndo}»");

            foreach (string error in LiteralErrors)
            {
                text.AppendLine("  ошибка литерала: " + error);
            }

            foreach (string error in ExpressionErrors)
            {
                text.AppendLine("  ошибка выражения: " + error);
            }

            foreach (string error in UnknownArgumentErrors)
            {
                text.AppendLine("  ошибка неизвестного аргумента: " + error);
            }

            foreach (string error in BothErrors)
            {
                text.AppendLine("  ошибка value+expression: " + error);
            }

            foreach (string error in BrokenExpressionErrors)
            {
                text.AppendLine("  ошибка битого выражения: " + error);
            }

            foreach (string error in VariableAddErrors)
            {
                text.AppendLine("  ошибка добавления переменной: " + error);
            }

            foreach (string error in DuplicateVariableErrors)
            {
                text.AppendLine("  ошибка дубля переменной: " + error);
            }

            foreach (string error in UnknownTypeErrors)
            {
                text.AppendLine("  ошибка типа переменной: " + error);
            }

            foreach (string error in BadValueErrors)
            {
                text.AppendLine("  ошибка значения переменной: " + error);
            }

            foreach (string error in NoVariablesErrors)
            {
                text.AppendLine("  ошибка «нет Variables»: " + error);
            }

            foreach (string error in VariableRemoveErrors)
            {
                text.AppendLine("  ошибка удаления переменной: " + error);
            }

            foreach (string error in RemoveMissingErrors)
            {
                text.AppendLine("  ошибка повторного удаления: " + error);
            }

            foreach (string error in Errors)
            {
                text.AppendLine("  ошибка сценария: " + error);
            }

            return text.ToString();
        }
    }
}
