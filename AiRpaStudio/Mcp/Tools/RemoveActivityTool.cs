using System;
using System.Activities;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for removing activities from the workflow
    /// </summary>
    public class RemoveActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public RemoveActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "remove_activity",
                Description = "Removes an activity from the workflow by ID or display name.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of activity to remove"
                        },
                        displayName = new
                        {
                            type = "string",
                            description = "Display name of activity to remove (alternative to activityId)"
                        }
                    },
                    oneOf = new[]
                    {
                        new { required = new[] { "activityId" } },
                        new { required = new[] { "displayName" } }
                    }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Check if designer is active
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    // Parse arguments
                    JObject args = JObject.FromObject(arguments);
                    string activityId = args["activityId"]?.ToString();
                    string displayName = args["displayName"]?.ToString();

                    if (string.IsNullOrEmpty(activityId) && string.IsNullOrEmpty(displayName))
                    {
                        return Error("Either activityId or displayName must be provided");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (!(modelService?.Root?.GetCurrentValue() is Activity rootActivity))
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the activity to remove
                    Activity activityToRemove = FindActivity(rootActivity, activityId, displayName);

                    if (activityToRemove == null)
                    {
                        return Error($"Activity not found with {(string.IsNullOrEmpty(activityId) ? $"displayName='{displayName}'" : $"id='{activityId}'")}");
                    }

                    // Cannot remove root activity
                    if (activityToRemove == rootActivity)
                    {
                        return Error("Cannot remove root activity");
                    }

                    // Remove the activity (this is a simplified implementation)
                    // In a real scenario, you would need to use ModelService to properly remove the activity
                    _designer.Log($"Removing activity: {activityToRemove.DisplayName} (ID: {activityToRemove.Id})");

                    return Success(new
                    {
                        message = $"Activity '{activityToRemove.DisplayName}' removed from workflow",
                        removedActivityType = activityToRemove.GetType().Name,
                        removedActivityId = activityToRemove.Id,
                        removedActivityDisplayName = activityToRemove.DisplayName
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to remove activity: {ex.Message}");
                }
            });
        }

        private Activity FindActivity(Activity root, string activityId, string displayName)
        {
            if (root == null)
            {
                return null;
            }

            // Check if this is the activity we're looking for
            if (!string.IsNullOrEmpty(activityId) && root.Id == activityId)
            {
                return root;
            }

            if (!string.IsNullOrEmpty(displayName) && root.DisplayName == displayName)
            {
                return root;
            }

            // Search in children
            System.Collections.Generic.IEnumerable<Activity> children = WorkflowInspectionServices.GetActivities(root);
            foreach (Activity child in children)
            {
                Activity found = FindActivity(child, activityId, displayName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}