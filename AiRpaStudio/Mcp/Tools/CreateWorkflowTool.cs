using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.create_workflow</c> — создаёт процесс из списка шагов палитры (этап 4 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Шаги становятся активностями корневого <c>Sequence</c>; XAML собирается объектной моделью
    /// и сериализатором WF, проверяется гейтом и сразу открывается в конструкторе (п. 1.3 плана).
    /// </para>
    /// </summary>
    internal sealed class CreateWorkflowTool : PaletteToolBase
    {
        internal const string ToolName = "xaml.create_workflow";

        internal CreateWorkflowTool(Func<IPaletteWorkflowService> serviceFactory)
            : base(serviceFactory)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Создаёт рабочий процесс RPA из списка шагов палитры, сохраняет валидный XAML " +
                    "и открывает его в конструкторе студии. Шаги становятся активностями корневого Sequence. " +
                    "Значения аргументов — только C#-выражения (expression) или литералы (value); лямбды запрещены.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "Путь к файлу процесса (.xaml), например C:\\Projects\\RPA-1\\Main.xaml"
                        },
                        project = new
                        {
                            type = "string",
                            description =
                                "Имя проекта (сборки) для TextExpression.ReferencesForImplementation; " +
                                "по умолчанию — имя папки файла"
                        },
                        @namespace = new
                        {
                            type = "string",
                            description = "Пространство имён класса процесса (по умолчанию из имени проекта)"
                        },
                        className = new
                        {
                            type = "string",
                            description = "Имя класса процесса (по умолчанию — имя файла без расширения)"
                        },
                        activities = new
                        {
                            type = "array",
                            description =
                                "Шаги процесса. Каждый шаг: { type, displayName?, typeArguments?, arguments?, children? }",
                            items = new { type = "object" }
                        }
                    },
                    required = new[] { "path" }
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
                    new[] { "пример: { \"path\": \"C:\\\\Projects\\\\RPA-1\\\\Main.xaml\", \"activities\": [{ \"type\": \"WriteLine\", \"arguments\": { \"Text\": { \"expression\": \"\\\"Готово\\\"\" } } }] }" },
                    GetSchema());
            }

            var request = new PaletteCreateWorkflowRequest
            {
                FilePath = path,
                ProjectName = Text(arguments, "project", "projectName"),
                NamespaceName = Text(arguments, "namespace"),
                ClassName = Text(arguments, "className")
            };

            var errors = new List<string>();
            JToken activitiesToken = arguments["activities"];

            if (activitiesToken is JArray activities)
            {
                for (int index = 0; index < activities.Count; index++)
                {
                    JToken item = activities[index];
                    PaletteActivitySpec spec = item.Type == JTokenType.String
                        ? new PaletteActivitySpec(item.ToString())
                        : ParseActivity(item as JObject, $"activities[{index}]", errors);

                    if (spec != null)
                    {
                        request.Activities.Add(spec);
                    }
                }
            }
            else if (activitiesToken != null && activitiesToken.Type != JTokenType.Null)
            {
                errors.Add("activities: ожидался массив шагов процесса");
            }

            if (errors.Count > 0)
            {
                return ParameterError("activities", string.Join("; ", errors), null, GetSchema());
            }

            return Respond(Service.CreateWorkflow(request));
        }
    }
}
