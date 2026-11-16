> Статус: planning
# План: переключение способов написания WF через MCP (3 галочки)

| Поле | Значение |
|---|---|
| Документ | `dev-docs\Workflow_Writing_Ways_Mcp_Plan.md` |
| Область | AiRpaStudio: чат-агент (`RPA.Chat` + `Agent.Core`) + встроенный MCP (`AiRpaStudio\Mcp`) |
| Цель | Тестировать отдельные способы написания Workflow Foundation, переключая их «на горячую» в запущенном приложении |
| Статус | План (код не меняется до отдельного решения) |

## 1. Итоговое архитектурное решение

Многофункциональный агент остаётся в чате (`RPA.Chat`). Способы написания WF распределяются так:

| Способ написания WF | Где живёт | Управляется галочкой |
|---|---|---|
| **Текст XAML** | Чат (`editor`, ветка записи `.xaml`) | нет отдельной галочки; управляется существующим `AllowEditFiles` + инструментом `editor` |
| **Палитра (дизайнер)** | MCP (`xaml.create_workflow`, `xaml.add_activity`, `xaml.set_argument`) | галочка №1 |
| **JSON AST** | MCP (`xaml.create_workflow_from_json`) | галочка №2 |
| **Code-First** | MCP (`xaml.create_workflow_from_code`) | галочка №3 |

Итого: **3 галочки, все — для MCP-способов**. Текст XAML остаётся нативным путём чата и не попадает под эти галочки.

Чат-агент при этом должен уметь вызывать MCP-инструменты **внутри процесса** (без HTTP/stdio), потому что обычный агент работает в чате, а три способа живут в MCP.

---

## 2. Текущее состояние (факты из кода)

### 2.1. Чат-агент

- Реестр инструментов чата строится в `RPA.Chat\Services\AgentTools\AgentToolFactory.cs` (`CreateRegistry()`).
- Пересборка «на горячую» уже есть: `ChatViewModel.RebuildAgentTools()` пересоздаёт `AgentToolRegistry` и `AgentRunner`. Вызывается из `ApplySettings()` и из `ClosePermissions()`.
- Гейт разрешений — одно измерение: `AgentPermissions` (`ReadFiles | EditFiles | ExecuteCommand | WebFetch | Subagents`). Маска строится в `RPA.Chat\Services\AgentPermissionsPolicy.cs` (`Resolve(SettingsData)`), применяется в `AgentToolRegistry.ForMode(mode, allowed)`.
- Текст XAML уже живёт в чате: инструмент `editor` (в `AgentToolFactory`) для `.xaml` идёт через `WorkflowXamlText.Normalize` + `IWorkflowXamlGate` (`WriteWorkflowXaml`).
- Палитра в чате сейчас дублируется отдельными инструментами: `activity_insert/delete/move/wrap/undo/redo`, `argument_set`, `variable_add/remove`.

### 2.2. MCP

- MCP-реестр: `AiRpaStudio\Mcp\McpToolRegistry.cs` (`CreateDefault`).
- Сервер: `AiRpaStudio\Mcp\RpaMcpServer.cs` (`IMcpServer`: `ListTools`, `CallTool`).
- MCP-сервер создаётся **только** в headless-режиме `--mcp-server` (`AiRpaStudio\App.xaml.cs`, `StartMcpServer`). В обычном режиме студии экземпляра `RpaMcpServer` нет.
- Способы в MCP уже есть три из трёх нужных:
  - палитра: `xaml.create_workflow`, `xaml.add_activity`, `xaml.set_argument`;
  - JSON AST: `xaml.create_workflow_from_json`;
  - Code-First: `xaml.create_workflow_from_code`;
  - read-only поддержка: `xaml.get_workflow`, `xaml.describe_contract`.

### 2.3. Ограничение (страж)

- Тест `AiRpaStudio.Tests\Workflow\WorkflowToolRegistryGuardTests.cs` → `RpaChatAndAgentCore_DoNotReferenceStudioMcp` запрещает исходникам `RPA.Chat` и `Agent.Core` ссылаться на `RPA.Mcp` / `AiRpaStudio\Mcp`.
- Значит: чат не может напрямую типизироваться на `RpaMcpServer`/`McpToolRegistry`. Нужен **общий контракт** в `RPA.Chat`/`Agent.Core`, а реализация — в `AiRpaStudio`.

### 2.4. Точка инъекции хоста в чат

- `RPA.Chat\Services\ChatEnvironment.cs` — статические точки интеграции, которые заполняет студия в `AiRpaStudio\Views\MainView.xaml.cs` (`InitializeControls`).
- Уже есть образцы: `WorkflowEditing`, `WorkflowOutline`, `WorkflowXamlGate`, `ActivityCatalog`. По этому же образцу вводится мост к MCP.

