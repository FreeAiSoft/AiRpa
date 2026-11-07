using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.create_workflow_from_json</c> — детерминированная сборка процесса по JSON AST
    /// (этап 6 конвейера генерации XAML).
    /// <para>
    /// Агент присылает описание процесса (активности, переменные, аргументы как C#-выражения), а не XAML:
    /// описание проверяется по строгой схеме (whitelist активностей и типов), затем превращается
    /// в объектную модель WF и проходит общий конвейер (канонизация → сериализатор WF → гейт валидации →
    /// вкладка конструктора). Текстовый XAML по-прежнему не принимается и не отдаётся как результат правки.
    /// </para>
    /// <para>
    /// Аргумент <c>describe: true</c> возвращает машиночитаемую схему (JSON Schema), whitelist активностей
    /// и типов — так агент не угадывает имена полей и активностей.
    /// </para>
    /// </summary>
    internal sealed class CreateWorkflowFromJsonTool : PaletteToolBase
    {
        internal const string ToolName = "xaml.create_workflow_from_json";

        private readonly Func<IJsonWorkflowService> _serviceFactory;

        internal CreateWorkflowFromJsonTool(Func<IJsonWorkflowService> serviceFactory)
        {
            _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        }

        /// <summary>
        /// Сервис JSON AST. Создаётся по требованию: при старте студии UI-поток может быть ещё не готов,
        /// а падение конструктора инструментов не должно ломать MCP-сервер.
        /// </summary>
        private IJsonWorkflowService JsonService => _serviceFactory();

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Создаёт рабочий процесс по строгому JSON-описанию (AST): активности, переменные и аргументы " +
                    "как C#-выражения. Описание проверяется до построения модели (whitelist активностей и типов), " +
                    "затем собирается объектная модель WF, XAML проходит гейт валидации и открывается в конструкторе. " +
                    "Вызов с {\"describe\": true} возвращает схему и whitelist. Пример: " +
                    "{\"path\": \"C:\\\\Projects\\\\RPA-1\\\\Main.xaml\", \"ast\": {\"variables\": [{\"name\": \"output\", " +
                    "\"type\": \"String\"}], \"activities\": [{\"type\": \"WriteLine\", \"arguments\": {\"Text\": " +
                    "\"\\\"готово\\\"\"}}]}}",
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
                        ast = new
                        {
                            description =
                                "JSON-описание процесса (объект или JSON-строка). Поля: project, name, namespace, " +
                                "variables[{name,type,default}], namespaces[], references[], activities[] | root. " +
                                "Активность: {type, displayName, typeArguments, arguments{}, children{}, variables}. " +
                                "Строка в аргументе — C#-выражение, литерал — {\"value\": ...}."
                        },
                        project = new
                        {
                            type = "string",
                            description = "Имя проекта (сборки); по умолчанию — из описания или из папки файла"
                        },
                        @namespace = new
                        {
                            type = "string",
                            description = "Пространство имён класса процесса"
                        },
                        className = new
                        {
                            type = "string",
                            description = "Имя класса процесса (по умолчанию — имя файла без расширения)"
                        },
                        describe = new
                        {
                            type = "boolean",
                            description = "Вернуть схему JSON AST, whitelist активностей и типов вместо сборки процесса"
                        }
                    },
                    required = new string[0]
                }
            };
        }

        protected override McpToolResult ExecuteCore(JObject arguments)
        {
            if (Flag(arguments, "describe"))
            {
                return DescribeSchema();
            }

            string path = Text(arguments, "path", "filePath");
            if (string.IsNullOrWhiteSpace(path))
            {
                return ParameterError(
                    "path",
                    "xaml.path: путь к файлу процесса не задан",
                    new[]
                    {
                        "пример: { \"path\": \"C:\\\\Projects\\\\RPA-1\\\\Main.xaml\", \"ast\": { \"activities\": [...] } }"
                    },
                    GetSchema());
            }

            string json = AstText(arguments);
            if (string.IsNullOrWhiteSpace(json))
            {
                return ParameterError(
                    "ast",
                    "xaml.ast: описание процесса не задано",
                    new[]
                    {
                        WorkflowJsonSchema.Describe(),
                        "пример: " + WorkflowJsonSchema.Example(),
                        "полная схема: вызовите инструмент с {\"describe\": true}"
                    },
                    GetSchema());
            }

            var request = new JsonWorkflowRequest
            {
                Json = json,
                FilePath = path,
                ProjectName = Text(arguments, "project", "projectName"),
                NamespaceName = Text(arguments, "namespace"),
                ClassName = Text(arguments, "className")
            };

            JsonWorkflowResult result = JsonService.Create(request);
            return Respond(result);
        }

        /// <summary>Схема и whitelist'ы: агент строит описание по ним, а не по догадке.</summary>
        private static McpToolResult DescribeSchema()
        {
            var content = new JObject
            {
                ["ok"] = true,
                ["schema"] = WorkflowJsonSchema.ToJsonSchema(),
                ["activities"] = new JArray(WorkflowJsonSchema.ActivityKeys().Cast<object>().ToArray()),
                ["types"] = new JArray(WorkflowJsonSchema.TypeNames().Cast<object>().ToArray()),
                ["example"] = WorkflowJsonSchema.Example()
            };

            return new McpToolResult { Success = true, Content = content };
        }

        /// <summary>Описание процесса может прийти объектом (обычно) или JSON-строкой (агент сериализовал сам).</summary>
        private static string AstText(JObject arguments)
        {
            JToken token = arguments?["ast"] ?? arguments?["workflow"];

            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            return token.ToString();
        }

        private static McpToolResult Respond(JsonWorkflowResult result)
        {
            var content = new JObject
            {
                ["ok"] = result.Ok,
                ["workflowId"] = result.WorkflowId,
                ["filePath"] = result.FilePath,
                ["activityCount"] = result.ActivityCount,
                ["opened"] = result.Opened,
                ["activated"] = result.Activated,
                ["variables"] = ToArray(result.Variables),
                ["namespaces"] = ToArray(result.Namespaces),
                ["references"] = ToArray(result.References),
                ["errors"] = ToArray(result.Errors),
                ["warnings"] = ToArray(result.Warnings),
                ["hints"] = ToArray(result.Hints)
            };

            // Собранный XAML возвращается только при ошибке: агенту он нужен для разбора, а не для правки.
            if (!result.Ok && !string.IsNullOrWhiteSpace(result.Xaml))
            {
                content["xaml"] = result.Xaml;
            }

            return new McpToolResult
            {
                Success = result.Ok,
                Content = content,
                Error = result.Ok ? null : string.Join(Environment.NewLine, result.Errors)
            };
        }
    }
}
