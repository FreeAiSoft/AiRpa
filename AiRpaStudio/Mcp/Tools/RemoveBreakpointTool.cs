using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for removing breakpoints from activities
    /// </summary>
    public class RemoveBreakpointTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public RemoveBreakpointTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "remove_breakpoint",
                Description = "Removes a breakpoint from a specific activity",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of the activity to remove breakpoint from"
                        },
                        activityName = new
                        {
                            type = "string",
                            description = "Display name of the activity (alternative to activityId)"
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
                    string activityName = args["activityName"]?.ToString();

                    if (string.IsNullOrEmpty(activityId) && string.IsNullOrEmpty(activityName))
                    {
                        return Error("Either activityId or activityName must be provided");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    // Find and remove breakpoint using same logic as SetBreakpointTool
                    ModelItem targetActivity = !string.IsNullOrEmpty(activityId)
                        ? FindActivityById(modelService.Root, activityId)
                        : FindActivityByName(modelService.Root, activityName);
                    if (targetActivity == null)
                    {
                        return Error($"Activity not found: {activityId ?? activityName}");
                    }

                    // Remove breakpoint
                    _ = (targetActivity.Properties["Breakpoint"]?.SetValue(false));

                    return Success(new
                    {
                        message = "Breakpoint removed from activity",
                        activityId = targetActivity.GetCurrentValue()?.GetType().GetProperty("Id")?.GetValue(targetActivity.GetCurrentValue())?.ToString(),
                        activityName = targetActivity.Properties["DisplayName"]?.ComputedValue?.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to remove breakpoint: {ex.Message}");
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