---

## 3. Целевой поток

```
Чат-агент (обычный, многофункциональный)
   │
   ├─ обычные инструменты: read_files / editor(.cs) / run_commands / build / git_commit / spec_* ...
   ├─ Текст XAML: editor (ветка .xaml) — нативно в чате
   │
   └─ MCP-мост (in-process):
         ChatEnvironment.WorkflowMcpBridge
              │  ListWorkflowTools() -> (name, schema, way)
              │  CallAsync(name, jsonArgs)
              ▼
         AiRpaStudio: реализация поверх RpaMcpServer / McpToolRegistry
              ├─ Палитра   (галочка №1)
              ├─ JSON AST  (галочка №2)
              └─ Code-First(галочка №3)
```

Галочка = фильтр по способу. Выключили галочку → инструменты этого способа:
1. не попадают в промпт (`AgentPromptBuilder`);
2. не попадают в native-схему (`ToolSchemaConverter`);
3. блокируются на исполнении (`AgentRunner`);
4. не видны внешнему MCP-клиенту (`RpaMcpServer.ListTools/CallTool`).

---

## 4. Фазы реализации

### Фаза 1. Общая модель способа (без привязки к MCP)

Файл: `Agent.Core\Models\Models.cs`.

Добавить рядом с `AgentPermissions`:

```csharp
[Flags]
public enum WorkflowWriteWay
{
    None = 0,
    Palette = 1,
    JsonAst = 2,
    CodeFirst = 4,
    All = Palette | JsonAst | CodeFirst
}
```

> Текст XAML в enum **не** включаем: он остаётся нативным путём чата и управляется `AllowEditFiles` + `editor`.

### Фаза 2. Контракт MCP-моста в общем слое

Новый файл: `RPA.Chat\Services\Workflow\IWorkflowMcpBridge.cs` (рядом с остальными контрактами `IWorkflowEditingService`, `IWorkflowXamlGate`).

Контракт (не ссылается на `RPA.Mcp`):

```csharp
public sealed class WorkflowMcpToolInfo
{
    public string Name { get; set; }              // "xaml.add_activity"
    public string Description { get; set; }
    public string ParametersSchema { get; set; }  // informal JSON-строка, как у IAgentTool
    public WorkflowWriteWay Way { get; set; }
    public bool Mutates { get; set; }
}

public interface IWorkflowMcpBridge
{
    IReadOnlyList<WorkflowMcpToolInfo> ListTools();
    Task<string> CallAsync(string name, string jsonArguments, CancellationToken cancellationToken);
}
```

Null-object: `NullWorkflowMcpBridge.Instance` (по образцу `NullWorkflowXamlGate`).

В `RPA.Chat\Services\ChatEnvironment.cs` добавить:

```csharp
public static IWorkflowMcpBridge WorkflowMcpBridge { get; set; } = NullWorkflowMcpBridge.Instance;
```

### Фаза 3. Реализация моста в AiRpaStudio

Новый файл: `AiRpaStudio\Mcp\StudioWorkflowMcpBridge.cs` (или `AiRpaStudio\Core\...`).

Реализация `IWorkflowMcpBridge` поверх `RpaMcpServer`/`McpToolRegistry`:
- `ListTools()` → берёт `McpToolRegistry.Schemas()` и маппит каждый MCP-инструмент на `WorkflowMcpToolInfo` (имя → способ по таблице §6).
- `CallAsync(name, jsonArgs)` → `RpaMcpServer.CallTool(name, JObject.Parse(jsonArgs))` и возвращает сериализованный `McpToolResult`.

Подключение хоста в `AiRpaStudio\Views\MainView.xaml.cs` (в `InitializeControls`):

```csharp
Chat.Services.ChatEnvironment.WorkflowMcpBridge = new RPA.Mcp.StudioWorkflowMcpBridge(/* singleton RpaMcpServer */);
```

В обычном режиме студии создаётся **один встроенный экземпляр** `RpaMcpServer` (см. Фазу 4).

### Фаза 4. Встроенный MCP-сервер в обычном режиме студии

Сейчас `RpaMcpServer` создаётся только при `--mcp-server` (`App.xaml.cs`).

Нужно:
- создать singleton `RpaMcpServer` и в обычном режиме студии (без транспорта `stdio/http`);
- не запускать транспорты в обычном режиме — только сам реестр инструментов;
- передать singleton в `StudioWorkflowMcpBridge`.

