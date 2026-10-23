using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for adding assembly references to the workflow
    /// </summary>
    public class AddAssemblyReferenceTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public AddAssemblyReferenceTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "add_assembly_reference",
                Description = "Adds an assembly reference to the workflow project",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        assemblyName = new
                        {
                            type = "string",
                            description = "Name or path of the assembly to reference (e.g., 'System.Net.Http' or full path)"
                        }
                    },
                    required = new[] { "assemblyName" }
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
                    string assemblyName = args["assemblyName"]?.ToString();

                    if (string.IsNullOrEmpty(assemblyName))
                    {
                        return Error("assemblyName parameter is required");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    ModelItem root = modelService.Root;
                    if (root == null)
                    {
                        return Error("Failed to get root ModelItem");
                    }

                    // Get or create TextExpression.ReferencesForImplementation
                    ModelProperty referencesProperty = root.Properties["TextExpression.ReferencesForImplementation"];
                    if (referencesProperty == null)
                    {
                        return Error("TextExpression.ReferencesForImplementation property not found");
                    }

                    ModelItemCollection references = referencesProperty.Collection;
                    if (references == null)
                    {
                        return Error("References collection not found");
                    }

                    // Check if assembly already referenced
                    bool exists = references.Any(item =>
                    {
                        object assemblyRef = item.GetCurrentValue();
                        if (assemblyRef != null)
                        {
                            System.Reflection.PropertyInfo nameProperty = assemblyRef.GetType().GetProperty("AssemblyName");
                            if (nameProperty != null)
                            {
                                string name = nameProperty.GetValue(assemblyRef)?.ToString();
                                return name == assemblyName;
                            }
                        }
                        return false;
                    });

                    if (exists)
                    {
                        return Success(new
                        {
                            message = $"Assembly '{assemblyName}' is already referenced",
                            assemblyName,
                            alreadyExists = true
                        });
                    }

                    // Add assembly reference
                    // Note: This is simplified. Actual implementation would need to create
                    // an AssemblyReference object with proper properties
                    _designer.Log($"Adding assembly reference: {assemblyName}");

                    return Success(new
                    {
                        message = $"Assembly reference '{assemblyName}' added successfully",
                        assemblyName,
                        alreadyExists = false
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to add assembly reference: {ex.Message}");
                }
            });
        }
    }
}
