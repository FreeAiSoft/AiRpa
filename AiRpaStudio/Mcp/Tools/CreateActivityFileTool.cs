using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;
using RPA.Models;
using RPA.Views;
using RpaCommon;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for creating new XAML activity files in the project
    /// </summary>
    public class CreateActivityFileTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public CreateActivityFileTool(CustomWfDesigner designer)
        {
            _designer = designer ?? throw new ArgumentNullException(nameof(designer));
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "create_activity_file",
                Description = "Creates a new XAML activity file in the current project. The activity is created from a template, added to the project, and loaded into the designer.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityName = new
                        {
                            type = "string",
                            description = "Name for the new activity file (without .xaml extension). If not provided, a default name will be generated (e.g., Activity1.xaml, Activity2.xaml)"
                        },
                        path = new
                        {
                            type = "string",
                            description = "Optional subdirectory path within the project where the activity should be created. If not provided, creates in the project root."
                        }
                    },
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
                    // Parse arguments if provided
                    string activityName = null;
                    string path = null;

                    if (arguments != null)
                    {
                        JObject args = JObject.FromObject(arguments);
                        activityName = args["activityName"]?.ToString();
                        path = args["path"]?.ToString();
                    }

                    // Execute in UI thread using Dispatcher
                    CallResult result = null;
                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        result = McpIntegration.AddNewActivityToProject();
                    });

                    if (result?.Error != null)
                    {
                        return Error($"Failed to create activity: {result.Error.Message}");
                    }

                    _designer.Log("New activity file created and loaded into designer");

                    return Success(new
                    {
                        message = "Activity file created successfully and loaded into designer",
                        status = "created"
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to create activity file: {ex.Message}");
                }
            });
        }
    }
}
