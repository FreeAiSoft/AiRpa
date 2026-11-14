using System.Activities;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.XamlIntegration;
using System.Xaml;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using RPA.Core;
using RPA.Core.Workflow;
using RPA.Logging;
using Activity = System.Activities.Activity;

namespace RPA.Helpers
{
    internal class DebuggerMappingsHelper
    {
        internal static void RegenerateSourceDebuggerMappings(WorkflowDesignerEx workflowDesignerEx)
        {
            if (workflowDesignerEx == null)
            {
                SourceLocationMappingDiagnostics.Write("RegenerateSourceDebuggerMappings: designer=<null>");
                return;
            }

            string loadedFile = TryGetLoadedFile(workflowDesignerEx);
            int before = workflowDesignerEx.DesignerSourceLocationMapping.Count;
            SourceLocationMappingDiagnostics.Write(
                $"RegenerateSourceDebuggerMappings: designer=#{workflowDesignerEx.GetHashCode()} file={loadedFile ?? "<null>"} mappingBefore={before}");

            // Updating the mapping between Model item and Source Location before we run the workflow
            // so that BP setting can re-use that information from the DesignerSourceLocationMapping.
            workflowDesignerEx.DesignerSourceLocationMapping.Clear();
            SourceLocationMappingDiagnostics.Write($"RegenerateSourceDebuggerMappings: mapping cleared (was {before} entries)");

            UpdateSourceLocationMappingInDebuggerService(workflowDesignerEx);

            SourceLocationMappingDiagnostics.Write($"RegenerateSourceDebuggerMappings: done mappingAfter={workflowDesignerEx.DesignerSourceLocationMapping.Count}");
        }

        /// <summary>
        /// Лёгкое обновление SourceLocation-маппинга актуальными координатами после <c>Save</c>.
        /// <para>
        /// <see cref="WorkflowDesigner.Save(string)"/> в конце инвалидирует внутренний маппинг
        /// <see cref="DebuggerService"/> (ставит <c>requiresUpdateSourceLocation</c> и сбрасывает
        /// <c>modelItemToSourceLocation</c>). Если после этого собрать точки, они теряются. Здесь
        /// маппинг пересобирается из свежего текста документа: модель после правки держит устаревшие
        /// отладочные символы координат.
        /// </para>
        /// </summary>
        internal static void RefreshSourceLocationMapping(WorkflowDesignerEx workflowDesignerEx)
        {
            if (workflowDesignerEx == null)
            {
                SourceLocationMappingDiagnostics.Write("RefreshSourceLocationMapping: designer=<null>");
                return;
            }

            string loadedFile = TryGetLoadedFile(workflowDesignerEx);
            SourceLocationMappingDiagnostics.Write($"RefreshSourceLocationMapping: designer=#{workflowDesignerEx.GetHashCode()} file={loadedFile ?? "<null>"}");

            object rootInstance = GetRootInstance(workflowDesignerEx);
            Activity documentRootElement = rootInstance == null ? null : GetRootWorkflowElement(rootInstance);
            if (documentRootElement == null)
            {
                SourceLocationMappingDiagnostics.Write("RefreshSourceLocationMapping: documentRootElement is null -> return");
                return;
            }

            Activity freshRoot;
            try
            {
                workflowDesignerEx.Flush();
                freshRoot = DeserializeFreshDocument(workflowDesignerEx.Text, workflowDesignerEx.LooseTypeRegistry);
            }
            catch (System.Exception ex)
            {
                SourceLocationMappingDiagnostics.WriteException(ex, "RefreshSourceLocationMapping: DeserializeFreshDocument failed");
                return;
            }

            if (freshRoot == null)
            {
                SourceLocationMappingDiagnostics.Write("RefreshSourceLocationMapping: freshRoot is null -> return");
                return;
            }

            workflowDesignerEx.DesignerSourceLocationMapping.Clear();
            SourceLocationProvider.CollectMapping(documentRootElement, freshRoot, workflowDesignerEx.DesignerSourceLocationMapping, loadedFile);
            SourceLocationMappingDiagnostics.Write($"RefreshSourceLocationMapping: CollectMapping produced {workflowDesignerEx.DesignerSourceLocationMapping.Count} entries");

            DebuggerService debuggerService = (DebuggerService)workflowDesignerEx.DebugManagerView;
            debuggerService.UpdateSourceLocations(workflowDesignerEx.DesignerSourceLocationMapping);
            ResetRequiresUpdateSourceLocation(debuggerService);
            RepairDebuggerServiceModelItemMapping(debuggerService, workflowDesignerEx);

            SourceLocationMappingDiagnostics.Write($"RefreshSourceLocationMapping: done mappingAfter={workflowDesignerEx.DesignerSourceLocationMapping.Count}");
        }

