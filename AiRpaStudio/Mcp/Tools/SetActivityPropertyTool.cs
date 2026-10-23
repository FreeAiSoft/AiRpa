using System;
using System.Activities;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for setting individual activity properties with more granular control
    /// </summary>
    public class SetActivityPropertyTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public SetActivityPropertyTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "set_activity_property",
                Description = "Sets a single property of an activity. More granular than update_activity for setting individual properties.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of activity to update"
                        },
                        displayName = new
                        {
                            type = "string",
                            description = "Display name of activity (alternative to activityId)"
                        },
                        propertyName = new
                        {
                            type = "string",
                            description = "Name of the property to set"
                        },
                        propertyValue = new
                        {
                            type = "string",
                            description = "Value to set (will be converted to appropriate type)"
                        }
                    },
                    required = new[] { "propertyName", "propertyValue" }
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
                    string propertyName = args["propertyName"]?.ToString();
                    string propertyValue = args["propertyValue"]?.ToString();

                    if (string.IsNullOrEmpty(activityId) && string.IsNullOrEmpty(displayName))
                    {
                        return Error("Either activityId or displayName must be provided");
                    }

                    if (string.IsNullOrEmpty(propertyName))
                    {
                        return Error("propertyName is required");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (!(modelService?.Root?.GetCurrentValue() is Activity rootActivity))
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the activity
                    Activity activity = FindActivity(rootActivity, activityId, displayName);
                    if (activity == null)
                    {
                        return Error($"Activity not found with {(string.IsNullOrEmpty(activityId) ? $"displayName='{displayName}'" : $"id='{activityId}'")}");
                    }

                    // Set the property
                    bool success = SetProperty(activity, propertyName, propertyValue);

                    return !success
                        ? Error($"Failed to set property '{propertyName}'. Property may not exist or value type may be invalid.")
                        : Success(new
                        {
                            message = $"Property '{propertyName}' set successfully on activity '{activity.DisplayName}'",
                            activityId = activity.Id,
                            activityDisplayName = activity.DisplayName,
                            propertyName,
                            propertyValue
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to set activity property: {ex.Message}");
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

        private bool SetProperty(Activity activity, string propertyName, string propertyValue)
        {
            try
            {
                Type activityType = activity.GetType();
                System.Reflection.PropertyInfo property = activityType.GetProperty(propertyName);

                if (property == null || !property.CanWrite)
                {
                    return false;
                }

                // Convert value to appropriate type
                object convertedValue;

                if (property.PropertyType == typeof(string))
                {
                    convertedValue = propertyValue;
                }
                else if (property.PropertyType == typeof(int))
                {
                    convertedValue = int.Parse(propertyValue);
                }
                else if (property.PropertyType == typeof(bool))
                {
                    convertedValue = bool.Parse(propertyValue);
                }
                else if (property.PropertyType == typeof(double))
                {
                    convertedValue = double.Parse(propertyValue);
                }
                else if (property.PropertyType == typeof(TimeSpan))
                {
                    convertedValue = TimeSpan.Parse(propertyValue);
                }
                else
                {
                    // Try generic conversion
                    convertedValue = Convert.ChangeType(propertyValue, property.PropertyType);
                }

                property.SetValue(activity, convertedValue);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}