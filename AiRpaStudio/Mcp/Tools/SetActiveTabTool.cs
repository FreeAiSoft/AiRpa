using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;
using RPA.Views;
using RpaCommon;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for switching the active workflow tab by file path
    /// </summary>
    public class SetActiveTabTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public SetActiveTabTool(CustomWfDesigner designer)
        {
            _designer = designer ?? throw new ArgumentNullException(nameof(designer));
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "set_active_tab",
                Description = "Switches the active workflow tab to the specified file path. Use this to focus on a specific workflow file that is already open.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new
                        {
                            type = "string",
                            description = "Full file path of the workflow to set as active (e.g., 'C:\\path\\to\\workflow.xaml')"
                        }
                    },
                    required = new[] { "filePath" }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Parse arguments
                    JObject args = JObject.FromObject(arguments);
                    string filePath = args["filePath"]?.ToString();

                    if (string.IsNullOrEmpty(filePath))
                    {
                        return Error("Parameter 'filePath' is required and cannot be empty.");
                    }

                    // Execute in UI thread using Dispatcher
                    CallResult result = null;
                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        result = _designer.SetActiveDesigner(filePath);
                    });

                    if (result?.Error != null)
                    {
                        return Error($"Failed to set active tab: {result.Error.Message}");
                    }

                    _designer.Log($"Active tab switched to: {filePath}");

                    return Success(new
                    {
                        message = "Active tab switched successfully",
                        filePath,
                        activeTab = _designer.ActiveWorkflowDesignerEx?.CurrentWorkflowFile
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Error switching active tab: {ex.Message}");
                }
            });
        }
    }
}
