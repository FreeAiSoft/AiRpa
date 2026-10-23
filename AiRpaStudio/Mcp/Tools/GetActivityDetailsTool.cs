using System;
using System.Activities;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting detailed information about an activity
    /// </summary>
    public class GetActivityDetailsTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetActivityDetailsTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_activity_details",
                Description = "Gets detailed information about an activity including all properties, parameters, and configuration.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of activity to get details for"
                        },
                        displayName = new
                        {
                            type = "string",
                            description = "Display name of activity (alternative to activityId)"
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
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    JObject args = JObject.FromObject(arguments);
                    string activityId = args["activityId"]?.ToString();
                    string displayName = args["displayName"]?.ToString();

                    if (string.IsNullOrEmpty(activityId) && string.IsNullOrEmpty(displayName))
                    {
                        return Error("Either activityId or displayName must be provided");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (modelService?.Root == null)
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the activity
                    Activity activity = FindActivity(modelService.Root.GetCurrentValue() as Activity, activityId, displayName);
                    if (activity == null)
                    {
                        return Error($"Activity not found with {(string.IsNullOrEmpty(activityId) ? $"displayName='{displayName}'" : $"id='{activityId}'")}");
                    }

                    // Get all properties and their values
                    Dictionary<string, object> properties = GetActivityProperties(activity);

                    // Get child activities count
                    int childrenCount = WorkflowInspectionServices.GetActivities(activity).Count();

                    return Success(new
                    {
                        activityId = activity.Id,
                        displayName = activity.DisplayName,
                        activityType = activity.GetType().Name,
                        fullTypeName = activity.GetType().FullName,
                        properties,
                        childrenCount,
                        hasChildren = childrenCount > 0
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to get activity details: {ex.Message}");
                }
            });
        }

        private Activity FindActivity(Activity root, string activityId, string displayName)
        {
            if (root == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(activityId) && root.Id == activityId)
            {
                return root;
            }

            if (!string.IsNullOrEmpty(displayName) && root.DisplayName == displayName)
            {
                return root;
            }

            foreach (Activity child in WorkflowInspectionServices.GetActivities(root))
            {
                Activity found = FindActivity(child, activityId, displayName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private Dictionary<string, object> GetActivityProperties(Activity activity)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            Type activityType = activity.GetType();

            foreach (System.Reflection.PropertyInfo prop in activityType.GetProperties())
            {
                try
                {
                    // Skip complex types that might cause serialization issues
                    if (prop.PropertyType.IsValueType || prop.PropertyType == typeof(string) || prop.PropertyType.IsPrimitive)
                    {
                        object value = prop.GetValue(activity);
                        properties[prop.Name] = value?.ToString() ?? "null";
                    }
                    else if (prop.Name == "DisplayName" || prop.Name == "Id")
                    {
                        object value = prop.GetValue(activity);
                        properties[prop.Name] = value?.ToString() ?? "null";
                    }
                    else
                    {
                        // For complex types, just show the type name
                        properties[prop.Name] = $"<{prop.PropertyType.Name}>";
                    }
                }
                catch
                {
                    properties[prop.Name] = "<error reading value>";
                }
            }

            return properties;
        }
    }
}