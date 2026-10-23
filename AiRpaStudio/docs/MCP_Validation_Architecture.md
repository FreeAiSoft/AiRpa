# MCP Parameter Validation Architecture

## Цель
Создать систему валидации параметров MCP инструментов, которая предоставляет AI-агенту подробную обратную связь при ошибках, включая корректную схему параметров.

## Текущая ситуация

### Проблемы
1. **Недостаточная обратная связь**: При ошибках AI получает только текстовое сообщение без примера правильного формата
2. **Нет валидации типов**: Инструменты не проверяют типы параметров до выполнения
3. **Нет централизованной валидации**: Каждый инструмент валидирует параметры по-своему
4. **AI не видит схему**: При ошибке AI не знает, какая структура параметров правильная

### Существующий код
```csharp
// McpToolResult - базовая структура результата
public class McpToolResult
{
    public bool Success { get; set; }
    public object Content { get; set; }
    public string Error { get; set; }
}

// BaseMcpTool - базовый класс инструментов
protected McpToolResult Error(string error)
{
    return new McpToolResult
    {
        Success = false,
        Content = null,
        Error = error  // Только текст ошибки
    };
}
```

## Архитектура решения

### 1. Расширение модели McpToolResult

```csharp
public class McpToolResult
{
    public bool Success { get; set; }
    public object Content { get; set; }
    public string Error { get; set; }
    
    // НОВОЕ: Детальная информация об ошибке валидации
    public ValidationError ValidationError { get; set; }
}

public class ValidationError
{
    /// <summary>
    /// Тип ошибки валидации
    /// </summary>
    public ValidationErrorType Type { get; set; }
    
    /// <summary>
    /// Человекочитаемое сообщение об ошибке
    /// </summary>
    public string Message { get; set; }
    
    /// <summary>
    /// Название параметра с ошибкой
    /// </summary>
    public string ParameterName { get; set; }
    
    /// <summary>
    /// Ожидаемый тип параметра
    /// </summary>
    public string ExpectedType { get; set; }
    
    /// <summary>
    /// Фактически полученный тип
    /// </summary>
    public string ActualType { get; set; }
    
    /// <summary>
    /// Полная схема инструмента для справки
    /// </summary>
    public object ToolSchema { get; set; }
    
    /// <summary>
    /// Пример правильного использования
    /// </summary>
    public string Example { get; set; }
}

public enum ValidationErrorType
{
    MissingRequiredParameter,
    InvalidParameterType,
    InvalidParameterValue,
    InvalidParameterFormat,
    UnknownParameter
}
```

### 2. Валидационные методы в BaseMcpTool

```csharp
public abstract class BaseMcpTool : IMcpTool
{
    // Существующие методы...
    
    /// <summary>
    /// Валидирует параметры согласно схеме инструмента
    /// </summary>
    protected ValidationResult ValidateArguments(object arguments)
    {
        var schema = GetSchema();
        var validator = new ParameterValidator(schema);
        return validator.Validate(arguments);
    }
    
    /// <summary>
    /// Создает результат с ошибкой валидации
    /// </summary>
    protected McpToolResult ValidationError(
        ValidationErrorType type,
        string message,
        string parameterName = null,
        string expectedType = null,
        string actualType = null)
    {
        var schema = GetSchema();
        var example = GenerateExample();
        
        return new McpToolResult
        {
            Success = false,
            Error = message,
            ValidationError = new ValidationError
            {
                Type = type,
                Message = message,
                ParameterName = parameterName,
                ExpectedType = expectedType,
                ActualType = actualType,
                ToolSchema = schema.InputSchema,
                Example = example
            }
        };
    }
    
    /// <summary>
    /// Генерирует пример правильного использования инструмента
    /// </summary>
    protected virtual string GenerateExample()
    {
        var schema = GetSchema();
        return JsonExampleGenerator.GenerateFromSchema(schema);
    }
    
    // Универсальный валидатор для всех инструментов
    protected McpToolResult ValidateAndExecute(
        object arguments,
        Func<object, McpToolResult> executeFunc)
    {
        var validationResult = ValidateArguments(arguments);
        
        if (!validationResult.IsValid)
        {
            return ValidationError(
                validationResult.ErrorType,
                validationResult.ErrorMessage,
                validationResult.ParameterName,
                validationResult.ExpectedType,
                validationResult.ActualType
            );
        }
        
        return executeFunc(arguments);
    }
}
```

### 3. Универсальный валидатор параметров

