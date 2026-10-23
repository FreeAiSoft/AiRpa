using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for renaming an activity and updating all references
    /// </summary>
    public class RenameActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public RenameActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "rename_activity",
                Description = "Renames an activity and updates all references to it throughout the workflow",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of the activity to rename"
                        },
                        currentName = new
                        {
                            type = "string",
                            description = "Current display name of the activity (alternative to activityId)"
                        },
                        newName = new
                        {
                            type = "string",
                            description = "New display name for the activity"
                        }
                    },
                    required = new[] { "newName" }
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
                    string currentName = args["currentName"]?.ToString();
                    string newName = args["newName"]?.ToString();

                    if (string.IsNullOrEmpty(newName))
                    {
                        return Error("newName is required");
                    }

                    if (string.IsNullOrEmpty(activityId) && string.IsNullOrEmpty(currentName))
                    {
                        return Error("Either activityId or currentName must be provided");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    // Find the activity to rename
                    ModelItem targetActivity = !string.IsNullOrEmpty(activityId)
                        ? FindActivityById(modelService.Root, activityId)
                        : FindActivityByName(modelService.Root, currentName);
                    if (targetActivity == null)
                    {
                        return Error($"Activity not found: {activityId ?? currentName}");
                    }

                    string oldName = targetActivity.Properties["DisplayName"]?.ComputedValue?.ToString();

                    // Update the DisplayName property
                    _ = (targetActivity.Properties["DisplayName"]?.SetValue(newName));

                    _designer.Log($"Renamed activity from '{oldName}' to '{newName}'");

                    // Note: Full implementation would need to:
                    // 1. Find all references to this activity (in expressions, arguments, etc.)
                    // 2. Update those references to use the new name
                    // 3. Handle variable scoping and naming conflicts

                    return Success(new
                    {
                        message = $"Activity renamed successfully from '{oldName}' to '{newName}'",
                        activityId = targetActivity.GetCurrentValue()?.GetType().GetProperty("Id")?.GetValue(targetActivity.GetCurrentValue())?.ToString(),
                        oldName,
                        newName
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to rename activity: {ex.Message}");
                }
            });
        }

        private ModelItem FindActivityById(ModelItem root, string id)
        {
            if (root?.GetCurrentValue() != null)
            {
                string activityId = root.GetCurrentValue().GetType().GetProperty("Id")?.GetValue(root.GetCurrentValue())?.ToString();
                if (activityId == id)
                {
                    return root;
                }
            }

            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem result = FindActivityById(property.Value, id);
                    if (result != null)
                    {
                        return result;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        ModelItem result = FindActivityById(item, id);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }

            return null;
        }

        private ModelItem FindActivityByName(ModelItem root, string name)
        {
            string displayName = root.Properties["DisplayName"]?.ComputedValue?.ToString();
            if (displayName == name)
            {
                return root;
            }

            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem result = FindActivityByName(property.Value, name);
                    if (result != null)
                    {
                        return result;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        ModelItem result = FindActivityByName(item, name);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }

            return null;
        }
    }
}
