# План доработок AiRpaStudio

## Цели

1. Удалить неиспользуемые в проекте зависимости (особенно иконки).
2. Удалить встроенного AI-агента (AiAgent).
3. Добавить в студию чат по образцу `C:\Users\Maxxx\source\repos\VibeCoder\VibeCoder`
   со всем функционалом, но в дизайне MahApps.Metro (настройки палитр не нужны).
4. Добавить менеджер NuGet пакетов.
5. Создать документацию и интегрировать её с режимом «Plan» чата.

Принятое решение: **MCP-сервер (`RPA.Mcp`) остаётся как есть, новый чат с ним не связываем.**

---

## Фаза 0. Базовая линия

1. Сборка текущего решения через MSBuild (VS) + прогон `vstest.console.exe` по
   `AiRpaStudio.Tests` — зафиксировать зелёный baseline.
2. Снять список реально используемых сборок (usage-греп `using`/XAML против
   `HintPath`/`packages.config`).

## Фаза 1. Очистка зависимостей (особенно иконки)

1. Удалить **`MahApps.Metro.IconPacks` 4.11.0** (не используется) из `packages.config`
   и `.csproj`; убрать `xmlns:iconPacks` из `AppSettingsView.xaml`, `ProjectCreatorView.xaml`.
2. Проверить/удалить прочие неиспользуемые пакеты по результатам Фазы 0 (кандидаты:
   `PuppeteerSharp`, RoslynPad-семейство, `System.Reactive*` и т.п.); разобрать
   дублирующие `.ico`.
3. Оставить используемое: `Material.Icons.WPF` (тулбар), `MahApps.Metro`,
   `AvalonDock/AvalonEdit`, Roslyn (`Microsoft.CodeAnalysis*`), NLog.

## Фаза 2. Удаление встроенного агента (без MCP)

1. Удалить `AiRpaStudio\AiAgent\` целиком и его `<Compile>/<Page>/<None>` из
   `AiRpaStudio.csproj`.
2. Удалить legacy `AiAgent\`, `AiAgentTest\`, `AiAgent.sln`, `RPA.sln`,
   `AiRpaStudio\RPA.sln`.
3. Убрать из `MainView.xaml.cs`: `using RPA.AiAgent.Control`, `_aiAgentChatControl`
   и его создание.
4. Вычистить AI-настройки агента из `Properties\Settings.*` и `App.config`.
5. Удалить тесты `AiAgentConvertersTests.cs`, `AiAgentModelsTests.cs`.
6. **MCP не трогаем** (`App.xaml.cs` `--mcp-server`, `Mcp\*`, `Models\McpIntegration.cs`
   остаются).
7. Обновить `AGENTS.md` (убрать упоминание `RPA.AiAgent`, оставить `RPA.Mcp`).

## Фаза 3. Перенос `VibeCoder.Core`

1. Подключить `VibeCoder.Core` (netstandard2.0) в решение (скопировать в репозиторий),
   зависимости Markdig / Newtonsoft.Json / DocumentFormat.OpenXml / NPOI.
2. Перенести тесты `VibeCoder.Tests` в `AiRpaStudio.Tests`.

## Фаза 4. Чат в студию (MahApps.Metro)

1. Новый модуль `RPA.Chat` (классический MVVM): `Chat\Models`, `ViewModels`, `Views`,
   `Controls`, `Converters`, `Services`.
2. Переписать UI `VibeCoderToolWindowControl.xaml` с MaterialDesign на MahApps.Metro.
3. **Убрать настройки палитр/тем** (ThemeService, ThemeSwatchItem, MaterialColorUtilities).
4. Перенести `ChatMessagesControl`, `NotificationBanner`, `SegmentedToggle`,
   `MermaidDiagramControl` (WebView2), конвертеры и `MarkdownRenderer`.
5. Функционал: стриминг, markdown/код-блоки, вложения, Plan/Act/Help, история
   разговоров, retry/token-usage, вопросы-опции.

## Фаза 5. Адаптация сервисов под студию

- `VsDocumentService` → вкладки/файлы AvalonEdit + `ProjectManager`.
- `OutputWindowService` → `RPA.Logging.Log` + `LogControlLibrary`.
- `BuildFeedbackService` → MSBuild (как `VsProjectBuilder`).
- `SettingsStore`/`UiStateStore` → `Properties.Settings`/JSON; `ChatHistoryStore` → JSON.
- Оставить: `GitService`, `ShellCommandService`, `AiProviderFactory`, Speech-to-text
  (NAudio/Vosk/Whisper + нативные DLL), `WebProxyBuilder`.

## Фаза 6. Агент-инструменты (без MCP)

1. Перенести `VibeCoder.Core.AgentTools` + `AgentToolFactory`; VS-специфичные инструменты
   заменить адаптерами студии.
2. **Категорию `Mcp` и `AgentPermissions.Mcp` отключить/удалить** — новый чат с MCP
   не связываем.

## Фаза 7. Интеграция чата в главное окно

- Добавить чат как докируемую панель AvalonDock в `MainView` (вместо удалённого
  `_aiAgentChatControl`), с сохранением layout.

## Фаза 8. Документация и интеграция с режимом «Plan» чата

1. Создать встроенную пользовательскую документацию студии (RU/EN) в виде набора
   Markdown-файлов: дизайнер workflow, активности, управление проектом, настройки,
   MCP-сервер, NuGet-менеджер.
2. Перенести/подключить `VibeCoder.Core.Services.HelpDocumentation` + `PromptLibrary`/
   `PromptPaths` и индекс документации (как в режиме Help у VibeCoder).
3. Интегрировать документацию с режимом **Plan** чата: обогащать системный промпт
   режима Plan справочной документацией и давать агенту инструмент поиска/чтения по
   встроенной документации (ответы «как работает студия», без изменяющих действий).
4. Покрыть тестами индексацию и поиск по документации (TDD).

## Фаза 9. Менеджер NuGet пакетов

1. Новый модуль `RPA.NuGet` (MahApps.Metro): поиск/просмотр/установка/обновление/удаление
   пакетов для текущего проекта.
2. `NuGet.Client` (`NuGet.Protocol`, `NuGet.Packaging`, `NuGet.Resolver`) для nuget.org и
   правки `packages.config`/`.csproj`.
3. Пункт меню/тулбар + докируемая панель.

## Фаза 10. Тесты и верификация (TDD)

- Юнит-тесты: Core-сервисы, парсеры, документация (индексация/поиск),
  NuGet-резолвер/конфиг, инструменты агента.
- Финальная сборка MSBuild + `vstest.console.exe` (по AGENTS.md).

---

## Риски

- Глубокая VS-зависимость VibeCoder (EnvDTE/VS Shell) — заменяется адаптерами студии.
- Нативные зависимости (Vosk/WebView2) — нужна доставка нативных DLL в output.
- Большой объём кода (~3000 строк code-behind чата) — перенос разбит на фазы с
  отдельной сборкой каждой.
