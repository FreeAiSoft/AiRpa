using System;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.redo</c> — возвращает отменённую правку конструктора (как <c>Ctrl+Y</c>).
    /// </summary>
    internal sealed class RedoTool : WorkflowEditToolBase
    {
        internal const string ToolName = "xaml.redo";

        internal RedoTool(Func<IWorkflowEditingService> editingFactory)
            : base(editingFactory, open: null)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description = "Возвращает последнюю отменённую правку открытого процесса (как Ctrl+Y).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" }
                    },
                    required = new[] { "path" }
                }
            };
        }

        protected override McpToolResult ExecuteCore(JObject arguments)
        {
            string path = RequiredText(arguments, "path");
            if (path == null)
            {
                return ParameterError("path", "xaml.path: путь к файлу процесса не задан", null, GetSchema());
            }

            var runner = new WorkflowEditRunner(Editing);
            return Respond(runner.Redo(path), IncludeOutline(arguments));
        }
    }
}
