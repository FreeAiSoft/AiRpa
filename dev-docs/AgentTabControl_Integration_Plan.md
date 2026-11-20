# План: подключение AgentTabControl (вкладки «Чат / Настройки») в AiRpaStudio

> Документ-план. Код не правим — только фиксируем, что и в каком порядке делать.

## 0. Резюме

В студии не видно переключателя вкладок «Чат / Настройки», потому что студия кладёт
в док-панель `Chat` контрол `ChatHostView`/`ChatControl`, а не `AgentTabControl`,
который и рендерит эти вкладки. Цель — перевести панель чата студии на `AgentTabControl`
и «подключить провода» к уже готовым `ChatPanel`/`SettingsPanel`.

## 1. Диагноз (факты)

### 1.1. Кто рендерит вкладки

Вкладки «Chat» / «Settings» рендерит сам тулкит — контрол
`Agent.Toolkit.Wpf/Controls/AgentTabControl.xaml(.cs)`. Это подкласс `TabControl`.

В конструкторе:

```csharp
Style = (Style)FindResource("MainTabControlStyle");

ChatPanel    = new ChatControl();
SettingsPanel = new SettingsControl();

AddTab(CoreTabs.Chat, ChatPanel);
AddTab(CoreTabs.Settings, SettingsPanel);
```

Заголовки/иконки/ключи берутся из UI-свободного `CoreTabs`
(`Agent.Core/Models/TabDefinition.cs`):

- `"Chat"` → header «Chat», иконки `MessageOutline`/`Message`;
- `"Settings"` → header «Settings», иконки `CogOutline`/`Cog`.

Стили вкладок лежат в `Styles/AgentTheme.xaml` (раздел «Main tab chrome»):

- `MainTabControlStyle`
- `MainTabItemStyle`
- `MainTabIconOutlineStyle`
- `MainTabIconFilledStyle`

### 1.2. Почему в студии их не видно

В `AiRpaStudio/Views/MainView.xaml.cs` в док «Chat» кладётся `ChatHostView`:

- строка 129: `chatPanel = new Chat.Views.ChatHostView();`
- строка 546: `args.Content = chatPanel;`
- строка 784: `Content = chatPanel`

`ChatHostView` (`RPA.Chat/Views/ChatHostView.xaml.cs`) внутри создаёт только `ChatControl`
и добавляет его в корневой `Grid`:

```csharp
_chatControl = new ChatControl { ... };
Root.Children.Add(_chatControl);
```

`ChatControl` — это только чат (тулбар/история/сообщения/ввод), вкладок в нём нет.

Старый `RPA.Chat/Views/ChatPanel.xaml` (в нём был свой `TabControl` со стилем
`MainTabItemStyle`) удалён; вместо него добавлен `ChatHostView`.

Итого: «тулкит умеет рендерить вкладки» и «в студии их не видно» не противоречат друг
другу — студия ещё не переведена с `ChatHostView` на `AgentTabControl`.

## 2. Проверка версии пакета (обязательно до правок)

Студия ссылается на `AgentToolkit.Wpf 1.19.0` (`RPA.Chat/RPA.Chat.csproj`).

В `AgentShared/feed` лежат и `1.19.0`, и `1.19.1`.

Нужно убедиться, что типы `AgentTabControl` и `SettingsControl` уже есть в потребляемой
сборке `1.19.0`; при отсутствии — поднять `PackageReference` до `1.19.1`.

## 3. Шаги реализации

### Шаг 1. Новая обёртка поверх AgentTabControl

Переписать `RPA.Chat/Views/ChatHostView.xaml(.cs)` так, чтобы корневым контролом был
`AgentTabControl`, а не голый `ChatControl`. Сохранить публичный API, на который
завязан `MainView`, чтобы минимизировать правки на стороне студии:

- `Host`, `ChatControl`;
- `Theme` / `IsDarkTheme` / `ThemeApplying` / `ThemeChanged`;
- `SendPromptAsync(prompt)`.

Внутри:

```csharp
_mainTabs = new AgentTabControl();

// Чат
var chat = _mainTabs.ChatPanel;
chat.DataContext = _model;                 // ChatModel
chat.Host = _host;                         // StudioChatHost (IChatHost)
chat.Settings = _settings;                 // SettingsData
chat.SettingsPersistence = _settingsStore; // ChatSettingsStore
chat.AttachServices(/* внешний mic */, /* внешний stt */);

// Настройки
var settingsPanel = _mainTabs.SettingsPanel;
settingsPanel.DataContext = <settingsVm>;  // VM: Settings (SettingsModel) + Cmd
```

Доменные вкладки студии (при необходимости) — через
`_mainTabs.AddHostTab(new TabDefinition(...), content)`, выбор — `TrySelectTab("...")`.

### Шаг 2. Настройки (SettingsPanel)

`SettingsControl` требует DataContext вида:

- `Settings` — тип `Agent.Models.SettingsModel`;
- `Cmd` — диспетчер команд (`Agent.Mvvm.BaseViewModel`).

Сконструировать `SettingsModel` с нужными `ISettingsStore` / `ISecretsStore` /
провайдерами либо минимальную VM, выставляющую `Settings` и `Cmd`.

`SettingsModel.ToSettingsData()` уже даёт канонический `SettingsData`, то есть маппинг
«настройки тулкита ↔ настройки студии» предусмотрен.

Синхронизировать тему: `SettingsControl` в режиме `Auto` не трогает тему — хост
владеет ею сам. Это совпадает с текущим `AgentThemeManager.SetMode(AgentThemeMode.Auto)`
в `MainView`.

### Шаг 3. Подключение в MainView

В `AiRpaStudio/Views/MainView.xaml.cs` сменить тип/конструкцию `chatPanel` на новую
обёртку. Строки `args.Content = chatPanel;` и `Content = chatPanel` остаются — теперь
в док попадает контрол со вкладками «Чат / Настройки».

Проверить, что остаются рабочими:

- `chatPanel.ThemeApplying = ApplyStudioAndToolkitTheme;`
- `chatPanel.ThemeChanged += OnChatThemeChanged;`
- `ApplyStudioAndToolkitTheme(chatPanel.Theme);`
- биндинг `ThemeToggleButton` на `IsDarkTheme`;
- `chatPanel.SendPromptAsync(prompt)`.

## 4. Проверка

1. Собрать решение (Release), убедиться в отсутствии ошибок.
2. Запустить студию, открыть панель `Chat` — виден переключатель вкладок
   «Чат / Настройки».
3. Вкладка «Чат»: отправка сообщений, история, режимы Plan/Act/Help, ввод, автоскролл.
4. Вкладка «Настройки»: поля загружены, кнопка Save сохраняет, тема переключается
   согласованно со студией (режим Auto).
5. Переключение темы из хоста студии перекрашивает обе вкладки без рестарта.

## 5. Риски

- **Версия пакета:** если `AgentTabControl`/`SettingsControl` отсутствуют в `1.19.0`,
  потребуется поднять до `1.19.1`.
- **Публичный API ChatHostView:** `MainView` и тесты (`ChatIntegrationGuardTests`,
  `ThemeApiGuardTests`) завязаны на `ChatHostView` и его свойства — при переписывании
  сохранить совместимость или обновить стражи.
- **Настройки:** неправильная синхронизация `BaseTheme`/`ThemeMode` может дать
  рассинхрон темы чата и студии.
