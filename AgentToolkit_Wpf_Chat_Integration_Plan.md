# План интеграции нового чата AgentToolkit.Wpf 1.19.0 (ChatControl) и удаления старого RPA.Chat.ChatPanel

> Документ-план. **Код не правим** — только фиксируем последовательность работ.
> Актуально для решения `AiRpa.sln` (`C:\Users\Maxxx\Desktop\AI-RPA`).

---

## 0. Резюме

**Проблема:** студия показывает «старый» чат — самописный `RPA.Chat.Views.ChatPanel`
(~1100 строк XAML + `ChatViewModel` ~1720 строк), а не готовый чат из пакета
`AgentToolkit.Wpf 1.19.0`.

**Причина:** в док-панель `Chat` студия кладёт именно `new Chat.Views.ChatPanel()`:

- `AiRpaStudio\Views\MainView.xaml.cs:60` — поле `private Chat.Views.ChatPanel chatPanel;`
- `AiRpaStudio\Views\MainView.xaml.cs:129` — `chatPanel = new Chat.Views.ChatPanel();`
- `AiRpaStudio\Views\MainView.xaml.cs:546` — `args.Content = chatPanel;` (восстановление раскладки)
- `AiRpaStudio\Views\MainView.xaml.cs:784` — `Content = chatPanel` (дефолтная раскладка)

Пакет подключён, но используется только «по кусочкам»:
`ChatMessagesControl`, `MarkdownToElementConverter`, `AgentThemeManager`.
Полноценный `Agent.Toolkit.Wpf.Controls.ChatControl` в коде не используется (0 ссылок).

**Решение:** подключить `ChatControl` из `AgentToolkit.Wpf`, реализовать контракт хоста
`IChatHost` в `RPA.Chat`, перевести студию на новую связку, старый UI удалить.

---

## 1. Что уже есть в AgentToolkit.Wpf (справочно, снято из исходников AgentShared)

Источник: `C:\Users\Maxxx\source\repos\AgentShared\Agent.Toolkit.Wpf`.

### 1.1. ChatControl
Файл: `Controls/ChatControl.xaml(.cs)`.

`ChatControl` владеет всем чатом: тулбар, история, список сообщений, поле ввода
(вложения/микрофон/очистка), пин-сообщение, очередь pending, переключатель Plan/Act/Help,
оверлеи Permissions/Docs, статус Cancel/Resume.

Публичное подключение:

```csharp
var chat = new ChatControl
{
    DataContext = chatModel,          // Agent.Models.ChatModel
    Host = chatHost,                  // Agent.Toolkit.Wpf.Controls.IChatHost
    Settings = settings,              // Agent.Core.Services.SettingsData
    SettingsPersistence = settingsStore // Agent.Core.Contracts.ISettingsStore (может быть null)
};
chat.AttachServices(mic, stt);        // IMicrophoneRecorder, ISpeechToTextService (поставляет хост/другой проект)
```

Доступные слоты для хост-контента:

- `VoiceErrorToastHost`
- `OptionsWarningToastHost`
- `CommitResultBarHost`
- `CheckpointRestoreErrorToastHost`
- `PromptCacheLossToastHost`
- `CommitEditorHost`
- `CreateRobotHost`
- `DocsTitleTextControl` / `DocsContentControl`

Полезные методы/свойства для хоста:

- `MessagesList`, `Queue`
- `AppendChatMessage(ChatMessage)`, `AppendChat(string)`, `AppendSystemMessage(string)`,
  `AppendExtensionError(string)`, `AppendCompaction(string)`, `AppendToolCall(name, result, blocked)`
- `ShowConversationView()`, `ShowHistoryView()`, `ApplyChatViewMode()`
- `EnsureChatReadyToReceive()`, `RefreshHistory()`
- `SetInputText(string)`, `AppendInputText(string)`
- `SetCompactIndicator(bool)`
- `RestoringUiState`
- события `VoiceError`, `PromptCacheLost`

### 1.2. IChatHost
Файл: `Controls/IChatHost.cs`. Члены (контракт хоста):

