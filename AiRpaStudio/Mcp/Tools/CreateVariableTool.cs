using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;
using RPA.Views;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for creating variables in workflow activities
    /// </summary>
    public class CreateVariableTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public CreateVariableTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "create_variable",
                Description = "Creates a new variable in the specified activity scope.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new
                        {
                            type = "string",
                            description = "Name of the variable"
                        },
                        variableType = new
                        {
                            type = "string",
                            description = "Type of the variable (e.g., String, Int32, Boolean, List<String>)",
                            @default = "String"
                        },
                        defaultValue = new
                        {
                            type = "string",
                            description = "Default value for the variable (optional)"
                        },
                        scopeActivityId = new
                        {
                            type = "string",
                            description = "ID of activity to use as scope (optional, uses root if not specified)"
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
                    string variableType = args["variableType"]?.ToString() ?? "String";
                    string defaultValue = args["defaultValue"]?.ToString();
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

                    // Find scope activity
                    ModelItem scopeModelItem = string.IsNullOrEmpty(scopeActivityId)
                        ? modelService.Root.Properties["Implementation"]?.Value
                        : FindModelItemById(modelService.Root, scopeActivityId);

                    if (scopeModelItem == null)
                    {
                        scopeModelItem = modelService.Root.Properties["Implementation"]?.Value;
                        if (scopeModelItem == null)
                        {
                            return Error("Could not find scope for variable. Workflow may not have an implementation.");
                        }
                    }

                    // Create variable
                    bool success = false;
                    string errorMsg = null;

                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Get or create Variables collection
                            ModelProperty variablesProperty = scopeModelItem.Properties["Variables"];
                            if (variablesProperty == null)
                            {
                                errorMsg = "Scope activity does not support variables";
                                return;
                            }

                            ModelItemCollection variablesCollection = variablesProperty.Collection;
                            if (variablesCollection == null)
                            {
                                errorMsg = "Could not access variables collection";
                                return;
                            }

                            // Check if variable already exists
                            foreach (ModelItem varItem in variablesCollection)
                            {
                                dynamic variable = varItem.GetCurrentValue();
                                if (variable.Name == name)
                                {
                                    errorMsg = $"Variable '{name}' already exists in this scope";
                                    return;
                                }
                            }

                            // Create variable based on type
                            Type type = GetTypeFromString(variableType);
                            if (type == null)
                            {
                                errorMsg = $"Unknown type: {variableType}";
                                return;
                            }

                            // Create Variable<T> using reflection
                            Type variableGenericType = typeof(Variable<>).MakeGenericType(type);
                            dynamic newVariable = Activator.CreateInstance(variableGenericType);
                            newVariable.Name = name;

                            // Set default value if provided
                            if (!string.IsNullOrEmpty(defaultValue))
                            {
                                try
                                {
                                    object convertedValue = Convert.ChangeType(defaultValue, type);
                                    newVariable.Default = convertedValue;
                                }
                                catch
                                {
                                    // Ignore default value conversion errors
                                }
                            }

                            // Add to collection
                            ModelTreeManager modelTreeManager = activeDesigner.Context.Services.GetService<ModelTreeManager>();
                            ModelItem variableModelItem = modelTreeManager.CreateModelItem(null, newVariable);
                            variablesCollection.Add(variableModelItem);

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
                        return Error($"Failed to create variable: {errorMsg}");
                    }

                    return !success
                        ? Error("Failed to create variable")
                        : Success(new
                        {
                            message = $"Variable '{name}' created successfully",
                            name,
                            variableType,
                            scope = scopeActivityId ?? "root"
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to create variable: {ex.Message}");
                }
            });
        }

        private Type GetTypeFromString(string typeName)
        {
            // Common types mapping
            Dictionary<string, Type> typeMap = new Dictionary<string, Type>
            {
                ["String"] = typeof(string),
                ["Int32"] = typeof(int),
                ["Int64"] = typeof(long),
                ["Boolean"] = typeof(bool),
                ["Double"] = typeof(double),
                ["Decimal"] = typeof(decimal),
                ["DateTime"] = typeof(DateTime),
                ["Object"] = typeof(object)
            };

            if (typeMap.TryGetValue(typeName, out Type type))
            {
                return type;
            }

            // Try to get type from loaded assemblies
            try
            {
                return Type.GetType(typeName) ?? Type.GetType($"System.{typeName}");
            }
            catch
            {
                return null;
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