        /// <summary>
        /// Десериализует свежий текст XAML в корневую активность с актуальными отладочными
        /// координатами. Для loose-XAML используется <see cref="LooseActivityXamlSchemaContext"/>
        /// (резолв <c>local:SubProcessN</c>), иначе — штатный контекст.
        /// </summary>
        private static Activity DeserializeFreshDocument(string xaml, WorkflowTypeRegistry registry)
        {
            if (string.IsNullOrWhiteSpace(xaml))
            {
                return null;
            }

            XamlSchemaContext schemaContext = registry != null
                ? (XamlSchemaContext)new LooseActivityXamlSchemaContext(registry)
                : new XamlSchemaContext();

            using (var textReader = new StringReader(xaml))
            using (var debuggerReader = new XamlDebuggerXmlReader(textReader, schemaContext))
            using (var builderReader = ActivityXamlServices.CreateBuilderReader(debuggerReader, schemaContext))
            {
                object loaded = XamlServices.Load(builderReader);
                return loaded == null ? null : GetRootWorkflowElement(loaded);
            }
        }

        private static string TryGetLoadedFile(WorkflowDesignerEx workflowDesignerEx)
        {
            try
            {
                return workflowDesignerEx.Context.Items.GetValue<WorkflowFileItem>().LoadedFile;
            }
            catch (System.Exception ex)
            {
                SourceLocationMappingDiagnostics.WriteException(ex, "TryGetLoadedFile failed");
                return null;
            }
        }

        private static string DescribeKey(object key)
        {
            if (key is Activity activity)
            {
                return SourceLocationMappingDiagnostics.Describe(activity);
            }

            return key?.GetType().FullName ?? "<null>";
        }

        /// <summary>
        /// Updating the mapping between Model item and Source Location before we run the workflow so that BP setting can re-use that information from the DesignerSourceLocationMapping.
        /// </summary>
        private static void UpdateSourceLocationMappingInDebuggerService(WorkflowDesignerEx workflowDesignerEx)
        {
            SourceLocationMappingDiagnostics.Write("UpdateSourceLocationMappingInDebuggerService: enter");

            // DebuggerService (IDesignerDebugView) публикуется первым: точки останова в контекстном меню
            // DesignerView зависят только от наличия сервиса, а не от успешной генерации маппинга. Раньше
            // обращение к DebugManagerView стояло в конце метода, и пустой корень или «sequence not found»
            // не публиковали сервис — пункты меню «Точка останова» схлопывались.
            _ = workflowDesignerEx.DebugManagerView;
            SourceLocationMappingDiagnostics.Write("UpdateSourceLocationMappingInDebuggerService: DebugManagerView ensured");

            object rootInstance = GetRootInstance(workflowDesignerEx);
            SourceLocationMappingDiagnostics.Write($"UpdateSourceLocationMappingInDebuggerService: rootInstance={(rootInstance == null ? "<null>" : rootInstance.GetType().FullName)}");

            if (rootInstance != null)
            {
                Activity documentRootElement = GetRootWorkflowElement(rootInstance);
                SourceLocationMappingDiagnostics.Write($"UpdateSourceLocationMappingInDebuggerService: documentRootElement={SourceLocationMappingDiagnostics.Describe(documentRootElement)}");

                if (documentRootElement == null)
                {
                    SourceLocationMappingDiagnostics.Write("UpdateSourceLocationMappingInDebuggerService: documentRootElement is null -> return");
                    return;
                }

                Activity rootRuntimeWorkflowElement = null;
                try
                {
                    rootRuntimeWorkflowElement = GetRootRuntimeWorkflowElement(workflowDesignerEx);
                    SourceLocationMappingDiagnostics.Write($"UpdateSourceLocationMappingInDebuggerService: rootRuntimeWorkflowElement={SourceLocationMappingDiagnostics.Describe(rootRuntimeWorkflowElement)}");
                }
                catch (System.Exception ex)
                {
                    SourceLocationMappingDiagnostics.WriteException(ex, "UpdateSourceLocationMappingInDebuggerService: GetRootRuntimeWorkflowElement failed");
                    System.Diagnostics.Debug.WriteLine(ErrorText.Of(ex));
                }

                string loadedFilePath = workflowDesignerEx.Context.Items.GetValue<WorkflowFileItem>().LoadedFile;
                SourceLocationMappingDiagnostics.Write($"UpdateSourceLocationMappingInDebuggerService: loadedFilePath={loadedFilePath ?? "<null>"}");

                // в ситуации если documentRootElement и rootRuntimeWorkflowElement == null - исключение
                // это происходит, когда xaml Не содержит элементов
                if (!(documentRootElement == null && rootRuntimeWorkflowElement == null))
                {
                    SourceLocationMappingDiagnostics.Write("UpdateSourceLocationMappingInDebuggerService: SourceLocationProvider.CollectMapping begin");
                    SourceLocationProvider.CollectMapping(documentRootElement, documentRootElement, workflowDesignerEx.DesignerSourceLocationMapping,
                       loadedFilePath);
                    SourceLocationMappingDiagnostics.Write($"UpdateSourceLocationMappingInDebuggerService: CollectMapping produced {workflowDesignerEx.DesignerSourceLocationMapping.Count} entries");

                    foreach (KeyValuePair<object, SourceLocation> entry in workflowDesignerEx.DesignerSourceLocationMapping)
                    {
                        SourceLocationMappingDiagnostics.Write($"    map: key={DescribeKey(entry.Key)} -> {SourceLocationMappingDiagnostics.Describe(entry.Value)}");
                    }
                }
            }
            else
            {
                SourceLocationMappingDiagnostics.Write("UpdateSourceLocationMappingInDebuggerService: rootInstance is null -> skip CollectMapping");
            }

            // Notify the DebuggerService of the new sourceLocationMapping.
            // When rootInstance == null, it'll just reset the mapping.
            DebuggerService debuggerService = (DebuggerService)workflowDesignerEx.DebugManagerView;
            SourceLocationMappingDiagnostics.Write($"UpdateSourceLocationMappingInDebuggerService: calling UpdateSourceLocations with {workflowDesignerEx.DesignerSourceLocationMapping.Count} entries");
            debuggerService.UpdateSourceLocations(workflowDesignerEx.DesignerSourceLocationMapping);
            SourceLocationMappingDiagnostics.Write("UpdateSourceLocationMappingInDebuggerService: UpdateSourceLocations done");

            ResetRequiresUpdateSourceLocation(debuggerService);
            RepairDebuggerServiceModelItemMapping(debuggerService, workflowDesignerEx);
        }

