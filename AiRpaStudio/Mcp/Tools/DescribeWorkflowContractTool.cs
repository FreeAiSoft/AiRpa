using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// <c>xaml.describe_contract</c> — контракт генерации процесса для агента (этап 7 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Инструмент ничего не собирает и ни чего не пишет на диск: он отдаёт правила (только
    /// <c>CSharpValue&lt;T&gt;</c>/<c>CSharpReference&lt;T&gt;</c>, только whitelist палитры), матрицу выбора
    /// инструмента, few-shot примеры и правило самокоррекции. Так агент получает контракт из студии,
    /// а не из «памяти модели», и примеры в его ответе совпадают с теми, что проверены тестами.
    /// </para>
    /// <para>
    /// Аргументы необязательные: <c>example</c> — вернуть только один пример по имени,
    /// <c>format: "text"</c> — вернуть готовый текст для системного промпта.
    /// </para>
    /// </summary>
    internal sealed class DescribeWorkflowContractTool : PaletteToolBase
    {
        internal const string ToolName = ContractToolNames.Describe;

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = ToolName,
                Description =
                    "Возвращает контракт генерации рабочего процесса: жёсткие правила (только CSharpValue<T>/" +
                    "CSharpReference<T>, только активности и типы палитры, XAML текстом не пишется), матрицу выбора " +
                    "инструмента (палитра / Code-First / JSON AST), few-shot примеры (Sequence+Assign, If, ForEach<T>, " +
                    "While, TryCatch, Bookmark+Persist) и правило самокоррекции при ошибке гейта. Ничего не собирает " +
                    "и не пишет на диск. Пример: {\"example\": \"if\"} или {\"format\": \"text\"} для текста промпта.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        example = new
                        {
                            type = "string",
                            description = "Имя примера: sequence-assign, if, foreach, while, trycatch, bookmark-persist"
                        },
                        format = new
                        {
                            type = "string",
                            description = "json (по умолчанию) — поля контракта; text — готовый текст для системного промпта"
                        }
                    },
                    required = new string[0]
                }
            };
        }

        protected override McpToolResult ExecuteCore(JObject arguments)
        {
            string exampleName = Text(arguments, "example");
            WorkflowAgentExample single = null;

            if (!string.IsNullOrWhiteSpace(exampleName))
            {
                single = WorkflowAgentContract.FindExample(exampleName);
                if (single == null)
                {
                    return ParameterError(
                        "example",
                        "xaml.example: пример «" + exampleName + "» не найден",
                        new[]
                        {
                            "доступные примеры: " +
                            string.Join(", ", WorkflowAgentContract.Examples.Select(example => example.Name)),
                            "вызовите инструмент без аргументов, чтобы получить весь контракт"
                        },
                        GetSchema());
                }
            }

            var content = new JObject
            {
                ["ok"] = true,
                ["contract"] = WorkflowAgentContract.Version,
                ["rules"] = ToArray(WorkflowAgentContract.Rules),
                ["toolSelection"] = new JArray(WorkflowAgentContract.ToolSelection
                    .Select(choice => new JObject
                    {
                        ["situation"] = choice.Situation,
                        ["tool"] = choice.Tool,
                        ["reason"] = choice.Reason
                    })
                    .Cast<object>()
                    .ToArray()),
                ["examples"] = new JArray(
                    (single != null ? new[] { single } : WorkflowAgentContract.Examples)
                    .Select(ToJson)
                    .Cast<object>()
                    .ToArray()),
                ["selfCorrection"] = ToArray(WorkflowAgentContract.SelfCorrection),
                ["hints"] = ToArray(new[]
                {
                    "примеры проверены прогоном через конвейер: собранный XAML открывается в конструкторе",
                    "схема JSON AST и whitelist активностей: " + ContractToolNames.Json + " с {\"describe\": true}",
                    "текст контракта для системного промпта: аргумент format = text; версия для человека — docs\\Workflow_Agent_Contract.md"
                })
            };

            if (string.Equals(Text(arguments, "format"), "text", StringComparison.OrdinalIgnoreCase))
            {
                content["text"] = single != null ? DescribeExample(single) : WorkflowAgentContract.Describe();
            }

            return new McpToolResult { Success = true, Content = content };
        }

        /// <summary>Один пример в машиночитаемом виде: имя, сценарий, инструмент, тело, пояснение.</summary>
        private static JObject ToJson(WorkflowAgentExample example)
        {
            return new JObject
            {
                ["name"] = example.Name,
                ["scenario"] = example.Scenario,
                ["tool"] = example.ToolName,
                ["format"] = example.FormatName,
                ["payload"] = example.Payload,
                ["notes"] = example.Notes
            };
        }

        /// <summary>Текст одного примера — чтобы агент мог подставить его в промпт целиком.</summary>
        private static string DescribeExample(WorkflowAgentExample example)
        {
            return "### " + example.Name + " — " + example.Scenario + " (" + example.ToolName + ")" +
                   Environment.NewLine + "```" + example.FormatName + Environment.NewLine +
                   example.Payload + Environment.NewLine + "```" + Environment.NewLine +
                   "Почему так: " + example.Notes;
        }
    }
}
