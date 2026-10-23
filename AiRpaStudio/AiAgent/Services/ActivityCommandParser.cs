using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace RPA.AiAgent.Services
{
    /// <summary>
    /// Интерфейс для парсинга команд из ответов AI
    /// </summary>
    public interface IActivityCommandParser
    {
        /// <summary>
        /// Парсит ответ AI и извлекает команды для MCP
        /// </summary>
        List<McpCommand> ParseCommands(string aiResponse);

        /// <summary>
        /// Проверяет, содержит ли текст команды
        /// </summary>
        bool ContainsCommands(string text);
    }

    /// <summary>
    /// Представляет команду для выполнения через MCP
    /// </summary>
    public class McpCommand
    {
        public string ToolName { get; set; }
        public object Arguments { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Парсер команд из ответов AI для MCP инструментов
    /// </summary>
    public class ActivityCommandParser : IActivityCommandParser
    {
        private static readonly Regex JsonBlockRegex = new Regex(
            @"```json\s*([\s\S]*?)\s*```",
            RegexOptions.Multiline
        );

        /// <summary>
        /// Проверяет, содержит ли текст команды
        /// </summary>
        public bool ContainsCommands(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && (
                   JsonBlockRegex.IsMatch(text) ||
                   text.Contains("\"jsonrpc\"") ||
                   text.Contains("\"action\"") ||
                   text.Contains("\"activityType\""));
        }

        /// <summary>
        /// Парсит ответ AI и извлекает команды для MCP
        /// </summary>
        public List<McpCommand> ParseCommands(string aiResponse)
        {
            List<McpCommand> commands = new List<McpCommand>();

            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                return commands;
            }

            // Ищем JSON блоки в markdown
            MatchCollection matches = JsonBlockRegex.Matches(aiResponse);

            foreach (Match match in matches)
            {
                try
                {
                    string jsonContent = match.Groups[1].Value;
                    McpCommand command = ParseSingleCommand(jsonContent);
                    if (command != null)
                    {
                        commands.Add(command);
                    }
                }
                catch (Exception ex)
                {
                    // Логируем, но продолжаем
                    Console.WriteLine($"Failed to parse command: {ex.Message}");
                }
            }

            // Если не нашли markdown блоки, пробуем найти JSON напрямую
            if (commands.Count == 0)
            {
                try
                {
                    McpCommand command = ParseSingleCommand(aiResponse);
                    if (command != null)
                    {
                        commands.Add(command);
                    }
                }
                catch { }
            }

            return commands;
        }

        /// <summary>
        /// Парсит одну команду из JSON строки
        /// Поддерживает JSON-RPC 2.0 формат и legacy формат для обратной совместимости
        /// </summary>
        private McpCommand ParseSingleCommand(string json)
        {
            JObject jObject = JObject.Parse(json);

            // Проверяем JSON-RPC 2.0 формат (приоритет)
            if (jObject["jsonrpc"] != null && jObject["method"]?.ToString() == "tools/call")
            {
                JToken paramsToken = jObject["params"];
                if (paramsToken != null)
                {
                    string toolName = paramsToken["name"]?.ToString();
                    JToken arguments = paramsToken["arguments"];

                    if (!string.IsNullOrEmpty(toolName) && arguments != null)
                    {
                        return new McpCommand
                        {
                            ToolName = toolName,
                            Arguments = arguments.ToObject<object>(),
                            Description = $"Call {toolName} tool"
                        };
                    }
                }
            }

            // Legacy формат для обратной совместимости
            string action = jObject["action"]?.ToString();

            if (!string.IsNullOrEmpty(action))
            {
                // Для add_activity обрабатываем специальный формат
                if (action == "add_activity")
                {
                    JToken activity = jObject["activity"];
                    if (activity != null)
                    {
                        return new McpCommand
                        {
                            ToolName = "add_activity",
                            Arguments = new
                            {
                                activityType = activity["type"]?.ToString(),
                                displayName = activity["displayName"]?.ToString(),
                                parameters = activity["parameters"],
                                children = activity["children"]
                            },
                            Description = $"Add {activity["type"]} activity"
                        };
                    }
                }
                else
                {
                    // Для остальных инструментов используем все поля кроме action
                    JObject args = new JObject();
                    foreach (JProperty prop in jObject.Properties())
                    {
                        if (prop.Name != "action")
                        {
                            args[prop.Name] = prop.Value;
                        }
                    }

                    return new McpCommand
                    {
                        ToolName = action,
                        Arguments = args.ToObject<object>(),
                        Description = $"Call {action} tool"
                    };
                }
            }

            return null;
        }
    }
}
