using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing all namespace imports in the workflow
    /// </summary>
    public class ListNamespacesTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public ListNamespacesTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_namespaces",
                Description = "Lists all namespace imports (using directives) in the workflow",
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

                    // Get TextExpression.NamespacesForImplementation
                    ModelProperty namespacesProperty = root.Properties["TextExpression.NamespacesForImplementation"];
                    if (namespacesProperty == null)
                    {
                        return Error("TextExpression.NamespacesForImplementation property not found");
                    }

                    ModelItemCollection namespaces = namespacesProperty.Collection;
                    if (namespaces == null)
                    {
                        return Success(new
                        {
                            message = "No namespaces found",
                            count = 0,
                            namespaces = new List<string>()
                        });
                    }

                    // Collect all namespaces
                    List<string> namespaceList = new List<string>();
                    foreach (ModelItem item in namespaces)
                    {
                        string ns = item.GetCurrentValue() as string;
                        if (!string.IsNullOrEmpty(ns))
                        {
                            namespaceList.Add(ns);
                        }
                    }

                    return Success(new
                    {
                        message = $"Found {namespaceList.Count} namespace(s)",
                        count = namespaceList.Count,
                        namespaces = namespaceList
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to list namespaces: {ex.Message}");
                }
            });
        }
    }
}