Варианты размещения:
- ленивое создание в `MainView` / `App` (по образцу `StudioWorkflowEditingService.Default`);
- либо статический синглтон `RpaMcpServer.Default` рядом с существующим `RpaMcpServer`.

Выбор не влияет на контракт чата (чат видит только `IWorkflowMcpBridge`).

### Фаза 5. Импорт MCP-инструментов в реестр чата

Файл: `RPA.Chat\Services\AgentTools\AgentToolFactory.cs`.

В `CreateRegistry()`:
- если `ChatEnvironment.WorkflowMcpBridge` не null-object и отдаёт инструменты — зарегистрировать каждый `WorkflowMcpToolInfo` как `AgentTool`:
  - `Permission` = `EditFiles` для мутирующих, `ReadFiles` для read-only;
  - `Way` = способ из `WorkflowMcpToolInfo`;
  - `handler` = делегат, вызывающий `bridge.CallAsync(name, args.ToString(), ct)`;
  - `ParametersSchema` = схема из моста (конвертировать `McpTool.InputSchema` в informal-строку при построении моста).

Это заменяет текущие дублирующие чат-инструменты палитры (см. Фазу 6).

### Фаза 6. Вынос палитры из чата в MCP (убрать дубли)

Текущие чат-инструменты палитры перестают быть источником правды:
- `activity_insert`, `activity_delete`, `activity_move`, `activity_wrap`, `activity_undo`, `activity_redo`,
- `argument_set`,
- `variable_add`, `variable_remove`.

Решение (рекомендую поэтапно, чтобы не ронять регрессии):
1. Сначала добавить MCP-мост + импорт инструментов (Фаза 5), не удаляя `activity_*`.
2. Убедиться, что `xaml.add_activity`/`xaml.set_argument`/`xaml.create_workflow` покрывают сценарии `activity_*`/`argument_set`/`variable_*`.
3. Пометить `activity_*` как deprecated и удалить, когда страж-тесты и прогон будут зелёными.

> Внимание: `variable_add`/`variable_remove` — это переменные процесса. В MCP отдельного инструмента переменных сейчас нет (переменные задаются через `xaml.create_workflow_from_json`/`xaml.create_workflow_from_code` или добавляются палитрой). Это зафиксировать отдельным пунктом: либо добавить `xaml.add_variable` в MCP, либо оставить переменные в чате как часть «палитры». По умолчанию в плане — **вынести в MCP как `xaml.add_variable`/`xaml.remove_variable`**, чтобы палитра была целостной.

### Фаза 7. Фильтр способа в чате

Файлы:
- `Agent.Core\AgentTools\AgentTool.cs` — свойство `WorkflowWriteWay Way { get; }` (default `None`).
- `Agent.Core\AgentTools\AgentToolRegistry.cs` — `ForMode(mode, allowed, allowedWays = WorkflowWriteWay.All)`:
  ```csharp
  .Where(t => (mode == Act || !t.Mutates)
           && (allowed & t.Permission) != 0
           && (t.Way == WorkflowWriteWay.None || (allowedWays & t.Way) != 0))
  ```
- `Agent.Core\AgentTools\ToolSchemaConverter.cs` — `BuildToolDefinitions(..., allowedWays)`.
- `Agent.Core\AgentTools\AgentPromptBuilder.cs` — `Build(..., allowedWays)` и `AppendWorkflowGuidance(..., allowedWays)`.
- `Agent.Core\AgentTools\AgentRunner.cs` — хранить `_allowedWays`, прокинуть в `BuildToolDefinitions`, и добавить второй блокирующий гейт:
  ```csharp
  if (tool.Way != WorkflowWriteWay.None && (_allowedWays & tool.Way) == 0)
      return Blocked("способ написания WF отключён");
  ```
  в `ExecuteNativeToolCallAsync` и `ExecuteJsonToolCallAsync` (по образцу текущего гейта `(_allowedPermissions & tool.Permission) == 0`).

> Текст XAML (`editor`) не помечается `Way`: он остаётся вне способов и продолжает управляться `AllowEditFiles`/`AllowReadFiles`. Это гарантирует, что галочки MCP не заблокируют обычную правку файлов агентом.

### Фаза 8. Настройки + политика способа

Файл: `RPA.Chat\Services\SettingsStore.cs` — в `SettingsData` добавить 3 поля (по умолчанию `true`):

```csharp
public bool AllowWfPalette { get; set; } = true;
public bool AllowWfJsonAst { get; set; } = true;
public bool AllowWfCodeFirst { get; set; } = true;
```

Новый файл: `RPA.Chat\Services\WorkflowWritePolicy.cs` (по образцу `AgentPermissionsPolicy`):

