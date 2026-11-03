using System;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Validation;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.CSharp.Activities;
using Newtonsoft.Json;
using RPA.Core;
using RPA.CSharpExpressionEditor;

namespace RPA.Helpers
{
    internal class LoadActivityToDesignerHelper
    {
        /// <summary>
        /// Creates a new Workflow Designer instance with C# Expression Editor
        /// </summary>
        /// <param name="sourceFile">Workflow FileName</param>
        internal static WorkflowDesignerEx LoadActivityToDesigner(string sourceFile)
        {
            if (!sourceFile.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(sourceFile))
            {
                return null;
            }

            // проверяем не загружена ли уже активность в какой-либо дизайнер
            WorkflowDesignerEx workflowDesignerEx = GetDesignerByFile(sourceFile);

            if (workflowDesignerEx == null)
            {
                ExpressionTextBox.RegisterExpressionActivityEditor(new CSharpValue<string>().Language, typeof(RoslynExpressionEditor), CSharpExpressionHelper.CreateExpressionFromString);

                workflowDesignerEx = new WorkflowDesignerEx();
                workflowDesignerEx.Context.Services.GetService<DesignerConfigurationService>().TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
                workflowDesignerEx.Context.Services.GetService<DesignerConfigurationService>().LoadingFromUntrustedSourceEnabled = true;
                workflowDesignerEx.Context.Services.Publish<IExpressionEditorService>(new RoslynExpressionEditorService());
                workflowDesignerEx.Context.Services.Publish<IValidationErrorService>(new DebugValidationErrorService());
                //load Workflow Xaml
                workflowDesignerEx.Load(sourceFile);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                LogDesignerViewState(workflowDesignerEx?.View as DependencyObject, "1/afterLoad");

                CustomWfDesigner.GetInstance().WorkflowDesigners.Add(workflowDesignerEx);
                CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = workflowDesignerEx;

                LogDesignerViewState(workflowDesignerEx?.View as DependencyObject, "2/afterAddedToWorkflowDesigners");

                ScheduleDesignerLayoutProbe(workflowDesignerEx, "2/afterAddedToWorkflowDesigners");

                CommandService commandService = new CommandService(workflowDesignerEx);
                workflowDesignerEx.Context.Services.Publish<ICommandService>(commandService);
                try
                {
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(workflowDesignerEx);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ErrorText.Of(ex));
                }
            }

            SetCurrentWorkflowFile(workflowDesignerEx);
            LoadBreakpoints(workflowDesignerEx, sourceFile);
            LogDesignerViewState(workflowDesignerEx?.View as DependencyObject, "3/beforeSetVariablesStyle");
            SetVariablesStyle(workflowDesignerEx);
            LogDesignerViewState(workflowDesignerEx?.View as DependencyObject, "3/afterSetVariablesStyle");

            // Инициализировать отслеживание VariableDesigner
            workflowDesignerEx.DesignerVariableContextMenuService.InitializeVariableDesignerTracking();

            // Добавить ссылку на netstandard.dll
            //EnsureNetstandardReference(workflowDesignerEx);

            // Сохранение после загрузки (нормализация XAML) не должно ломать открытие воркфлоу:
            // файл может быть занят другим процессом (OneDrive, антивирус, индексатор, вторая копия
            // студии, ранее открытый StreamReader) — раньше необработанный IOException из Save
            // всплывал наружу и ронял отладчик в CustomWfDesigner.WorkflowOpen().
            SaveAfterLoad(workflowDesignerEx, sourceFile);

            return workflowDesignerEx;
        }

