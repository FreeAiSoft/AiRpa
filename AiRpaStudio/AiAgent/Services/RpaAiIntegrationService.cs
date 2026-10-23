using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RPA.Mcp;

namespace RPA.AiAgent.Services
{
    /// <summary>
    /// Интерфейс для интеграции AI с RPA через MCP
    /// </summary>
    public interface IRpaAiIntegrationService
    {
        /// <summary>
        /// Обрабатывает ответ AI и выполняет команды
        /// </summary>
        Task<RpaOperationResult> ProcessAiResponseAsync(string aiResponse, Func<string, bool> checkPermission = null);

        /// <summary>
        /// Генерирует контекст для AI (доступные инструменты)
        /// </summary>
        Task<string> GenerateAiContextAsync();
    }

    /// <summary>
    /// Детальный результат выполнения RPA операций
    /// </summary>
    public class RpaOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> ExecutedCommands { get; set; }
        public List<string> Errors { get; set; }
        public bool HasMoreCommands { get; set; }
        public int TotalCommandsFound { get; set; }

        public RpaOperationResult()
        {
            ExecutedCommands = new List<string>();
            Errors = new List<string>();
        }
    }

    /// <summary>
    /// Сервис интеграции AI с RPA через MCP протокол
    /// Связывает AI чат с MCP сервером для выполнения команд
    /// </summary>
    public class RpaAiIntegrationService : IRpaAiIntegrationService
    {
        private readonly IActivityCommandParser _parser;
        private readonly IMcpServer _mcpServer;

        public RpaAiIntegrationService(
            IActivityCommandParser parser,
            IMcpServer mcpServer)
        {
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _mcpServer = mcpServer ?? throw new ArgumentNullException(nameof(mcpServer));
        }

        /// <summary>
        /// Обрабатывает ответ AI и выполняет команды через MCP
        /// </summary>
        /// <param name="aiResponse">Ответ AI для обработки</param>
        /// <param name="checkPermission">Функция проверки разрешения на выполнение инструмента</param>
        public async Task<RpaOperationResult> ProcessAiResponseAsync(string aiResponse, Func<string, bool> checkPermission = null)
        {
            Debug.WriteLine($"[RpaAiIntegration] === ProcessAiResponseAsync START ===");
            Debug.WriteLine($"[RpaAiIntegration] AI Response length: {aiResponse?.Length ?? 0}");

            RpaOperationResult result = new RpaOperationResult
            {
                Success = true
            };

            try
            {
                // Проверяем наличие команд
                bool containsCommands = _parser.ContainsCommands(aiResponse);
                Debug.WriteLine($"[RpaAiIntegration] ContainsCommands: {containsCommands}");

                if (!containsCommands)
                {
                    result.Success = false;
                    result.Message = "No commands found in AI response";
                    Debug.WriteLine($"[RpaAiIntegration] No commands found, returning");
                    return result;
                }

                // Парсим команды
                List<McpCommand> commands = _parser.ParseCommands(aiResponse);
                Debug.WriteLine($"[RpaAiIntegration] Parsed {commands.Count} command(s)");

                if (commands.Count == 0)
                {
                    result.Success = false;
                    result.Message = "Failed to parse commands from AI response";
                    Debug.WriteLine($"[RpaAiIntegration] Parse failed, returning");
                    return result;
                }

                // КРИТИЧЕСКИ ВАЖНО: Выполняем только ПЕРВУЮ команду
                result.TotalCommandsFound = commands.Count;
                result.HasMoreCommands = commands.Count > 1;

                McpCommand command = commands[0];
                Debug.WriteLine($"[RpaAiIntegration] Executing command: {command.ToolName}");

                try
                {
                    // Проверяем разрешение, если функция проверки предоставлена
                    bool hasPermission = checkPermission == null || checkPermission(command.ToolName);
                    Debug.WriteLine($"[RpaAiIntegration] Permission check: {hasPermission} (checkPermission={(checkPermission == null ? "NULL" : "PROVIDED")})");

                    if (!hasPermission)
                    {
                        result.Errors.Add(
                            $"❌ Инструмент '{command.ToolName}' требует одобрения. Включите auto-approve или одобрите вручную."
                        );
                        result.Success = false;
                        Debug.WriteLine($"[RpaAiIntegration] Permission denied for: {command.ToolName}");
                    }
                    else
                    {
                        Debug.WriteLine($"[RpaAiIntegration] Calling MCP tool: {command.ToolName}");

                        McpToolResult toolResult = await _mcpServer.CallTool(
                            command.ToolName,
                            command.Arguments
                        );

                        Debug.WriteLine($"[RpaAiIntegration] Tool result: Success={toolResult.Success}");

                        if (toolResult.Success)
                        {
                            string contentString = toolResult.Content != null
                                ? JsonConvert.SerializeObject(toolResult.Content, Formatting.Indented)
                                : "Success";

                            result.ExecutedCommands.Add(
                                $"{command.ToolName}: {contentString}"
                            );
                            result.Success = true;
                            Debug.WriteLine($"[RpaAiIntegration] Command executed successfully");
                        }
                        else
                        {
                            // Format validation errors with detailed information
                            string errorMessage = toolResult.ValidationError != null
                                ? FormatValidationError(toolResult.ValidationError, command.ToolName)
                                : $"{command.ToolName} failed: {toolResult.Error}";

                            result.Errors.Add(errorMessage);
                            result.Success = false;
                            Debug.WriteLine($"[RpaAiIntegration] Command failed: {errorMessage}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        $"Error executing {command.ToolName}: {ex.Message}"
                    );
                    result.Success = false;
                    Debug.WriteLine($"[RpaAiIntegration] Exception executing command: {ex.Message}");
                    Debug.WriteLine($"[RpaAiIntegration] Stack trace: {ex.StackTrace}");
                }

                // Формируем итоговое сообщение
                result.Message = result.Success
                    ? result.HasMoreCommands
                        ? $"✅ Выполнена команда {command.ToolName} (найдено еще {commands.Count - 1} команд(ы), которые будут выполнены после следующего ответа)"
                        : $"✅ Успешно выполнена команда {command.ToolName}"
                    : $"❌ Ошибка выполнения команды {command.ToolName}";

                Debug.WriteLine($"[RpaAiIntegration] Final result: {result.Message}");
                Debug.WriteLine($"[RpaAiIntegration] === ProcessAiResponseAsync END ===");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Failed to process AI response: {ex.Message}";
                result.Errors.Add(ex.ToString());
                Debug.WriteLine($"[RpaAiIntegration-ERROR] Top-level exception: {ex.Message}");
                Debug.WriteLine($"[RpaAiIntegration-ERROR] Stack trace: {ex.StackTrace}");
                return result;
            }
        }

        /// <summary>
        /// Генерирует контекст для AI о доступных инструментах
        /// </summary>
        public async Task<string> GenerateAiContextAsync()
        {
            try
            {
                IEnumerable<McpTool> tools = await _mcpServer.ListTools();
                StringBuilder sb = new StringBuilder();

                _ = sb.AppendLine("## Доступные MCP инструменты");
                _ = sb.AppendLine();
                _ = sb.AppendLine("Вы можете управлять RPA workflow designer, используя следующие инструменты.");
                _ = sb.AppendLine();
                _ = sb.AppendLine("**КРИТИЧЕСКИ ВАЖНО: Структура add_activity**");
                _ = sb.AppendLine();
                _ = sb.AppendLine("При добавлении активностей свойства ОБЯЗАТЕЛЬНО передаются через объект 'properties':");
                _ = sb.AppendLine("```json");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("  \"activityType\": \"Delay\",");
                _ = sb.AppendLine("  \"properties\": {");
                _ = sb.AppendLine("    \"DisplayName\": \"Задержка 7 секунд\",");
                _ = sb.AppendLine("    \"Duration\": \"00:00:07\"");
                _ = sb.AppendLine("  }");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("```");
                _ = sb.AppendLine();
                _ = sb.AppendLine("**НЕПРАВИЛЬНО (НЕ ДЕЛАЙТЕ ТАК):**");
                _ = sb.AppendLine("```json");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("  \"activityType\": \"Delay\",");
                _ = sb.AppendLine("  \"displayName\": \"Задержка\",  // ❌ НЕ ТАК!");
                _ = sb.AppendLine("  \"duration\": \"00:00:07\"      // ❌ НЕ ТАК!");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("```");
                _ = sb.AppendLine();
                _ = sb.AppendLine("**ВАЖНО: Используйте ТОЛЬКО JSON-RPC 2.0 формат!**");
                _ = sb.AppendLine();
                _ = sb.AppendLine("```json");
                _ = sb.AppendLine("{");
                _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                _ = sb.AppendLine("  \"method\": \"tools/call\",");
                _ = sb.AppendLine("  \"params\": {");
                _ = sb.AppendLine("    \"name\": \"имя_инструмента\",");
                _ = sb.AppendLine("    \"arguments\": {");
                _ = sb.AppendLine("      \"параметр1\": \"значение1\"");
                _ = sb.AppendLine("    }");
                _ = sb.AppendLine("  },");
                _ = sb.AppendLine("  \"id\": 1");
                _ = sb.AppendLine("}");
                _ = sb.AppendLine("```");
                _ = sb.AppendLine();

                int toolIndex = 0;
                foreach (McpTool tool in tools)
                {
                    toolIndex++;
                    _ = sb.AppendLine($"### {toolIndex}. {tool.Name}");
                    _ = sb.AppendLine($"**Описание:** {tool.Description}");
                    _ = sb.AppendLine();

                    // Добавляем примеры использования для основных инструментов
                    _ = sb.AppendLine("**Пример использования:**");
                    _ = sb.AppendLine("```json");

                    switch (tool.Name)
                    {
                        case "add_activity":
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine("    \"name\": \"add_activity\",");
                            _ = sb.AppendLine("    \"arguments\": {");
                            _ = sb.AppendLine("      \"activityType\": \"Sequence\",");
                            _ = sb.AppendLine("      \"properties\": {");
                            _ = sb.AppendLine("        \"DisplayName\": \"Моя последовательность\"");
                            _ = sb.AppendLine("      }");
                            _ = sb.AppendLine("    }");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;

                        case "list_activities":
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine("    \"name\": \"list_activities\",");
                            _ = sb.AppendLine("    \"arguments\": {}");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;

                        //case "get_workflow_info":
                        //    _ = sb.AppendLine("{");
                        //    _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                        //    _ = sb.AppendLine("  \"method\": \"tools/call\",");
                        //    _ = sb.AppendLine("  \"params\": {");
                        //    _ = sb.AppendLine("    \"name\": \"get_workflow_info\",");
                        //    _ = sb.AppendLine("    \"arguments\": {");
                        //    _ = sb.AppendLine("      \"includeActivities\": true");
                        //    _ = sb.AppendLine("    }");
                        //    _ = sb.AppendLine("  },");
                        //    _ = sb.AppendLine($"  \"id\": {toolIndex}");
                        //    _ = sb.AppendLine("}");
                        //    break;

                        case "list_files":
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine("    \"name\": \"list_files\",");
                            _ = sb.AppendLine("    \"arguments\": {");
                            _ = sb.AppendLine("      \"path\": \".\",");
                            _ = sb.AppendLine("      \"recursive\": true");
                            _ = sb.AppendLine("    }");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;

                        case "read_file":
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine("    \"name\": \"read_file\",");
                            _ = sb.AppendLine("    \"arguments\": {");
                            _ = sb.AppendLine("      \"path\": \"example.xaml\"");
                            _ = sb.AppendLine("    }");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;

                        case "search_files":
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine("    \"name\": \"search_files\",");
                            _ = sb.AppendLine("    \"arguments\": {");
                            _ = sb.AppendLine("      \"path\": \".\",");
                            _ = sb.AppendLine("      \"pattern\": \"TODO\",");
                            _ = sb.AppendLine("      \"filePattern\": \"*.cs\"");
                            _ = sb.AppendLine("    }");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;

                        case "get_file_info":
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine("    \"name\": \"get_file_info\",");
                            _ = sb.AppendLine("    \"arguments\": {");
                            _ = sb.AppendLine("      \"path\": \"Main.xaml\"");
                            _ = sb.AppendLine("    }");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;

                        case "get_open_tabs":
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine("    \"name\": \"get_open_tabs\",");
                            _ = sb.AppendLine("    \"arguments\": {}");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;

                        default:
                            _ = sb.AppendLine("{");
                            _ = sb.AppendLine("  \"jsonrpc\": \"2.0\",");
                            _ = sb.AppendLine("  \"method\": \"tools/call\",");
                            _ = sb.AppendLine("  \"params\": {");
                            _ = sb.AppendLine($"    \"name\": \"{tool.Name}\",");
                            _ = sb.AppendLine("    \"arguments\": {}");
                            _ = sb.AppendLine("  },");
                            _ = sb.AppendLine($"  \"id\": {toolIndex}");
                            _ = sb.AppendLine("}");
                            break;
                    }

                    _ = sb.AppendLine("```");
                    _ = sb.AppendLine();
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Failed to generate AI context: {ex.Message}";
            }
        }

        /// <summary>
        /// Форматирует ошибку валидации в подробное сообщение для AI-агента
        /// </summary>
        private string FormatValidationError(ValidationError validationError, string toolName)
        {
            StringBuilder sb = new StringBuilder();

            _ = sb.AppendLine($"❌ **Ошибка валидации инструмента '{toolName}'**");
            _ = sb.AppendLine();

            // Тип ошибки с описанием на русском
            _ = sb.AppendLine($"**Тип ошибки:** {GetErrorTypeDescription(validationError.Type)}");
            _ = sb.AppendLine($"**Сообщение:** {validationError.Message}");

            if (!string.IsNullOrEmpty(validationError.ParameterName))
            {
                _ = sb.AppendLine($"**Параметр:** `{validationError.ParameterName}`");
            }

            if (!string.IsNullOrEmpty(validationError.ExpectedType))
            {
                _ = sb.AppendLine($"**Ожидаемый тип:** `{validationError.ExpectedType}`");
            }

            if (!string.IsNullOrEmpty(validationError.ActualType))
            {
                _ = sb.AppendLine($"**Фактический тип:** `{validationError.ActualType}`");
            }

            _ = sb.AppendLine();
            _ = sb.AppendLine("---");
            _ = sb.AppendLine();
            _ = sb.AppendLine("**Правильный формат команды:**");
            _ = sb.AppendLine();

            _ = !string.IsNullOrEmpty(validationError.Example)
                ? sb.AppendLine(validationError.Example)
                : sb.AppendLine("_(Пример недоступен)_");

            return sb.ToString();
        }

        /// <summary>
        /// Получает описание типа ошибки на русском языке
        /// </summary>
        private string GetErrorTypeDescription(ValidationErrorType type)
        {
            return type switch
            {
                ValidationErrorType.MissingRequiredParameter => "Отсутствует обязательный параметр",
                ValidationErrorType.InvalidParameterType => "Неверный тип параметра",
                ValidationErrorType.InvalidParameterValue => "Недопустимое значение параметра",
                ValidationErrorType.InvalidParameterFormat => "Неверный формат параметра",
                ValidationErrorType.UnknownParameter => "Неизвестный параметр",
                _ => "Ошибка валидации"
            };
        }
    }
}
