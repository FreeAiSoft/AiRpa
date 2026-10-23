# MCP Command Format Guide / Руководство по формату команд MCP

## Проблема / Problem

Команды вида:
```json
{
  "action": "list_files",
  "path": "."
}
```

**НЕ РАБОТАЮТ** потому что MCP сервер использует стандарт JSON-RPC 2.0.

## Правильный формат / Correct Format

MCP сервер ожидает команды в формате JSON-RPC 2.0:

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

## Доступные методы / Available Methods

### 1. Initialize (Инициализация)
```json
{
  "jsonrpc": "2.0",
  "method": "initialize",
  "params": {},
  "id": 1
}
```

**Ответ:**
```json
{
  "jsonrpc": "2.0",
  "result": {
    "name": "rpa-studio",
    "version": "1.0.0",
    "description": "MCP Server for RPA Studio - provides tools for workflow automation and AI integration"
  },
  "id": 1
}
```

### 2. List Tools (Список инструментов)
```json
{
  "jsonrpc": "2.0",
  "method": "tools/list",
  "params": {},
  "id": 2
}
```

**Ответ содержит список всех доступных инструментов с их описаниями и схемами.**

### 3. Call Tool (Вызов инструмента)
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "<tool_name>",
    "arguments": {
      // аргументы инструмента
    }
  },
  "id": 3
}
```

## Примеры команд для файловых инструментов / File Tool Examples

### list_files - Список файлов

**Базовый пример:**
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

**С рекурсией:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "list_files",
    "arguments": {
      "path": ".",
      "recursive": true,
      "includeHidden": false
    }
  },
  "id": 1
}
```

### read_file - Чтение файла

**Полное чтение:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_file",
    "arguments": {
      "path": "Main.xaml"
    }
  },
  "id": 2
}
```

**Чтение диапазона строк:**
```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "read_file",
    "arguments": {
      "path": "Main.xaml",
      "startLine": 1,
      "endLine": 50
    }
  },
  "id": 2
}
```

### search_files - Поиск в файлах

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "search_files",
    "arguments": {
      "path": ".",
      "pattern": "TODO",
      "filePattern": "*.cs",
      "maxResults": 100
    }
  },
  "id": 3
}
```

### get_file_info - Информация о файле

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "get_file_info",
    "arguments": {
      "path": "Main.xaml"
    }
  },
  "id": 4
}
```

## Использование McpCommandHelper / Using McpCommandHelper

Для упрощения создания команд используйте класс [`McpCommandHelper`](../Mcp/McpCommandHelper.cs):

```csharp
using RPA.Mcp;

// Initialize
string initCommand = McpCommandHelper.CreateInitializeRequest();

// List tools
string listToolsCommand = McpCommandHelper.CreateListToolsRequest();

// List files
string listFilesCommand = McpCommandHelper.CreateListFilesRequest(".", recursive: true);

// Read file
string readFileCommand = McpCommandHelper.CreateReadFileRequest("Main.xaml");

// Search files
string searchCommand = McpCommandHelper.CreateSearchFilesRequest(".", "TODO", "*.cs");

// Get file info
string fileInfoCommand = McpCommandHelper.CreateGetFileInfoRequest("Main.xaml");

// Custom tool call
string customCommand = McpCommandHelper.CreateToolCallRequest("my_tool", new { param1 = "value1" });

// Convert simple format to JSON-RPC (для обратной совместимости)
string simpleCommand = @"{""action"": ""list_files"", ""path"": "".""}";
string jsonRpcCommand = McpCommandHelper.ConvertSimpleCommandToJsonRpc(simpleCommand);
```

## Формат ответов / Response Format

### Успешный ответ / Success Response
```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "content": {
      // результаты выполнения
    }
  },
  "id": 1
}
```

### Ответ с ошибкой / Error Response
```json
{
  "jsonrpc": "2.0",
  "error": {
    "code": -32601,
    "message": "Method not found",
    "data": "Additional error information"
  },
  "id": 1
}
```

## Коды ошибок JSON-RPC / JSON-RPC Error Codes

| Код / Code | Значение / Meaning |
|------------|-------------------|
| -32700 | Parse error - Невалидный JSON |
| -32600 | Invalid request - Запрос не соответствует спецификации |
| -32601 | Method not found - Метод не найден |
| -32602 | Invalid params - Неверные параметры |
| -32603 | Internal error - Внутренняя ошибка сервера |

## Примеры использования через HTTP / HTTP Usage Examples

```bash
# Используя curl
curl -X POST http://localhost:3000/ \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "list_files",
      "arguments": {"path": "."}
    },
    "id": 1
  }'
```

```javascript
// Используя JavaScript fetch
const response = await fetch('http://localhost:3000/', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    jsonrpc: '2.0',
    method: 'tools/call',
    params: {
      name: 'list_files',
      arguments: { path: '.' }
    },
    id: 1
  })
});

const result = await response.json();
console.log(result);
```

## Связанные файлы / Related Files

- [`RpaMcpServer.cs`](../Mcp/RpaMcpServer.cs) - Основная реализация сервера
- [`McpCommandHelper.cs`](../Mcp/McpCommandHelper.cs) - Вспомогательный класс для создания команд
- [`McpModels.cs`](../Mcp/McpModels.cs) - Модели данных MCP
- [`HttpMcpServerHost.cs`](../Mcp/HttpMcpServerHost.cs) - HTTP хост для сервера
- [`StdioMcpServerHost.cs`](../Mcp/StdioMcpServerHost.cs) - Stdio хост для интеграции с Claude Desktop

## Дополнительная информация / Additional Information

Для получения дополнительной информации см.:
- [MCP Architecture](MCP_Architecture.md)
- [AI MCP Integration Guide](AI_MCP_Integration_Guide.md)
- [MCP README](../Mcp/README.md)