```csharp
Task SendChatAsync(string text, IReadOnlyList<ChatAttachment> attachments);
void CancelAgentRun();
Task ResumeAgentRunAsync();
bool IsAgentRunning { get; }
void OpenCommitControl();
void SendBuildErrors();
void OpenCreateRobot();
void RebuildAgentTools();
void StartNewChat();
void OpenConversation(string id);
void OnConversationRemoved();
void ShowDocsIndex();
Task CompactNowAsync();
void RequestMemoryUpdate();
```

### 1.3. ChatModel
Файл: `C:\Users\Maxxx\source\repos\AgentShared\Agent.Models\ChatModel.cs`.

Уже используется текущим `ChatViewModel`:
`new ChatModel(new ChatHistoryStore(), new UiStateStore())`.

Ключевые члены:

```csharp
ChatModel(IChatHistoryStore historyStore, IUiStateStore uiStateStore = null,
          IChatChildModel childModel = null, IChatPromptSink chatSink = null,
          IWorkspaceProvider workspaceProvider = null, Func<DateTime> utcNow = null,
          IAgentLogger logger = null);

void Run();                          // инициализация истории (идемпотентно)
ObservableCollection<ChatMessage> Messages { get; }
ObservableCollection<PendingMessage> Pending { get; }
ObservableCollection<ChatAttachment> Attachments { get; }
ObservableCollection<ChatConversation> History { get; }

ChatMessage AppendMessage(ChatMessage message);
void ApplyCompactedTranscript(IEnumerable<ChatMessage> compacted);
PendingMessage Enqueue(string text, IReadOnlyList<ChatAttachment> attachments = null);
PendingMessage DequeueNext();

void StartNewChat(bool markDirty = true);
bool OpenConversation(string id, bool markDirty = true);
bool DeleteConversation(string id);
bool ToggleImportant(string id);
bool ClearHistory();
void SaveHistoryNow();
void RefreshHistory();
ChatConversation EnsureActiveConversation();
void CloseActiveChat();

void ShowConversationView();
void ShowHistoryView();
void PersistUiState(MainTab activeTab);
UiStateData LoadUiState();

AgentMode Mode { get; set; }
AgentPhase Phase { get; set; }
bool HasResumableRun { get; set; }
ChatMessage PinnedMessage { get; }
bool IsConversationView { get; }
bool IsHistoryView { get; }

void SetContextWindow(int contextWindowSize, int compactThresholdTokens, int maxInputTokens);
void RecordTokenUsage(TokenUsage usage);
```

`ChatControl` сам подписывается на `ChatModel.PropertyChanged`/`PromptCacheLost` и раскладывает
`Messages`/`Pending`/`Attachments`/`History` по своим спискам — то есть биндинги вручную
делать не нужно, только присвоить `DataContext`.

### 1.4. Тема
`Agent.Theming.AgentThemeManager` уже используется в студии:

```csharp
AgentThemeManager.SetMode(AgentThemeMode.Auto);      // уже в MainView.xaml.cs:166
AgentThemeManager.Apply(theme.ToName());             // уже в ApplyStudioAndToolkitTheme
```

`ChatControl` читает ключи `Theme.*` через `{DynamicResource}` и сам перекрашивается —
отдельный `ChatStudioBridge`/`ChatTheme.*` для него не нужен.

---

## 2. Диагноз текущего кода (что и где менять)

### 2.1. Точка подключения старого чата
`AiRpaStudio\Views\MainView.xaml.cs`:

