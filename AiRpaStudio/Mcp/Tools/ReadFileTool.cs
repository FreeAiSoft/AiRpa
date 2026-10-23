using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for reading file contents from the project
    /// </summary>
    public class ReadFileTool : BaseMcpTool
    {
        private readonly string _projectRoot;

        public ReadFileTool(string projectRoot)
        {
            _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "read_file",
                Description = "Reads the contents of a file. Supports text files and returns content with line numbers. Can read specific line ranges.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "Relative path to the file from project root"
                        },
                        startLine = new
                        {
                            type = "integer",
                            description = "Starting line number (1-based, optional)"
                        },
                        endLine = new
                        {
                            type = "integer",
                            description = "Ending line number (1-based, optional)"
                        },
                        encoding = new
                        {
                            type = "string",
                            description = "File encoding (default: UTF8)",
                            @enum = new[] { "UTF8", "ASCII", "Unicode", "UTF32" }
                        }
                    },
                    required = new[] { "path" }
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
                    string relativePath = args["path"]?.ToString();
                    int? startLine = args["startLine"]?.ToObject<int?>();
                    int? endLine = args["endLine"]?.ToObject<int?>();
                    string encodingName = args["encoding"]?.ToString() ?? "UTF8";

                    if (string.IsNullOrEmpty(relativePath))
                    {
                        return Error("File path is required");
                    }

                    string fullPath = Path.Combine(_projectRoot, relativePath);

                    if (!File.Exists(fullPath))
                    {
                        return Error($"File not found: {relativePath}");
                    }

                    FileInfo fileInfo = new FileInfo(fullPath);

                    // Check if file is too large (> 10MB)
                    if (fileInfo.Length > 10 * 1024 * 1024)
                    {
                        return Error($"File is too large ({fileInfo.Length / (1024 * 1024)}MB). Maximum size is 10MB.");
                    }

                    Encoding encoding = GetEncoding(encodingName);
                    string[] lines = File.ReadAllLines(fullPath, encoding);

                    // Apply line range if specified
                    int actualStartLine = Math.Max(1, startLine ?? 1);
                    int actualEndLine = Math.Min(lines.Length, endLine ?? lines.Length);

                    if (actualStartLine > lines.Length)
                    {
                        return Error($"Start line {actualStartLine} exceeds file length ({lines.Length} lines)");
                    }

                    StringBuilder content = new StringBuilder();
                    for (int i = actualStartLine - 1; i < actualEndLine; i++)
                    {
                        _ = content.AppendLine($"{i + 1} | {lines[i]}");
                    }

                    return Success(new
                    {
                        path = relativePath,
                        fullPath,
                        totalLines = lines.Length,
                        startLine = actualStartLine,
                        endLine = actualEndLine,
                        linesRead = actualEndLine - actualStartLine + 1,
                        size = fileInfo.Length,
                        encoding = encodingName,
                        modified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        content = content.ToString()
                    });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Error($"Access denied: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return Error($"Error reading file: {ex.Message}");
                }
            });
        }

        private Encoding GetEncoding(string encodingName)
        {
            switch (encodingName.ToUpperInvariant())
            {
                case "UTF8":
                    return Encoding.UTF8;
                case "ASCII":
                    return Encoding.ASCII;
                case "UNICODE":
                    return Encoding.Unicode;
                case "UTF32":
                    return Encoding.UTF32;
                default:
                    return Encoding.UTF8;
            }
        }
    }
}
