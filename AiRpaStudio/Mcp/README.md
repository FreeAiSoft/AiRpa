# RPA Studio MCP Server

## Обзор

MCP (Model Context Protocol) Server для RPA Studio предоставляет стандартизированный способ взаимодействия AI-агентов с Workflow Designer через набор инструментов (tools).

## Архитектура

```
RPA Studio
├── Mcp/
│   ├── RpaMcpServer.cs           # Основной MCP сервер
│   ├── IMcpServer.cs             # Интерфейс MCP сервера
│   ├── IMcpTool.cs               # Интерфейс для инструментов
│   ├── McpModels.cs              # Модели данных (Request, Response, Tool)
│   ├── McpCommandHelper.cs       # Вспомогательный класс для создания команд
│   ├── StdioMcpServerHost.cs    # Хост для stdio (Claude Desktop)
│   ├── HttpMcpServerHost.cs     # Хост для HTTP (внешние приложения)
│   └── Tools/
│       ├── BaseMcpTool.cs       # Базовый класс для инструментов
│       # Workflow tools
│       ├── AddActivityTool.cs   # Добавление активностей
│       ├── ListActivitiesTool.cs # Список доступных активностей
│       ├── GetWorkflowInfoTool.cs # Информация о workflow
│       ├── RemoveActivityTool.cs # Удаление активностей
│       ├── UpdateActivityTool.cs # Обновление параметров
│       ├── SaveWorkflowTool.cs  # Сохранение workflow
│       # File system tools
│       ├── ListFilesTool.cs     # Список файлов
│       ├── ReadFileTool.cs      # Чтение файлов
│       ├── GetFileInfoTool.cs   # Информация о файле
│       └── SearchFilesTool.cs   # Поиск в файлах
```

## Запуск MCP Server

### Режим stdio (для Claude Desktop)

```bash
RPA.exe --mcp-server --stdio
```

### Режим HTTP (для внешних приложений)

```bash
RPA.exe --mcp-server --port=3000
```

По умолчанию используется порт 3000, если не указан параметр `--port`.

## Конфигурация Claude Desktop

Добавьте в конфигурацию Claude Desktop (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "rpa-studio": {
      "command": "C:/Path/To/RPA.exe",
      "args": ["--mcp-server", "--stdio"]
    }
  }
}
```

## ⚠️ ВАЖНО: Формат команд

MCP сервер использует протокол JSON-RPC 2.0. Команды вида:
```json
{
  "action": "list_files",
  "path": "."
}
```

**НЕ РАБОТАЮТ!**

Используйте правильный формат JSON-RPC 2.0. См. подробное руководство: [MCP Command Format Guide](../docs/MCP_Command_Format_Guide.md)

Краткий пример:
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "list_files",
    "arguments": {
      "path": "."
    }
  },
  "id": 1
}
```

Или используйте класс [`McpCommandHelper`](McpCommandHelper.cs) для упрощения создания команд.

## Доступные инструменты (Tools)

### Workflow Tools

### 1. add_activity

Добавляет новую активность в текущий workflow.

**Параметры:**
- `activityType` (обязательный): Тип активности
- `displayName` (опциональный): Отображаемое имя
- `parameters` (опциональный): Параметры активности
- `children` (опциональный): Дочерние активности

**Пример:**
```json
{
  "activityType": "Sequence",
  "displayName": "Main Process",
  "children": [
    {
      "activityType": "WriteLine",
      "parameters": {
        "text": "Hello, World!"
      }
    }
  ]
}
```

**Поддерживаемые типы активностей:**
- Control Flow: `Sequence`, `If`, `While`, `DoWhile`, `ForEach`, `Switch`, `Parallel`, `Pick`
- Primitives: `WriteLine`, `Assign`, `Delay`, `InvokeMethod`
- Error Handling: `TryCatch`
- Flow: `Flowchart`, `FlowDecision`, `FlowSwitch`

### 2. list_activities

Возвращает список всех доступных типов активностей с описаниями.

**Параметры:**
- `category` (опциональный): Фильтр по категории

**Категории:**
- `ControlFlow` - Управляющие конструкции
- `Primitives` - Базовые операции
- `ErrorHandling` - Обработка ошибок
- `Flow` - Flowchart элементы
- `Collection` - Работа с коллекциями
- `All` - Все категории (по умолчанию)

**Пример ответа:**
```json
{
  "category": "Primitives",
  "count": 4,
  "activities": [
    {
      "type": "WriteLine",
      "displayName": "Write Line",
      "category": "Primitives",
      "description": "Writes text to console output",
      "parameters": [
        {
          "name": "Text",
          "type": "string",
          "required": true,
          "description": "Text to write"
        }
      ]
    }
  ]
}
```