| Строка | Сейчас | Станет |
|---|---|---|
| 60 | `private Chat.Views.ChatPanel chatPanel;` | поле нового контрола/хоста |
| 129 | `chatPanel = new Chat.Views.ChatPanel();` | создание новой связки |
| 166 | `AgentThemeManager.SetMode(AgentThemeMode.Auto);` | остаётся |
| 169 | `chatPanel.ViewModel.ThemeApplying = ApplyStudioAndToolkitTheme;` | убрать / заменить |
| 171 | `chatPanel.ViewModel.ThemeChanged += OnChatThemeChanged;` | убрать / заменить |
| 172 | `ApplyStudioAndToolkitTheme(chatPanel.ViewModel.Theme);` | заменить источник темы |
| 175 | `chatPanel.ReapplyTheme();` | убрать |
| 178-183 | биндинг `ThemeToggleButton` на `ChatViewModel.IsDarkTheme` | заменить источник |
| 546 | `args.Content = chatPanel;` | `args.Content = <новый контрол>;` |
| 784 | `Content = chatPanel` | `Content = <новый контрол>` |
| 835-836 | `ActivateChatPanel(); await chatPanel.ViewModel.SendPromptAsync(prompt);` | `ActivateChatPanel(); await <host>.SendChatAsync(prompt, …);` |

### 2.2. Старый UI/VM к удалению
- `RPA.Chat\Views\ChatPanel.xaml` / `ChatPanel.xaml.cs`
- `RPA.Chat\ViewModels\ChatViewModel.cs`
- `RPA.Chat\ViewModels\SettingsViewModel.cs`
- `RPA.Chat\Controls\*` (старые: `PinnedChatMessageControl`, `NotificationBanner`,
  `SegmentedToggle`, `SegmentedToggleItem`, `BottomUpWrapPanel`, `VirtualizingWrapPanel`)
