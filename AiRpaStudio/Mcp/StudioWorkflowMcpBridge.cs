using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Models;
using RPA.Shared.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;

namespace RPA.Mcp
{
    /// <summary>
    /// Мост «чат ↔ встроенный MCP» (план «3 галочки», in-process). Чат видит только
    /// <see cref="IWorkflowMcpBridge"/> из <c>RPA.Chat</c> и не ссылается на <c>RPA.Mcp</c>;
    /// здесь контракт реализуется поверх <see cref="RpaMcpServer"/>/<c>McpToolRegistry</c>.
    /// </summary>
    internal sealed class StudioWorkflowMcpBridge : IWorkflowMcpBridge
    {
        private readonly RpaMcpServer _server;

        internal StudioWorkflowMcpBridge(RpaMcpServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>
        /// Приводит схемы MCP-инструментов к <see cref="WorkflowMcpToolInfo"/>: объектная JSON Schema
        /// (<c>McpTool.InputSchema</c>) сериализуется в informal-строку <see cref="WorkflowMcpToolInfo.ParametersSchema"/>,
        /// как у <c>IAgentTool</c>, иначе <c>ToolSchemaConverter</c> не построит native-схему.
        /// Способ (<see cref="WorkflowWriteWay"/>) и мутация берутся из реестра (<see cref="RpaMcpServer.WayOf"/>
        /// / <see cref="RpaMcpServer.MutatesOf"/>), а не из локальной таблицы, чтобы мост и реестр не расходились.
        /// </summary>
        public IReadOnlyList<WorkflowMcpToolInfo> ListTools()
        {
            IEnumerable<McpTool> schemas = _server.ListTools().GetAwaiter().GetResult();

            return schemas
                .Select(schema => new WorkflowMcpToolInfo
                {
                    Name = schema.Name,
                    Description = schema.Description,
                    ParametersSchema = SerializeSchema(schema.InputSchema),
                    Way = _server.WayOf(schema.Name),
                    Mutates = _server.MutatesOf(schema.Name)
                })
                .ToArray();
        }

        /// <summary>
        /// Вызывает MCP-инструмент и возвращает сериализованный <see cref="McpToolResult"/> —
        /// тот же формат (<c>Success/Content/Error/ValidationError</c>), что видит внешний MCP-клиент.
        /// </summary>
        public async Task<string> CallAsync(string name, string jsonArguments, CancellationToken cancellationToken)
        {
            JObject arguments = ParseArguments(jsonArguments);
            McpToolResult result = await _server.CallTool(name, arguments).ConfigureAwait(false);
            return JsonConvert.SerializeObject(result);
        }

        private static JObject ParseArguments(string jsonArguments)
        {
            if (string.IsNullOrWhiteSpace(jsonArguments))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(jsonArguments);
            }
            catch (Exception)
            {
                return new JObject();
            }
        }

        private static string SerializeSchema(object inputSchema)
        {
            if (inputSchema == null)
            {
                return "{}";
            }

            try
            {
                return JsonConvert.SerializeObject(inputSchema);
            }
            catch (Exception)
            {
                return "{}";
            }
        }
    }
}
