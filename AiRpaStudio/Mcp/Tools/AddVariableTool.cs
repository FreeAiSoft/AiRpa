using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.add_variable</c> — объявляет переменную в активности с коллекцией <c>Variables</c>
    /// (Sequence, Flowchart, корень процесса). Пачка переменных — одна атомарная правка.
    /// </summary>
    internal sealed class AddVariableTool : WorkflowEditToolBase
    {
        internal const string ToolName = "xaml.add_variable";

        internal AddVariableTool(Func<IWorkflowEditingService> editingFactory, Func<string, DesignerOpenResult> open = null)
            : base(editingFactory, open)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Объявляет переменную в активности, у которой есть коллекция Variables (Sequence, Flowchart, корень). " +
                    "Значение по умолчанию — литерал (value) или выражение (expression). Пачка variables — атомарно.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        elementId = new { type = "string", description = "Идентификатор владельца переменной (пусто — корень)" },
                        name = new { type = "string", description = "Имя переменной" },
                        typeName = new { type = "string", description = "Тип: String, Int32, Boolean, ..." },
                        value = new { description = "Значение по умолчанию литералом" },
                        expression = new { type = "string", description = "Значение по умолчанию выражением" },
                        language = new { type = "string", description = "csharp или vb (по умолчанию — по документу)" },
                        variables = new
                        {
                            type = "array",
                            description = "Несколько переменных одной атомарной пачкой",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    elementId = new { type = "string" },
                                    name = new { type = "string" },
                                    typeName = new { type = "string" },
                                    value = new { },
                                    expression = new { type = "string" },
                                    language = new { type = "string" }
                                }
                            }
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

            var requests = new List<WorkflowVariableAddRequest>();

            if (arguments["variables"] is JArray variables)
            {
                foreach (JToken token in variables)
                {
                    if (!(token is JObject item))
                    {
                        return ParameterError("variables", "xaml.variables: каждая переменная должна быть объектом", null, GetSchema());
                    }

                    if (!TryAddVariable(item, requests, out string error))
                    {
                        return ParameterError("variables", error, null, GetSchema());
                    }
                }
            }
            else if (!TryAddVariable(arguments, requests, out string singleError))
            {
                return ParameterError("name", singleError, null, GetSchema());
            }

            if (requests.Count == 0)
            {
                return ParameterError("name", "xaml.name: не задана переменная", null, GetSchema());
            }

            DesignerOpenResult opened = EnsureOpen(path);
            if (!opened.Ok)
            {
                return OpenFailure(opened);
            }

            var runner = new WorkflowEditRunner(Editing);
            WorkflowEditResult result = runner.AddVariable(path, requests, ParseValidation(arguments), ToolName);

            return Respond(result, IncludeOutline(arguments));
        }

        private static bool TryAddVariable(JObject source, List<WorkflowVariableAddRequest> requests, out string error)
        {
            error = null;

            var request = new WorkflowVariableAddRequest
            {
                ElementId = Text(source, "elementId") ?? Text(source, "parent"),
                Name = Text(source, "name"),
                TypeName = Text(source, "typeName") ?? Text(source, "type"),
                Expression = Text(source, "expression"),
                Language = Text(source, "language")
            };

            if (!TrySetLiteral(source, "value", value => request.SetValue(value), out string literalError))
            {
                error = literalError;
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                error = "xaml.name: не задано имя переменной";
                return false;
            }

            requests.Add(request);
            return true;
        }
    }
}
