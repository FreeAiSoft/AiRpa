using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.create_workflow_from_code</c> — Code-First через Roslyn (этап 5 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Агент присылает C#-код процесса; песочница компилирует его, транслятор достаёт дерево активностей,
    /// дальше работает общий конвейер: канонизация → сериализатор WF → гейт валидации → вкладка
    /// конструктора. Текстовый XAML агентом не принимается вообще — только объектная модель (п. 1.1 плана).
    /// </para>
    /// <para>
    /// Ответ содержит машиночитаемую диагностику компиляции (<c>{code, line, column, message, hint}</c>),
    /// чтобы агент исправлял код сам, а не переспрашивал.
    /// </para>
    /// </summary>
    internal sealed class CreateWorkflowFromCodeTool : PaletteToolBase
    {
        internal const string ToolName = "xaml.create_workflow_from_code";

        private readonly Func<ICodeFirstWorkflowService> _serviceFactory;

        internal CreateWorkflowFromCodeTool(Func<ICodeFirstWorkflowService> serviceFactory)
        {
            _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        }

        /// <summary>
        /// Сервис Code-First. Создаётся по требованию: при старте студии UI-поток может быть ещё не готов,
        /// а падение конструктора инструментов не должно ломать MCP-сервер.
        /// </summary>
        private ICodeFirstWorkflowService CodeFirstService => _serviceFactory();

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Создаёт рабочий процесс из C#-кода (Roslyn): код компилируется в песочнице, дерево активностей " +
                    "сериализуется штатным сериализатором WF, XAML проходит гейт валидации и открывается в конструкторе. " +
                    "Код объявляет public static Activity Build() (или класс, наследующий Activity с Implementation). " +
                    "Выражения — только CSharpValue<T>/CSharpReference<T>, лямбды и System.IO/Reflection/Net/Diagnostics запрещены.",
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
                        code = new
                        {
                            type = "string",
                            description =
                                "Код процесса на C#. Пример: using System.Activities; using System.Activities.Statements; " +
                                "public static class Workflow { public static Activity Build() { var s = new Sequence(); " +
                                "s.Activities.Add(new WriteLine { Text = new InArgument<string>(\"готово\") }); return s; } }"
                        },
                        project = new
                        {
                            type = "string",
                            description =
                                "Имя проекта (сборки) для TextExpression.ReferencesForImplementation; по умолчанию — имя папки файла"
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
                        }
                    },
                    required = new[] { "path", "code" }
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
                    new[] { "пример: { \"path\": \"C:\\\\Projects\\\\RPA-1\\\\Main.xaml\", \"code\": \"...\" }" },
                    GetSchema());
            }

            string code = Text(arguments, "code", "source");
            if (string.IsNullOrWhiteSpace(code))
            {
                return ParameterError(
                    "code",
                    "xaml.code: код процесса не задан",
                    new[] { RoslynSandbox.CodeFirstContractHint },
                    GetSchema());
            }

            var request = new CodeFirstWorkflowRequest
            {
                Code = code,
                FilePath = path,
                ProjectName = Text(arguments, "project", "projectName"),
                NamespaceName = Text(arguments, "namespace"),
                ClassName = Text(arguments, "className")
            };

            CodeFirstResult result = CodeFirstService.Create(request);
            return Respond(result);
        }

        private static McpToolResult Respond(CodeFirstResult result)
        {
            var content = new JObject
            {
                ["ok"] = result.Ok,
                ["workflowId"] = result.WorkflowId,
                ["filePath"] = result.FilePath,
                ["entryPoint"] = result.EntryPoint,
                ["activityCount"] = result.ActivityCount,
                ["opened"] = result.Opened,
                ["activated"] = result.Activated,
                ["namespaces"] = ToArray(result.Namespaces),
                ["references"] = ToArray(result.References),
                ["diagnostics"] = DescribeDiagnostics(result.Diagnostics),
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

        /// <summary>Машиночитаемая диагностика компиляции: код, позиция, текст и подсказка.</summary>
        private static JArray DescribeDiagnostics(IEnumerable<RoslynDiagnostic> diagnostics)
        {
            var items = new JArray();

            foreach (RoslynDiagnostic diagnostic in diagnostics ?? Enumerable.Empty<RoslynDiagnostic>())
            {
                items.Add(new JObject
                {
                    ["code"] = diagnostic.Id,
                    ["severity"] = diagnostic.Severity,
                    ["line"] = diagnostic.Line,
                    ["column"] = diagnostic.Column,
                    ["message"] = diagnostic.Message,
                    ["hint"] = diagnostic.Hint
                });
            }

            return items;
        }
    }
}
