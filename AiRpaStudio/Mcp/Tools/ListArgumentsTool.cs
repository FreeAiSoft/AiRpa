using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing all workflow arguments
    /// </summary>
    public class ListArgumentsTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public ListArgumentsTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_arguments",
                Description = "Lists all arguments defined in the workflow with their types and directions.",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
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

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                    ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();

                    if (modelService?.Root == null)
                    {
                        return Error("No root activity found in workflow");
                    }

                    List<object> argumentsList = new List<object>();

                    // Get Properties collection from root
                    ModelProperty propertiesProperty = modelService.Root.Properties["Properties"];
                    if (propertiesProperty?.Collection != null)
                    {
                        foreach (ModelItem propItem in propertiesProperty.Collection)
                        {
                            try
                            {
                                if (!(propItem.GetCurrentValue() is DynamicActivityProperty prop))
                                {
                                    continue;
                                }

                                string direction = "Unknown";
                                string typeName = "Unknown";

                                if (prop.Type != null)
                                {
                                    // Determine direction from type
                                    if (prop.Type.IsGenericType)
                                    {
                                        Type genericTypeDef = prop.Type.GetGenericTypeDefinition();
                                        if (genericTypeDef == typeof(InArgument<>))
                                        {
                                            direction = "In";
                                        }
                                        else if (genericTypeDef == typeof(OutArgument<>))
                                        {
                                            direction = "Out";
                                        }
                                        else if (genericTypeDef == typeof(InOutArgument<>))
                                        {
                                            direction = "InOut";
                                        }

                                        // Get the actual type
                                        Type[] genericArgs = prop.Type.GetGenericArguments();
                                        if (genericArgs.Length > 0)
                                        {
                                            typeName = genericArgs[0].Name;
                                        }
                                    }
                                }

                                object defaultValue = null;
                                try
                                {
                                    if (prop.Value != null && direction != "Out")
                                    {
                                        dynamic argValue = prop.Value;
                                        defaultValue = argValue.Expression;
                                    }
                                }
                                catch
                                {
                                    // Ignore errors getting default value
                                }

                                argumentsList.Add(new
                                {
                                    name = prop.Name,
                                    direction,
                                    type = typeName,
                                    defaultValue = defaultValue?.ToString() ?? "null"
                                });
                            }
                            catch
                            {
                                // Skip properties that can't be processed
                            }
                        }
                    }

                    return Success(new
                    {
                        count = argumentsList.Count,
                        arguments = argumentsList
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to list arguments: {ex.Message}");
                }
            });
        }
    }
}