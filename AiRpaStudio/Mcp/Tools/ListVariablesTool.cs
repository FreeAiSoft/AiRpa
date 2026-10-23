using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing all variables in the workflow
    /// </summary>
    public class ListVariablesTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public ListVariablesTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_variables",
                Description = "Lists all variables in the workflow with their scopes and types.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        scopeActivityId = new
                        {
                            type = "string",
                            description = "ID of specific activity to list variables for (optional, lists all if not specified)"
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
                    string scopeActivityId = args["scopeActivityId"]?.ToString();

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (modelService?.Root == null)
                    {
                        return Error("No root activity found in workflow");
                    }

                    List<object> variables = new List<object>();

                    if (string.IsNullOrEmpty(scopeActivityId))
                    {
                        // List all variables from all scopes
                        CollectAllVariables(modelService.Root, variables);
                    }
                    else
                    {
                        // List variables from specific scope
                        ModelItem scopeItem = FindModelItemById(modelService.Root, scopeActivityId);
                        if (scopeItem == null)
                        {
                            return Error($"Activity with id '{scopeActivityId}' not found");
                        }

                        CollectVariablesFromScope(scopeItem, variables);
                    }

                    return Success(new
                    {
                        count = variables.Count,
                        variables
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to list variables: {ex.Message}");
                }
            });
        }

        private void CollectAllVariables(ModelItem item, List<object> variables)
        {
            if (item == null)
            {
                return;
            }

            // Get variables from current item
            CollectVariablesFromScope(item, variables);

            // Recursively process children
            foreach (ModelProperty property in item.Properties)
            {
                if (property.Value != null)
                {
                    CollectAllVariables(property.Value, variables);
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem childItem in property.Collection)
                    {
                        CollectAllVariables(childItem, variables);
                    }
                }
            }
        }

        private void CollectVariablesFromScope(ModelItem scopeItem, List<object> variables)
        {
            if (scopeItem == null)
            {
                return;
            }

            ModelProperty variablesProperty = scopeItem.Properties["Variables"];
            if (variablesProperty?.Collection == null)
            {
                return;
            }

            Activity scopeActivity = scopeItem.GetCurrentValue() as Activity;
            string scopeName = scopeActivity?.DisplayName ?? "Unknown";
            string scopeId = scopeActivity?.Id ?? "Unknown";

            foreach (ModelItem varItem in variablesProperty.Collection)
            {
                try
                {
                    dynamic variable = varItem.GetCurrentValue();
                    string varName = variable.Name?.ToString() ?? "Unnamed";
                    string varType = variable.GetType().GetGenericArguments()[0].Name;
                    object defaultValue = null;

                    try
                    {
                        defaultValue = variable.Default;
                    }
                    catch
                    {
                        // Ignore errors getting default value
                    }

                    variables.Add(new
                    {
                        name = varName,
                        type = varType,
                        defaultValue = defaultValue?.ToString() ?? "null",
                        scope = new
                        {
                            activityId = scopeId,
                            activityName = scopeName
                        }
                    });
                }
                catch
                {
                    // Skip variables that can't be processed
                }
            }
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