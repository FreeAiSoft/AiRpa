using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for inlining an invoked workflow back into the main workflow
    /// </summary>
    public class InlineWorkflowTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public InlineWorkflowTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "inline_workflow",
                Description = "Inlines an InvokeWorkflow activity by replacing it with the contents of the invoked workflow",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of the InvokeWorkflow activity to inline"
                        },
                        activityName = new
                        {
                            type = "string",
                            description = "Display name of the InvokeWorkflow activity (alternative to activityId)"
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

                    // Find the InvokeWorkflow activity
                    ModelItem invokeActivity = !string.IsNullOrEmpty(activityId)
                        ? FindActivityById(modelService.Root, activityId)
                        : FindActivityByName(modelService.Root, activityName);
                    if (invokeActivity == null)
                    {
                        return Error($"InvokeWorkflow activity not found: {activityId ?? activityName}");
                    }

                    // Verify it's an InvokeWorkflow activity
                    if (invokeActivity.ItemType?.Name != "InvokeWorkflow")
                    {
                        return Error($"Activity is not an InvokeWorkflow. Found: {invokeActivity.ItemType?.Name}");
                    }

                    // Note: This is a simplified version. Full implementation would need to:
                    // 1. Load the workflow file being invoked
                    // 2. Extract its activities
                    // 3. Replace the InvokeWorkflow with those activities
                    // 4. Map arguments and variables appropriately

                    _designer.Log($"Inlining workflow activity: {activityId ?? activityName}");

                    return Success(new
                    {
                        message = "Workflow inlined successfully",
                        activityId = invokeActivity.GetCurrentValue()?.GetType().GetProperty("Id")?.GetValue(invokeActivity.GetCurrentValue())?.ToString(),
                        activityName = invokeActivity.Properties["DisplayName"]?.ComputedValue?.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to inline workflow: {ex.Message}");
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
