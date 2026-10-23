# Руководство по использованию системы валидации MCP инструментов

## Обзор

Система валидации MCP инструментов автоматически проверяет параметры команд от AI-агента и предоставляет детальную обратную связь с примерами правильного использования.

## Реализованные компоненты

### 1. Модели данных (`Mcp/McpModels.cs`)

- **`ValidationError`** - детальная информация об ошибке валидации
- **`ValidationErrorType`** - типы ошибок (отсутствует параметр, неверный тип и т.д.)
- **`ValidationResult`** - результат валидации параметров

### 2. Валидатор (`Mcp/Tools/ParameterValidator.cs`)

Проверяет:
- ✅ Наличие обязательных параметров
- ✅ Типы параметров (string, integer, boolean, object, array)
- ✅ Значения enum параметров
- ✅ Неизвестные параметры

### 3. Генератор примеров (`Mcp/Tools/JsonExampleGenerator.cs`)

Автоматически создает правильные примеры JSON-RPC команд из схемы инструмента.

### 4. Базовый класс инструмента (`Mcp/Tools/BaseMcpTool.cs`)

Добавлены методы:
- `ValidateArguments()` - валидация параметров
- `ValidationError()` - создание ошибки валидации
- `GenerateExample()` - генерация примера
- `ValidateAndExecute()` - валидация + выполнение

### 5. Форматирование ошибок (`AiAgent/Services/RpaAiIntegrationService.cs`)

Красиво форматирует ошибки валидации для отображения в чате AI-агента.

## Как использовать в инструменте

### Вариант 1: Автоматическая валидация в начале Execute()

```csharp
public override async Task<McpToolResult> Execute(object arguments)
{
    return await Task.Run(() =>
    {
        // Валидация параметров
        ValidationResult validation = ValidateArguments(arguments);
        if (!validation.IsValid)
        {
            return ValidationError(validation);
        }

        // Ваш код выполнения инструмента
        // ...
    });
}
```

### Вариант 2: Использование ValidateAndExecute()

```csharp
public override async Task<McpToolResult> Execute(object arguments)
{
    return await Task.Run(() => ValidateAndExecute(arguments, args =>
    {
        // Ваш код выполнения
        // Параметры уже валидированы
        JObject jArgs = JObject.FromObject(args);
        // ...
        return Success(result);
    }));
}
```

### Вариант 3: Async ValidateAndExecuteAsync()

```csharp
public override async Task<McpToolResult> Execute(object arguments)
{
    return await ValidateAndExecuteAsync(arguments, async args =>
    {
        // Async код выполнения
        await Task.Delay(100);
        return Success(result);
    });
}
```

## Примеры ошибок

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
  }
}
```

**Ответ системы:**
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
  "activityType": "Sequence",
  "position": "after"  // ❌ string вместо integer
}
```

**Ответ системы:**
```
❌ **Ошибка валидации инструмента 'add_activity'**

**Тип ошибки:** Неверный тип параметра
**Сообщение:** Parameter 'position' has invalid type. Expected 'integer', got 'string'
**Параметр:** `position`
**Ожидаемый тип:** `integer`
**Фактический тип:** `string`

---

**Правильный формат команды:**

[правильный пример с integer]
```

### Пример 3: Недопустимое значение enum

**Запрос AI:**
```json
{
  "activityType": "CustomSequence"  // ❌ нет в списке допустимых
}
```

**Ответ системы:**
```
❌ **Ошибка валидации инструмента 'add_activity'**

**Тип ошибки:** Недопустимое значение параметра
**Сообщение:** Parameter 'activityType' has invalid value 'CustomSequence'. Allowed values: Sequence, If, While, ...
**Параметр:** `activityType`
**Ожидаемый тип:** `enum: [Sequence, If, While, ...]`
**Фактический тип:** `CustomSequence`

---

**Правильный формат команды:**

[правильный пример с валидным enum]
```

## Как добавить валидацию в существующий инструмент

### Шаг 1: Обновите метод Execute()

**Было:**
```csharp
public override async Task<McpToolResult> Execute(object arguments)
{
    try
    {
        JObject args = JObject.FromObject(arguments);
        string param = args["param"]?.ToString();
        
        if (string.IsNullOrEmpty(param))
        {
            return Error("param is required");
        }
        
        // execute logic
    }
    catch (Exception ex)
    {
        return Error(ex.Message);
    }
}
```

**Стало:**
```csharp
public override async Task<McpToolResult> Execute(object arguments)
{
    return await Task.Run(() =>
    {
        // Автоматическая валидация
        ValidationResult validation = ValidateArguments(arguments);
        if (!validation.IsValid)
        {
            return ValidationError(validation);
        }

        try
        {
            // Параметры уже провалидированы
            JObject args = JObject.FromObject(arguments);
            string param = args["param"].ToString();
            
            // execute logic
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    });
}
```

### Шаг 2: Убедитесь, что схема правильная

Проверьте, что в `GetSchema()` корректно указаны:
- Все параметры в `properties`
- Обязательные параметры в `required`
- Типы параметров (`type`)
- Enum значения (если есть)

```csharp
public override McpTool GetSchema()
{
    return new McpTool
    {
        Name = "my_tool",
        Description = "Tool description",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                requiredParam = new
                {
                    type = "string",
                    description = "This parameter is required"
                },
                optionalParam = new
                {
                    type = "integer",
                    description = "This parameter is optional"
                }
            },
            required = new[] { "requiredParam" }
        }
    };
}
```

## Тестирование валидации

### Тест 1: Отсутствует обязательный параметр
```json
{
  "optionalParam": 5
  // requiredParam отсутствует
}
```
Ожидается: ValidationError с типом `MissingRequiredParameter`

### Тест 2: Неверный тип
```json
{
  "requiredParam": "value",
  "optionalParam": "not a number"  // должен быть integer
}
```
Ожидается: ValidationError с типом `InvalidParameterType`

### Тест 3: Неизвестный параметр
```json
{
  "requiredParam": "value",
  "unknownParam": "test"  // параметр не в схеме
}
```
Ожидается: ValidationError с типом `UnknownParameter`

### Тест 4: Все параметры корректны
```json
{
  "requiredParam": "value",
  "optionalParam": 5
}
```
Ожидается: Успешная валидация, выполнение инструмента

## Преимущества системы

### Для AI-агента:
✅ Получает точную информацию о том, что не так
✅ Видит правильный формат команды
✅ Может самостоятельно исправить ошибки

### Для разработчиков:
✅ Централизованная валидация
✅ Автоматическая генерация примеров
✅ Минимум кода в каждом инструменте

### Для пользователей:
✅ Меньше ошибок от AI
✅ Быстрое исправление проблем
✅ Понятные сообщения об ошибках

## Следующие шаги

1. ✅ Протестировать систему на `add_activity`
2. ⏳ Добавить валидацию в другие критичные инструменты
3. ⏳ Собрать статистику исправления ошибок AI
4. ⏳ Расширить типы валидации (regex, min/max и т.д.)

## Заключение

Система валидации значительно улучшает взаимодействие AI-агента с MCP инструментами, обеспечивая:
- Автоматическую проверку параметров
- Детальную обратную связь
- Примеры правильного использования
- Самокоррекцию ошибок AI

Все это приводит к более стабильной и предсказуемой работе автоматизации.