- `RPA.Chat\Converters\InverseBooleanToVisibilityConverter.cs`
- `RPA.Chat\Themes\ChatTheme.Dark.xaml`, `ChatTheme.Light.xaml`, `ChatStudioBridge.cs`, `ChatFonts.cs`
- пустые папки `RPA.Chat\Markdown\`, `RPA.Chat\Models\` (если после удаления пусты)

### 2.3. Сервисы/логика — ОСТАЮТСЯ (это и есть будущий хост)
- `RPA.Chat\Services\ChatEnvironment.cs` — статические точки интеграции студии
  (`SolutionPathProvider`, `DocumentService`, `ActivityCatalog`, `WorkflowOutline`,
  `WorkflowEditing`, `WorkflowXamlGate`, `WorkflowMcpBridge`, `ActivityPaletteNotifier`,
  `SpecStore`, `WorkflowRun`).
- `RPA.Chat\Services\AgentTools\`, `Services\Spec\`, `Services\Workflow\`
- `ChatHistoryStore`, `UiStateStore`, `SettingsStore` (см. шаг про настройки),
  `ChatPaths`, `ChatLog`, `SafeText`, `AiProviderFactory`, `GitService`,
  `MsBuildFeedbackService`, `OutputWindowService`, `ShellCommandService`,
  `AgentPermissionsPolicy`, `WorkflowWritePolicy`.
- Голосовой ввод — НЕ в этом плане: реализация `IMicrophoneRecorder` поставляется другим проектом.
  Существующие `MicrophoneRecorder` и `SpeechToTextFactory` не адаптируются под контракт тулкита;
  их судьба — вместе с удалением старого UI (шаг 8).
- `RPA.Shared\AgentTools\` — `AgentPromptBuilder`, `AgentRunner`, `AgentToolRegistry` и т.д.

---

## 3. Пошаговый план

Порядок шагов построен так, чтобы после каждого шага проект оставался компилируемым,
а переключение на новый чат произошло одним атомарным шагом (шаг 6). Это минимизирует окно,
в котором старый и новый чат сосуществуют.

---

### Шаг 0. Зафиксировать решения (3 решения)

#### Решение S1 — единый тип настроек
`ChatControl.Settings` типизирован **каноническим** `Agent.Core.Services.SettingsData`
(сборка `Agent.Core.dll` из пакета `AgentToolkit`). Сейчас в `RPA.Chat` есть **форк**
`RPA.Chat.Services.SettingsData` (файл `RPA.Chat\Services\SettingsStore.cs`).

**Рекомендация:** перейти на канонический `Agent.Core.Services.SettingsData`.

- Форк удаляется/перестаёт использоваться как тип настроек.
- RPA-специфичные флаги `AllowWfPalette`, `AllowWfJsonAst`, `AllowWfCodeFirst` выносятся
  в отдельный хост-контракт (например `ChatEnvironment` или новый `RpaAgentOptions`),
  потому что тулкиту они не нужны.
- `AgentPermissionsPolicy.Resolve(...)` и `WorkflowWritePolicy.Resolve(...)` переписываются
  под канонический тип + RPA-флаги.
- Новый `ISettingsStore` читает/пишет тот же файл `%AppData%\AiRpaStudio\Chat\chat_settings.json`
  и мигрирует старые поля (`Theme`, `UseDarkTheme` → `BaseTheme`; отсутствующие поля → дефолты).
- `SettingsControl` из тулкита получает `SettingsModel`/`SettingsData` без отдельного
  `SettingsViewModel`.

**Альтернатива (не рекомендуется):** оставить форк и синхронизировать его с каноническим типом
при создании `ChatControl`. Даёт два источника истины и двойную миграцию.

#### Решение S2 — где живёт хост
**Рекомендация:** тонкая обёртка в `RPA.Chat` (новый контрол, например
`RPA.Chat\Views\ChatHostView.xaml(.cs)` или переписанный `ChatPanel`), которая собирает
связку `ChatModel + IChatHost + ChatControl + слоты`. В `MainView.xaml.cs` меняется только
тип поля и создание. Причина: `MainView` остаётся тонким, `contentId == "Chat"` не меняется.

Альтернатива: собирать `ChatControl` прямо в `MainView` (меньше слоёв, но `MainView` раздувается).

#### Решение S3 — голосовой ввод вне этого плана
Реализация `Agent.Toolkit.Wpf.Controls.IMicrophoneRecorder` — **ответственность другого проекта**,
а не `RPA.Chat`. В рамках этой интеграции микрофон не реализуется и не адаптируется:

- `RPA.Chat` НЕ объявляет существующий `MicrophoneRecorder` реализацией `IMicrophoneRecorder`
  и не меняет его под контракт тулкита.
- Реализации `IMicrophoneRecorder` и `ISpeechToTextService` для `ChatControl.AttachServices(...)`
  поставляет внешний проект/хост.
- Пока внешняя реализация не подключена, `AttachServices` не вызывается (или получает `null`) —
  голосовой ввод в новом чате не используется, остальной чат работает.

---

### Шаг 1. TDD: стражи интеграции (red)

Сначала — падающие тесты в `AiRpaStudio.Tests`. Правило репозитория: TDD обязательно.

Новые стражи:

1. **`MainView` использует `ChatControl`/новый хост, а не `ChatPanel`:**
   - исходник `MainView.xaml.cs` не содержит `new Chat.Views.ChatPanel()` (или поле не `ChatPanel`);
   - содержит создание `ChatControl` (прямо или через обёртку);
   - при `contentId == "Chat"` в док кладётся новый контрол.

2. **`IChatHost` реализован и подключён:**
   - тип, реализующий `Agent.Toolkit.Wpf.Controls.IChatHost`, существует в `RPA.Chat`;
   - код создания `ChatControl` присваивает `Host = ...`.

3. **Тема через `AgentThemeManager`, а не через `ChatStudioBridge`:**
   - в `MainView`/новом контроле нет вызовов `ChatStudioBridge.Apply(...)`;
   - тема транслируется вызовом `AgentThemeManager.Apply(...)`;
   - старые `ChatTheme.*` / `ChatPanel.ApplyTheme` не используются.

4. **`ChatEnvironment.*` по-прежнему заполняется в `MainView.InitializeControls`:**
   - стражи на `SolutionPathProvider`, `ActivityCatalog`, `WorkflowOutline`, `WorkflowEditing`,
     `WorkflowXamlGate`, `WorkflowMcpBridge`, `ActivityPaletteNotifier`, `WorkflowRun`.

5. **Настройки — канонический тип:**
   - код хоста использует `Agent.Core.Services.SettingsData`, а не `RPA.Chat.Services.SettingsData`.

6. **Старый UI удалён:**
   - файлы `ChatPanel.xaml(.cs)`, `ChatViewModel.cs`, `SettingsViewModel.cs`, `ChatTheme.*`,
     `ChatStudioBridge.cs`, `ChatFonts.cs` отсутствуют (или помечены как удалённые);
   - `RPA.Chat.csproj` не ссылается на них.

7. **Голосовой ввод не реализуется в `RPA.Chat`:**
   - в `RPA.Chat` нет типа, реализующего `Agent.Toolkit.Wpf.Controls.IMicrophoneRecorder`;
   - `RPA.Chat\Services\MicrophoneRecorder.cs` не объявляет этот интерфейс.

Существующие тесты, которые нужно обновить после миграции:
- `ThemeAcceptanceTests`
- `ThemeApiGuardTests`
- `LogAndChatThemeTests`
- `ChatThemeBridgeTests`
- `ChatThemeSettingsTests`
- `ChatViewModelTests`
- `StudioScrollBarTests` (тест-кейсы на `RPA.Chat\Views\ChatPanel.xaml`)

---

### Шаг 2. Настройки: канонический SettingsData + ISettingsStore

Файлы:
- новый `RPA.Chat\Services\ChatSettingsStore.cs` — реализация
  `Agent.Core.Contracts.ISettingsStore` поверх существующего пути `ChatPaths.SettingsFile`;
- миграция JSON: `Theme`/`UseDarkTheme` → `BaseTheme`; пропущенные поля → дефолты
  (`Agent.Core.Services.SettingsData.Normalize`).
- RPA-флаги WF выносятся (см. S1).

Ключевой скелет:

```csharp
using Agent.Core.Contracts;
using Agent.Core.Services;

