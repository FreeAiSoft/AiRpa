using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting the parent activity of a given activity
    /// </summary>
    public class GetParentActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetParentActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_parent_activity",
                Description = "Gets the parent activity of a specified activity.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of the activity to get parent for"
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

                    if (modelService?.Root == null)
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the ModelItem for the activity
                    ModelItem activityItem = FindModelItemById(modelService.Root, activityId);
                    if (activityItem == null)
                    {
                        return Error($"Activity with id '{activityId}' not found");
                    }

                    // Get parent
                    ModelItem parentItem = activityItem.Parent;
                    if (parentItem == null)
                    {
                        return Success(new
                        {
                            message = "Activity has no parent (it is the root)",
                            hasParent = false
                        });
                    }

                    return !(parentItem.GetCurrentValue() is Activity parentActivity)
                        ? Error("Parent is not an activity")
                        : Success(new
                        {
                            hasParent = true,
                            parent = new
                            {
                                id = parentActivity.Id,
                                displayName = parentActivity.DisplayName,
                                activityType = parentActivity.GetType().Name
                            }
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to get parent activity: {ex.Message}");
                }
            });
        }

        private ModelItem FindModelItemById(ModelItem root, string id)
        {
            if (root == null)
            {
                return null;
            }

            Activity activity = root.GetCurrentValue() as Activity;
            if (activity?.Id == id)
            {
                return root;
            }

            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem found = FindModelItemById(property.Value, id);
                    if (found != null)
                    {
                        return found;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        ModelItem found = FindModelItemById(item, id);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }
    }
}