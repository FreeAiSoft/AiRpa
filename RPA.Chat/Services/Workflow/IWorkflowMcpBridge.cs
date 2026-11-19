using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Models;
using RPA.Shared.Models;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Один MCP-инструмент способа написания Workflow Foundation, приведённый к контракту чата.
    /// Чат не ссылается на слой MCP студии (страж
    /// <c>RpaChatAndAgentCore_DoNotReferenceStudioMcp</c>), поэтому схема MCP-инструмента
    /// нормализуется в informal-строку <see cref="ParametersSchema"/>, как у <c>IAgentTool</c>.
    /// </summary>
    public sealed class WorkflowMcpToolInfo
    {
        /// <summary>Машинное имя MCP-инструмента, например <c>"xaml.add_activity"</c>.</summary>
        public string Name { get; set; }

        /// <summary>Человекочитаемое описание для системного промпта.</summary>
        public string Description { get; set; }

        /// <summary>Informal JSON-схема аргументов (та же форма, что у <c>IAgentTool.ParametersSchema</c>).</summary>
        public string ParametersSchema { get; set; }

        /// <summary>Способ написания WF, которым управляет этот инструмент; read-only поддержка — <c>None</c>.</summary>
        public WorkflowWriteWay Way { get; set; }

        /// <summary>Инструмент меняет состояние (файлы, модель конструктора, сборку).</summary>
        public bool Mutates { get; set; }
    }

    /// <summary>
    /// Мост «чат ↔ встроенный MCP» (in-process, без транспорта stdio/http). Чат вызывает MCP-инструменты
    /// трёх способов написания WF (палитра, JSON AST, code-first) внутри процесса. Контракт живёт в чате,
    /// реализация — в студии (<c>AiRpaStudio</c>) поверх <c>RpaMcpServer</c>/<c>McpToolRegistry</c>.
    /// </summary>
    public interface IWorkflowMcpBridge
    {
        /// <summary>Список доступных MCP-инструментов со способом, мутацией и схемой.</summary>
        IReadOnlyList<WorkflowMcpToolInfo> ListTools();

        /// <summary>
        /// Вызывает MCP-инструмент по имени и возвращает сериализованный текст результата.
        /// <paramref name="jsonArguments"/> — JSON-объект аргументов вызова.
        /// </summary>
        Task<string> CallAsync(string name, string jsonArguments, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Null-object: хост студии не подключил мост. Список пуст, а вызов возвращает объяснение —
    /// инструменты MCP-способов отвечают текстом «мост не подключён», а не падают.
    /// </summary>
    public sealed class NullWorkflowMcpBridge : IWorkflowMcpBridge
    {
        public static readonly NullWorkflowMcpBridge Instance = new NullWorkflowMcpBridge();

        private const string UnavailableMessage =
            "мост MCP не подключён хостом студии: способы написания WF (палитра, JSON AST, code-first) " +
            "недоступны в этой сборке чата";

        public IReadOnlyList<WorkflowMcpToolInfo> ListTools()
        {
            return new WorkflowMcpToolInfo[0];
        }

        public Task<string> CallAsync(string name, string jsonArguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(UnavailableMessage);
        }
    }
}
