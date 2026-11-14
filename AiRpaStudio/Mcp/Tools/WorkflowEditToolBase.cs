using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Общая часть MCP-инструментов палитры, которые правят уже открытый процесс через
    /// <see cref="IWorkflowEditingService"/> (тот же мост «агент ↔ открытый дизайнер», что использовали
    /// вынесенные чат-инструменты <c>activity_*</c>/<c>argument_set</c>/<c>variable_*</c>).
    /// <para>
    /// Здесь — разбор аргументов, приведение <see cref="WorkflowEditResult"/> к единому MCP-ответу
    /// <c>{ok, operation, message, elementId, newElementId, addresses[], appliedCount, canUndo, canRedo,
    /// validation, errors[], hints[], text}</c> и (опционально) открытие вкладки до правки.
    /// Сама оркестрация правки остаётся в <see cref="WorkflowEditRunner"/>: атомарность пачки и валидация
    /// не дублируются, поэтому миграция не меняет поведение.
    /// </para>
    /// </summary>
    internal abstract class WorkflowEditToolBase : PaletteToolBase
    {
        private readonly Func<IWorkflowEditingService> _editingFactory;
        private readonly Func<string, DesignerOpenResult> _open;

        /// <param name="editingFactory">Мост правок (в студии — <c>StudioWorkflowEditingService.Default</c>).</param>
        /// <param name="open">
        /// Открытие вкладки до правки (по умолчанию — студийный <see cref="DesignerOpenService"/>).
        /// Тесты подменяют его, чтобы не трогать UI-поток.
        /// </param>
        protected WorkflowEditToolBase(
            Func<IWorkflowEditingService> editingFactory,
            Func<string, DesignerOpenResult> open = null)
        {
            _editingFactory = editingFactory ?? throw new ArgumentNullException(nameof(editingFactory));
            _open = open ?? (path => DesignerOpenService.ForStudio().OpenOrActivate(path));
        }

        /// <summary>Мост правок; создаётся по требованию, чтобы регистрация не трогала UI-поток.</summary>
        protected IWorkflowEditingService Editing => _editingFactory();

        /// <summary>Открывает вкладку процесса (или активирует уже открытую).</summary>
        protected DesignerOpenResult EnsureOpen(string path)
        {
            return _open(path);
        }

        /// <summary>Приводит результат правки к единому MCP-ответу; <c>text</c> — человекочитаемый ответ.</summary>
        protected static McpToolResult Respond(WorkflowEditResult result, bool includeOutline)
        {
            if (result == null)
            {
                return Failure("правка не вернула результат");
            }

            var errors = result.Issues
                .Where(issue => issue.IsError)
                .Select(issue => issue.ToString())
                .ToArray();

            var hints = result.Issues
                .Where(issue => !issue.IsError)
                .Select(issue => issue.ToString())
                .ToArray();

            var content = new JObject
            {
                ["ok"] = result.Ok,
                ["operation"] = result.Operation,
                ["message"] = result.Message,
                ["elementId"] = result.ElementId,
                ["newElementId"] = result.NewElementId,
                ["addresses"] = ToArray(result.Addresses),
                ["appliedCount"] = result.AppliedCount,
                ["canUndo"] = result.CanUndo,
                ["canRedo"] = result.CanRedo,
                ["validation"] = result.Validation?.Summary,
                ["errors"] = ToArray(errors),
                ["hints"] = ToArray(hints),
                ["text"] = WorkflowEditText.Describe(result, includeOutline)
            };

            return new McpToolResult
            {
                Success = result.Ok,
                Content = content,
                Error = result.Ok ? null : string.Join(Environment.NewLine, errors)
            };
        }

        /// <summary>Ответ «вкладка не открылась».</summary>
        protected static McpToolResult OpenFailure(DesignerOpenResult opened)
        {
            string message = opened != null && opened.Errors.Count > 0
                ? string.Join(Environment.NewLine, opened.Errors)
                : "вкладка процесса не открыта";

            return Failure(message);
        }

        /// <summary><c>validate</c>: <c>report</c> (по умолчанию), <c>off</c> или <c>strict</c>.</summary>
        protected static WorkflowEditValidation ParseValidation(JObject arguments)
        {
            string text = Text(arguments, "validate");

            switch (text?.ToLowerInvariant())
            {
                case "off":
                case "none":
                case "false":
                    return WorkflowEditValidation.Off;
                case "strict":
                    return WorkflowEditValidation.Strict;
                default:
                    return WorkflowEditValidation.Report;
            }
        }

        /// <summary>Показывать структуру процесса в ответе (по умолчанию — да).</summary>
        protected static bool IncludeOutline(JObject arguments)
        {
            JToken token = arguments?["include_outline"];
            if (token == null || token.Type == JTokenType.Null || string.IsNullOrWhiteSpace(token.ToString()))
            {
                return true;
            }

            return !bool.TryParse(token.ToString().Trim(), out bool value) || value;
        }

        /// <summary>Литерал (<c>value</c>): строка, число или <c>true/false</c>; пустая строка — тоже литерал.</summary>
        protected static bool TrySetLiteral(JObject source, string name, Action<string> set, out string error)
        {
            error = null;
            JToken token = source?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            {
                error = "Invalid \"" + name + "\" argument: expected a string, number or boolean literal.";
                return false;
            }

            set(token.Type == JTokenType.String ? token.Value<string>() : token.ToString());
            return true;
        }

        /// <summary>Непустая строка аргумента; <c>null</c> — не задан.</summary>
        protected static string RequiredText(JObject arguments, string name)
        {
            string value = Text(arguments, name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
