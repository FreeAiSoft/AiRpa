using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Models;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing files and directories in the project
    /// Restricted to access only the project folder from ProjectManager
    /// </summary>
    public class ListFilesTool : BaseMcpTool
    {
        public ListFilesTool()
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_files",
                Description = "Lists files and directories at the specified path. Returns information about files including names, types, sizes, and modification dates.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "Relative path from project root (use '.' for root directory)"
                        },
                        recursive = new
                        {
                            type = "boolean",
                            description = "List files recursively (default: false)"
                        },
                        includeHidden = new
                        {
                            type = "boolean",
                            description = "Include hidden files and directories (default: false)"
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
                    // Get project folder from ProjectManager
                    string projectRoot = ProjectManager.GetInstance().ProjectFolder;

                    if (string.IsNullOrEmpty(projectRoot))
                    {
                        return Error("Project folder is not initialized. Please open a project first.");
                    }

                    JObject args = JObject.FromObject(arguments);
                    string relativePath = args["path"]?.ToString() ?? ".";
                    bool recursive = args["recursive"]?.ToObject<bool>() ?? false;
                    bool includeHidden = args["includeHidden"]?.ToObject<bool>() ?? false;

                    // Combine and normalize paths
                    string fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
                    string normalizedProjectRoot = Path.GetFullPath(projectRoot);

                    // Security check: ensure the requested path is within project folder
                    if (!fullPath.StartsWith(normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return Error($"Access denied: Path '{relativePath}' is outside the project folder.");
                    }

                    if (!Directory.Exists(fullPath))
                    {
                        return Error($"Directory not found: {relativePath}");
                    }

                    List<object> items = new List<object>();

                    if (recursive)
                    {
                        CollectFilesRecursive(fullPath, normalizedProjectRoot, items, includeHidden);
                    }
                    else
                    {
                        CollectFilesFlat(fullPath, normalizedProjectRoot, items, includeHidden);
                    }

                    return Success(new
                    {
                        path = relativePath,
                        fullPath,
                        recursive,
                        totalItems = items.Count,
                        items
                    });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Error($"Access denied: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return Error($"Error listing files: {ex.Message}");
                }
            });
        }

        private void CollectFilesFlat(string directory, string projectRoot, List<object> items, bool includeHidden)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(directory);

            // Add directories
            foreach (DirectoryInfo dir in dirInfo.GetDirectories())
            {
                if (!includeHidden && (dir.Attributes & FileAttributes.Hidden) != 0)
                {
                    continue;
                }

                items.Add(new
                {
                    name = dir.Name,
                    type = "directory",
                    path = GetRelativePath(projectRoot, dir.FullName),
                    size = (long?)null,
                    modified = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    attributes = dir.Attributes.ToString()
                });
            }

            // Add files
            foreach (FileInfo file in dirInfo.GetFiles())
            {
                if (!includeHidden && (file.Attributes & FileAttributes.Hidden) != 0)
                {
                    continue;
                }

                items.Add(new
                {
                    name = file.Name,
                    type = "file",
                    path = GetRelativePath(projectRoot, file.FullName),
                    size = file.Length,
                    extension = file.Extension,
                    modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    attributes = file.Attributes.ToString()
                });
            }
        }

        private void CollectFilesRecursive(string directory, string projectRoot, List<object> items, bool includeHidden)
        {
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(directory);

                // Add current directory
                if (directory != projectRoot)
                {
                    items.Add(new
                    {
                        name = dirInfo.Name,
                        type = "directory",
                        path = GetRelativePath(projectRoot, dirInfo.FullName),
                        size = (long?)null,
                        modified = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        attributes = dirInfo.Attributes.ToString()
                    });
                }

                // Add files in current directory
                foreach (FileInfo file in dirInfo.GetFiles())
                {
                    if (!includeHidden && (file.Attributes & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }

                    items.Add(new
                    {
                        name = file.Name,
                        type = "file",
                        path = GetRelativePath(projectRoot, file.FullName),
                        size = file.Length,
                        extension = file.Extension,
                        modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        attributes = file.Attributes.ToString()
                    });
                }

                // Recurse into subdirectories
                foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
                {
                    if (!includeHidden && (subDir.Attributes & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }

                    CollectFilesRecursive(subDir.FullName, projectRoot, items, includeHidden);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
            }
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}