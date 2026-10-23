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
    /// Tool for deleting variables from workflow
    /// </summary>
    public class DeleteVariableTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public DeleteVariableTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "delete_variable",
                Description = "Deletes a variable from its scope.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new
                        {
                            type = "string",
                            description = "Name of the variable to delete"
                        },
                        scopeActivityId = new
                        {
                            type = "string",
                            description = "ID of activity where variable is defined (optional, searches all scopes if not specified)"
                        }
                    },
                    required = new[] { "name" }
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
                    string name = args["name"]?.ToString();
                    string scopeActivityId = args["scopeActivityId"]?.ToString();

                    if (string.IsNullOrEmpty(name))
                    {
                        return Error("Variable name is required");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (modelService?.Root == null)
                    {
                        return Error("No root activity found in workflow");
                    }

                    bool success = false;
                    string errorMsg = null;
                    string scopeName = null;

                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            ModelItem scopeItem = string.IsNullOrEmpty(scopeActivityId)
                                ? FindVariableScope(modelService.Root, name)
                                : FindModelItemById(modelService.Root, scopeActivityId);

                            if (scopeItem == null)
                            {
                                errorMsg = "Could not find scope containing the variable";
                                return;
                            }

                            Activity scopeActivity = scopeItem.GetCurrentValue() as Activity;
                            scopeName = scopeActivity?.DisplayName ?? "Unknown";

                            ModelProperty variablesProperty = scopeItem.Properties["Variables"];
                            if (variablesProperty?.Collection == null)
                            {
                                errorMsg = "Scope does not have variables collection";
                                return;
                            }

                            // Find and remove the variable
                            ModelItem variableToRemove = null;
                            foreach (ModelItem varItem in variablesProperty.Collection)
                            {
                                dynamic variable = varItem.GetCurrentValue();
                                if (variable.Name == name)
                                {
                                    variableToRemove = varItem;
                                    break;
                                }
                            }

                            if (variableToRemove == null)
                            {
                                errorMsg = $"Variable '{name}' not found in scope";
                                return;
                            }

                            _ = variablesProperty.Collection.Remove(variableToRemove);
                            activeDesigner.Flush();
                            success = true;
                        }
                        catch (Exception ex)
                        {
                            errorMsg = ex.Message;
                        }
                    });

                    if (!string.IsNullOrEmpty(errorMsg))
                    {
                        return Error($"Failed to delete variable: {errorMsg}");
                    }

                    return !success
                        ? Error("Failed to delete variable")
                        : Success(new
                        {
                            message = $"Variable '{name}' deleted successfully from scope '{scopeName}'",
                            name,
                            scope = scopeName
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to delete variable: {ex.Message}");
                }
            });
        }

        private ModelItem FindVariableScope(ModelItem item, string variableName)
        {
            if (item == null)
            {
                return null;
            }

            ModelProperty variablesProperty = item.Properties["Variables"];
            if (variablesProperty?.Collection != null)
            {
                foreach (ModelItem varItem in variablesProperty.Collection)
                {
                    dynamic variable = varItem.GetCurrentValue();
                    if (variable.Name == variableName)
                    {
                        return item;
                    }
                }
            }

            foreach (ModelProperty property in item.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem found = FindVariableScope(property.Value, variableName);
                    if (found != null)
                    {
                        return found;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem childItem in property.Collection)
                    {
                        ModelItem found = FindVariableScope(childItem, variableName);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
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