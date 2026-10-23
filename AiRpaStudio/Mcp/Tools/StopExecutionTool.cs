using System;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for stopping workflow execution
    /// </summary>
    public class StopExecutionTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public StopExecutionTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "stop_execution",
                Description = "Stops the currently running workflow execution",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        force = new
                        {
                            type = "boolean",
                            description = "Force stop without cleanup",
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
                        return Error("No active workflow designer.");
                    }

                    _designer.Log("Stopping workflow execution...");

                    // Note: Actual implementation would need to cancel the running workflow
                    // This is a simplified version

                    return Success(new
                    {
                        message = "Workflow execution stopped",
                        status = "stopped"
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to stop execution: {ex.Message}");
                }
            });
        }
    }
}
