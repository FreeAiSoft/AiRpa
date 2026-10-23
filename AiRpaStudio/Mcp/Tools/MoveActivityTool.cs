using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;
using RPA.Views;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for moving activities within the workflow
    /// </summary>
    public class MoveActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public MoveActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "move_activity",
                Description = "Moves an activity to a different position within the workflow. Can change order within the same container or move to a different container.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of activity to move"
                        },
                        targetContainerId = new
                        {
                            type = "string",
                            description = "ID of target container activity (optional, uses current parent if not specified)"
                        },
                        newIndex = new
                        {
                            type = "integer",
                            description = "New index position in target container (0-based)"
                        }
                    },
                    required = new[] { "activityId", "newIndex" }
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
                    string targetContainerId = args["targetContainerId"]?.ToString();
                    int? newIndex = args["newIndex"]?.ToObject<int>();

                    if (string.IsNullOrEmpty(activityId))
                    {
                        return Error("activityId is required");
                    }

                    if (!newIndex.HasValue || newIndex.Value < 0)
                    {
                        return Error("newIndex must be a non-negative integer");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (!(modelService?.Root?.GetCurrentValue() is Activity rootActivity))
                    {
                        return Error("No root activity found in workflow");
                    }

                    // Find the activity to move
                    ModelItem activityToMove = FindModelItemById(modelService.Root, activityId);
                    if (activityToMove == null)
                    {
                        return Error($"Activity with id '{activityId}' not found");
                    }

                    // Find parent and target containers
                    ModelItem sourceParent = activityToMove.Parent;
                    ModelItem targetParent = string.IsNullOrEmpty(targetContainerId)
                        ? sourceParent
                        : FindModelItemById(modelService.Root, targetContainerId);

                    if (targetParent == null)
                    {
                        return Error($"Target container with id '{targetContainerId}' not found");
                    }

                    // Execute move operation on UI thread
                    bool success = false;
                    string errorMsg = null;

                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            success = MoveActivity(activityToMove, sourceParent, targetParent, newIndex.Value);
                            if (success)
                            {
                                activeDesigner.Flush();
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMsg = ex.Message;
                        }
                    });

                    if (!string.IsNullOrEmpty(errorMsg))
                    {
                        return Error($"Failed to move activity: {errorMsg}");
                    }

                    if (!success)
                    {
                        return Error("Failed to move activity");
                    }

                    Activity movedActivity = activityToMove.GetCurrentValue() as Activity;
                    return Success(new
                    {
                        message = $"Activity '{movedActivity?.DisplayName}' moved successfully",
                        activityId,
                        newIndex = newIndex.Value
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to move activity: {ex.Message}");
                }
            });
        }

        private bool MoveActivity(ModelItem activity, ModelItem sourceParent, ModelItem targetParent, int newIndex)
        {
            try
            {
                ModelItemCollection sourceCollection = GetActivitiesCollection(sourceParent);
                ModelItemCollection targetCollection = GetActivitiesCollection(targetParent);

                if (sourceCollection == null || targetCollection == null)
                {
                    return false;
                }

                // Remove from source
                _ = sourceCollection.Remove(activity);

                // Add to target at specified index
                if (newIndex >= targetCollection.Count)
                {
                    targetCollection.Add(activity);
                }
                else
                {
                    targetCollection.Insert(newIndex, activity);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private ModelItemCollection GetActivitiesCollection(ModelItem parent)
        {
            if (parent == null)
            {
                return null;
            }

            // Try common collection property names
            if (parent.Properties["Activities"]?.Collection != null)
            {
                return parent.Properties["Activities"].Collection;
            }

            if (parent.Properties["Branches"]?.Collection != null)
            {
                return parent.Properties["Branches"].Collection;
            }

            return parent.Properties["Nodes"]?.Collection != null ? parent.Properties["Nodes"].Collection : null;
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

            // Search in properties
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