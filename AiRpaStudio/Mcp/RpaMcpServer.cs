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
        private readonly McpToolRegistry _tools;
        private readonly CustomWfDesigner _designer;
        private readonly string _projectRoot;

        public RpaMcpServer()
            : this(null, null)
        {
        }

        /// <summary>
        /// Сервер с заданным реестром инструментов (тесты). <paramref name="designer"/> можно не задавать:
        /// статический синглтон студии (<c>CustomWfDesigner.GetInstance</c>) подтягивается только для лога,
        /// иначе любой тест сервера тянул бы за собой WPF-состояние студии.
        /// </summary>
        internal RpaMcpServer(McpToolRegistry tools, CustomWfDesigner designer = null)
        {
            _designer = designer;
            _projectRoot = Directory.GetCurrentDirectory();

            // Реестр инструментов: регистрация на старте сервера (палитра xaml.* — этап 4 плана
            // docs\Valid_Xaml_Designer_Plan.md). Сервис палитры создаётся по первому вызову.
            _tools = tools ?? McpToolRegistry.CreateDefault(Log);
        }

        /// <summary>Лог студии: сообщения инструментов палитры попадают в общий log.txt.</summary>
        private void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                (_designer ?? CustomWfDesigner.GetInstance())?.Log(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Лог MCP недоступен: " + ErrorText.Of(ex));
            }
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
            List<McpTool> tools = _tools.Schemas().ToList();
            return Task.FromResult<IEnumerable<McpTool>>(tools);
        }

        /// <summary>
        /// Call a specific tool with arguments
        /// </summary>
        public async Task<McpToolResult> CallTool(string name, object arguments)
        {
            if (!_tools.TryGet(name, out IMcpTool tool))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Tool '{name}' not found. Available tools: {string.Join(", ", _tools.Names)}"
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
