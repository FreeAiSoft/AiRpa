using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for running/executing the workflow
    /// </summary>
    public class RunWorkflowTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public RunWorkflowTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "run_workflow",
                Description = "Executes the current workflow",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        arguments = new
                        {
                            type = "object",
                            description = "Input arguments for the workflow execution",
                            additionalProperties = true
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

                    // Convert JObject to Dictionary for workflow execution
                    Dictionary<string, object> inputArgs = new Dictionary<string, object>();
                    if (args["arguments"] is JObject workflowArgs)
                    {
                        foreach (JProperty prop in workflowArgs.Properties())
                        {
                            inputArgs[prop.Name] = prop.Value.ToObject<object>();
                        }
                    }

                    // Start workflow execution
                    ActivityRunner runner = new ActivityRunner();

                    // Note: This is a simplified version. Actual implementation would need
                    // to handle async execution and provide status updates
                    _designer.Log("Starting workflow execution...");

                    // Run the workflow with the current file path
                    string workflowPath = _designer.ActiveWorkflowDesignerEx.CurrentWorkflowFile;
                    runner.Run(workflowPath);

                    return Success(new
                    {
                        message = "Workflow execution started",
                        status = "running",
                        workflowFile = _designer.ActiveWorkflowDesignerEx.CurrentWorkflowFile
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to run workflow: {ex.Message}");
                }
            });
        }
    }
}
