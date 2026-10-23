using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting the complete activity tree with hierarchy
    /// </summary>
    public class GetActivityTreeTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetActivityTreeTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_activity_tree",
                Description = "Gets the complete activity tree showing the hierarchical structure of all activities in the workflow.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includeDetails = new
                        {
                            type = "boolean",
                            description = "Include detailed information about each activity (default: false)",
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
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    Newtonsoft.Json.Linq.JObject args = Newtonsoft.Json.Linq.JObject.FromObject(arguments);
                    bool includeDetails = args["includeDetails"]?.ToObject<bool>() ?? false;

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService from workflow designer");
                    }

                    if (modelService.Root == null)
                    {
                        return Error("Workflow has no root element. The workflow may be empty or not fully loaded.");
                    }

                    // The root is typically an ActivityBuilder, get the Implementation property
                    ModelItem rootModelItem = modelService.Root;
                    ModelItem implementationModelItem = rootModelItem.Properties["Implementation"]?.Value;

                    if (implementationModelItem == null)
                    {
                        return Error("Workflow has no Implementation. The workflow may be empty.");
                    }

                    if (!(implementationModelItem.GetCurrentValue() is Activity rootActivity))
                    {
                        return Error("Implementation is not an Activity. The workflow may be in an invalid state.");
                    }

                    object tree = BuildActivityTree(rootActivity, includeDetails, 0);

                    return Success(new
                    {
                        workflowName = rootActivity.DisplayName,
                        tree
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to get activity tree: {ex.Message}");
                }
            });
        }
        private List<object> BuildChildrenList(Activity activity, bool includeDetails, int level)
        {
            List<object> children = new List<object>();

            // Handle special activity types with named branches
            if (activity is If ifActivity)
            {
                // Condition - extract expression from InArgument
                if (ifActivity.Condition != null && ifActivity.Condition.Expression != null)
                {
                    object conditionTree = BuildActivityTree(ifActivity.Condition.Expression, includeDetails, level + 1, "Condition");
                    if (conditionTree != null)
                    {
                        children.Add(conditionTree);
                    }
                }

                // Then branch
                if (ifActivity.Then != null)
                {
                    object thenTree = BuildActivityTree(ifActivity.Then, includeDetails, level + 1, "Then");
                    if (thenTree != null)
                    {
                        children.Add(thenTree);
                    }
                }

                // Else branch
                if (ifActivity.Else != null)
                {
                    object elseTree = BuildActivityTree(ifActivity.Else, includeDetails, level + 1, "Else");
                    if (elseTree != null)
                    {
                        children.Add(elseTree);
                    }
                }
            }
            else if (activity is While whileActivity)
            {
                // Condition - Activity<bool>
                if (whileActivity.Condition != null)
                {
                    object conditionTree = BuildActivityTree(whileActivity.Condition, includeDetails, level + 1, "Condition");
                    if (conditionTree != null)
                    {
                        children.Add(conditionTree);
                    }
                }

                // Body
                if (whileActivity.Body != null)
                {
                    object bodyTree = BuildActivityTree(whileActivity.Body, includeDetails, level + 1, "Body");
                    if (bodyTree != null)
                    {
                        children.Add(bodyTree);
                    }
                }
            }
            else if (activity is DoWhile doWhileActivity)
            {
                // Body
                if (doWhileActivity.Body != null)
                {
                    object bodyTree = BuildActivityTree(doWhileActivity.Body, includeDetails, level + 1, "Body");
                    if (bodyTree != null)
                    {
                        children.Add(bodyTree);
                    }
                }

                // Condition - Activity<bool>
                if (doWhileActivity.Condition != null)
                {
                    object conditionTree = BuildActivityTree(doWhileActivity.Condition, includeDetails, level + 1, "Condition");
                    if (conditionTree != null)
                    {
                        children.Add(conditionTree);
                    }
                }
            }
            else if (activity is TryCatch tryCatchActivity)
            {
                // Try
                if (tryCatchActivity.Try != null)
                {
                    object tryTree = BuildActivityTree(tryCatchActivity.Try, includeDetails, level + 1, "Try");
                    if (tryTree != null)
                    {
                        children.Add(tryTree);
                    }
                }

                // Note: Catches are complex generic types - use default behavior for them
                // Get remaining children (catches, finally) using default inspection
                foreach (Activity child in WorkflowInspectionServices.GetActivities(activity))
                {
                    // Skip the Try block since we already added it
                    if (child != tryCatchActivity.Try)
                    {
                        object childTree = BuildActivityTree(child, includeDetails, level + 1);
                        if (childTree != null)
                        {
                            children.Add(childTree);
                        }
                    }
                }
            }
            else
            {
                // Default behavior for other activities
                foreach (Activity child in WorkflowInspectionServices.GetActivities(activity))
                {
                    object childTree = BuildActivityTree(child, includeDetails, level + 1);
                    if (childTree != null)
                    {
                        children.Add(childTree);
                    }
                }
            }

            return children;
        }


        private object BuildActivityTree(Activity activity, bool includeDetails, int level, string branch = null)
        {
            if (activity == null)
            {
                return null;
            }

            List<object> children = BuildChildrenList(activity, includeDetails, level);

            if (includeDetails)
            {
                Dictionary<string, object> properties = new Dictionary<string, object>();
                Type activityType = activity.GetType();

                foreach (System.Reflection.PropertyInfo prop in activityType.GetProperties())
                {
                    try
                    {
                        if (prop.PropertyType.IsValueType || prop.PropertyType == typeof(string) || prop.PropertyType.IsPrimitive)
                        {
                            object value = prop.GetValue(activity);
                            properties[prop.Name] = value?.ToString() ?? "null";
                        }
                    }
                    catch
                    {
                        // Skip properties that can't be read
                    }
                }

                object result = new
                {
                    id = activity.Id,
                    displayName = activity.DisplayName,
                    activityType = activity.GetType().Name,
                    level,
                    branch,
                    childrenCount = children.Count,
                    properties,
                    children
                };
                return result;
            }
            else
            {
                object result = new
                {
                    id = activity.Id,
                    displayName = activity.DisplayName,
                    activityType = activity.GetType().Name,
                    level,
                    branch,
                    childrenCount = children.Count,
                    children
                };
                return result;
            }
        }
    }
}
