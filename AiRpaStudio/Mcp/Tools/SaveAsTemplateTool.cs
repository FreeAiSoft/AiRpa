using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for saving the current workflow as a template
    /// </summary>
    public class SaveAsTemplateTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public SaveAsTemplateTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "save_as_template",
                Description = "Saves the current workflow as a template for future use",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        templateName = new
                        {
                            type = "string",
                            description = "Name for the template"
                        },
                        description = new
                        {
                            type = "string",
                            description = "Description of the template (optional)"
                        },
                        overwrite = new
                        {
                            type = "boolean",
                            description = "Overwrite if template already exists",
                            @default = false
                        }
                    },
                    required = new[] { "templateName" }
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
                    string templateName = args["templateName"]?.ToString();
                    string description = args["description"]?.ToString();
                    bool overwrite = args["overwrite"]?.ToObject<bool>() ?? false;

                    if (string.IsNullOrEmpty(templateName))
                    {
                        return Error("templateName is required");
                    }

                    // Ensure templates directory exists
                    string templatesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");
                    if (!Directory.Exists(templatesDir))
                    {
                        _ = Directory.CreateDirectory(templatesDir);
                    }

                    // Create template file path
                    string templatePath = Path.Combine(templatesDir, $"{templateName}.xaml");

                    // Check if file already exists
                    if (File.Exists(templatePath) && !overwrite)
                    {
                        return Error($"Template '{templateName}' already exists. Use overwrite=true to replace it.");
                    }

                    // Get current workflow content
                    string currentFile = _designer.ActiveWorkflowDesignerEx.CurrentWorkflowFile;
                    if (string.IsNullOrEmpty(currentFile) || !File.Exists(currentFile))
                    {
                        return Error("Current workflow must be saved before creating a template");
                    }

                    // Copy current workflow to template
                    File.Copy(currentFile, templatePath, overwrite);

                    // Optionally create a metadata file with description
                    if (!string.IsNullOrEmpty(description))
                    {
                        string metadataPath = Path.Combine(templatesDir, $"{templateName}.json");
                        string metadata = $"{{\"name\":\"{templateName}\",\"description\":\"{description}\",\"created\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"}}";
                        File.WriteAllText(metadataPath, metadata);
                    }

                    _designer.Log($"Saved workflow as template: {templateName}");

                    return Success(new
                    {
                        message = $"Workflow saved as template '{templateName}'",
                        templateName,
                        templatePath,
                        hasDescription = !string.IsNullOrEmpty(description)
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to save as template: {ex.Message}");
                }
            });
        }
    }
}
