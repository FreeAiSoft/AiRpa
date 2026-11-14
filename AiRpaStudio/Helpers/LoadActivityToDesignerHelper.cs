using System;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using RPA.Core;
using RPA.Core.Services;
using RPA.Core.Workflow;

namespace RPA.Helpers
{
    internal class LoadActivityToDesignerHelper
    {
        /// <summary>
        /// Creates a new Workflow Designer instance with C# Expression Editor
        /// </summary>
        /// <param name="sourceFile">Workflow FileName</param>
        /// <param name="looseTypeRegistry">Реестр вложенных XAML-активностей для loose-загрузки (<c>null</c> — штатный путь).</param>
        internal static WorkflowDesignerEx LoadActivityToDesigner(string sourceFile, WorkflowTypeRegistry looseTypeRegistry = null)
        {
            if (string.IsNullOrWhiteSpace(sourceFile)
                || !sourceFile.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(sourceFile))
            {
                return null;
            }

            // проверяем не загружена ли уже активность в какой-либо дизайнер
            WorkflowDesignerEx workflowDesignerEx = GetDesignerByFile(sourceFile);

            if (workflowDesignerEx == null)
            {
                workflowDesignerEx = new WorkflowDesignerEx();

                // Среда дизайнера — одна на все пути открытия (студия, стенд конструктора, MCP):
                // DesignerConfigurationService (.NET Framework 4.5 + LoadingFromUntrustedSourceEnabled),
                // Roslyn-редактор выражений, IExpressionEditorService, IValidationErrorService.
                // Дублировать эти установки нельзя: расхождение путей даёт «работает в одном
                // инструменте, ломается в другом» (`конвейер генерации XAML`, п. 2.3).
                WfDesignerEnvironment.Configure(workflowDesignerEx);

                if (looseTypeRegistry != null)
                {
                    LooseXamlDesignerLoader.Open(workflowDesignerEx, sourceFile, looseTypeRegistry);
                }
                else
                {
                    //load Workflow Xaml
                    workflowDesignerEx.Load(sourceFile);
                }
#pragma warning disable IDE0004 // Remove Unnecessary Cast

                CustomWfDesigner.GetInstance().WorkflowDesigners.Add(workflowDesignerEx);
                CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = workflowDesignerEx;

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
            SetVariablesStyle(workflowDesignerEx);

            // Инициализировать отслеживание VariableDesigner
            workflowDesignerEx.DesignerVariableContextMenuService.InitializeVariableDesignerTracking();

            // Добавить ссылку на netstandard.dll
            //EnsureNetstandardReference(workflowDesignerEx);

            // Сохранение после загрузки (нормализация XAML) не должно ломать открытие воркфлоу:
            // файл может быть занят другим процессом (OneDrive, антивирус, индексатор, вторая копия
            // студии, ранее открытый StreamReader) — раньше необработанный IOException из Save
            // всплывал наружу и ронял отладчик в CustomWfDesigner.WorkflowOpen().
            if (looseTypeRegistry == null && !IsInsideWorkflowProject(sourceFile))
            {
                SaveAfterLoad(workflowDesignerEx, sourceFile);
            }

            return workflowDesignerEx;
        }

        /// <summary>
        /// Сохраняет воркфлоу сразу после загрузки (нормализация XAML в дизайнере).
        /// Кратковременная блокировка файла (антивирус, индексатор, OneDrive) переживается повторами,
        /// длительная — только предупреждением в лог: дизайнер всё равно должен открыться, а не падать
        /// с <see cref="IOException"/> наружу. Ранее необработанное исключение из Save останавливало
        /// отладчик в <see cref="CustomWfDesigner.WorkflowOpen"/>.
        /// </summary>
        /// <summary>
        /// Файл принадлежит текущему WF-проекту студии. Такие файлы открываются loose-путём (без сборки
        /// и без DLL), поэтому штатная «нормализация после загрузки» (<c>designer.Save</c>) их не должна
        /// касаться: эталонный проект обязан только читаться, а не перезаписываться при открытии.
        /// </summary>
        private static bool IsInsideWorkflowProject(string sourceFile)
        {
            RPA.Entities.Project project = CustomWfDesigner.GetInstance().Project;

            if (project == null
                || !project.IsWorkflowProject
                || string.IsNullOrWhiteSpace(project.ProjectDirPath)
                || string.IsNullOrWhiteSpace(sourceFile))
            {
                return false;
            }

            string projectDir = Path.GetFullPath(project.ProjectDirPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return Path.GetFullPath(sourceFile).StartsWith(projectDir, StringComparison.OrdinalIgnoreCase);
        }

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

        /// <summary>Имя shell bar в шаблоне <c>DesignerView</c>.</summary>
        private const string ShellBarElementName = WfDesignerTheme.ShellBarElementName;

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

        internal static void LoadBreakpoints(WorkflowDesigner designer, string filePath)
        {
            try
            {
                List<BreakpointStore.BreakpointRecord> records = BreakpointStore.Load(filePath);
                if (records == null)
                {
                    records = TryImportLegacySidecar(filePath);
                }

                if (records == null || records.Count == 0)
                {
                    return;
                }

                DebuggerService debuggerService = (DebuggerService)designer.DebugManagerView;
                if (debuggerService == null)
                {
                    return;
                }

                WorkflowDesignerEx designerEx = designer as WorkflowDesignerEx;
                foreach (BreakpointStore.BreakpointRecord bp in records)
                {
                    SourceLocation sourceLocation = ResolveBreakpointLocation(designerEx, filePath, bp);
                    if (sourceLocation == null)
                    {
                        // «Призрак»: точка не привязана к живой активности. Не вставляем её,
                        // иначе она дождётся новой активности, вставленной на те же координаты.
                        continue;
                    }
                    BreakpointTypes breakpointType = (BreakpointTypes)bp.BreakpointType;
                    if (breakpointType == BreakpointTypes.None)
                    {
                        breakpointType = BreakpointTypes.Bounded | BreakpointTypes.Enabled;
                    }

                    debuggerService.InsertBreakpoint(sourceLocation, breakpointType);

                    // Проверка дубликатов перед добавлением точки останова
                    if (designerEx != null && !designerEx.BreakpointList.Contains(sourceLocation))
                    {
                        designerEx.BreakpointList.Add(sourceLocation);
                    }
                }
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"Ошибка при загрузке точек останова: {ex}", (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        /// <summary>
        /// Восстанавливает SourceLocation точки: сначала по стабильному якорю Activity.Id
        /// (точка переживает сдвиг строк после правки), затем — по сохранённым координатам. Если точка не привязана к живой активности, возвращается <c>null</c>.
        /// </summary>
        private static SourceLocation ResolveBreakpointLocation(WorkflowDesignerEx designerEx, string filePath, BreakpointStore.BreakpointRecord bp)
        {
            if (designerEx != null && !string.IsNullOrWhiteSpace(bp.ActivityId))
            {
                SourceLocation anchored = FindByActivityId(designerEx.DesignerSourceLocationMapping, bp.ActivityId, bp.DisplayName);
                if (anchored != null)
                {
                    return anchored;
                }
            }

            return FindByCoordinates(designerEx?.DesignerSourceLocationMapping, bp)
                ?? (designerEx == null ? new SourceLocation(filePath, bp.StartLine, bp.StartColumn, bp.EndLine, bp.EndColumn) : null);
        }

        private static SourceLocation FindByActivityId(Dictionary<object, SourceLocation> mapping, string activityId, string displayName)
        {
            if (mapping == null || mapping.Count == 0 || string.IsNullOrWhiteSpace(activityId))
            {
                return null;
            }

            // 1. Точное совпадение QualifiedId.
            foreach (KeyValuePair<object, SourceLocation> entry in mapping)
            {
                if (entry.Key is System.Activities.Activity activity && string.Equals(activity.Id, activityId, StringComparison.Ordinal))
                {
                    return entry.Value;
                }
            }

            // 2. Совпадение по последней компоненте QualifiedId (устойчиво к сдвигу корня x:Class).
            string lastStored = LastIdComponent(activityId);
            if (lastStored == null)
            {
                return null;
            }

            SourceLocation fallback = null;
            foreach (KeyValuePair<object, SourceLocation> entry in mapping)
            {
                if (!(entry.Key is System.Activities.Activity activity))
                {
                    continue;
                }

                if (LastIdComponent(activity.Id) != lastStored)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = entry.Value;
                }

                if (!string.IsNullOrWhiteSpace(displayName) && string.Equals(activity.DisplayName, displayName, StringComparison.Ordinal))
                {
                    return entry.Value;
                }
            }

            return fallback;
        }

        private static SourceLocation FindByCoordinates(Dictionary<object, SourceLocation> mapping, BreakpointStore.BreakpointRecord bp)
        {
            if (mapping == null || mapping.Count == 0 || bp == null)
            {
                return null;
            }

            foreach (KeyValuePair<object, SourceLocation> entry in mapping)
            {
                SourceLocation location = entry.Value;
                if (location != null
                    && location.StartLine == bp.StartLine
                    && location.StartColumn == bp.StartColumn
                    && location.EndLine == bp.EndLine
                    && location.EndColumn == bp.EndColumn)
                {
                    return location;
                }
            }

            return null;
        }

        private static string LastIdComponent(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            int index = id.LastIndexOf('.');
            return index >= 0 ? id.Substring(index + 1) : id;
        }

        /// <summary>
        /// Разовый перенос точек из легаси sidecar (<c>&lt;file&gt;.breakpoints</c>) в новый
        /// пользовательский store. Файл проекта не удаляется — только читается один раз при миграции.
        /// </summary>
        private static List<BreakpointStore.BreakpointRecord> TryImportLegacySidecar(string filePath)
        {
            string sidecar = filePath + ".breakpoints";
            if (!File.Exists(sidecar))
            {
                return null;
            }

            try
            {
                List<BreakpointData> legacy = JsonConvert.DeserializeObject<List<BreakpointData>>(File.ReadAllText(sidecar));
                if (legacy == null || legacy.Count == 0)
                {
                    return null;
                }

                int defaultType = (int)(BreakpointTypes.Bounded | BreakpointTypes.Enabled);
                List<BreakpointStore.BreakpointRecord> records = legacy
                    .Select(bp => new BreakpointStore.BreakpointRecord
                    {
                        StartLine = bp.StartLine,
                        StartColumn = bp.StartColumn,
                        EndLine = bp.EndLine,
                        EndColumn = bp.EndColumn,
                        BreakpointType = defaultType
                    })
                    .ToList();

                BreakpointStore.Save(filePath, records);
                return records;
            }
            catch (Exception)
            {
                return null;
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
