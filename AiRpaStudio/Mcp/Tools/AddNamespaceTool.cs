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
    /// Tool for adding namespace/using directives to the workflow
    /// </summary>
    public class AddNamespaceTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public AddNamespaceTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "add_namespace",
                Description = "Adds a namespace import (using directive) to the workflow",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        @namespace = new
                        {
                            type = "string",
                            description = "Namespace to import (e.g., 'System.Collections.Generic')"
                        }
                    },
                    required = new[] { "namespace" }
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
                    string namespaceName = args["namespace"]?.ToString();

                    if (string.IsNullOrEmpty(namespaceName))
                    {
                        return Error("namespace parameter is required");
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

                    // Get or create TextExpression.NamespacesForImplementation
                    ModelProperty namespacesProperty = root.Properties["TextExpression.NamespacesForImplementation"];
                    if (namespacesProperty == null)
                    {
                        return Error("TextExpression.NamespacesForImplementation property not found");
                    }

                    ModelItemCollection namespaces = namespacesProperty.Collection;
                    if (namespaces == null)
                    {
                        return Error("Namespaces collection not found");
                    }

                    // Check if namespace already exists
                    bool exists = namespaces.Any(item =>
                    {
                        string value = item.GetCurrentValue() as string;
                        return value == namespaceName;
                    });

                    if (exists)
                    {
                        return Success(new
                        {
                            message = $"Namespace '{namespaceName}' already exists",
                            @namespace = namespaceName,
                            alreadyExists = true
                        });
                    }

                    // Add namespace
                    _ = namespaces.Add(namespaceName);

                    _designer.Log($"Added namespace: {namespaceName}");

                    return Success(new
                    {
                        message = $"Namespace '{namespaceName}' added successfully",
                        @namespace = namespaceName,
                        alreadyExists = false
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to add namespace: {ex.Message}");
                }
            });
        }
    }
}
