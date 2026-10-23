using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for creating a workflow from a template
    /// </summary>
    public class CreateFromTemplateTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public CreateFromTemplateTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "create_from_template",
                Description = "Creates a new workflow from a template file",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        templateName = new
                        {
                            type = "string",
                            description = "Name of the template to use"
                        },
                        templatePath = new
                        {
                            type = "string",
                            description = "Full path to template file (alternative to templateName)"
                        },
                        targetPath = new
                        {
                            type = "string",
                            description = "Path where the new workflow will be created"
                        },
                        workflowName = new
                        {
                            type = "string",
                            description = "Name for the new workflow"
                        }
                    },
                    required = new[] { "targetPath", "workflowName" }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    JObject args = JObject.FromObject(arguments);
                    string templateName = args["templateName"]?.ToString();
                    string templatePath = args["templatePath"]?.ToString();
                    string targetPath = args["targetPath"]?.ToString();
                    string workflowName = args["workflowName"]?.ToString();

                    if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(workflowName))
                    {
                        return Error("targetPath and workflowName are required");
                    }

                    if (string.IsNullOrEmpty(templateName) && string.IsNullOrEmpty(templatePath))
                    {
                        return Error("Either templateName or templatePath must be provided");
                    }

                    // Determine template file path
                    string sourceTemplate;
                    if (!string.IsNullOrEmpty(templatePath))
                    {
                        sourceTemplate = templatePath;
                    }
                    else
                    {
                        // Look for template in templates directory
                        string templatesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");
                        sourceTemplate = Path.Combine(templatesDir, $"{templateName}.xaml");
                    }

                    if (!File.Exists(sourceTemplate))
                    {
                        return Error($"Template file not found: {sourceTemplate}");
                    }

                    // Create target directory if needed
                    string targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        _ = Directory.CreateDirectory(targetDir);
                    }

                    // Copy template to target
                    string targetFile = Path.Combine(targetPath, $"{workflowName}.xaml");
                    File.Copy(sourceTemplate, targetFile, false);

                    _designer.Log($"Created workflow from template: {targetFile}");

                    return Success(new
                    {
                        message = $"Workflow created successfully from template",
                        workflowPath = targetFile,
                        templateUsed = sourceTemplate
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to create workflow from template: {ex.Message}");
                }
            });
        }
    }
}