namespace RPA.Chat.Services
{
    public sealed class ChatSettingsStore : ISettingsStore
    {
        public SettingsData Load() { /* JSON -> SettingsData + миграция */ }
        public void Save(SettingsData settings) { /* SettingsData -> JSON */ }
    }
}
```

Потребители настроек (`ChatHost`) читают канонический `SettingsData`:
`AllowReadFiles`, `AllowEditFiles`, `AllowExecuteCommand`, `AllowWebFetch`, `AllowMcp`,
`AllowSubagents`, `AskBeforeEachTool`, `SpeechToTextProvider`, `SpeechAutoSend`,
`ToolCallingMode`, `MemoryBankMode`, `ContextWindowSize`, `CompactThreshold` и т.д.

---

### Шаг 3. Микрофон: реализовать IMicrophoneRecorder - не делать, это ответственность другого проекта

Шаг **не выполняется** в `RPA.Chat` (см. решение S3).

- Реализация `Agent.Toolkit.Wpf.Controls.IMicrophoneRecorder` — ответственность другого проекта.
- Существующий `RPA.Chat\Services\MicrophoneRecorder.cs` НЕ объявляется реализацией
  `IMicrophoneRecorder` и не адаптируется под этот контракт.
- Голосовой ввод в `ChatControl` подключается только внешним поставщиком через
  `ChatControl.AttachServices(mic, stt)`. Пока внешняя реализация не подключена — вызов
  опускается, кнопка микрофона в новом чате не используется.
- Судьба существующих `MicrophoneRecorder`/`SpeechToTextFactory` решается при удалении
  старого UI (шаг 8), а не здесь.

---

### Шаг 4. Реализовать IChatHost в RPA.Chat

Новый файл, например `RPA.Chat\Services\ChatHost.cs` (или `Services\StudioChatHost.cs`).

Сюда переносится бизнес-логика из `ChatViewModel`:

```csharp
using Agent.Toolkit.Wpf.Controls;
using Agent.Models;
using Agent.Core.Models;
using Agent.Core.Services;

namespace RPA.Chat.Services
{
    public sealed class StudioChatHost : IChatHost
    {
        public ChatModel Model { get; }
        public SettingsData Settings { get; }

        public StudioChatHost(ChatModel model, SettingsData settings /* + зависимости */)
        {
            Model = model;
            Settings = settings;
            RebuildAgentTools();
        }