```csharp
public class ParameterValidator
{
    private readonly McpTool _schema;
    
    public ParameterValidator(McpTool schema)
    {
        _schema = schema;
    }
    
    public ValidationResult Validate(object arguments)
    {
        if (arguments == null)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorType = ValidationErrorType.MissingRequiredParameter,
                ErrorMessage = "Arguments object is null"
            };
        }
        
        var jObject = JObject.FromObject(arguments);
        var schemaObj = JObject.FromObject(_schema.InputSchema);
        
        // 1. Проверка обязательных параметров
        var required = schemaObj["required"]?.ToObject<string[]>() ?? new string[0];
        foreach (var requiredParam in required)
        {
            if (jObject[requiredParam] == null)
            {
                var properties = schemaObj["properties"] as JObject;
                var paramSchema = properties?[requiredParam];
                var paramType = paramSchema?["type"]?.ToString() ?? "unknown";
                
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorType = ValidationErrorType.MissingRequiredParameter,
                    ErrorMessage = $"Required parameter '{requiredParam}' is missing",
                    ParameterName = requiredParam,
                    ExpectedType = paramType
                };
            }
        }
        
        // 2. Проверка типов параметров
        var properties = schemaObj["properties"] as JObject;
        if (properties != null)
        {
            foreach (var prop in jObject.Properties())
            {
                var paramSchema = properties[prop.Name];
                if (paramSchema == null)
                {
                    // Неизвестный параметр
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorType = ValidationErrorType.UnknownParameter,
                        ErrorMessage = $"Unknown parameter '{prop.Name}'",
                        ParameterName = prop.Name
                    };
                }
                
                var expectedType = paramSchema["type"]?.ToString();
                var actualType = GetJTokenType(prop.Value);
                
                if (!IsTypeCompatible(expectedType, actualType))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorType = ValidationErrorType.InvalidParameterType,
                        ErrorMessage = $"Parameter '{prop.Name}' has invalid type. Expected '{expectedType}', got '{actualType}'",
                        ParameterName = prop.Name,
                        ExpectedType = expectedType,
                        ActualType = actualType
                    };
                }
                
                // 3. Проверка enum значений
                var enumValues = paramSchema["enum"]?.ToObject<string[]>();
                if (enumValues != null && !enumValues.Contains(prop.Value.ToString()))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorType = ValidationErrorType.InvalidParameterValue,
                        ErrorMessage = $"Parameter '{prop.Name}' has invalid value. Allowed values: {string.Join(", ", enumValues)}",
                        ParameterName = prop.Name,
                        ExpectedType = $"enum: [{string.Join(", ", enumValues)}]",
                        ActualType = prop.Value.ToString()
                    };
                }
            }
        }
        
        return new ValidationResult { IsValid = true };
    }
    
    private string GetJTokenType(JToken token)
    {
        return token.Type switch
        {
            JTokenType.String => "string",
            JTokenType.Integer => "integer",
            JTokenType.Float => "number",
            JTokenType.Boolean => "boolean",
            JTokenType.Object => "object",
            JTokenType.Array => "array",
            JTokenType.Null => "null",
            _ => "unknown"
        };
    }
    
    private bool IsTypeCompatible(string expectedType, string actualType)
    {
        if (expectedType == actualType) return true;
        
        // number может быть integer или float
        if (expectedType == "number" && (actualType == "integer" || actualType == "float"))
            return true;
            
        return false;
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public ValidationErrorType ErrorType { get; set; }
    public string ErrorMessage { get; set; }
    public string ParameterName { get; set; }
    public string ExpectedType { get; set; }
    public string ActualType { get; set; }
}
```

### 4. Генератор примеров из схемы