### 3. get_workflow_info

Получает информацию о текущем workflow.

**Параметры:**
- `includeActivities` (опциональный): Включить список активностей (default: false)

**Пример ответа:**
```json
{
  "filePath": "C:/Projects/MyProcess/Main.xaml",
  "isLoaded": true,
  "rootActivityType": "Sequence",
  "rootDisplayName": "Root",
  "hasUnsavedChanges": false,
  "activityCount": 5,
  "activities": [
    {
      "type": "WriteLine",
      "displayName": "Write Line 1",
      "id": "1.1",
      "level": 0
    }
  ]
}
```

### 4. remove_activity

Удаляет активность из workflow.

**Параметры:**
- `activityId` (опциональный): ID активности
- `displayName` (опциональный): Имя активности

*Примечание: Необходимо указать либо `activityId`, либо `displayName`*

**Пример:**
```json
{
  "activityId": "1.2"
}
```

или

```json
{
  "displayName": "Write Line 1"
}
```

### 5. update_activity

Обновляет параметры существующей активности.

**Параметры:**
- `activityId` (опциональный): ID активности
- `displayName` (опциональный): Имя активности
- `parameters` (обязательный): Новые значения параметров

**Пример:**
```json
{
  "displayName": "Write Line 1",
  "parameters": {
    "Text": "Updated text",
    "DisplayName": "Updated Write Line"
  }
}
```

### 6. save_workflow

Сохраняет текущий workflow в файл.

**Параметры:**
- `path` (опциональный): Путь к файлу
- `saveAs` (опциональный): Принудительное "Сохранить как" (default: false)

**Пример:**
```json
{
  "path": "C:/Projects/MyProcess/Main.xaml"
}
```

или для сохранения в текущий файл:

```json
{}
```

### File System Tools

#### 7. list_files

Возвращает список файлов и директорий по указанному пути.

**Параметры:**
- `path` (обязательный): Относительный путь от корня проекта
- `recursive` (опциональный): Рекурсивный поиск (default: false)
- `includeHidden` (опциональный): Включить скрытые файлы (default: false)

**Пример:**
```json
{
  "path": ".",
  "recursive": true,
  "includeHidden": false
}
```

#### 8. read_file

Читает содержимое файла.

**Параметры:**
- `path` (обязательный): Относительный путь к файлу
- `startLine` (опциональный): Начальная строка
- `endLine` (опциональный): Конечная строка

**Пример:**
```json
{
  "path": "Main.xaml",
  "startLine": 1,
  "endLine": 50
}
```

#### 9. search_files

Ищет текст или regex паттерн в файлах.

**Параметры:**
- `path` (обязательный): Путь для поиска
- `pattern` (обязательный): Regex паттерн для поиска
- `filePattern` (опциональный): Фильтр файлов (например, "*.cs")
- `maxResults` (опциональный): Максимум результатов (default: 100)

**Пример:**
```json
{
  "path": ".",
  "pattern": "TODO",
  "filePattern": "*.cs",
  "maxResults": 50
}
```

#### 10. get_file_info

Получает информацию о файле.

**Параметры:**
- `path` (обязательный): Путь к файлу

**Пример:**
```json
{
  "path": "Main.xaml"
}
```

## JSON-RPC 2.0 Protocol

Все запросы и ответы соответствуют спецификации JSON-RPC 2.0.

### Структура запроса

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "add_activity",
    "arguments": {
      "activityType": "WriteLine",
      "parameters": {
        "text": "Hello"
      }
    }
  },
  "id": 1
}
```

### Структура ответа (успех)

```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "content": {
      "message": "Activity 'WriteLine' added to workflow",
      "activityType": "WriteLine",
      "displayName": "Write Line",
      "id": "1.1"
    }
  },
  "id": 1
}
```

### Структура ответа (ошибка)

```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": false,
    "error": "No active workflow designer. Please open or create a workflow first."
  },
  "id": 1
}
```

## Примеры использования

### С Claude Desktop

После настройки конфигурации, просто спросите Claude:

```
Создай workflow с последовательностью, которая содержит WriteLine с текстом "Hello, World!"
```

Claude автоматически вызовет необходимые инструменты MCP сервера.

### С HTTP API

```bash
curl -X POST http://localhost:3000 \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "list_activities",
      "arguments": {
        "category": "Primitives"
      }
    },
    "id": 1
  }'