        /// <summary>
        /// Сбрасывает private-флаг <c>requiresUpdateSourceLocation</c> у <see cref="DebuggerService"/>.
        /// <para>
        /// После ручного <see cref="DebuggerService.UpdateSourceLocations(Dictionary{object, SourceLocation})"/>
        /// маппинг актуален, но флаг остаётся взведённым: его ставит <c>InvalidateSourceLocationMapping</c>
        /// в конце <c>WorkflowDesigner.Load/Save</c>. Без сброса первый же доступ к
        /// <see cref="IDesignerDebugView.SelectedLocation"/> перестраивает маппинг из штатного
        /// <c>ObjectToSourceLocationMapping</c>, который для loose-пути открытия пуст, и затирает ручной
        /// маппинг — «Вставить точку останова» молча ничего не делает.
        /// </para>
        /// </summary>
        private static void ResetRequiresUpdateSourceLocation(DebuggerService debuggerService)
        {
            try
            {
                FieldInfo field = typeof(DebuggerService).GetField(
                    "requiresUpdateSourceLocation",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (field == null)
                {
                    SourceLocationMappingDiagnostics.Write("ResetRequiresUpdateSourceLocation: field not found");
                    return;
                }

                field.SetValue(debuggerService, false);
                SourceLocationMappingDiagnostics.Write("ResetRequiresUpdateSourceLocation: requiresUpdateSourceLocation = false");
            }
            catch (System.Exception ex)
            {
                SourceLocationMappingDiagnostics.WriteException(ex, "ResetRequiresUpdateSourceLocation failed");
            }
        }

        /// <summary>
        /// Дозаполняет private-словари <c>modelItemToSourceLocation</c> и <c>sourceLocationToModelItem</c>
        /// у <see cref="DebuggerService"/>. <see cref="DebuggerService.UpdateSourceLocations(Dictionary{object, SourceLocation})"/>
        /// ищет <c>ModelItem</c> через <c>GetModelItem(key)</c> без развёртывания дерева, поэтому для loose-пути
        /// в словарь попадает только корень (<c>ActivityBuilder</c>), а вложенные активности — нет. Из-за этого
        /// <see cref="IDesignerDebugView.SelectedLocation"/> остаётся <c>null</c> даже при валидном ручном маппинге.
        /// Здесь записи досопоставляются через <c>GetModelItem(key, shouldExpandModelTree: true)</c>.
        /// </summary>
        private static void RepairDebuggerServiceModelItemMapping(DebuggerService debuggerService, WorkflowDesignerEx workflowDesignerEx)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo mtmField = typeof(DebuggerService).GetField("modelTreeManager", flags);
                FieldInfo modelToLocField = typeof(DebuggerService).GetField("modelItemToSourceLocation", flags);
                FieldInfo locToModelField = typeof(DebuggerService).GetField("sourceLocationToModelItem", flags);

                if (mtmField == null || modelToLocField == null || locToModelField == null)
                {
                    SourceLocationMappingDiagnostics.Write("RepairDebuggerServiceModelItemMapping: some field not found");
                    return;
                }

                var modelTreeManager = mtmField.GetValue(debuggerService) as ModelTreeManager;
                var modelToLoc = modelToLocField.GetValue(debuggerService) as Dictionary<ModelItem, SourceLocation>;
                var locToModel = locToModelField.GetValue(debuggerService) as Dictionary<SourceLocation, ModelItem>;

                if (modelTreeManager == null || modelToLoc == null || locToModel == null)
                {
                    SourceLocationMappingDiagnostics.Write("RepairDebuggerServiceModelItemMapping: null state");
                    return;
                }

                int added = 0;
                foreach (KeyValuePair<object, SourceLocation> entry in workflowDesignerEx.DesignerSourceLocationMapping)
                {
                    ModelItem modelItem = modelTreeManager.GetModelItem(entry.Key, true);
                    if (modelItem == null)
                    {
                        continue;
                    }

                    if (!modelToLoc.ContainsKey(modelItem))
                    {
                        modelToLoc.Add(modelItem, entry.Value);
                        added++;
                    }

                    if (!locToModel.ContainsKey(entry.Value))
                    {
                        locToModel.Add(entry.Value, modelItem);
                    }
                }

                SourceLocationMappingDiagnostics.Write($"RepairDebuggerServiceModelItemMapping: added {added} modelItem mappings");
            }
            catch (System.Exception ex)
            {
                SourceLocationMappingDiagnostics.WriteException(ex, "RepairDebuggerServiceModelItemMapping failed");
            }
        }

