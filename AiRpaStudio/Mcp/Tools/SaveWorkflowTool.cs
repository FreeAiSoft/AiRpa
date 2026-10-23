using System;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for saving the current workflow to file
    /// </summary>
    public class SaveWorkflowTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public SaveWorkflowTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "save_workflow",
                Description = "Saves the current workflow to file. If no path is specified, saves to the current file location.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "File path to save workflow (optional, uses current file if not specified)"
                        },
                        saveAs = new
                        {
                            type = "boolean",
                            description = "Force 'Save As' behavior even if file exists",
                            @default = false
                        }
                    }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            try
            {
                // Parse arguments on background thread
                JObject args = JObject.FromObject(arguments);
                string path = args["path"]?.ToString();
                bool saveAs = args["saveAs"]?.ToObject<bool>() ?? false;

                // Execute save operation on UI thread
                return await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // Check if designer is active
                        if (_designer.ActiveWorkflowDesignerEx == null)
                        {
                            return Error("No active workflow designer. Please open or create a workflow first.");
                        }

                        WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;
                        string currentFile = activeDesigner.CurrentWorkflowFile;

                        // Determine save behavior
                        if (!string.IsNullOrEmpty(path))
                        {
                            // Save to specified path
                            activeDesigner.Save(path);
                            _designer.Log($"Workflow saved to: {path}");

                            return Success(new
                            {
                                message = "Workflow saved successfully",
                                filePath = path,
                                isNewFile = true
                            });
                        }
                        else if (!string.IsNullOrEmpty(currentFile) && !saveAs)
                        {
                            // Save to current file
                            _designer.WorkflowSave();
                            _designer.Log($"Workflow saved: {currentFile}");

                            return Success(new
                            {
                                message = "Workflow saved successfully",
                                filePath = currentFile,
                                isNewFile = false
                            });
                        }
                        else
                        {
                            // No path specified and no current file - need to prompt user
                            return Error("No file path specified and workflow has not been saved before. Please provide a 'path' parameter.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return Error($"Failed to save workflow: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                return Error($"Failed to save workflow: {ex.Message}");
            }
        }
    }
}