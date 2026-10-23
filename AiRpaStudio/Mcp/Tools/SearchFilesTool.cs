using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for searching files by name or content
    /// </summary>
    public class SearchFilesTool : BaseMcpTool
    {
        private readonly string _projectRoot;

        public SearchFilesTool(string projectRoot)
        {
            _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "search_files",
                Description = "Searches for files by name pattern or content. Supports regex patterns and returns matching files with context.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "Directory to search in (relative to project root)"
                        },
                        pattern = new
                        {
                            type = "string",
                            description = "Search pattern (filename pattern or content regex)"
                        },
                        searchType = new
                        {
                            type = "string",
                            description = "Type of search",
                            @enum = new[] { "name", "content", "both" }
                        },
                        filePattern = new
                        {
                            type = "string",
                            description = "File extension filter (e.g., '*.cs', '*.xaml')"
                        },
                        caseSensitive = new
                        {
                            type = "boolean",
                            description = "Case sensitive search (default: false)"
                        },
                        maxResults = new
                        {
                            type = "integer",
                            description = "Maximum number of results (default: 100)"
                        }
                    },
                    required = new[] { "path", "pattern" }
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
                    string relativePath = args["path"]?.ToString() ?? ".";
                    string pattern = args["pattern"]?.ToString();
                    string searchType = args["searchType"]?.ToString() ?? "both";
                    string filePattern = args["filePattern"]?.ToString() ?? "*.*";
                    bool caseSensitive = args["caseSensitive"]?.ToObject<bool>() ?? false;
                    int maxResults = args["maxResults"]?.ToObject<int>() ?? 100;

                    if (string.IsNullOrEmpty(pattern))
                    {
                        return Error("Search pattern is required");
                    }

                    string fullPath = Path.Combine(_projectRoot, relativePath);

                    if (!Directory.Exists(fullPath))
                    {
                        return Error($"Directory not found: {relativePath}");
                    }

                    List<object> results = new List<object>();
                    RegexOptions regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

                    // Search by name
                    if (searchType == "name" || searchType == "both")
                    {
                        SearchByName(fullPath, pattern, filePattern, regexOptions, results, maxResults);
                    }

                    // Search by content
                    if (searchType == "content" || searchType == "both")
                    {
                        SearchByContent(fullPath, pattern, filePattern, regexOptions, results, maxResults);
                    }

                    return Success(new
                    {
                        searchPath = relativePath,
                        pattern,
                        searchType,
                        caseSensitive,
                        totalResults = results.Count,
                        results = results.Take(maxResults)
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Error searching files: {ex.Message}");
                }
            });
        }

        private void SearchByName(string directory, string pattern, string filePattern, RegexOptions options, List<object> results, int maxResults)
        {
            if (results.Count >= maxResults)
            {
                return;
            }

            try
            {
                Regex regex = new Regex(pattern, options);
                DirectoryInfo dirInfo = new DirectoryInfo(directory);

                foreach (FileInfo file in dirInfo.GetFiles(filePattern, SearchOption.AllDirectories))
                {
                    if (results.Count >= maxResults)
                    {
                        break;
                    }

                    if (regex.IsMatch(file.Name))
                    {
                        results.Add(new
                        {
                            type = "name_match",
                            path = GetRelativePath(_projectRoot, file.FullName),
                            name = file.Name,
                            size = file.Length,
                            modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
            }
        }

        private void SearchByContent(string directory, string pattern, string filePattern, RegexOptions options, List<object> results, int maxResults)
        {
            if (results.Count >= maxResults)
            {
                return;
            }

            try
            {
                Regex regex = new Regex(pattern, options);
                DirectoryInfo dirInfo = new DirectoryInfo(directory);

                foreach (FileInfo file in dirInfo.GetFiles(filePattern, SearchOption.AllDirectories))
                {
                    if (results.Count >= maxResults)
                    {
                        break;
                    }

                    // Skip large files (> 5MB)
                    if (file.Length > 5 * 1024 * 1024)
                    {
                        continue;
                    }

                    // Skip binary files
                    if (IsBinaryFile(file.Extension))
                    {
                        continue;
                    }

                    try
                    {
                        string[] lines = File.ReadAllLines(file.FullName);
                        List<object> matches = new List<object>();

                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (regex.IsMatch(lines[i]))
                            {
                                matches.Add(new
                                {
                                    lineNumber = i + 1,
                                    line = lines[i].Trim(),
                                    context = GetContext(lines, i, 2)
                                });

                                if (matches.Count >= 10) // Max 10 matches per file
                                {
                                    break;
                                }
                            }
                        }

                        if (matches.Count > 0)
                        {
                            results.Add(new
                            {
                                type = "content_match",
                                path = GetRelativePath(_projectRoot, file.FullName),
                                name = file.Name,
                                matchCount = matches.Count,
                                matches
                            });
                        }
                    }
                    catch (Exception)
                    {
                        // Skip files that can't be read as text
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
            }
        }

        private List<string> GetContext(string[] lines, int lineIndex, int contextLines)
        {
            List<string> context = new List<string>();
            int start = Math.Max(0, lineIndex - contextLines);
            int end = Math.Min(lines.Length - 1, lineIndex + contextLines);

            for (int i = start; i <= end; i++)
            {
                string prefix = i == lineIndex ? ">" : " ";
                context.Add($"{prefix} {i + 1} | {lines[i]}");
            }

            return context;
        }

        private bool IsBinaryFile(string extension)
        {
            string[] binaryExtensions = { ".exe", ".dll", ".pdb", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".zip", ".rar", ".7z", ".pdf", ".doc", ".docx", ".xls", ".xlsx" };
            return binaryExtensions.Contains(extension.ToLowerInvariant());
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}