```

## Обработка ошибок

### Коды ошибок JSON-RPC

- `-32700` Parse error - Невалидный JSON
- `-32600` Invalid Request - Невалидный запрос
- `-32601` Method not found - Метод не найден
- `-32602` Invalid params - Невалидные параметры
- `-32603` Internal error - Внутренняя ошибка

### Ошибки инструментов

Каждый инструмент возвращает объект с полем `success`:
- `success: true` - Операция выполнена успешно
- `success: false` - Произошла ошибка, детали в поле `error`

Типичные ошибки:
- "No active workflow designer" - Не открыт workflow
- "Activity not found" - Активность не найдена
- "Invalid parameters" - Неверные параметры

## Расширение функциональности

### Добавление нового инструмента

1. Создайте класс, наследующий [`BaseMcpTool`](Tools/BaseMcpTool.cs)
2. Реализуйте методы [`GetSchema()`](Tools/BaseMcpTool.cs:15) и [`Execute()`](Tools/BaseMcpTool.cs:20)
3. Зарегистрируйте инструмент в [`RpaMcpServer`](RpaMcpServer.cs:23)

**Пример:**

```csharp
public class CustomTool : BaseMcpTool
{
    public override McpTool GetSchema()
    {
        return new McpTool
        {
            Name = "custom_tool",
            Description = "My custom tool",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    param1 = new
                    {
                        type = "string",
                        description = "Parameter description"
                    }
                },
                required = new[] { "param1" }
            }
        };
    }

    public override async Task<McpToolResult> Execute(object arguments)
    {
        // Реализация логики
        return Success(new { result = "Success" });
    }
}
```

Затем в [`RpaMcpServer.cs`](RpaMcpServer.cs):

```csharp
_tools = new Dictionary<string, IMcpTool>
{
    // ...
    ["custom_tool"] = new CustomTool()
};
```

## Безопасность

### Аутентификация и авторизация

Текущая реализация не включает аутентификацию. Для production использования рекомендуется:

1. Добавить API ключи для HTTP режима
2. Использовать process isolation для stdio режима
3. Реализовать валидацию всех входных данных
4. Добавить audit logging всех операций

### Ограничения

- MCP сервер имеет полный доступ к Workflow Designer
- Рекомендуется запускать только в доверенной среде
- Для удаленного доступа используйте VPN или другие защищенные каналы

## Логирование

Все операции логируются через [`CustomWfDesigner.Log()`](../Helpers/CustomWfDesigner.cs:108).

Уровни логирования:
- `Trace` - Детальная информация
- `Info` - Общая информация
- `Warn` - Предупреждения
- `Error` - Ошибки

## Тестирование

### Unit тесты

```csharp
[TestMethod]
public async Task AddActivity_WriteLine_Success()
{
    var tool = new AddActivityTool(_designer);
    
    var result = await tool.Execute(new
    {
        activityType = "WriteLine",
        parameters = new { text = "Test" }
    });
    
    Assert.IsTrue(result.Success);
}
```

### Integration тесты

```csharp
[TestMethod]
public async Task McpServer_ListTools_ReturnsAllTools()
{
    var server = new RpaMcpServer();
    var tools = await server.ListTools();
    
    Assert.AreEqual(6, tools.Count());
}
```

## Производительность

- Stdio режим: минимальная задержка (~10ms)
- HTTP режим: задержка зависит от сети (~50ms локально)
- Рекомендуется использовать stdio для локальных AI-агентов

## Поддержка AI моделей

### Нативная поддержка MCP
- ✅ Claude Desktop
- ✅ Claude API (через MCP SDK)
- ✅ GPT-4 (через MCP proxy)

### Через адаптер
- ⚠️ Ollama (требуется промежуточный адаптер)
- ⚠️ Другие локальные модели

## Troubleshooting

### MCP сервер не запускается

1. Проверьте права доступа к файлу
2. Убедитесь, что порт не занят (для HTTP режима)
3. Проверьте логи приложения

### Claude не видит инструменты

1. Проверьте конфигурацию `claude_desktop_config.json`
2. Перезапустите Claude Desktop
3. Проверьте, что путь к RPA.exe указан правильно

### Ошибка "No active workflow designer"

1. Откройте или создайте workflow в RPA Studio
2. Убедитесь, что workflow активен в дизайнере

## Версионирование

Текущая версия: **1.0.0**

### История изменений

#### v1.0.0 (2025-01-13)
- Начальная реализация MCP сервера
- 6 базовых инструментов
- Поддержка stdio и HTTP режимов
- Интеграция с RPA Studio

## Лицензия

Данный код является частью RPA Studio проекта.

## Связанная документация

- [MCP Command Format Guide](../docs/MCP_Command_Format_Guide.md) - Подробное руководство по формату команд
- [MCP Architecture](../docs/MCP_Architecture.md) - Архитектура MCP системы
- [AI MCP Integration Guide](../docs/AI_MCP_Integration_Guide.md) - Интеграция с AI

## Поддержка

Для вопросов и предложений обращайтесь к документации проекта.