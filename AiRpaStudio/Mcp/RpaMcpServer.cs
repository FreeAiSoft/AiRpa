using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RPA.Core;
using RPA.Helpers;
using Agent.Core.Models;
using RPA.Chat.Services;

namespace RPA.Mcp
{
    /// <summary>
    /// Main MCP Server implementation for RPA Studio
    /// Provides tools for AI agents to interact with workflow designer
    /// </summary>
    public class RpaMcpServer : IMcpServer
    {
        private static readonly Lazy<RpaMcpServer> Shared =
            new Lazy<RpaMcpServer>(() => new RpaMcpServer());

        private readonly McpToolRegistry _tools;
        private readonly CustomWfDesigner _designer;
        private readonly Func<WorkflowWriteWay> _allowedWays;
        private readonly string _projectRoot;

        public RpaMcpServer()
            : this(null, null)
        {
        }

        /// <summary>
        /// Встроенный singleton для обычного режима студии: только реестр инструментов, без
        /// транспортов <c>stdio/http</c>. Чат обращается к MCP-способам написания WF через
        /// <see cref="StudioWorkflowMcpBridge"/> поверх этого экземпляра. Headless-режим
        /// <c>--mcp-server</c> создаёт собственный экземпляр в <c>App.StartMcpServer</c>.
        /// </summary>
        public static RpaMcpServer Default => Shared.Value;

        /// <summary>
        /// Сервер с заданным реестром инструментов (тесты). <paramref name="designer"/> можно не задавать:
        /// статический синглтон студии (<c>CustomWfDesigner.GetInstance</c>) подтягивается только для лога,
        /// иначе любой тест сервера тянул бы за собой WPF-состояние студии.
        /// </summary>
        internal RpaMcpServer(McpToolRegistry tools, CustomWfDesigner designer = null, Func<WorkflowWriteWay> wayProvider = null)
        {
            _designer = designer;
            _projectRoot = Directory.GetCurrentDirectory();

            // Реестр инструментов: регистрация на старте сервера (палитра xaml.* — этап 4 плана
            // `конвейер генерации XAML`). Сервис палитры создаётся по первому вызову.
            _tools = tools ?? McpToolRegistry.CreateDefault(Log);

            // Маска способов для headless-клиентов: по умолчанию читается из chat_settings.json на каждый
            // вызов ListTools/CallTool. Тесты могут подставить фиксированную маску для детерминизма.
            _allowedWays = wayProvider ?? ResolveAllowedWays;
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


        /// <summary>Актуальная маска способов из настроек чата (headless-режим читает тот же файл).</summary>
        private static WorkflowWriteWay ResolveAllowedWays()
        {
            try
            {
                return WorkflowWritePolicy.Resolve(SettingsStore.Load());
            }
            catch
            {
                // Нечитаемые настройки не должны класть MCP-сервер: отдаём всё, как при дефолтах.
                return WorkflowWriteWay.All;
            }
        }

        /// <summary>Способ написания WF по имени инструмента (источник — McpToolRegistry, без дублирующей таблицы).</summary>
        internal WorkflowWriteWay WayOf(string name) => _tools.WayOf(name);

        /// <summary>Мутирует ли инструмент (источник — McpToolRegistry).</summary>
        internal bool MutatesOf(string name) => _tools.MutatesOf(name);

        private static string WayName(WorkflowWriteWay way)
        {
            switch (way)
            {
                case WorkflowWriteWay.Palette: return "palette (designer)";
                case WorkflowWriteWay.JsonAst: return "JSON AST";
                case WorkflowWriteWay.CodeFirst: return "code-first";
                default: return way.ToString();
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
            WorkflowWriteWay allowedWays = _allowedWays();
            List<McpTool> tools = _tools.Schemas(allowedWays).ToList();
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

            WorkflowWriteWay way = _tools.WayOf(name);
            WorkflowWriteWay allowedWays = _allowedWays();
            if (way != WorkflowWriteWay.None && (allowedWays & way) == 0)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Tool '{name}' is disabled: the '{WayName(way)}' workflow writing way is turned off in chat settings."
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
