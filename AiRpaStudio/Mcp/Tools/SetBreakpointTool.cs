using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for setting breakpoints on activities
    /// </summary>
    public class SetBreakpointTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public SetBreakpointTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "set_breakpoint",
                Description = "Sets a breakpoint on a specific activity in the workflow for debugging purposes",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of the activity to set breakpoint on"
                        },
                        activityName = new
                        {
                            type = "string",
                            description = "Display name of the activity (alternative to activityId)"
                        },
                        enabled = new
                        {
                            type = "boolean",
                            description = "Whether the breakpoint is enabled",
                            @default = true
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
                    bool enabled = args["enabled"]?.ToObject<bool>() ?? true;

                    if (string.IsNullOrEmpty(activityId) && string.IsNullOrEmpty(activityName))
                    {
                        return Error("Either activityId or activityName must be provided");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    // Find activity
                    ModelItem targetActivity = !string.IsNullOrEmpty(activityId)
                        ? FindActivityById(modelService.Root, activityId)
                        : FindActivityByName(modelService.Root, activityName);
                    if (targetActivity == null)
                    {
                        return Error($"Activity not found: {activityId ?? activityName}");
                    }

                    // Set breakpoint metadata
                    _ = (targetActivity.Properties["Breakpoint"]?.SetValue(enabled));

                    return Success(new
                    {
                        message = $"Breakpoint {(enabled ? "set" : "disabled")} on activity",
                        activityId = targetActivity.GetCurrentValue()?.GetType().GetProperty("Id")?.GetValue(targetActivity.GetCurrentValue())?.ToString(),
                        activityName = targetActivity.Properties["DisplayName"]?.ComputedValue?.ToString(),
                        enabled
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to set breakpoint: {ex.Message}");
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
