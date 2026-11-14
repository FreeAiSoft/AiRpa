using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.remove_variable</c> — удаляет переменную по имени из активности с коллекцией
    /// <c>Variables</c>. Пачка имён — одна атомарная правка.
    /// </summary>
    internal sealed class RemoveVariableTool : WorkflowEditToolBase
    {
        internal const string ToolName = "xaml.remove_variable";

        internal RemoveVariableTool(Func<IWorkflowEditingService> editingFactory, Func<string, DesignerOpenResult> open = null)
            : base(editingFactory, open)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Удаляет переменную по имени из активности с коллекцией Variables (Sequence, Flowchart, корень). " +
                    "Неизвестные имена возвращаются ошибкой со списком объявленных переменных.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        elementId = new { type = "string", description = "Идентификатор владельца переменной (пусто — корень)" },
                        name = new { type = "string", description = "Имя удаляемой переменной" },
                        variables = new
                        {
                            type = "array",
                            description = "Имена переменных (или объекты {elementId, name}) для удаления пачкой",
                            items = new { }
                        },
                        validate = new { type = "string", description = "report (по умолчанию), off или strict" },
                        include_outline = new { type = "boolean", description = "Вернуть структуру после правки" }
                    },
                    required = new[] { "path", "name" }
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

            var requests = new List<WorkflowVariableRemoveRequest>();
            string owner = Text(arguments, "elementId");

            if (arguments["variables"] is JArray variables)
            {
                foreach (JToken token in variables)
                {
                    if (token is JObject item)
                    {
                        string name = Text(item, "name");
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            return ParameterError("variables", "xaml.variables: каждый объект должен содержать name", null, GetSchema());
                        }

                        requests.Add(new WorkflowVariableRemoveRequest
                        {
                            ElementId = Text(item, "elementId") ?? owner,
                            Name = name
                        });
                        continue;
                    }

                    string plain = token?.ToString().Trim();
                    if (!string.IsNullOrEmpty(plain))
                    {
                        requests.Add(new WorkflowVariableRemoveRequest { ElementId = owner, Name = plain });
                    }
                }
            }
            else
            {
                string name = Text(arguments, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return ParameterError("name", "xaml.name: не задано имя переменной", null, GetSchema());
                }

                requests.Add(new WorkflowVariableRemoveRequest { ElementId = owner, Name = name });
            }

            if (requests.Count == 0)
            {
                return ParameterError("name", "xaml.name: не задано имя переменной", null, GetSchema());
            }

            DesignerOpenResult opened = EnsureOpen(path);
            if (!opened.Ok)
            {
                return OpenFailure(opened);
            }

            var runner = new WorkflowEditRunner(Editing);
            WorkflowEditResult result = runner.RemoveVariable(path, requests, ParseValidation(arguments), ToolName);

            return Respond(result, IncludeOutline(arguments));
        }
    }
}
