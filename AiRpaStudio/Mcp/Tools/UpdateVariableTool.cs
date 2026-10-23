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
    /// Tool for updating variable properties
    /// </summary>
    public class UpdateVariableTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public UpdateVariableTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "update_variable",
                Description = "Updates properties of an existing variable (name, type, default value, or scope).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        currentName = new
                        {
                            type = "string",
                            description = "Current name of the variable to update"
                        },
                        scopeActivityId = new
                        {
                            type = "string",
                            description = "ID of activity where variable is defined (optional, searches all scopes if not specified)"
                        },
                        newName = new
                        {
                            type = "string",
                            description = "New name for the variable (optional)"
                        },
                        newDefaultValue = new
                        {
                            type = "string",
                            description = "New default value for the variable (optional)"
                        }
                    },
                    required = new[] { "currentName" }
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
                    string currentName = args["currentName"]?.ToString();
                    string scopeActivityId = args["scopeActivityId"]?.ToString();
                    string newName = args["newName"]?.ToString();
                    string newDefaultValue = args["newDefaultValue"]?.ToString();

                    if (string.IsNullOrEmpty(currentName))
                    {
                        return Error("currentName is required");
                    }

                    if (string.IsNullOrEmpty(newName) && string.IsNullOrEmpty(newDefaultValue))
                    {
                        return Error("At least one property to update (newName or newDefaultValue) must be provided");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (modelService?.Root == null)
                    {
                        return Error("No root activity found in workflow");
                    }

                    bool success = false;
                    string errorMsg = null;

                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            ModelItem scopeItem = string.IsNullOrEmpty(scopeActivityId)
                                ? FindVariableScope(modelService.Root, currentName)
                                : FindModelItemById(modelService.Root, scopeActivityId);

                            if (scopeItem == null)
                            {
                                errorMsg = "Could not find scope containing the variable";
                                return;
                            }

                            ModelProperty variablesProperty = scopeItem.Properties["Variables"];
                            if (variablesProperty?.Collection == null)
                            {
                                errorMsg = "Scope does not have variables collection";
                                return;
                            }

                            // Find the variable
                            ModelItem variableItem = null;
                            foreach (ModelItem varItem in variablesProperty.Collection)
                            {
                                dynamic variable = varItem.GetCurrentValue();
                                if (variable.Name == currentName)
                                {
                                    variableItem = varItem;
                                    break;
                                }
                            }

                            if (variableItem == null)
                            {
                                errorMsg = $"Variable '{currentName}' not found in scope";
                                return;
                            }

                            // Update properties
                            dynamic variableObj = variableItem.GetCurrentValue();

                            if (!string.IsNullOrEmpty(newName))
                            {
                                variableObj.Name = newName;
                            }

                            if (!string.IsNullOrEmpty(newDefaultValue))
                            {
                                try
                                {
                                    Type varType = variableObj.GetType().GetGenericArguments()[0];
                                    object convertedValue = Convert.ChangeType(newDefaultValue, varType);
                                    variableObj.Default = convertedValue;
                                }
                                catch (Exception ex)
                                {
                                    errorMsg = $"Failed to convert default value: {ex.Message}";
                                    return;
                                }
                            }

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
                        return Error($"Failed to update variable: {errorMsg}");
                    }

                    return !success
                        ? Error("Failed to update variable")
                        : Success(new
                        {
                            message = $"Variable '{currentName}' updated successfully",
                            currentName,
                            newName = newName ?? currentName,
                            updated = true
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to update variable: {ex.Message}");
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