        /// <summary>
        /// Сохраняет воркфлоу сразу после загрузки (нормализация XAML в дизайнере).
        /// Кратковременная блокировка файла (антивирус, индексатор, OneDrive) переживается повторами,
        /// длительная — только предупреждением в лог: дизайнер всё равно должен открыться, а не падать
        /// с <see cref="IOException"/> наружу. Ранее необработанное исключение из Save останавливало
        /// отладчик в <see cref="CustomWfDesigner.WorkflowOpen"/>.
        /// </summary>
        private static void SaveAfterLoad(WorkflowDesigner designer, string sourceFile)
        {
            const int attempts = 3;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    designer.Save(sourceFile);
                    return;
                }
                catch (Exception ex) when (!(ex is IOException) && !(ex is UnauthorizedAccessException))
                {
                    throw;
                }
                catch (Exception) when (attempt < attempts)
                {
                    // файл освободится через мгновение — ждём и пробуем снова
                    System.Threading.Thread.Sleep(100 * attempt);
                }
                catch (Exception ex)
                {
                    CustomWfDesigner.GetInstance().Log(
                        $"Воркфлоу {sourceFile} загружен, но не пересохранён: файл занят другим процессом или доступен только для чтения — {ErrorText.Of(ex)}",
                        (int)RpaCommon.Enums.LogLevel.Warn);
                }
            }
        }



        /// <summary>
        /// Диагностика поверхности дизайнера: отличает «визуальное дерево ещё не построено» от
        /// «элемента нет вовсе». Выключается флагом <see cref="EnableDesignerDiagnostics"/>;
        /// после подтверждения причины её нужно удалить вместе с вызовами.
        /// </summary>
        private const bool EnableDesignerDiagnostics = true;

        /// <summary>Имя shell bar в шаблоне <c>DesignerView</c>.</summary>
        private const string ShellBarElementName = WfDesignerTheme.ShellBarElementName;

        /// <summary>
        /// Пишет в лог состояние поверхности дизайнера на заданном этапе: построено ли визуальное дерево,
        /// есть ли <c>xnDesignerView</c>/<c>shellBar</c>, виден ли shell bar. Исключений не бросает.
        /// </summary>
        internal static void LogDesignerViewState(DependencyObject view, string stage)
        {
            if (!EnableDesignerDiagnostics)
            {
                return;
            }

            try
            {
                int visualChildren = view is Visual ? VisualTreeHelper.GetChildrenCount(view) : -1;
                int logicalChildren = view == null ? -1 : LogicalTreeHelper.GetChildren(view).Cast<object>().Count();
                bool isLoaded = view is FrameworkElement frameworkElement && frameworkElement.IsLoaded;
                bool hasDesignerView = WfDesignerTheme.FindByName(view, WfDesignerTheme.DesignerViewElementName) != null;
                bool hasShellBar = WfDesignerTheme.FindByName(view, ShellBarElementName) != null;
                string shellBarVisibility = (view as DesignerView)?.WorkflowShellBarItemVisibility.ToString() ?? "n/a";

                CustomWfDesigner.GetInstance().Log(
                    $"[diag] {stage}: view={view?.GetType().Name ?? "null"} visualChildren={visualChildren} " +
                    $"logicalChildren={logicalChildren} IsLoaded={isLoaded} " +
                    $"{WfDesignerTheme.DesignerViewElementName}={hasDesignerView} {ShellBarElementName}={hasShellBar} " +
                    $"ShellBarItemVisibility={shellBarVisibility}",
                    (int)RpaCommon.Enums.LogLevel.Trace);
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"[diag] {stage}: сбой диагностики — {ErrorText.Of(ex)}", (int)RpaCommon.Enums.LogLevel.Trace);
            }
        }


        /// <summary>
        /// Диагностика хлебных крошек: ищет «белый пиксель» на hairline между крошками и дизайнером —
        /// перечисляет подозрительные элементы (размер ≤ 2px, светлая или не-solid кисть) вместе с их
        /// кистями, размерами, рамками и отступами, цепочку родителей и попадания хит-теста в правом
        /// нижнем углу последней крошки. Исключений не бросает.
        /// После подтверждения причины метод удаляется вместе с вызовами.
        /// </summary>
        internal static void LogBreadCrumbArtifacts(DependencyObject view, string stage)
        {
            if (!EnableDesignerDiagnostics || view == null)
            {
                return;
            }

            try
            {
                if (!(WfDesignerTheme.FindByName(view, WfDesignerTheme.BreadCrumbElementName) is ListBox breadCrumb))
                {
                    LogDiag($"{stage}: {WfDesignerTheme.BreadCrumbElementName} не найден");
                    return;
                }

                LogDiag($"{stage}: крошек {breadCrumb.Items.Count}, size={breadCrumb.ActualWidth:0.#}x{breadCrumb.ActualHeight:0.#}, " +
                        $"ClipToBounds={breadCrumb.ClipToBounds}, Padding={breadCrumb.Padding}");

                foreach (DependencyObject element in XamlHelper.FindVisualChildren<DependencyObject>(breadCrumb))
                {
                    if (IsSuspiciousArtifact(element))
                    {
                        LogDiag($"{stage}: подозрительный элемент {DescribeElement(element)}");
                    }
                }

                LogBreadCrumbHitTest(view as UIElement, breadCrumb, stage);
                LogBreadCrumbParents(breadCrumb, stage);

                if (view is FrameworkElement frameworkElement
                    && !stage.EndsWith("/afterLayout", StringComparison.Ordinal))
                {
                    // Раскладка крошек завершается позже применения темы — вторая точка замера.
                    _ = frameworkElement.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => LogBreadCrumbArtifacts(frameworkElement, stage + "/afterLayout")));
                }
            }
            catch (Exception ex)
            {
                LogDiag($"{stage}: сбой диагностики — {ErrorText.Of(ex)}");
            }
        }

        /// <summary>Хит-тест по правому нижнему углу последней крошки: кто рисует пиксель на разделителе.</summary>
        private static void LogBreadCrumbHitTest(UIElement reference, ListBox breadCrumb, string stage)
        {
            if (reference == null || breadCrumb.Items.Count == 0)
            {
                return;
            }

            if (!(breadCrumb.ItemContainerGenerator.ContainerFromIndex(breadCrumb.Items.Count - 1) is FrameworkElement last)
                || last.ActualWidth <= 0
                || last.ActualHeight <= 0)
            {
                LogDiag($"{stage}: контейнер последней крошки ещё не создан — хит-тест невозможен");
                return;
            }

            foreach (Point offset in new[] { new Point(-0.5, -0.5), new Point(-2.5, -0.5), new Point(-0.5, -2.5) })
            {
                Point point = last.TranslatePoint(
                    new Point(last.ActualWidth + offset.X, last.ActualHeight + offset.Y),
                    reference);

                VisualTreeHelper.HitTest(
                    reference,
                    null,
                    result =>
                    {
                        if (result.VisualHit != null)
                        {
                            LogDiag($"{stage}: hit ({point.X:0.#};{point.Y:0.#}) → {DescribeElement(result.VisualHit)}");
                        }

                        return HitTestResultBehavior.Continue;
                    },
                    new PointHitTestParameters(point));
            }
        }

        /// <summary>Цепочка родителей крошки: отличает светлую кисть от просвета до светлого предка.</summary>
        private static void LogBreadCrumbParents(DependencyObject breadCrumb, string stage)
        {
            DependencyObject parent = VisualTreeHelper.GetParent(breadCrumb);

            for (int depth = 0; parent != null && depth < 6; depth++)
            {
                LogDiag($"{stage}: родитель[{depth}] {DescribeElement(parent)}");

                if (!(parent is Visual))
                {
                    return;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        /// <summary>Подозрительный элемент: пиксельный размер, светлая или не-solid кисть в любом свойстве.</summary>
        private static bool IsSuspiciousArtifact(DependencyObject element)
        {
            if (!(element is FrameworkElement framework))
            {
                return false;
            }

            if (framework.ActualWidth > 0
                && framework.ActualHeight > 0
                && (framework.ActualWidth <= 2 || framework.ActualHeight <= 2))
            {
                return true;
            }

            foreach (DependencyProperty property in WfDesignerTheme.BrushPropertiesOf(element.GetType()))
            {
                if (!(element.GetValue(property) is Brush brush) || ReferenceEquals(brush, Brushes.Transparent))
                {
                    continue;
                }

                if (!(brush is SolidColorBrush solid) || PerceivedLuminance(solid.Color) > 0.5)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Элемент одной строкой: тип, имя, размер, отступы, рамка, все кисти.</summary>
        private static string DescribeElement(DependencyObject element)
        {
            if (element == null)
            {
                return "null";
            }

            StringBuilder text = new StringBuilder(element.GetType().Name);

            if (element is FrameworkElement framework)
            {
                if (!string.IsNullOrEmpty(framework.Name))
                {
                    _ = text.Append(" Name=").Append(framework.Name);
                }

                _ = text.Append(" size=").Append(framework.ActualWidth.ToString("0.#"))
                    .Append('x').Append(framework.ActualHeight.ToString("0.#"))
                    .Append(" margin=").Append(framework.Margin)
                    .Append(" clip=").Append(framework.ClipToBounds);

                if (framework is Control control)
                {
                    _ = text.Append(" thickness=").Append(control.BorderThickness).Append(" padding=").Append(control.Padding);
                }
                else if (framework is Border border)
                {
                    _ = text.Append(" thickness=").Append(border.BorderThickness).Append(" padding=").Append(border.Padding);
                }
            }

            foreach (DependencyProperty property in WfDesignerTheme.BrushPropertiesOf(element.GetType()))
            {
                if (element.GetValue(property) is Brush brush && !ReferenceEquals(brush, Brushes.Transparent))
                {
                    _ = text.Append(' ').Append(property.Name).Append('=').Append(DescribeBrush(brush));
                }
            }

            return text.ToString();
        }

        private static string DescribeBrush(Brush brush)
        {
            return brush is SolidColorBrush solid
                ? solid.Color.ToString()
                : brush is GradientBrush gradient
                ? gradient.GetType().Name + "("
                    + string.Join("→", gradient.GradientStops.Select(stop => stop.Color.ToString())) + ")"
                : brush.GetType().Name;
        }

        private static double PerceivedLuminance(Color color)
        {
            return ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255d;
        }

        private static void LogDiag(string message)
        {
            CustomWfDesigner.GetInstance().Log("[diag] " + message, (int)RpaCommon.Enums.LogLevel.Trace);
        }

        /// <summary>Отложенный зонд: состояние поверхности после того, как дизайнер реально прогрузился.</summary>
        private static void ScheduleDesignerLayoutProbe(WorkflowDesignerEx workflowDesignerEx, string stage)
        {
            if (!EnableDesignerDiagnostics || !(workflowDesignerEx?.View is FrameworkElement view))
            {
                return;
            }

            _ = view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                LogDesignerViewState(view, stage + "/afterLoaded")));
        }

        /// <summary>
        /// Оформление shell bar дизайнера. Поверхность <c>DesignerView</c> строится шаблоном только после
        /// того, как вьюха попадёт в визуальное дерево, а это делает <see cref="DocGroupHelper.AddDocument"/>
        /// уже после загрузки файла. Поэтому <c>shellBar</c> при первом проходе может отсутствовать — тогда
        /// оформление откладывается до <see cref="FrameworkElement.Loaded"/>, иначе метод ничего не делает
        /// (раньше здесь падал <c>Enumerable.First</c> и дизайнер не открывался вовсе).
        /// </summary>
        private static void SetVariablesStyle(WorkflowDesignerEx workflowDesignerEx)
        {
            if (!(workflowDesignerEx?.View is FrameworkElement view))
            {
                return;
            }

            if (TryApplyShellBarStyle(view))
            {
                return;
            }

            void onLoaded(object sender, RoutedEventArgs args)
            {
                view.Loaded -= onLoaded;

                if (TryApplyShellBarStyle(view))
                {
                    return;
                }

                CustomWfDesigner.GetInstance().Log(
                    $"[diag] SetVariablesStyle: {ShellBarElementName} отсутствует и после Loaded — оформление не применено",
                    (int)RpaCommon.Enums.LogLevel.Trace);
            }

            view.Loaded += onLoaded;
        }

        /// <summary>
        /// Применяет оформление к shell bar, если он уже есть в дереве. Возвращает <c>true</c>, когда элемент
        /// найден. Не бросает: отсутствие shell bar — нормальное состояние до построения шаблона
        /// <c>DesignerView</c> (именно на этом падал прежний <c>Enumerable.First</c>).
        /// </summary>
        internal static bool TryApplyShellBarStyle(DependencyObject view)
        {
            if (WfDesignerTheme.FindByName(view, ShellBarElementName) is StatusBar shellBar)
            {
                ApplyShellBarStyle(shellBar);
                return true;
            }

            return false;
        }

        private static void ApplyShellBarStyle(StatusBar shellBar)
        {
            // Цвета только из палитры: именованный серый не следовал теме (страж — WfDesignerThemeTests).
            WfDesignerTheme.ThemeShellBar(shellBar, StudioPalette.Active);
        }

        /// <summary>
        /// Get Designer By laoded File
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public static WorkflowDesignerEx GetDesignerByFile(string sourceFile)
        {
            return CustomWfDesigner.GetInstance().WorkflowDesigners
                .FirstOrDefault(d => ((WorkflowFileItem)d.Context.Items.First(i => i.ItemType == typeof(WorkflowFileItem))).LoadedFile == sourceFile);
        }

        private static void SetCurrentWorkflowFile(WorkflowDesignerEx workflowDesignerEx)
        {
            Properties.Settings.Default.CurrentWorkflowFile = workflowDesignerEx.Context.Items.GetValue<WorkflowFileItem>().LoadedFile;
            Properties.Settings.Default.Save();
        }

        private static void LoadBreakpoints(WorkflowDesigner designer, string filePath)
        {
            try
            {
                string breakPointsFile = $"{filePath}.breakpoints";

                if (!File.Exists(breakPointsFile))
                {
                    return;
                }

                DebuggerService debuggerService = (DebuggerService)designer.DebugManagerView;
                if (debuggerService != null)
                {
                    string json = File.ReadAllText(breakPointsFile);
                    List<BreakpointData> breakpointData = JsonConvert.DeserializeObject<List<BreakpointData>>(json);

                    if (breakpointData != null && breakpointData.Count > 0)
                    {
                        WorkflowDesignerEx designerEx = (WorkflowDesignerEx)designer;
                        foreach (BreakpointData bp in breakpointData)
                        {
                            SourceLocation sourceLocation = new SourceLocation(filePath, bp.StartLine, bp.StartColumn, bp.EndLine, bp.EndColumn);
                            debuggerService.InsertBreakpoint(sourceLocation, BreakpointTypes.Bounded | BreakpointTypes.Enabled);

                            // Проверка дубликатов перед добавлением точки останова
                            if (!designerEx.BreakpointList.Contains(sourceLocation))
                            {
                                designerEx.BreakpointList.Add(sourceLocation);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"Ошибка при загрузке точек останова: {ex}", (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        /// <summary>
        /// Ensures that netstandard.dll reference is added to the workflow
        /// </summary>
#pragma warning disable IDE0051 // Remove unused private members
        private static void EnsureNetstandardReference(WorkflowDesignerEx workflowDesignerEx)
#pragma warning restore IDE0051 // Remove unused private members
        {
            try
            {
                ModelService modelService = workflowDesignerEx.Context.Services.GetService<ModelService>();
                if (modelService == null)
                {
                    CustomWfDesigner.GetInstance().Log("Failed to get ModelService for adding netstandard reference", (int)RpaCommon.Enums.LogLevel.Warn);
                    return;
                }

                ModelItem root = modelService.Root;
                if (root == null)
                {
                    CustomWfDesigner.GetInstance().Log("Failed to get root ModelItem for adding netstandard reference", (int)RpaCommon.Enums.LogLevel.Warn);
                    return;
                }

                // Get TextExpression.ReferencesForImplementation
                ModelProperty referencesProperty = root.Properties["TextExpression.ReferencesForImplementation"];
                if (referencesProperty == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠ TextExpression.ReferencesForImplementation property not found in workflow. This is normal for workflows without C# expressions.");
                    return;
                }

                ModelItemCollection references = referencesProperty.Collection;
                if (references == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠ References collection is null. Cannot add netstandard reference.");
                    return;
                }

                // Check if netstandard is already referenced
                string netstandardAssemblyName = "netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";
                bool alreadyExists = references.Any(item =>
                {
                    object assemblyRef = item.GetCurrentValue();
                    if (assemblyRef != null)
                    {
                        PropertyInfo nameProperty = assemblyRef.GetType().GetProperty("AssemblyName");
                        if (nameProperty != null)
                        {
                            object value = nameProperty.GetValue(assemblyRef);
                            string name = value?.ToString();
                            return name != null && (name == netstandardAssemblyName || name.StartsWith("netstandard,"));
                        }
                    }
                    return false;
                });

                if (alreadyExists)
                {
                    System.Diagnostics.Debug.WriteLine("✓ netstandard reference already exists in workflow");
                    return;
                }

                // Create AssemblyReference type using reflection
                Type assemblyReferenceType = Type.GetType("System.Activities.Presentation.View.AssemblyReference, System.Activities.Presentation");
                if (assemblyReferenceType == null)
                {
                    CustomWfDesigner.GetInstance().Log("Failed to find AssemblyReference type", (int)RpaCommon.Enums.LogLevel.Warn);
                    return;
                }

                // Create new AssemblyReference instance
                object assemblyReference = Activator.CreateInstance(assemblyReferenceType);
                PropertyInfo assemblyNameProperty = assemblyReferenceType.GetProperty("AssemblyName");
                if (assemblyNameProperty != null)
                {
                    // Create AssemblyName object
                    AssemblyName netstandardName = new AssemblyName(netstandardAssemblyName);
                    assemblyNameProperty.SetValue(assemblyReference, netstandardName);

                    // Add to collection
                    _ = references.Add(assemblyReference);

                    System.Diagnostics.Debug.WriteLine("✓ Successfully added netstandard reference to workflow");
                    CustomWfDesigner.GetInstance().Log("Added netstandard reference to workflow", (int)RpaCommon.Enums.LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"Failed to add netstandard reference: {ex}", (int)RpaCommon.Enums.LogLevel.Warn);
                System.Diagnostics.Debug.WriteLine($"Failed to add netstandard reference: {ex}");
            }
        }

        private class BreakpointData
        {
            public int StartLine { get; set; }
            public int StartColumn { get; set; }
            public int EndLine { get; set; }
            public int EndColumn { get; set; }
        }
    }
}
