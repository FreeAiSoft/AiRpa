using System.Activities;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Services;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using RPA.Core;
using Activity = System.Activities.Activity;

namespace RPA.Helpers
{
    internal class DebuggerMappingsHelper
    {
        internal static void RegenerateSourceDebuggerMappings(WorkflowDesignerEx workflowDesignerEx)
        {
            // Updating the mapping between Model item and Source Location before we run the workflow
            // so that BP setting can re-use that information from the DesignerSourceLocationMapping.
            workflowDesignerEx.DesignerSourceLocationMapping.Clear();
            UpdateSourceLocationMappingInDebuggerService(workflowDesignerEx);
        }

        /// <summary>
        /// Updating the mapping between Model item and Source Location before we run the workflow so that BP setting can re-use that information from the DesignerSourceLocationMapping.
        /// </summary>
        /// <returns></returns>
        private static void UpdateSourceLocationMappingInDebuggerService(WorkflowDesignerEx workflowDesignerEx)
        {
            object rootInstance = GetRootInstance(workflowDesignerEx);
            //Dictionary<object, SourceLocation> sourceLocationMapping = new Dictionary<object, SourceLocation>();

            if (rootInstance != null)
            {
                Activity documentRootElement = GetRootWorkflowElement(rootInstance);
                if (documentRootElement == null)
                {
                    return;
                }
                Activity rootRuntimeWorkflowElement = GetRootRuntimeWorkflowElement(workflowDesignerEx);
                string loadedFilePath = workflowDesignerEx.Context.Items.GetValue<WorkflowFileItem>().LoadedFile;

                // в ситуации если documentRootElement и rootRuntimeWorkflowElement == null - исключение
                // это происходит, когда xaml Не содержит элементов
                if (!(documentRootElement == null && rootRuntimeWorkflowElement == null))
                {
                    //SourceLocationProvider.CollectMapping(rootRuntimeWorkflowElement, documentRootElement, sourceLocationMapping,
                    //loadedFilePath);
                    SourceLocationProvider.CollectMapping(documentRootElement, documentRootElement, workflowDesignerEx.DesignerSourceLocationMapping,
                       loadedFilePath);
                }
            }

            // Notify the DebuggerService of the new sourceLocationMapping.
            // When rootInstance == null, it'll just reset the mapping.
            DebuggerService debuggerService = (DebuggerService)workflowDesignerEx.DebugManagerView;
            debuggerService.UpdateSourceLocations(workflowDesignerEx.DesignerSourceLocationMapping);
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