        public async Task SendChatAsync(string text, IReadOnlyList<ChatAttachment> attachments) { ... }
        public void CancelAgentRun() { ... }
        public Task ResumeAgentRunAsync() { ... }
        public bool IsAgentRunning => _agentRunner != null; // или фаза
        public void OpenCommitControl() { ... }
        public void SendBuildErrors() { ... }
        public void OpenCreateRobot() { ... }
        public void RebuildAgentTools() { ... }
        public void StartNewChat() { Model.StartNewChat(); }
        public void OpenConversation(string id) { Model.OpenConversation(id); }
        public void OnConversationRemoved() { ... }
        public void ShowDocsIndex() { ... }
        public Task CompactNowAsync() { ... }
        public void RequestMemoryUpdate() { ... }
    }
}
```

Что переносится из `ChatViewModel`:
- агентский цикл: `SendChatTextAsync`, `BuildTranscript`, `BuildContextBudget`,
  `BuildSubAgentBudget`, `RunSubAgentAsync`, `AskQuestionAsync`;
- инструменты: `RebuildAgentTools`, `BuildToolRegistry`, `AgentToolFactory`,
  `AgentPermissionsPolicy.Resolve`, `WorkflowWritePolicy.Resolve`;
- подписки на `AgentRunner` (`ToolExecuted`, `ContextCompacted`, `RetryOccurred`);
- хост-специфика: `CommitAsync`/`CommitSendAsync`/`CommitAutoGenerateAsync`,
  `BuildCompilationErrorsContextAsync`, `SendErrorsAsync`, `ShowDocsIndex`, `DoItAsync`;
- статус/notice: `ShowNotice`, `AppendChat`, `AppendToolCall`, `AppendCompaction`,
  `AppendExtensionError` (через `ChatControl` или через `Model.AppendMessage`).

Хост не трогает WPF-контролы напрямую: сообщения идут в `ChatModel.Messages`/
`ChatModel.Pending`, `ChatControl` отображает их сам. Статус агента — через
`Model.Phase` / `Model.HasResumableRun` (их читает `ChatControl`).

---

### Шаг 5. Тонкая обёртка ChatHostView

Новый файл, например `RPA.Chat\Views\ChatHostView.xaml(.cs)`:

```csharp
public partial class ChatHostView : UserControl
{
    private readonly ChatControl _chatControl;
    private readonly StudioChatHost _host;

    public ChatHostView()
    {
        InitializeComponent();

        var model = new ChatModel(new ChatHistoryStore(), new UiStateStore());
        var settingsStore = new ChatSettingsStore();
        var settings = settingsStore.Load();

        _host = new StudioChatHost(model, settings /* + зависимости */);

        _chatControl = new ChatControl
        {
            DataContext = model,
            Host = _host,
            Settings = settings,
            SettingsPersistence = settingsStore
        };
        // Голосовой ввод (IMicrophoneRecorder / ISpeechToTextService) поставляет другой проект;
        // в RPA.Chat этот контракт не реализуется. Пока внешней реализации нет — вызов опускается.
        // _chatControl.AttachServices(/* внешний mic */, /* внешний stt */);

        // Хост-слоты (commit-редактор, create-robot, тосты) — по мере переноса:
        // _chatControl.CommitEditorHost.Content = ...;
        // _chatControl.CreateRobotHost.Content = ...;

        Root.Child = _chatControl; // или Content = _chatControl
        model.Run();
    }

