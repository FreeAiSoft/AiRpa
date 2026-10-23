using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting detailed file or directory information
    /// </summary>
    public class GetFileInfoTool : BaseMcpTool
    {
        private readonly string _projectRoot;

        public GetFileInfoTool(string projectRoot)
        {
            _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_file_info",
                Description = "Gets detailed information about a file or directory including size, dates, attributes, and permissions.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "Relative path to the file or directory from project root"
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

                    if (string.IsNullOrEmpty(relativePath))
                    {
                        return Error("File path is required");
                    }

                    string fullPath = Path.Combine(_projectRoot, relativePath);

                    if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                    {
                        return Error($"Path not found: {relativePath}");
                    }

                    bool isDirectory = Directory.Exists(fullPath);

                    return isDirectory ? GetDirectoryInfo(fullPath, relativePath) : GetFileInfoDetails(fullPath, relativePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Error($"Access denied: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return Error($"Error getting file info: {ex.Message}");
                }
            });
        }

        private McpToolResult GetFileInfoDetails(string fullPath, string relativePath)
        {
            FileInfo fileInfo = new FileInfo(fullPath);

            return Success(new
            {
                path = relativePath,
                fullPath,
                type = "file",
                name = fileInfo.Name,
                extension = fileInfo.Extension,
                size = fileInfo.Length,
                sizeFormatted = FormatBytes(fileInfo.Length),
                created = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                modified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                accessed = fileInfo.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"),
                attributes = fileInfo.Attributes.ToString(),
                isReadOnly = fileInfo.IsReadOnly,
                exists = fileInfo.Exists
            });
        }

        private McpToolResult GetDirectoryInfo(string fullPath, string relativePath)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(fullPath);

            // Count files and subdirectories
            int fileCount = 0;
            int dirCount = 0;
            long totalSize = 0;

            try
            {
                foreach (FileInfo file in dirInfo.GetFiles())
                {
                    fileCount++;
                    totalSize += file.Length;
                }

                foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
                {
                    dirCount++;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Can't access all files/directories
            }

            return Success(new
            {
                path = relativePath,
                fullPath,
                type = "directory",
                name = dirInfo.Name,
                created = dirInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                modified = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                accessed = dirInfo.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"),
                attributes = dirInfo.Attributes.ToString(),
                exists = dirInfo.Exists,
                fileCount,
                directoryCount = dirCount,
                totalSize,
                totalSizeFormatted = FormatBytes(totalSize)
            });
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}