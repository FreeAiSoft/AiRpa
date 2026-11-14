using System;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.undo</c> — отменяет последнюю правку конструктора (как <c>Ctrl+Z</c>).
    /// </summary>
    internal sealed class UndoTool : WorkflowEditToolBase
    {
        internal const string ToolName = "xaml.undo";

        internal UndoTool(Func<IWorkflowEditingService> editingFactory)
            : base(editingFactory, open: null)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description = "Отменяет последнюю правку открытого процесса в конструкторе (как Ctrl+Z).",
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
            return Respond(runner.Undo(path), IncludeOutline(arguments));
        }
    }
}