```csharp
public static class WorkflowWritePolicy
{
    public static WorkflowWriteWay Resolve(SettingsData settings)
    {
        if (settings == null) return WorkflowWriteWay.None;
        var ways = WorkflowWriteWay.None;
        if (settings.AllowWfPalette)   ways |= WorkflowWriteWay.Palette;
        if (settings.AllowWfJsonAst)   ways |= WorkflowWriteWay.JsonAst;
        if (settings.AllowWfCodeFirst) ways |= WorkflowWriteWay.CodeFirst;
        return ways;
    }
}
```

### Фаза 9. Hot-reload в чате

Файл: `RPA.Chat\ViewModels\ChatViewModel.cs`.

- Добавить `BuildAllowedWays() => WorkflowWritePolicy.Resolve(_settings)`.
- В `RebuildAgentTools()` прокинуть `allowedWays`:
  - в `AgentRunner` (для рантайм-гейта);
  - в `AgentPromptBuilder.Build` (для промпта);
  - в `AgentToolFactory` / импорт MCP-инструментов (для фильтра при регистрации).
- Добавить команду немедленного применения (без закрытия overlay):
  ```csharp
  public void ApplyWorkflowWaysNow()
  {
      SettingsStore.Save(_settings);
      RebuildAgentTools();
  }
  ```
  (та же логика, что сейчас в `ClosePermissions()`, но вызывается по клику галочки).

> Текущий прогон агента не рвём: `AgentRunner` фиксирует маску на старте прогона. Переключение во время активного запроса применится к следующему сообщению.

### Фаза 10. UI: 3 галочки

Файл: `RPA.Chat\Views\ChatPanel.xaml`, overlay «Agent permissions».

Добавить секцию «Workflow writing ways (MCP)» с тремя строками:

| Подпись | Binding | Command |
|---|---|---|
| Palette (designer) | `Settings.AllowWfPalette` | `ApplyWorkflowWaysNowCommand` |
| JSON AST | `Settings.AllowWfJsonAst` | `ApplyWorkflowWaysNowCommand` |
| Code-First | `Settings.AllowWfCodeFirst` | `ApplyWorkflowWaysNowCommand` |

Каждая строка:
```xml
<StackPanel Orientation="Horizontal" Margin="0,2">
    <ToggleButton Style="{StaticResource SwitchToggleButtonStyle}"
                  IsChecked="{Binding Settings.AllowWfPalette, Mode=TwoWay}"
                  Command="{Binding ApplyWorkflowWaysNowCommand}" VerticalAlignment="Center" />
    <TextBlock Text="Palette (designer)" VerticalAlignment="Center" Margin="8,0,0,0" />
</StackPanel>
```

`ApplyWorkflowWaysNowCommand` (RelayCommand) вызывает `ApplyWorkflowWaysNow()`.

### Фаза 11. Фильтрация способа в MCP (для внешних клиентов)

Файлы: `AiRpaStudio\Mcp\McpToolRegistry.cs`, `AiRpaStudio\Mcp\RpaMcpServer.cs`.

- `McpToolRegistry` дополнить таблицей «имя MCP-инструмента → `WorkflowWriteWay`» и методами:
  - `Schemas(WorkflowWriteWay allowedWays)`;
  - `TryGet(name, WorkflowWriteWay allowedWays, out tool)`.
- `RpaMcpServer`:
  - `ListTools()` читает актуальную маску (`SettingsStore.Load()` → `WorkflowWritePolicy.Resolve`) и возвращает только разрешённые способы;
  - `CallTool()` при вызове инструмента запрещённого способа возвращает `Success=false` с пояснением.

Маску читать на каждый вызов (просто и детерминированно) либо кэшировать с коротким TTL + `FileSystemWatcher` на `chat_settings.json`. Для первой итерации достаточно «читать на каждый вызов».

### Фаза 12. Тесты и стражи

- `AiRpaStudio.Tests\Workflow\WorkflowToolRegistryGuardTests.cs`:
  - добавить колонку `Way` в таблицу стража;
  - негативы: мутирующий MCP-импортированный инструмент без `Way` ловится; выключенный способ прячет инструмент из Act; read-only `workflow_outline`/`activity_search`/`xaml.get_workflow` не зависят от способа;
  - сохранить инвариант «RPA.Chat/Agent.Core не ссылаются на `RPA.Mcp`/`AiRpaStudio\Mcp`» (мост живёт в `RPA.Chat`, реализация — в `AiRpaStudio`).
