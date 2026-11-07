using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.add_activity</c> — вставляет активность в открытый процесс (этап 4 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Вставка идёт через <c>ModelService</c>/<c>ModelItem</c>: ViewState и IdRef проставляет сам
    /// конструктор, поэтому активность сразу видна на диаграмме (п. 3 этапа 4). После правки файл
    /// сохраняется, прогоняется через гейт и вкладка остаётся активной; при провале гейта правка
    /// откатывается.
    /// </para>
    /// </summary>
    internal sealed class AddActivityTool : PaletteToolBase
    {
        internal const string ToolName = "xaml.add_activity";

        internal AddActivityTool(Func<IPaletteWorkflowService> serviceFactory)
            : base(serviceFactory)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Вставляет активность палитры в открытый процесс (файл открывается автоматически). " +
                    "Без parentId активность добавляется в корневой Sequence; parentId и container берутся " +
                    "из xaml.get_workflow. Значения аргументов — expression (C#-выражение) или value (литерал).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        type = new
                        {
                            type = "string",
                            description = "Тип активности: Sequence, If, While, Assign, WriteLine, Delay, InvokeMethod..."
                        },
                        displayName = new { type = "string", description = "Подпись активности в конструкторе" },
                        typeArguments = new
                        {
                            type = "array",
                            description = "Аргументы типа для обобщённых активностей (ForEach, Switch, AddToCollection)",
                            items = new { type = "string" }
                        },
                        arguments = new
                        {
                            type = "object",
                            description =
                                "Аргументы: { \"Text\": { \"expression\": \"\\\"привет\\\"\" } } или " +
                                "{ \"Text\": { \"value\": \"привет\" } }"
                        },
                        children = new
                        {
                            type = "object",
                            description =
                                "Вложенные активности по контейнерам: { \"Activities\": [ ... ], \"Then\": [ ... ] }"
                        },
                        parentId = new
                        {
                            type = "string",
                            description = "Id родительской активности из xaml.get_workflow (пусто — корень процесса)"
                        },
                        container = new
                        {
                            type = "string",
                            description = "Имя контейнера родителя: Activities, Then, Else, Body, Try, Finally"
                        },
                        index = new
                        {
                            type = "integer",
                            description = "Позиция в коллекции родителя; по умолчанию — в конец"
                        }
                    },
                    required = new[] { "path", "type" }
                }
            };
        }

        protected override McpToolResult ExecuteCore(JObject arguments)
        {
            string path = Text(arguments, "path", "filePath");
            if (string.IsNullOrWhiteSpace(path))
            {
                return ParameterError(
                    "path",
                    "xaml.path: путь к файлу процесса не задан",
                    new[] { "файл процесса открывается автоматически, если он существует" },
                    GetSchema());
            }

            var errors = new List<string>();
            PaletteActivitySpec spec = ParseActivity(arguments, "activity", errors);
            if (errors.Count > 0 || spec == null)
            {
                return ParameterError("type", string.Join("; ", errors), null, GetSchema());
            }

            int? index = Number(arguments, "index");
            if (index.HasValue && index.Value < 0)
            {
                return ParameterError("index", "xaml.index: позиция не может быть отрицательной", null, GetSchema());
            }

            var request = new PaletteAddActivityRequest
            {
                FilePath = path,
                Activity = spec,
                ParentActivityId = Text(arguments, "parentId", "parentActivityId"),
                ContainerName = Text(arguments, "container", "containerName"),
                Index = index
            };

            return Respond(Service.AddActivity(request));
        }
    }
}
