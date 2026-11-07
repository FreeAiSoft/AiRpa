using System;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.set_argument</c> — задаёт аргумент существующей активности (этап 4 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Значение становится <c>CSharpValue&lt;T&gt;</c> (In) или <c>CSharpReference&lt;T&gt;</c> (Out/InOut):
    /// именно такие выражения редактируются Roslyn-редактором студии и переживают сериализацию.
    /// </para>
    /// </summary>
    internal sealed class SetArgumentTool : PaletteToolBase
    {
        internal const string ToolName = "xaml.set_argument";

        internal SetArgumentTool(Func<IPaletteWorkflowService> serviceFactory)
            : base(serviceFactory)
        {
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Задаёт аргумент активности открытого процесса: C#-выражение (expression) или литерал (value). " +
                    "Активность адресуется id из xaml.get_workflow. Для Out/InOut-аргументов допустимо только выражение.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string", description = "Путь к файлу процесса (.xaml)" },
                        activityId = new
                        {
                            type = "string",
                            description = "Id активности из xaml.get_workflow (или её displayName, если он уникален)"
                        },
                        argument = new { type = "string", description = "Имя аргумента: Text, Condition, To, Value..." },
                        expression = new
                        {
                            type = "string",
                            description = "C#-выражение, например \"DateTime.Now.ToString()\""
                        },
                        value = new { description = "Литеральное значение (строка, число, true/false)" },
                        type = new
                        {
                            type = "string",
                            description = "Имя типа значения для необобщённых аргументов (String, Int32, Boolean...)"
                        }
                    },
                    required = new[] { "path", "activityId", "argument" }
                }
            };
        }

        protected override McpToolResult ExecuteCore(JObject arguments)
        {
            string path = Text(arguments, "path", "filePath");
            if (string.IsNullOrWhiteSpace(path))
            {
                return ParameterError("path", "xaml.path: путь к файлу процесса не задан", null, GetSchema());
            }

            string activityId = Text(arguments, "activityId", "id");
            if (string.IsNullOrWhiteSpace(activityId))
            {
                return ParameterError(
                    "activityId",
                    "xaml.activityId: не задан идентификатор активности",
                    new[] { "идентификаторы активностей возвращает xaml.get_workflow" },
                    GetSchema());
            }

            string argumentName = Text(arguments, "argument", "argumentName");
            if (string.IsNullOrWhiteSpace(argumentName))
            {
                return ParameterError("argument", "xaml.argument: не задано имя аргумента", null, GetSchema());
            }

            string expression = Text(arguments, "expression", "expr");
            JToken valueToken = arguments["value"];

            PaletteValue value = !string.IsNullOrWhiteSpace(expression)
                ? PaletteValue.FromExpression(expression, Text(arguments, "type", "typeName"))
                : valueToken != null && valueToken.Type != JTokenType.Null
                    ? PaletteValue.FromLiteral(
                        valueToken is JValue jValue ? jValue.Value : valueToken.ToString(),
                        Text(arguments, "type", "typeName"))
                    : null;

            if (value == null)
            {
                return ParameterError(
                    "expression",
                    "xaml.value: не задано ни expression, ни value",
                    new[] { "пример: { \"activityId\": \"root/Activities[0]\", \"argument\": \"Text\", \"expression\": \"\\\"Готово\\\"\" }" },
                    GetSchema());
            }

            var request = new PaletteSetArgumentRequest
            {
                FilePath = path,
                ActivityId = activityId,
                ArgumentName = argumentName,
                Value = value
            };

            return Respond(Service.SetArgument(request));
        }
    }
}