```csharp
public static class JsonExampleGenerator
{
    public static string GenerateFromSchema(McpTool schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"jsonrpc\": \"2.0\",");
        sb.AppendLine("  \"method\": \"tools/call\",");
        sb.AppendLine("  \"params\": {");
        sb.AppendLine($"    \"name\": \"{schema.Name}\",");
        sb.AppendLine("    \"arguments\": {");
        
        var schemaObj = JObject.FromObject(schema.InputSchema);
        var properties = schemaObj["properties"] as JObject;
        var required = schemaObj["required"]?.ToObject<string[]>() ?? new string[0];
        
        if (properties != null)
        {
            var propList = properties.Properties().ToList();
            for (int i = 0; i < propList.Count; i++)
            {
                var prop = propList[i];
                var propSchema = prop.Value as JObject;
                var propType = propSchema?["type"]?.ToString() ?? "string";
                var description = propSchema?["description"]?.ToString() ?? "";
                var isRequired = required.Contains(prop.Name);
                
                var exampleValue = GenerateExampleValue(propType, propSchema, prop.Name);
                var comma = i < propList.Count - 1 ? "," : "";
                var reqMark = isRequired ? " // REQUIRED" : " // optional";
                
                sb.AppendLine($"      \"{prop.Name}\": {exampleValue}{comma}{reqMark}");
            }
        }
        
        sb.AppendLine("    }");
        sb.AppendLine("  },");
        sb.AppendLine("  \"id\": 1");
        sb.AppendLine("}");
        sb.AppendLine("```");
        
        return sb.ToString();
    }
    
    private static string GenerateExampleValue(string type, JObject schema, string paramName)
    {
        // Проверка enum
        var enumValues = schema?["enum"]?.ToObject<string[]>();
        if (enumValues != null && enumValues.Length > 0)
        {
            return $"\"{enumValues[0]}\"";
        }
        
        return type switch
        {
            "string" => $"\"example_{paramName}\"",
            "integer" => "0",
            "number" => "0.0",
            "boolean" => "true",
            "object" => "{}",
            "array" => "[]",
            _ => "null"
        };
    }
}
```

### 5. Обновление RpaAiIntegrationService

```csharp
public async Task<RpaOperationResult> ProcessAiResponseAsync(
    string aiResponse, 
    Func<string, bool> checkPermission = null)
{
    // ... существующий код ...
    
    foreach (McpCommand command in commands)
    {
        try
        {
            McpToolResult toolResult = await _mcpServer.CallTool(
                command.ToolName,
                command.Arguments
            );
            
            if (toolResult.Success)
            {
                // ... существующий код ...
            }
            else
            {
                // НОВОЕ: Форматирование ошибки валидации
                string errorMessage = toolResult.ValidationError != null
                    ? FormatValidationError(toolResult.ValidationError, command.ToolName)
                    : $"{command.ToolName} failed: {toolResult.Error}";
                
                result.Errors.Add(errorMessage);
                result.Success = false;
            }
        }
        catch (Exception ex)
        {
            // ... существующий код ...
        }
    }
    
    return result;
}

private string FormatValidationError(ValidationError validationError, string toolName)
{
    var sb = new StringBuilder();
    
    sb.AppendLine($"❌ **Ошибка валидации инструмента '{toolName}'**");
    sb.AppendLine();
    
    // Тип ошибки и сообщение
    sb.AppendLine($"**Тип ошибки:** {GetErrorTypeDescription(validationError.Type)}");
    sb.AppendLine($"**Сообщение:** {validationError.Message}");
    
    if (!string.IsNullOrEmpty(validationError.ParameterName))
    {
        sb.AppendLine($"**Параметр:** `{validationError.ParameterName}`");
    }
    
    if (!string.IsNullOrEmpty(validationError.ExpectedType))
    {
        sb.AppendLine($"**Ожидаемый тип:** `{validationError.ExpectedType}`");
    }
    
    if (!string.IsNullOrEmpty(validationError.ActualType))
    {
        sb.AppendLine($"**Фактический тип:** `{validationError.ActualType}`");
    }
    
    sb.AppendLine();
    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine("**Правильный формат команды:**");
    sb.AppendLine();
    
    if (!string.IsNullOrEmpty(validationError.Example))
    {
        sb.AppendLine(validationError.Example);
    }
    
    return sb.ToString();
}

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
```

## Примеры сообщений об ошибках

### Пример 1: Отсутствует обязательный параметр

**Запрос AI:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "add_activity",
    "arguments": {
      "properties": {
        "DisplayName": "My Sequence"
      }
    }
  },
  "id": 1
}
```

**Ответ системы в чат:**
```
❌ **Ошибка валидации инструмента 'add_activity'**

**Тип ошибки:** Отсутствует обязательный параметр
**Сообщение:** Required parameter 'activityType' is missing
**Параметр:** `activityType`
**Ожидаемый тип:** `string`

---

**Правильный формат команды:**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "add_activity",
    "arguments": {
      "activityType": "Sequence", // REQUIRED
      "parentActivityId": "example_parentActivityId", // optional
      "position": 0, // optional
      "properties": {}, // optional
      "arguments": {}, // optional
      "children": [] // optional
    }
  },
  "id": 1
}
```
```

### Пример 2: Неверный тип параметра

**Запрос AI:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "add_activity",
    "arguments": {
      "activityType": "Sequence",
      "position": "after"
    }
  },
  "id": 1
}
```

