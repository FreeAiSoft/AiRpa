using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing available workflow templates
    /// </summary>
    public class ListTemplatesTool : BaseMcpTool
    {
#pragma warning disable IDE0052 // Remove unread private members
        private readonly CustomWfDesigner _designer;
#pragma warning restore IDE0052 // Remove unread private members

        public ListTemplatesTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_templates",
                Description = "Lists all available workflow templates",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        templatesPath = new
                        {
                            type = "string",
                            description = "Custom path to templates directory (optional)"
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
                    string templatesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");

                    if (!Directory.Exists(templatesDir))
                    {
                        return Success(new
                        {
                            message = "No templates directory found",
                            count = 0,
                            templates = new List<object>(),
                            templatesPath = templatesDir
                        });
                    }

                    // Get all .xaml files in templates directory
                    string[] templateFiles = Directory.GetFiles(templatesDir, "*.xaml");
                    List<object> templates = new List<object>();

                    foreach (string file in templateFiles)
                    {
                        FileInfo fi = new FileInfo(file);
                        templates.Add(new
                        {
                            name = Path.GetFileNameWithoutExtension(file),
                            fileName = fi.Name,
                            path = file,
                            size = fi.Length,
                            lastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }

                    return Success(new
                    {
                        message = $"Found {templates.Count} template(s)",
                        count = templates.Count,
                        templates,
                        templatesPath = templatesDir
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to list templates: {ex.Message}");
                }
            });
        }
    }
}
