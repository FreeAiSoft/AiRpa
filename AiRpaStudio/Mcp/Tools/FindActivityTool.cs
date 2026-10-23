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
    /// Tool for finding activities by various criteria
    /// </summary>
    public class FindActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public FindActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "find_activity",
                Description = "Finds activities matching specific criteria (type, display name, or property values).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityType = new
                        {
                            type = "string",
                            description = "Type of activity to find (e.g., 'Sequence', 'WriteLine')"
                        },
                        displayName = new
                        {
                            type = "string",
                            description = "Display name to search for (supports partial match)"
                        },
                        propertyName = new
                        {
                            type = "string",
                            description = "Property name to search in"
                        },
                        propertyValue = new
                        {
                            type = "string",
                            description = "Property value to match"
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
                    string activityType = args["activityType"]?.ToString();
                    string displayName = args["displayName"]?.ToString();
                    string propertyName = args["propertyName"]?.ToString();
                    string propertyValue = args["propertyValue"]?.ToString();

                    if (string.IsNullOrEmpty(activityType) && string.IsNullOrEmpty(displayName) &&
                        string.IsNullOrEmpty(propertyName))
                    {
                        return Error("At least one search criterion must be provided");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (!(modelService?.Root?.GetCurrentValue() is Activity rootActivity))
                    {
                        return Error("No root activity found in workflow");
                    }

                    List<object> matches = new List<object>();
                    SearchActivities(rootActivity, activityType, displayName, propertyName, propertyValue, matches);

                    return Success(new
                    {
                        count = matches.Count,
                        matches
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to find activities: {ex.Message}");
                }
            });
        }

        private void SearchActivities(Activity activity, string activityType, string displayName,
            string propertyName, string propertyValue, List<object> matches)
        {
            if (activity == null)
            {
                return;
            }

            bool isMatch = true;

            // Check activity type
            if (!string.IsNullOrEmpty(activityType))
            {
                if (!activity.GetType().Name.Equals(activityType, StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = false;
                }
            }

            // Check display name
            if (isMatch && !string.IsNullOrEmpty(displayName))
            {
                if (activity.DisplayName.IndexOf(displayName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    isMatch = false;
                }
            }

            // Check property
            if (isMatch && !string.IsNullOrEmpty(propertyName))
            {
                System.Reflection.PropertyInfo prop = activity.GetType().GetProperty(propertyName);
                if (prop == null)
                {
                    isMatch = false;
                }
                else if (!string.IsNullOrEmpty(propertyValue))
                {
                    try
                    {
                        object value = prop.GetValue(activity);
                        if (value == null || value.ToString().IndexOf(propertyValue, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            isMatch = false;
                        }
                    }
                    catch
                    {
                        isMatch = false;
                    }
                }
            }

            if (isMatch)
            {
                matches.Add(new
                {
                    id = activity.Id,
                    displayName = activity.DisplayName,
                    activityType = activity.GetType().Name,
                    fullTypeName = activity.GetType().FullName
                });
            }

            // Search children recursively
            foreach (Activity child in WorkflowInspectionServices.GetActivities(activity))
            {
                SearchActivities(child, activityType, displayName, propertyName, propertyValue, matches);
            }
        }
    }
}
