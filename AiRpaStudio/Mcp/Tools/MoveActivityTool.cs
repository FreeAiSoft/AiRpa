using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.move_activity</c> — переносит активность в другой контейнер открытого процесса.
    /// Аргументы активности сохраняются; адреса берутся из <c>xaml.get_workflow</c>.
    /// </summary>
    internal sealed class MoveActivityTool : WorkflowEditToolBase
    {
        internal const string ToolName = "xaml.move_activity";

        internal MoveActivityTool(Func<IWorkflowEditingService> editingFactory, Func<string, DesignerOpenResult> open = null)
            : base(editingFactory, open)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Переносит активность в другой контейнер открытого процесса (аргументы сохраняются). " +
                    "Адреса elementId/parent берутся из xaml.get_workflow; несколько переносов — пачкой moves.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        elementId = new { type = "string", description = "Идентификатор переносимой активности" },
                        parent = new { type = "string", description = "Идентификатор нового родителя (пусто — корень)" },
                        container = new { type = "string", description = "Имя контейнера нового родителя (Activities, Then, Body)" },
                        index = new { type = "integer", description = "Позиция в новом контейнере (по умолчанию — в конец)" },
                        moves = new
                        {
                            type = "array",
                            description = "Несколько переносов одной атомарной пачкой",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    elementId = new { type = "string" },
                                    parent = new { type = "string" },
                                    container = new { type = "string" },
                                    index = new { type = "integer" }
                                }
                            }
                        },
                        validate = new { type = "string", description = "report (по умолчанию), off или strict" },
                        include_outline = new { type = "boolean", description = "Вернуть структуру после правки" }
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

            var requests = new List<WorkflowMoveRequest>();

            if (arguments["moves"] is JArray moves)
            {
                foreach (JToken token in moves)
                {
                    if (!(token is JObject item))
                    {
                        return ParameterError("moves", "xaml.moves: каждый перенос должен быть объектом", null, GetSchema());
                    }

                    if (!TryAddMove(item, requests, out string error))
                    {
                        return ParameterError("moves", error, null, GetSchema());
                    }
                }
            }
            else if (!TryAddMove(arguments, requests, out string singleError))
            {
                return ParameterError("elementId", singleError, null, GetSchema());
            }

            if (requests.Count == 0)
            {
                return ParameterError("elementId", "xaml.elementId: не задан перенос активности", null, GetSchema());
            }

            DesignerOpenResult opened = EnsureOpen(path);
            if (!opened.Ok)
            {
                return OpenFailure(opened);
            }

            var runner = new WorkflowEditRunner(Editing);
            WorkflowEditResult result = runner.Move(path, requests, ParseValidation(arguments), ToolName);

            return Respond(result, IncludeOutline(arguments));
        }

        private static bool TryAddMove(JObject source, List<WorkflowMoveRequest> requests, out string error)
        {
            error = null;

            string elementId = Text(source, "elementId");
            if (string.IsNullOrWhiteSpace(elementId))
            {
                error = "xaml.elementId: не задан идентификатор переносимой активности";
                return false;
            }

            int? index = Number(source, "index");
            if (index.HasValue && index.Value < 0)
            {
                error = "xaml.index: позиция не может быть отрицательной";
                return false;
            }

            requests.Add(new WorkflowMoveRequest
            {
                ElementId = elementId,
                ParentId = Text(source, "parent"),
                Container = Text(source, "container"),
                Index = index
            });

            return true;
        }
    }
}
