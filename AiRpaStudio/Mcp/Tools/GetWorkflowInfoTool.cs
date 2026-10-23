using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting information about the current workflow
    /// </summary>
    public class GetWorkflowInfoTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetWorkflowInfoTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_workflow_info",
                Description = "Gets information about the current workflow including file path, root activity type, and optionally the list of activities.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includeActivities = new
                        {
                            type = "boolean",
                            description = "Include detailed list of activities in workflow",
                            @default = false
                        }
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

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;

                    // Parse arguments
                    JObject args = JObject.FromObject(arguments);
                    bool includeActivities = args["includeActivities"]?.ToObject<bool>() ?? false;

                    // Get workflow file info
                    WorkflowFileItem fileItem = activeDesigner.Context.Items.GetValue<WorkflowFileItem>();
                    string filePath = fileItem?.LoadedFile ?? "Unsaved workflow";

                    // Get root activity
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();
                    Activity rootActivity = modelService?.Root?.GetCurrentValue() as Activity;

                    string rootActivityType = rootActivity?.GetType().Name ?? "Unknown";
                    string rootDisplayName = rootActivity?.DisplayName ?? "Root";

                    // Build result
                    object result = new
                    {
                        filePath,
                        isLoaded = !string.IsNullOrEmpty(fileItem?.LoadedFile),
                        rootActivityType,
                        rootDisplayName,
                        activityCount = includeActivities ? CountActivities(rootActivity) : 0,
                        activities = includeActivities ? GetActivitiesList(rootActivity) : null
                    };

                    return Success(result);
                }
                catch (Exception ex)
                {
                    return Error($"Failed to get workflow info: {ex.Message}");
                }
            });
        }

        private int CountActivities(Activity root)
        {
            if (root == null)
            {
                return 0;
            }

            int count = 1; // Count the root itself

            // Get all child activities recursively
            IEnumerable<Activity> children = WorkflowInspectionServices.GetActivities(root);
            count += children.Count();

            return count;
        }

        private List<object> GetActivitiesList(Activity root)
        {
            List<object> activities = new List<object>();

            if (root == null)
            {
                return activities;
            }

            // Add activities recursively
            AddActivityInfo(activities, root, 0);

            return activities;
        }

        private void AddActivityInfo(List<object> list, Activity activity, int level)
        {
            if (activity == null)
            {
                return;
            }

            list.Add(new
            {
                type = activity.GetType().Name,
                displayName = activity.DisplayName,
                id = activity.Id,
                level
            });

            // Get child activities
            IEnumerable<Activity> children = WorkflowInspectionServices.GetActivities(activity);
            foreach (Activity child in children)
            {
                AddActivityInfo(list, child, level + 1);
            }
        }
    }
}