    public async Task SendPromptAsync(string prompt)
    {
        _chatControl.EnsureChatReadyToReceive();
        await _host.SendChatAsync(prompt, Array.Empty<ChatAttachment>());
    }
}
```

`ChatControl` сам берёт `DataContext = ChatModel` и раскладывает коллекции — ручные
биндинги (`ChatMessages.Messages = ...` и т.п.) не нужны.

---

### Шаг 6. Подключение в MainView (атомарное переключение)

В `AiRpaStudio\Views\MainView.xaml.cs`:

1. Заменить тип поля:
   ```csharp
   private Chat.Views.ChatHostView chatPanel;   // или новое имя chatView
   ```

2. В `InitializeControls()`:
   ```csharp
   chatPanel = new Chat.Views.ChatHostView();
   ```
   Заполнение `ChatEnvironment.*` (строки 136–164) — **без изменений**.

3. Тема:
   ```csharp
   AgentThemeManager.SetMode(AgentThemeMode.Auto);        // уже есть
   ApplyStudioAndToolkitTheme(/* активная тема студии */); // вместо chatPanel.ViewModel.Theme
   ```
   Убрать `ThemeApplying`/`ThemeChanged`/`ReapplyTheme` (строки 169–175).

4. Биндинг `ThemeToggleButton` (строки 178–183) переключить на источник темы студии
   (активная тема `AppSettings`/`StudioPalette` или `SettingsData.BaseTheme`), а не на
   `ChatViewModel.IsDarkTheme`.

5. `contentId == "Chat"` (строка 546) и дефолтная раскладка (строка 784):
   ```csharp
   args.Content = chatPanel;   // тип уже новый, строка не меняется
   Content = chatPanel;
   ```

6. `OnDesignerErrorSendRequested` (строки 835–836):
   ```csharp
   ActivateChatPanel();
   await chatPanel.SendPromptAsync(prompt);
   ```

7. `OnChatThemeChanged`/`ReapplyWorkflowControlThemes` оставить для перекраски WF/документов,
   но убрать из неё логику старого чата.

---

### Шаг 7. Тема: убрать ChatTheme/ChatStudioBridge

- В `MainView` больше нет `ChatPanel.ApplyTheme`, `ChatStudioBridge`, `ChatTheme.*`.
- Тема тулкита ведётся только `AgentThemeManager.SetMode(Auto)` + `AgentThemeManager.Apply(...)`.
- `ApplyStudioAndToolkitTheme` остаётся: сначала `Services.AppSettings.ApplyTheme(theme)`
  (палитра `Studio.*`), затем `AgentThemeManager.Apply(theme.ToName())` (палитра `Theme.*`).

Файлы темы чата к удалению: `RPA.Chat\Themes\ChatTheme.Dark.xaml`,
`ChatTheme.Light.xaml`, `ChatStudioBridge.cs`, `ChatFonts.cs`.

---

### Шаг 8. Удаление старого UI

Удалить/исключить из проекта (см. §2.2) и поправить `RPA.Chat\RPA.Chat.csproj`:

- убрать `<Page Include="Themes\ChatTheme.*.xaml">`;
- пересмотреть `<PackageReference>`:
  - `AgentToolkit` / `AgentToolkit.Wpf` — остаются;
  - `NAudio` — проверить после удаления старого `MicrophoneRecorder`/`SpeechToTextFactory`:
    голосовой ввод поставляет другой проект, поэтому если в `RPA.Chat` потребителей NAudio
    не останется — зависимость убрать;
  - `Markdig` — вероятно не нужен в `RPA.Chat` (рендер в тулките), проверить после удаления;
  - `Material.Icons.WPF` — проверить, остались ли потребители в `RPA.Chat`;
  - `System.Threading.Tasks.Extensions` — оставить (правило единой версии, `AiRpaStudio\AGENTS.md`).

Важно: `ChatEnvironment`, `AgentTools`, `Spec`, `Workflow`, `ChatHistoryStore`,
`UiStateStore`, `SettingsStore`/новый `ChatSettingsStore`, `GitService`,
`MsBuildFeedbackService` **не удаляются**. `MicrophoneRecorder`/`SpeechToTextFactory`
относятся к голосовому вводу (ответственность другого проекта) и в этом плане
не адаптируются; решение об их удалении принимается отдельно от интеграции чата.

---

### Шаг 9. Тесты и сборка (green)

1. Обновить существующие тесты, завязанные на старый чат (см. шаг 1).
2. Убедиться, что новые стражи зелёные.
3. Полная сборка (по правилам репозитория):
   ```
   MSBuild AiRpa.sln /p:Configuration=Release
   ```
   → 0 ошибок.

4. Прогон тестов (правило: `dotnet test` не работает, legacy WPF):
   ```
   MSBuild.exe AiRpaStudio.Tests\AiRpaStudio.Tests.csproj -t:Build
   vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll
   ```

5. Ручная приёмка:
   - открыть панель `Chat` — новый `ChatControl`;
   - отправить сообщение → агент отвечает, инструменты работают;
   - переключить тему Light/Dark → чат перекрашивается сразу (без «отставания на переключатель»);
   - Permissions/Docs, история, «Send errors to chat», «отправить ошибку дизайнера» работают;
     голосовой ввод подключается внешней реализацией `IMicrophoneRecorder` (вне этого плана).

---

## 4. Чек-лист верификации (критерий готовности)

- [ ] В док-панель `Chat` кладётся `ChatControl` (или его тонкая обёртка), а не `ChatPanel`.
- [ ] `IChatHost` реализован и подключён (`Host = ...`).
- [ ] `ChatControl.DataContext` = `ChatModel`, `Settings` = канонический `SettingsData`.
- [ ] `ChatEnvironment.*` заполняется в `MainView.InitializeControls` без изменений.
- [ ] Тема: `AgentThemeManager.SetMode(Auto)` + `Apply(...)`; нет `ChatStudioBridge`/`ChatTheme.*`.
- [ ] `ThemeToggleButton` биндится на источник темы студии, а не `ChatViewModel.IsDarkTheme`.
- [ ] `OnDesignerErrorSendRequested` отправляет промпт через новый хост.
- [ ] Старые файлы `ChatPanel`, `ChatViewModel`, `SettingsViewModel`, `ChatTheme.*`,
      `ChatStudioBridge`, `ChatFonts` удалены из `RPA.Chat.csproj`.
- [ ] Голосовой ввод: `RPA.Chat` не реализует `IMicrophoneRecorder`; реализацию поставляет другой проект.
- [ ] Сборка Release — 0 ошибок.
- [ ] Полный прогон тестов — зелёный (включая обновлённые и новые стражи).

---

## 5. Риски и откат

| Риск | Меры |
|---|---|
| Два `SettingsData` разъезжаются | Шаг 2 делает канонический тип единственным; миграция JSON; стражи |
| Commit/create-robot UI теряется при удалении `ChatPanel` | Перенести в слоты `CommitEditorHost`/`CreateRobotHost` (или временно оставить мини-обёртки) |
| Отставание темы чата | Ведём `Theme.*` через `AgentThemeManager` в `ApplyStudioAndToolkitTheme`; убираем двойной механизм |
| `Material.Icons` vs `Material.Icons.WPF` | `ChatControl` использует `Material.Icons` (транзитивно из `AgentToolkit.Wpf`); проверить обе ссылки при сборке |
| Сломать существующие тесты | Обновляем тесты в том же коммите; TDD-стражи до реализации |
| Регресс сервисов агента | Сервисы `RPA.Chat\Services\**` и `RPA.Shared\**` не трогаем, только переиспользуем из `StudioChatHost` |
| Голосовой ввод не работает в новом чате | Реализация `IMicrophoneRecorder` — вне `RPA.Chat` (другой проект); до её подключения кнопка микрофона не используется, чат работает |

**Откат:** заменить в `MainView` создание нового контрола обратно на `ChatPanel` и вернуть
удалённые файлы из git (изменение локализовано шагами 4–6).

---

## 6. Порядок коммитов (рекомендуемый)

1. `test: стражи интеграции ChatControl (red)` — шаг 1.
2. `feat(chat): канонический SettingsData + ChatSettingsStore` — шаг 2.
3. `feat(chat): StudioChatHost (IChatHost) из логики ChatViewModel` — шаг 4.
4. `feat(chat): ChatHostView + подключение в MainView + тема через AgentThemeManager` — шаги 5–7.
5. `refactor(chat): удалить старый ChatPanel/ChatViewModel/темы чата` — шаг 8.
6. `chore: обновить тесты и стражи, полная сборка + прогон` — шаг 9.
