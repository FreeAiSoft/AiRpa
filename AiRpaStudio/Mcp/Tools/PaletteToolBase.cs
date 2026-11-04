using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;
using RPA.Helpers;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Общая часть инструментов палитры (<c>xaml.*</c>, этап 4 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Разбор аргументов, разбор описания активности и единый формат ответа
    /// <c>{ok, workflowId, filePath, activityId, xaml, errors[], warnings[], hints[]}</c> живут здесь,
    /// чтобы все четыре инструмента отвечали одинаково и агент учился на одном формате ошибок.
    /// </para>
    /// </summary>
    internal abstract class PaletteToolBase : IMcpTool
    {
        private readonly Func<IPaletteWorkflowService> _serviceFactory;

        /// <summary>
        /// Конструктор для инструментов, которым палитра не нужна (Code-First, этап 5 плана): база остаётся
        /// ради разбора аргументов и единого формата ответа.
        /// </summary>
        protected PaletteToolBase()
        {
        }

        protected PaletteToolBase(Func<IPaletteWorkflowService> serviceFactory)
        {
            _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        }

        /// <summary>
        /// Сервис палитры. Создаётся по требованию: при старте студии UI-поток может быть ещё не готов,
        /// а падение конструктора инструментов не должно ломать MCP-сервер.
        /// </summary>
        protected IPaletteWorkflowService Service => _serviceFactory != null
            ? _serviceFactory()
            : throw new InvalidOperationException("инструмент создан без сервиса палитры");

        public abstract McpTool GetSchema();

        public Task<McpToolResult> Execute(object arguments)
        {
            // MCP-вызов приходит из фонового потока: работа с конструктором уходит на UI-поток внутри
            // PaletteWorkflowService, поэтому здесь достаточно не блокировать вызывающего.
            return Task.Run(() =>
            {
                try
                {
                    return ExecuteCore(ParseArguments(arguments));
                }
                catch (Exception ex)
                {
                    return Failure(ErrorText.Describe("инструмент палитры не выполнен", ex));
                }
            });
        }

        protected abstract McpToolResult ExecuteCore(JObject arguments);

        /// <summary>Ответ инструмента по результату палитры.</summary>
        protected static McpToolResult Respond(PaletteResult result)
        {
            var content = new JObject
            {
                ["ok"] = result.Ok,
                ["workflowId"] = result.WorkflowId,
                ["filePath"] = result.FilePath,
                ["activityId"] = result.ActivityId,
                ["activityCount"] = result.Snapshot?.ActivityCount,
                ["errors"] = ToArray(result.Errors),
                ["warnings"] = ToArray(result.Warnings),
                ["hints"] = ToArray(result.Hints)
            };

            if (!string.IsNullOrWhiteSpace(result.Xaml))
            {
                content["xaml"] = result.Xaml;
            }

            if (result.Snapshot?.Root != null)
            {
                content["workflow"] = DescribeActivity(result.Snapshot.Root);
            }

            return new McpToolResult
            {
                Success = result.Ok,
                Content = content,
                Error = result.Ok ? null : string.Join(Environment.NewLine, result.Errors)
            };
        }

        /// <summary>Ошибка аргументов инструмента: агент видит, какой параметр неверен и что делать.</summary>
        protected static McpToolResult ParameterError(string parameterName, string message, IEnumerable<string> hints = null, McpTool schema = null)
        {
            var content = new JObject
            {
                ["ok"] = false,
                ["errors"] = ToArray(new[] { message }),
                ["hints"] = ToArray(hints ?? Enumerable.Empty<string>())
            };

            return new McpToolResult
            {
                Success = false,
                Content = content,
                Error = message,
                ValidationError = new ValidationError
                {
                    Type = ValidationErrorType.MissingRequiredParameter,
                    Message = message,
                    ParameterName = parameterName,
                    ToolSchema = schema?.InputSchema
                }
            };
        }

        /// <summary>Ответ «инструмент не выполнен» (исключение внутри инструмента, не ошибка аргументов).</summary>
        protected static McpToolResult Failure(string message)
        {
            return new McpToolResult
            {
                Success = false,
                Content = new JObject
                {
                    ["ok"] = false,
                    ["errors"] = ToArray(new[] { message })
                },
                Error = message
            };
        }

        protected static JObject ParseArguments(object arguments)
        {
            if (arguments == null)
            {
                return new JObject();
            }

            if (arguments is JObject parsed)
            {
                return parsed;
            }

            if (arguments is JValue value)
            {
                return value.Type == JTokenType.String
                    ? ParseArguments(value.Value<string>())
                    : new JObject();
            }

            if (arguments is string text)
            {
                try
                {
                    return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
                }
                catch (Exception)
                {
                    return new JObject();
                }
            }

            try
            {
                return JObject.FromObject(arguments);
            }
            catch (Exception)
            {
                return new JObject();
            }
        }

        protected static string Text(JObject arguments, params string[] names)
        {
            if (arguments == null || names == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                JToken token = arguments[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    continue;
                }

                if (token.Type == JTokenType.String)
                {
                    string value = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }

                    continue;
                }

                return token.ToString();
            }

            return null;
        }

        protected static bool Flag(JObject arguments, string name)
        {
            JToken token = arguments?[name];
            if (token == null)
            {
                return false;
            }

            return token.Type == JTokenType.Boolean
                ? token.Value<bool>()
                : bool.TryParse(token.ToString(), out bool parsed) && parsed;
        }

        protected static int? Number(JObject arguments, string name)
        {
            JToken token = arguments?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            return int.TryParse(token.ToString(), out int parsed) ? parsed : (int?)null;
        }

        /// <summary>
        /// Описание активности из JSON: <c>type</c>, <c>displayName</c>, <c>typeArguments</c>,
        /// <c>arguments</c> (значение или объект <c>{expression|value, type}</c>) и <c>children</c>
        /// (контейнер → массив активностей).
        /// </summary>
        protected static PaletteActivitySpec ParseActivity(JObject json, string path, List<string> errors)
        {
            if (json == null)
            {
                errors.Add($"{path}: описание активности не задано");
                return null;
            }

            string type = Text(json, "type", "activityType");
            if (string.IsNullOrWhiteSpace(type))
            {
                errors.Add($"{path}: не задан тип активности (type), например \"WriteLine\"");
                return null;
            }

            var spec = new PaletteActivitySpec(type)
            {
                DisplayName = Text(json, "displayName")
            };

            if (json["typeArguments"] is JArray typeArguments)
            {
                foreach (JToken item in typeArguments)
                {
                    spec.TypeArguments.Add(item.ToString());
                }
            }

            if (json["arguments"] is JObject arguments)
            {
                foreach (JProperty property in arguments.Properties())
                {
                    PaletteValue value = ParseValue(property.Value, $"{path}.arguments.{property.Name}", errors);
                    if (value != null)
                    {
                        spec.WithArgument(property.Name, value);
                    }
                }
            }

            if (json["children"] is JObject children)
            {
                foreach (JProperty property in children.Properties())
                {
                    if (!(property.Value is JArray items))
                    {
                        errors.Add($"{path}.children.{property.Name}: ожидался массив активностей");
                        continue;
                    }

                    foreach (JToken item in items)
                    {
                        PaletteActivitySpec child = ParseActivity(
                            item as JObject, $"{path}.children.{property.Name}", errors);

                        if (child != null)
                        {
                            spec.WithChild(property.Name, child);
                        }
                    }
                }
            }

            return spec;
        }

        private static PaletteValue ParseValue(JToken token, string path, List<string> errors)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                errors.Add($"{path}: значение не задано");
                return null;
            }

            if (token is JObject value)
            {
                string expression = Text(value, "expression", "expr");
                string typeName = Text(value, "type", "typeName");

                if (!string.IsNullOrWhiteSpace(expression))
                {
                    return PaletteValue.FromExpression(expression, typeName);
                }

                if (value["value"] != null)
                {
                    return PaletteValue.FromLiteral(LiteralOf(value["value"]), typeName);
                }

                errors.Add($"{path}: нужен expression (C#-выражение) или value (литерал)");
                return null;
            }

            if (token is JArray)
            {
                errors.Add($"{path}: массив как значение аргумента не поддерживается");
                return null;
            }

            return PaletteValue.FromLiteral(LiteralOf(token));
        }

        private static object LiteralOf(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token is JValue value)
            {
                return value.Value;
            }

            return token.ToString();
        }

        protected static JArray ToArray(IEnumerable<string> items)
        {
            return new JArray((items ?? Enumerable.Empty<string>()).Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        /// <summary>Дерево процесса для ответа <c>xaml.get_workflow</c>.</summary>
        private static JObject DescribeActivity(WorkflowActivitySnapshot node)
        {
            if (node == null)
            {
                return null;
            }

            return new JObject
            {
                ["id"] = node.Id,
                ["type"] = node.TypeName,
                ["displayName"] = node.DisplayName,
                ["arguments"] = new JObject(node.Arguments
                    .Where(argument => argument.IsSet)
                    .ToDictionary(argument => argument.Name, argument => (JToken)argument.Expression)),
                ["children"] = new JArray(node.Children.Select(child => (JToken)DescribeActivity(child)))
            };
        }
    }
}
