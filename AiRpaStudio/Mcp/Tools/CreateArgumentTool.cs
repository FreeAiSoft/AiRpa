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
    /// Tool for creating workflow arguments
    /// </summary>
    public class CreateArgumentTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public CreateArgumentTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "create_argument",
                Description = "Creates a new argument for the workflow. Arguments are used to pass data in and out of workflows.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new
                        {
                            type = "string",
                            description = "Name of the argument"
                        },
                        direction = new
                        {
                            type = "string",
                            description = "Direction of the argument",
                            @enum = new[] { "In", "Out", "InOut" }
                        },
                        argumentType = new
                        {
                            type = "string",
                            description = "Type of the argument (e.g., String, Int32, Boolean)",
                            @default = "String"
                        },
                        defaultValue = new
                        {
                            type = "string",
                            description = "Default value for the argument (optional)"
                        }
                    },
                    required = new[] { "name", "direction" }
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
                    string direction = args["direction"]?.ToString();
                    string argumentType = args["argumentType"]?.ToString() ?? "String";
                    string defaultValue = args["defaultValue"]?.ToString();

                    if (string.IsNullOrEmpty(name))
                    {
                        return Error("Argument name is required");
                    }

                    if (string.IsNullOrEmpty(direction))
                    {
                        return Error("Argument direction is required");
                    }

                    ArgumentDirection argDirection;
                    switch (direction.ToLower())
                    {
                        case "in":
                            argDirection = ArgumentDirection.In;
                            break;
                        case "out":
                            argDirection = ArgumentDirection.Out;
                            break;
                        case "inout":
                            argDirection = ArgumentDirection.InOut;
                            break;
                        default:
                            return Error($"Invalid direction '{direction}'. Must be In, Out, or InOut");
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
                            // Get Properties collection from root
                            ModelProperty propertiesProperty = modelService.Root.Properties["Properties"];
                            if (propertiesProperty?.Collection == null)
                            {
                                errorMsg = "Root activity does not support properties/arguments";
                                return;
                            }

                            ModelItemCollection propertiesCollection = propertiesProperty.Collection;

                            // Check if argument already exists
                            foreach (ModelItem propItem in propertiesCollection)
                            {
                                dynamic prop = propItem.GetCurrentValue();
                                if (prop.Name == name)
                                {
                                    errorMsg = $"Argument '{name}' already exists";
                                    return;
                                }
                            }

                            // Get type
                            Type type = GetTypeFromString(argumentType);
                            if (type == null)
                            {
                                errorMsg = $"Unknown type: {argumentType}";
                                return;
                            }

                            // Create appropriate argument type
                            Type argumentGenericType;
                            switch (argDirection)
                            {
                                case ArgumentDirection.In:
                                    argumentGenericType = typeof(InArgument<>).MakeGenericType(type);
                                    break;
                                case ArgumentDirection.Out:
                                    argumentGenericType = typeof(OutArgument<>).MakeGenericType(type);
                                    break;
                                case ArgumentDirection.InOut:
                                    argumentGenericType = typeof(InOutArgument<>).MakeGenericType(type);
                                    break;
                                default:
                                    errorMsg = "Invalid argument direction";
                                    return;
                            }

                            dynamic newArgument = Activator.CreateInstance(argumentGenericType);

                            // Set default value if provided and direction allows it
                            if (!string.IsNullOrEmpty(defaultValue) && argDirection != ArgumentDirection.Out)
                            {
                                try
                                {
                                    object convertedValue = Convert.ChangeType(defaultValue, type);
                                    newArgument.Expression = convertedValue;
                                }
                                catch
                                {
                                    // Ignore default value conversion errors
                                }
                            }

                            // Create DynamicActivityProperty
                            DynamicActivityProperty property = new DynamicActivityProperty
                            {
                                Name = name,
                                Type = argumentGenericType,
                                Value = newArgument
                            };

                            // Add to collection
                            ModelTreeManager modelTreeManager = activeDesigner.Context.Services.GetService<ModelTreeManager>();
                            ModelItem propertyModelItem = modelTreeManager.CreateModelItem(null, property);
                            propertiesCollection.Add(propertyModelItem);

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
                        return Error($"Failed to create argument: {errorMsg}");
                    }

                    return !success
                        ? Error("Failed to create argument")
                        : Success(new
                        {
                            message = $"Argument '{name}' created successfully",
                            name,
                            direction,
                            argumentType
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to create argument: {ex.Message}");
                }
            });
        }

        private Type GetTypeFromString(string typeName)
        {
            System.Collections.Generic.Dictionary<string, Type> typeMap = new System.Collections.Generic.Dictionary<string, Type>
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

            try
            {
                return Type.GetType(typeName) ?? Type.GetType($"System.{typeName}");
            }
            catch
            {
                return null;
            }
        }
    }
}