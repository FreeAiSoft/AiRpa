using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RPA.Core;
using RPA.Helpers;

namespace RPA.Mcp
{
    /// <summary>
    /// Main MCP Server implementation for RPA Studio
    /// Provides tools for AI agents to interact with workflow designer
    /// </summary>
    public class RpaMcpServer : IMcpServer
    {
        private readonly Dictionary<string, IMcpTool> _tools;
        private readonly CustomWfDesigner _designer;
        private readonly string _projectRoot;

        public RpaMcpServer()
        {
            _designer = CustomWfDesigner.GetInstance();
            _projectRoot = Directory.GetCurrentDirectory();

            _tools = new Dictionary<string, IMcpTool>();
        }

        /// <summary>
        /// Initialize the MCP server
        /// </summary>
        public Task<McpServerInfo> Initialize()
        {
            return Task.FromResult(new McpServerInfo
            {
                Name = "rpa-studio",
                Version = "1.0.0",
                Description = "MCP Server for RPA Studio - provides tools for workflow automation and AI integration"
            });
        }

        /// <summary>
        /// List all available tools
        /// </summary>
        public Task<IEnumerable<McpTool>> ListTools()
        {
            List<McpTool> tools = _tools.Values.Select(tool => tool.GetSchema()).ToList();
            return Task.FromResult<IEnumerable<McpTool>>(tools);
        }

        /// <summary>
        /// Call a specific tool with arguments
        /// </summary>
        public async Task<McpToolResult> CallTool(string name, object arguments)
        {
            if (!_tools.TryGetValue(name, out IMcpTool tool))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Tool '{name}' not found. Available tools: {string.Join(", ", _tools.Keys)}"
                };
            }

            try
            {
                return await tool.Execute(arguments);
            }
            catch (Exception ex)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Error executing tool '{name}': {ex}"
                };
            }
        }

        /// <summary>
        /// Handle JSON-RPC 2.0 request
        /// </summary>
        public async Task<McpResponse> HandleRequest(McpRequest request)
        {
            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        McpServerInfo info = await Initialize();
                        return new McpResponse
                        {
                            Result = info,
                            Id = request.Id
                        };

                    case "tools/list":
                        IEnumerable<McpTool> tools = await ListTools();
                        return new McpResponse
                        {
                            Result = new { tools },
                            Id = request.Id
                        };

                    case "tools/call":
                        dynamic callParams = request.Params;
                        dynamic toolName = callParams?.name?.ToString();
                        dynamic arguments = callParams?.arguments;

                        if (string.IsNullOrEmpty(toolName))
                        {
                            return new McpResponse
                            {
                                Error = new McpError
                                {
                                    Code = -32602,
                                    Message = "Invalid params: 'name' is required"
                                },
                                Id = request.Id
                            };
                        }

                        dynamic result = await CallTool(toolName, arguments);
                        return new McpResponse
                        {
                            Result = result,
                            Id = request.Id
                        };

                    default:
                        return new McpResponse
                        {
                            Error = new McpError
                            {
                                Code = -32601,
                                Message = $"Method '{request.Method}' not found"
                            },
                            Id = request.Id
                        };
                }
            }
            catch (Exception ex)
            {
                return new McpResponse
                {
                    Error = new McpError
                    {
                        Code = -32603,
                        Message = "Internal error",
                        Data = ErrorText.Of(ex)
                    },
                    Id = request.Id
                };
            }
        }
    }
}
