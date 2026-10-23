using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.XamlIntegration;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xaml;
using Newtonsoft.Json.Linq;
using RPA.Core;
using RPA.Views;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for copying activities with all their settings
    /// </summary>
    public class CopyActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public CopyActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "copy_activity",
                Description = "Creates a copy of an activity with all its settings. The copy can be placed in the same or different container.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of activity to copy"
                        },
                        targetContainerId = new
                        {
                            type = "string",
                            description = "ID of target container for the copy (optional, uses source parent if not specified)"
                        },
                        newDisplayName = new
                        {
                            type = "string",
                            description = "Display name for the copied activity (optional, will append 'Copy' to original name)"
                        }
                    },
                    required = new[] { "activityId" }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    JObject args = JObject.FromObject(arguments);
                    string activityId = args["activityId"]?.ToString();
                    string targetContainerId = args["targetContainerId"]?.ToString();
                    string newDisplayName = args["newDisplayName"]?.ToString();

                    if (string.IsNullOrEmpty(activityId))
                    {
                        return Error("activityId is required");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (modelService?.Root == null)
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the activity to copy
                    ModelItem activityToCopy = FindModelItemById(modelService.Root, activityId);
                    if (activityToCopy == null)
                    {
                        return Error($"Activity with id '{activityId}' not found");
                    }

                    if (!(activityToCopy.GetCurrentValue() is Activity originalActivity))
                    {
                        return Error("Failed to get activity instance");
                    }

                    // Create a deep copy using XAML serialization
                    Activity copiedActivity = DeepCopyActivity(originalActivity);
                    if (copiedActivity == null)
                    {
                        return Error("Failed to create copy of activity");
                    }

                    // Set display name
                    copiedActivity.DisplayName = string.IsNullOrEmpty(newDisplayName)
                        ? $"{originalActivity.DisplayName} Copy"
                        : newDisplayName;

                    // Find target container
                    ModelItem targetParent = string.IsNullOrEmpty(targetContainerId)
                        ? activityToCopy.Parent
                        : FindModelItemById(modelService.Root, targetContainerId);

                    if (targetParent == null)
                    {
                        return Error("Target container not found");
                    }

                    // Add copy to workflow
                    bool success = false;
                    string errorMsg = null;

                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            ModelTreeManager modelTreeManager = activeDesigner.Context.Services.GetService<ModelTreeManager>();
                            ModelItem copiedModelItem = modelTreeManager.CreateModelItem(null, copiedActivity);

                            ModelItemCollection collection = GetActivitiesCollection(targetParent);
                            if (collection != null)
                            {
                                collection.Add(copiedModelItem);
                                activeDesigner.Flush();
                                success = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMsg = ex.Message;
                        }
                    });

                    return !string.IsNullOrEmpty(errorMsg)
                        ? Error($"Failed to add copied activity: {errorMsg}")
                        : !success
                        ? Error("Failed to add copied activity to workflow")
                        : Success(new
                        {
                            message = $"Activity '{originalActivity.DisplayName}' copied successfully",
                            originalId = activityId,
                            copyDisplayName = copiedActivity.DisplayName,
                            copyId = copiedActivity.Id
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to copy activity: {ex.Message}");
                }
            });
        }

        private Activity DeepCopyActivity(Activity activity)
        {
            try
            {
                // Serialize activity to XAML string
                StringBuilder sb = new StringBuilder();
                using (StringWriter stringWriter = new StringWriter(sb))
                {
                    using (XamlWriter xamlWriter = ActivityXamlServices.CreateBuilderWriter(new XamlXmlWriter(stringWriter, new XamlSchemaContext())))
                    {
                        XamlServices.Save(xamlWriter, activity);
                    }
                }

                string xaml = sb.ToString();

                // Deserialize back to create a copy
                using (StringReader stringReader = new StringReader(xaml))
                {
                    Activity copy = ActivityXamlServices.Load(stringReader);
                    return copy;
                }
            }
            catch
            {
                return null;
            }
        }

        private ModelItemCollection GetActivitiesCollection(ModelItem parent)
        {
            return parent == null
                ? null
                : parent.Properties["Activities"]?.Collection != null
                ? parent.Properties["Activities"].Collection
                : parent.Properties["Branches"]?.Collection != null
                ? parent.Properties["Branches"].Collection
                : parent.Properties["Nodes"]?.Collection != null ? parent.Properties["Nodes"].Collection : null;
        }

        private ModelItem FindModelItemById(ModelItem root, string id)
        {
            if (root == null)
            {
                return null;
            }

            Activity activity = root.GetCurrentValue() as Activity;
            if (activity?.Id == id)
            {
                return root;
            }

            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem found = FindModelItemById(property.Value, id);
                    if (found != null)
                    {
                        return found;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        ModelItem found = FindModelItemById(item, id);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }
    }
}