**Ответ системы в чат:**
```
❌ **Ошибка валидации инструмента 'add_activity'**

**Тип ошибки:** Неверный тип параметра
**Сообщение:** Parameter 'position' has invalid type. Expected 'integer', got 'string'
**Параметр:** `position`
**Ожидаемый тип:** `integer`
**Фактический тип:** `string`

---

**Правильный формат команды:**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "add_activity",
    "arguments": {
      "activityType": "Sequence", // REQUIRED
      "position": 0, // optional - MUST BE INTEGER, not "after" or "before"
      "properties": {}
    }
  },
  "id": 1
}
```
```

### Пример 3: Недопустимое значение enum

**Запрос AI:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "add_activity",
    "arguments": {
      "activityType": "CustomSequence"
    }
  },
  "id": 1
}
```

**Ответ системы в чат:**
```
❌ **Ошибка валидации инструмента 'add_activity'**

**Тип ошибки:** Недопустимое значение параметра
**Сообщение:** Parameter 'activityType' has invalid value. Allowed values: Sequence, If, While, DoWhile, ForEach, WriteLine, Assign, Delay, InvokeMethod, TryCatch, Flowchart, Parallel, Pick, Switch, FlowDecision, FlowSwitch
**Параметр:** `activityType`
**Ожидаемый тип:** `enum: [Sequence, If, While, DoWhile, ForEach, WriteLine, Assign, Delay, InvokeMethod, TryCatch, Flowchart, Parallel, Pick, Switch, FlowDecision, FlowSwitch]`
**Фактический тип:** `CustomSequence`

---

**Правильный формат команды:**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "add_activity",
    "arguments": {
      "activityType": "Sequence", // REQUIRED - must be one of the allowed values
      "properties": {
        "DisplayName": "My Sequence"
      }
    }
  },
  "id": 1
}
```
```

## План реализации

### Этап 1: Базовые модели (1-2 часа)
1. ✅ Создать `ValidationError` класс в `Mcp/McpModels.cs`
2. ✅ Создать `ValidationErrorType` enum
3. ✅ Добавить `ValidationError` свойство в `McpToolResult`
4. ✅ Создать `ValidationResult` класс

### Этап 2: Валидатор (2-3 часа)
1. ✅ Создать `ParameterValidator` класс в `Mcp/Tools/ParameterValidator.cs`
2. ✅ Реализовать проверку обязательных параметров
3. ✅ Реализовать проверку типов параметров
4. ✅ Реализовать проверку enum значений
5. ✅ Добавить тесты для валидатора

### Этап 3: Генератор примеров (1-2 часа)
1. ✅ Создать `JsonExampleGenerator` класс в `Mcp/Tools/JsonExampleGenerator.cs`
2. ✅ Реализовать генерацию примеров из схемы
3. ✅ Добавить поддержку комментариев для обязательных полей

### Этап 4: Обновление BaseMcpTool (1-2 часа)
1. ✅ Добавить метод `ValidateArguments()`
2. ✅ Добавить метод `ValidationError()`
3. ✅ Добавить метод `GenerateExample()`
4. ✅ Добавить метод `ValidateAndExecute()`

### Этап 5: Обновление инструментов (2-3 часа)
1. ✅ Обновить `AddActivityTool` для использования валидации
2. ✅ Обновить другие критичные инструменты
3. ✅ Тестирование валидации на разных инструментах

### Этап 6: Форматирование в UI (1-2 часа)
1. ✅ Обновить `RpaAiIntegrationService.FormatValidationError()`
2. ✅ Обновить отображение ошибок в чате
3. ✅ Добавить поддержку markdown форматирования

### Этап 7: Тестирование (2-3 часа)
1. ✅ Тестирование с различными типами ошибок
2. ✅ Проверка форматирования в UI
3. ✅ Проверка, что AI понимает и исправляет ошибки
4. ✅ Документирование

## Ожидаемые результаты

1. **Для AI-агента:**
   - Получает детальную информацию об ошибках
   - Видит правильный формат команды
   - Может самостоятельно исправить ошибки

2. **Для разработчиков:**
   - Централизованная валидация параметров
   - Автоматическая генерация примеров
   - Легко расширяемая система

3. **Для пользователей:**
   - Понятные сообщения об ошибках
   - Быстрое исправление проблем
   - Лучшее качество работы AI

## Метрики успеха

- AI-агент самостоятельно исправляет >= 90% ошибок валидации
- Снижение количества повторных ошибок на >= 80%
- Время на исправление ошибки < 30 секунд
- Пользователи понимают причину ошибки в >= 95% случаев