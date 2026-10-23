using System;
using System.Activities;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting child activities of a given activity
    /// </summary>
    public class GetChildActivitiesTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetChildActivitiesTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_child_activities",
                Description = "Gets all direct child activities of a specified activity.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of the activity to get children for"
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

                    if (string.IsNullOrEmpty(activityId))
                    {
                        return Error("activityId is required");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (!(modelService?.Root?.GetCurrentValue() is Activity rootActivity))
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the activity
                    Activity activity = FindActivity(rootActivity, activityId);
                    if (activity == null)
                    {
                        return Error($"Activity with id '{activityId}' not found");
                    }

                    // Get children
                    List<object> children = new List<object>();
                    foreach (Activity child in WorkflowInspectionServices.GetActivities(activity))
                    {
                        children.Add(new
                        {
                            id = child.Id,
                            displayName = child.DisplayName,
                            activityType = child.GetType().Name
                        });
                    }

                    return Success(new
                    {
                        parentId = activityId,
                        parentDisplayName = activity.DisplayName,
                        childrenCount = children.Count,
                        children
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to get child activities: {ex.Message}");
                }
            });
        }

        private Activity FindActivity(Activity root, string activityId)
        {
            if (root == null)
            {
                return null;
            }

            if (root.Id == activityId)
            {
                return root;
            }

            foreach (Activity child in WorkflowInspectionServices.GetActivities(root))
            {
                Activity found = FindActivity(child, activityId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}