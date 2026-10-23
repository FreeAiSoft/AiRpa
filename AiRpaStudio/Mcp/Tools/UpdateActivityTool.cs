using System;
using System.Activities;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for updating parameters of existing activities
    /// </summary>
    public class UpdateActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public UpdateActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "update_activity",
                Description = "Updates parameters of an existing activity in the workflow.",
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
                            description = "Display name of activity to update (alternative to activityId)"
                        },
                        parameters = new
                        {
                            type = "object",
                            description = "New parameter values to set",
                            additionalProperties = true
                        }
                    },
                    required = new[] { "parameters" }
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

                    if (!(args["parameters"] is JObject parameters) || parameters.Count == 0)
                    {
                        return Error("parameters object is required and must contain at least one property");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (!(modelService?.Root?.GetCurrentValue() is Activity rootActivity))
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the activity to update
                    Activity activityToUpdate = FindActivity(rootActivity, activityId, displayName);

                    if (activityToUpdate == null)
                    {
                        return Error($"Activity not found with {(string.IsNullOrEmpty(activityId) ? $"displayName='{displayName}'" : $"id='{activityId}'")}");
                    }

                    // Update parameters
                    int updatedCount = UpdateActivityParameters(activityToUpdate, parameters);

                    _designer.Log($"Updated activity: {activityToUpdate.DisplayName} (ID: {activityToUpdate.Id}) - {updatedCount} parameter(s) changed");

                    return Success(new
                    {
                        message = $"Activity '{activityToUpdate.DisplayName}' updated successfully",
                        activityType = activityToUpdate.GetType().Name,
                        activityId = activityToUpdate.Id,
                        activityDisplayName = activityToUpdate.DisplayName,
                        parametersUpdated = updatedCount
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to update activity: {ex.Message}");
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

        private int UpdateActivityParameters(Activity activity, JObject parameters)
        {
            int updatedCount = 0;
            Type activityType = activity.GetType();

            foreach (System.Collections.Generic.KeyValuePair<string, JToken> param in parameters)
            {
                string propertyName = param.Key;
                object value = param.Value?.ToObject<object>();

                // Try to set the property
                System.Reflection.PropertyInfo property = activityType.GetProperty(propertyName);
                if (property != null && property.CanWrite)
                {
                    try
                    {
                        // Convert value to property type if needed
                        object convertedValue = Convert.ChangeType(value, property.PropertyType);
                        property.SetValue(activity, convertedValue);
                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        _designer.Log($"Failed to update property '{propertyName}': {ex.Message}", (int)RpaCommon.Enums.LogLevel.Warn);
                    }
                }
                else
                {
                    _designer.Log($"Property '{propertyName}' not found or not writable on activity type '{activityType.Name}'", (int)RpaCommon.Enums.LogLevel.Warn);
                }
            }

            return updatedCount;
        }
    }
}
