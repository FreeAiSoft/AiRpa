using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting all file paths of currently open workflow tabs
    /// </summary>
    public class GetOpenTabsTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetOpenTabsTool(CustomWfDesigner designer)
        {
            _designer = designer ?? throw new ArgumentNullException(nameof(designer));
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_open_tabs",
                Description = "Returns a list of all currently open workflow tabs with their file paths. Useful for understanding which workflows are currently loaded in the designer.",
                InputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_designer.WorkflowDesigners == null)
                    {
                        return Error("WorkflowDesigners collection is not initialized.");
                    }

                    List<object> openTabs = new List<object>();

                    foreach (WorkflowDesignerEx designer in _designer.WorkflowDesigners)
                    {
                        try
                        {
                            string filePath = designer.CurrentWorkflowFile;

                            openTabs.Add(new
                            {
                                path = filePath,
                                fileName = Path.GetFileName(filePath),
                                isActive = designer == _designer.ActiveWorkflowDesignerEx
                            });
                        }
                        catch (Exception ex)
                        {
                            // Skip this designer if there's an error getting its file path
                            Console.WriteLine($"Error getting file path for designer: {ex.Message}");
                        }
                    }

                    string activeTab = _designer.ActiveWorkflowDesignerEx?.CurrentWorkflowFile ?? "None";

                    return Success(new
                    {
                        totalTabs = openTabs.Count,
                        activeTab,
                        tabs = openTabs
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Error getting open tabs: {ex.Message}");
                }
            });
        }
    }
}
