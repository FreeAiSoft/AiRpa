using System;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.wrap_activity</c> — оборачивает активность новой активностью (Sequence, If, TryCatch, While,
    /// ForEach): обёртка встаёт на место элемента, элемент становится её первым ребёнком.
    /// </summary>
    internal sealed class WrapActivityTool : WorkflowEditToolBase
    {
        internal const string ToolName = "xaml.wrap_activity";

        internal WrapActivityTool(Func<IWorkflowEditingService> editingFactory, Func<string, DesignerOpenResult> open = null)
            : base(editingFactory, open)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Оборачивает активность новой активностью-контейнером: обёртка занимает место элемента, " +
                    "элемент становится её первым ребёнком. Используется, чтобы положить шаг внутрь If/TryCatch/Sequence.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        elementId = new { type = "string", description = "Идентификатор обёртываемой активности" },
                        wrapperType = new { type = "string", description = "Тип обёртки: Sequence, If, TryCatch, While, ForEach" },
                        container = new { type = "string", description = "Контейнер обёртки, куда кладётся элемент (Then, Activities)" },
                        displayName = new { type = "string", description = "Подпись обёртки в конструкторе" },
                        validate = new { type = "string", description = "report (по умолчанию), off или strict" },
                        include_outline = new { type = "boolean", description = "Вернуть структуру после правки" }
                    },
                    required = new[] { "path", "elementId", "wrapperType" }
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

            string elementId = Text(arguments, "elementId");
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return ParameterError("elementId", "xaml.elementId: не задан идентификатор активности", null, GetSchema());
            }

            string wrapperType = Text(arguments, "wrapperType");
            if (string.IsNullOrWhiteSpace(wrapperType))
            {
                return ParameterError("wrapperType", "xaml.wrapperType: не задан тип обёртки", null, GetSchema());
            }

            var request = new WorkflowWrapRequest
            {
                ElementId = elementId,
                WrapperType = wrapperType,
                Container = Text(arguments, "container"),
                DisplayName = Text(arguments, "displayName")
            };

            DesignerOpenResult opened = EnsureOpen(path);
            if (!opened.Ok)
            {
                return OpenFailure(opened);
            }

            var runner = new WorkflowEditRunner(Editing);
            WorkflowEditResult result = runner.Wrap(path, new[] { request }, ParseValidation(arguments), ToolName);

            return Respond(result, IncludeOutline(arguments));
        }
    }
}
