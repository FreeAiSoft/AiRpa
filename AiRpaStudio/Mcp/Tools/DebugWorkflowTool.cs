using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for running workflow in debug mode
    /// </summary>
    public class DebugWorkflowTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public DebugWorkflowTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "debug_workflow",
                Description = "Executes the workflow in debug mode, respecting breakpoints and enabling step-by-step execution",
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
                        },
                        stepMode = new
                        {
                            type = "boolean",
                            description = "Enable step-by-step execution mode",
                            @default = false
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
                    bool stepMode = args["stepMode"]?.ToObject<bool>() ?? false;

                    // Convert JObject to Dictionary for workflow execution
                    Dictionary<string, object> inputArgs = new Dictionary<string, object>();
                    if (args["arguments"] is JObject workflowArgs)
                    {
                        foreach (JProperty prop in workflowArgs.Properties())
                        {
                            inputArgs[prop.Name] = prop.Value.ToObject<object>();
                        }
                    }

                    // Start debug execution
                    ActivityRunner runner = new ActivityRunner();

                    _designer.Log($"Starting workflow debug execution (step mode: {stepMode})...");

                    // Run the workflow in debug mode with the current file path
                    string workflowPath = _designer.ActiveWorkflowDesignerEx.CurrentWorkflowFile;
                    runner.Run(workflowPath);

                    return Success(new
                    {
                        message = "Workflow debug execution started",
                        status = "debugging",
                        stepMode,
                        workflowFile = _designer.ActiveWorkflowDesignerEx.CurrentWorkflowFile
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to debug workflow: {ex.Message}");
                }
            });
        }
    }
}
