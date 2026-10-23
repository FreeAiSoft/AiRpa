using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing all assembly references in the workflow
    /// </summary>
    public class ListAssembliesTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public ListAssembliesTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_assemblies",
                Description = "Lists all assembly references in the workflow project",
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

                    // Get TextExpression.ReferencesForImplementation
                    ModelProperty referencesProperty = root.Properties["TextExpression.ReferencesForImplementation"];
                    if (referencesProperty == null)
                    {
                        return Error("TextExpression.ReferencesForImplementation property not found");
                    }

                    ModelItemCollection references = referencesProperty.Collection;
                    if (references == null)
                    {
                        return Success(new
                        {
                            message = "No assembly references found",
                            count = 0,
                            assemblies = new List<string>()
                        });
                    }

                    // Collect all assemblies
                    List<object> assemblyList = new List<object>();
                    foreach (ModelItem item in references)
                    {
                        object assemblyRef = item.GetCurrentValue();
                        if (assemblyRef != null)
                        {
                            System.Reflection.PropertyInfo nameProperty = assemblyRef.GetType().GetProperty("AssemblyName");
                            if (nameProperty != null)
                            {
                                string name = nameProperty.GetValue(assemblyRef)?.ToString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    assemblyList.Add(new
                                    {
                                        assemblyName = name,
                                        type = assemblyRef.GetType().Name
                                    });
                                }
                            }
                        }
                    }

                    return Success(new
                    {
                        message = $"Found {assemblyList.Count} assembly reference(s)",
                        count = assemblyList.Count,
                        assemblies = assemblyList
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to list assemblies: {ex.Message}");
                }
            });
        }
    }
}