- `AiRpaStudio.Tests\ChatViewModelTests.cs` — тесты `WorkflowWritePolicy.Resolve` (null → None, каждый флаг, All).
- `AiRpaStudio.Tests\AgentToolsTests.cs` / `AgentToolFactoryTests.cs` — фильтр `ForMode` по второму измерению.
- `AiRpaStudio.Tests\Workflow\WorkflowEditorXamlTests.cs` — `editor` продолжает писать `.xaml` независимо от галочек MCP (текст XAML не под галочками).

---

## 5. Сводная таблица маппинга инструментов

### MCP (3 способа)

| Способ | Инструменты | Mutates | Way |
|---|---|---|---|
| Палитра | `xaml.create_workflow` | да | `Palette` |
| Палитра | `xaml.add_activity` | да | `Palette` |
| Палитра | `xaml.set_argument` | да | `Palette` |
| Палитра | `xaml.add_variable` (новый) | да | `Palette` |
| Палитра | `xaml.remove_variable` (новый) | да | `Palette` |
| Палитра | `xaml.get_workflow` | нет | `None` (read-only поддержка) |
| JSON AST | `xaml.create_workflow_from_json` | да | `JsonAst` |
| Code-First | `xaml.create_workflow_from_code` | да | `CodeFirst` |
| — | `xaml.describe_contract` | нет | `None` (read-only поддержка) |

### Чат (текст XAML, вне галочек)

| Инструмент | Way |
|---|---|
| `editor` (запись `.xaml` через `WriteWorkflowXaml`) | `None` (управляется `AllowEditFiles`) |
| `editor` (запись `.cs` и прочих файлов) | `None` |

### Устаревающие чат-инструменты палитры (после выноса в MCP)

`activity_insert`, `activity_delete`, `activity_move`, `activity_wrap`, `activity_undo`, `activity_redo`, `argument_set`, `variable_add`, `variable_remove` — выносятся в MCP (Фаза 6) и удаляются из чата после миграции.

---

## 6. Риски и открытые вопросы

1. **`editor` пишет и `.xaml`, и `.cs`.** «Текст XAML» не изолирован в отдельную галочку, поэтому тест «только текст XAML» означает: 3 MCP-галочки выключены + `AllowEditFiles` включён. Обычная правка `.cs` тем же `editor` остаётся доступной. Если нужна строгая изоляция текста XAML — отдельным шагом добавить sub-gate внутри `WriteWorkflowXaml` (вне текущего объёма).
2. **`variable_add`/`variable_remove`.** В MCP сейчас нет инструментов переменных. Нужно решить: добавить `xaml.add_variable`/`xaml.remove_variable` (рекомендуется) либо оставить переменные в чате. План исходит из добавления в MCP.
3. **`activity_*` и `xaml.*` дублируют палитру.** До удаления чат-палитры два пути сосуществуют. Мигрировать строго после зелёных тестов, чтобы не потерять сценарии живого конструктора.
4. **Страж `RpaChatAndAgentCore_DoNotReferenceStudioMcp`.** Мост должен быть объявлен в `RPA.Chat`, реализация — в `AiRpaStudio`. В `RPA.Chat`/`Agent.Core` не должно появиться строк `RPA.Mcp`/`AiRpaStudio\Mcp`.
5. **MCP-сервер в обычном режиме.** Сейчас создаётся только при `--mcp-server`. Встроенный singleton не должен стартовать `stdio/http`-транспорты в обычном режиме.
6. **Схемы MCP → informal-схема `IAgentTool`.** `McpTool.InputSchema` (объектная JSON Schema) нужно нормализовать в строку `ParametersSchema` при импорте в чат, иначе `ToolSchemaConverter` не построит native-схему.
7. **Синхронизация настроек между чатом и MCP.** Оба читают одни и те же 3 поля из `chat_settings.json` через `SettingsStore.Load()`. Если MCP headless запущен отдельным процессом, он читает тот же файл из `%AppData%\AiRpaStudio\Chat`.

---

## 7. Порядок внедрения

1. Фаза 1 — `WorkflowWriteWay` в `Agent.Core` (независимо).
2. Фаза 2 — `IWorkflowMcpBridge` + null-object + `ChatEnvironment` (независимо).
3. Фаза 4 + 3 — встроенный `RpaMcpServer` + `StudioWorkflowMcpBridge` + подключение в `MainView`.
4. Фаза 5 + 7 — импорт MCP-инструментов в чат + фильтр способа.
5. Фаза 8 + 9 — настройки + политика + hot-reload.
6. Фаза 10 — 3 галочки в UI.
7. Фаза 6 — миграция/удаление `activity_*` после покрытия MCP.
8. Фаза 11 — фильтрация способа в headless MCP.
9. Фаза 12 — тесты/стражи параллельно с каждой фазой.
