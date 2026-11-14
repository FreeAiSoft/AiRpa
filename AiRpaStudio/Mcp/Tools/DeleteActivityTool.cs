using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.delete_activity</c> — удаляет активности открытого процесса по идентификаторам
    /// <c>xaml.get_workflow</c>/<c>workflow_outline</c>. Пачка удалений — одна атомарная правка.
    /// </summary>
    internal sealed class DeleteActivityTool : WorkflowEditToolBase
    {
        internal const string ToolName = "xaml.delete_activity";

        internal DeleteActivityTool(Func<IWorkflowEditingService> editingFactory, Func<string, DesignerOpenResult> open = null)
            : base(editingFactory, open)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Удаляет активности открытого процесса по идентификаторам из xaml.get_workflow. " +
                    "Пачка elementIds применяется атомарно: если какой-то адрес не найден, ничего не удаляется.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        elementId = new { type = "string", description = "Идентификатор активности из xaml.get_workflow" },
                        elementIds = new
                        {
                            type = "array",
                            description = "Несколько идентификаторов для удаления одной атомарной пачкой",
                            items = new { type = "string" }
                        },
                        validate = new { type = "string", description = "report (по умолчанию), off или strict" },
                        include_outline = new
                        {
                            type = "boolean",
                            description = "Вернуть структуру процесса после правки (по умолчанию true)"
                        }
                    },
                    required = new[] { "path", "elementId" }
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

            var elementIds = new List<string>();
            string single = Text(arguments, "elementId");
            if (!string.IsNullOrWhiteSpace(single))
            {
                elementIds.Add(single);
            }

            if (arguments["elementIds"] is JArray array)
            {
                foreach (JToken token in array)
                {
                    string id = token?.ToString().Trim();
                    if (!string.IsNullOrEmpty(id))
                    {
                        elementIds.Add(id);
                    }
                }
            }

            if (elementIds.Count == 0)
            {
                return ParameterError(
                    "elementId",
                    "xaml.elementId: не задан идентификатор активности",
                    new[] { "идентификаторы активностей возвращает xaml.get_workflow" },
                    GetSchema());
            }

            DesignerOpenResult opened = EnsureOpen(path);
            if (!opened.Ok)
            {
                return OpenFailure(opened);
            }

            var runner = new WorkflowEditRunner(Editing);
            WorkflowEditResult result = runner.Delete(path, elementIds, ParseValidation(arguments), ToolName);

            return Respond(result, IncludeOutline(arguments));
        }
    }
}
