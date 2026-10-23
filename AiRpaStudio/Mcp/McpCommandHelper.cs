using Newtonsoft.Json;

namespace RPA.Mcp
{
    /// <summary>
    /// Helper class for creating and formatting MCP commands
    /// Simplifies creating JSON-RPC 2.0 requests
    /// </summary>
    public static class McpCommandHelper
    {
        private static int _requestId;

        /// <summary>
        /// Create an initialize request
        /// </summary>
        public static string CreateInitializeRequest()
        {
            var request = new McpRequest
            {
                Method = "initialize",
                Params = new { },
                Id = ++_requestId
            };

            return JsonConvert.SerializeObject(request);
        }

        /// <summary>
        /// Create a tools/list request
        /// </summary>
        public static string CreateListToolsRequest()
        {
            var request = new McpRequest
            {
                Method = "tools/list",
                Params = new { },
                Id = ++_requestId
            };

            return JsonConvert.SerializeObject(request);
        }

        /// <summary>
        /// Create a tools/call request
        /// </summary>
        /// <param name="toolName">Name of the tool to call</param>
        /// <param name="toolArguments">Tool arguments as an object</param>
        public static string CreateToolCallRequest(string toolName, object toolArguments)
        {
            var request = new McpRequest
            {
                Method = "tools/call",
                Params = new
                {
                    name = toolName,
                    arguments = toolArguments
                },
                Id = ++_requestId
            };

            return JsonConvert.SerializeObject(request);
        }

        /// <summary>
        /// Create a list_files tool call request
        /// </summary>
        public static string CreateListFilesRequest(string path, bool recursive = false, bool includeHidden = false)
        {
            return CreateToolCallRequest("list_files", new
            {
                path,
                recursive,
                includeHidden
            });
        }

        /// <summary>
        /// Create a read_file tool call request
        /// </summary>
        public static string CreateReadFileRequest(string path, int? startLine = null, int? endLine = null)
        {
            var args = new
            {
                path,
                startLine,
                endLine
            };

            return CreateToolCallRequest("read_file", args);
        }

        /// <summary>
        /// Create a search_files tool call request
        /// </summary>
        public static string CreateSearchFilesRequest(string path, string pattern, string filePattern = null, int maxResults = 100)
        {
            return CreateToolCallRequest("search_files", new
            {
                path,
                pattern,
                filePattern,
                maxResults
            });
        }

        /// <summary>
        /// Create a get_file_info tool call request
        /// </summary>
        public static string CreateGetFileInfoRequest(string path)
        {
            return CreateToolCallRequest("get_file_info", new { path });
        }

        /// <summary>
        /// Parse MCP response
        /// </summary>
        public static McpResponse ParseResponse(string json)
        {
            return JsonConvert.DeserializeObject<McpResponse>(json);
        }

        /// <summary>
        /// Create a simplified command from action and arguments
        /// This allows using simple format like { "action": "list_files", "path": "." }
        /// and converts it to proper JSON-RPC format
        /// </summary>
        public static string ConvertSimpleCommandToJsonRpc(string simpleCommand)
        {
            try
            {
                var obj = JsonConvert.DeserializeObject<dynamic>(simpleCommand);

                if (obj.action != null)
                {
                    string action = obj.action.ToString();

                    // Remove "action" property and use the rest as arguments
                    var argumentsObj = new Newtonsoft.Json.Linq.JObject();
                    foreach (var prop in obj)
                    {
                        if (prop.Name != "action")
                        {
                            argumentsObj[prop.Name] = prop.Value;
                        }
                    }

                    return CreateToolCallRequest(action, argumentsObj);
                }

                // If no action property, assume it's already in correct format
                return simpleCommand;
            }
            catch
            {
                // If parsing fails, return as is
                return simpleCommand;
            }
        }
    }
}