        private static object GetRootInstance(WorkflowDesignerEx workflowDesignerEx)
        {
            ModelService modelService = workflowDesignerEx.Context.Services.GetService<ModelService>();

#pragma warning disable IDE0046 // Convert to conditional expression
            if (modelService != null)
            {
                return modelService.Root.GetCurrentValue();
            }
            else
            {
                return null;
            }
#pragma warning restore IDE0046 // Convert to conditional expression
        }

        // Get root WorkflowElement.  Currently only handle when the object is ActivitySchemaType or WorkflowElement.
        // May return null if it does not know how to get the root activity.
        private static Activity GetRootWorkflowElement(object rootModelObject)
        {
            Debug.Assert(rootModelObject != null, "Cannot pass null as rootModelObject");

            Activity rootWorkflowElement;
#pragma warning disable IDE0045 // Convert to conditional expression
            if (rootModelObject is IDebuggableWorkflowTree debuggableWorkflowTree)
            {
                rootWorkflowElement = debuggableWorkflowTree.GetWorkflowRoot();
            }
            else // Loose xaml case.
            {
                rootWorkflowElement = rootModelObject as Activity;
            }
#pragma warning restore IDE0045 // Convert to conditional expression
            return rootWorkflowElement;
        }

        private static Activity GetRootRuntimeWorkflowElement(WorkflowDesignerEx workflowDesignerEx)
        {
            workflowDesignerEx.Flush();
            MemoryStream workflowStream = new MemoryStream(Encoding.UTF8.GetBytes(workflowDesignerEx.Text));
            Activity root = ActivityXamlServices.Load(workflowStream, new ActivityXamlServicesSettings { CompileExpressions = true });
            WorkflowInspectionServices.CacheMetadata(root);

            IEnumerator<Activity> activitiesEnum = WorkflowInspectionServices.GetActivities(root).GetEnumerator();
            while (activitiesEnum.MoveNext())
            {
                if (activitiesEnum.Current.Id == "1.1")
                {
                    return activitiesEnum.Current;
                }
            }

            throw new System.Exception("sequence not found");
        }

        internal static Dictionary<string, Activity> BuildActivityIdToWfElementMap(Dictionary<object, SourceLocation> wfElementToSourceLocationMap)
        {
            Dictionary<string, Activity> map = new Dictionary<string, Activity>();

            Activity wfElement;
            foreach (object instance in wfElementToSourceLocationMap.Keys)
            {
                wfElement = instance as Activity;
                if (wfElement != null)
                {
                    map.Add(wfElement.Id, wfElement);
                }
            }

            return map;
        }
    }
}
