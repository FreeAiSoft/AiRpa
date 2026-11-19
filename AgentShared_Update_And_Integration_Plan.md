# План: обновление пакетов AgentShared и доинтеграция в AI-RPA

> Документ-план. Код и `.csproj`/`.sln`/`nuget.config` по нему **не правим** — только
> фиксируем, что и в каком порядке делать. Актуализирует и расширяет
> `AgentShared_Migration_Plan.md` (тот целился в 1.10.0; теперь целевая версия — **1.17.0**).

## 0. Резюме

1. **Обновить пакеты** `AgentToolkit` / `AgentToolkit.Wpf` с **1.9.0** до **1.17.0**
   (каноническая версия из `Directory.Build.props` AgentShared).
2. **Доинтегрировать** то, что ещё не завершено: закончить миграцию локального форка
   `Agent.Core` (сборка `Agent.Rpa`) на пакеты AgentShared, и (по решению владельца)
   перенять новые общие возможности AgentShared — тему Dark/Light, каталог AI-провайдеров,
   общие UI-контролы (`ChatControl`/`SettingsControl`/`IChatHost`).

---

## 1. Текущее состояние (факты, снято 19.11.2026)

### 1.1. Версии пакетов

| Проект | Способ ссылки | Текущая версия | Целевая |
|---|---|---|---|
| `Agent.Core\Agent.Core.csproj` (локальный форк, сборка `Agent.Rpa`) | `PackageReference` | `AgentToolkit 1.9.0` | будет удалён (Часть B) |
| `RPA.Chat\RPA.Chat.csproj` | `PackageReference` | `AgentToolkit 1.9.0` + `AgentToolkit.Wpf 1.9.0` | **1.17.0** |
| `AiRpaStudio\AiRpaStudio.csproj` | `ProjectReference` на форк (нет прямого PackageReference на AgentToolkit) | транзитивно 1.9.0 | `PackageReference 1.17.0` после Части B |
| `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` | `ProjectReference` на форк | транзитивно 1.9.0 | `PackageReference 1.17.0` после Части B |
| `Rpa.RobotPackaging\Rpa.RobotPackaging.csproj` | `ProjectReference` на форк | транзитивно 1.9.0 | `PackageReference 1.17.0` (или локальный код, Часть B) |

Доступные версии:
- `C:\Users\Maxxx\source\repos\AgentShared\feed\` — есть **1.15.0 … 1.17.0** (и автоинкременты `1.17.0.1/.2/.3`). Каноника — **1.17.0**.
- `AI-RPA\nuget_local\` — только `AgentToolkit 1.8.0/1.9.0/1.10.0` и `AgentToolkit.Wpf 1.8.0/1.9.0/1.10.0`. **1.11.0+ отсутствуют.**

### 1.2. Источники пакетов (проблема в `nuget.config`)

Сейчас в `nuget.config` три источника:

```
local         -> nuget_local            (mapping: AgentToolkit, AgentToolkit.Wpf)
nuget.org     -> api.nuget.org          (mapping: *)
AgentShared\feed -> C:\...\AgentShared\feed   (mapping: НЕТ)
```

- Источник `AgentShared\feed` **добавлен, но не замаплен** в `packageSourceMapping`,
  поэтому фактически не участвует в резолве (для `AgentToolkit` сработает только `local`).
- Значит, чтобы взять 1.17.0, нужно либо **скопировать два `.nupkg` 1.17.0 в `nuget_local`**,
  либо **добавить `AgentShared\feed` в `packageSourceMapping`** для паттернов `AgentToolkit`/`AgentToolkit.Wpf`.
- В `packages\` (legacy) лежат каталоги `AgentToolkit.1.8.0`, `AgentToolkit.1.9.0`, `AgentToolkit.Wpf.1.8.0`.

### 1.3. Локальный форк `Agent.Core` (сборка `Agent.Rpa`)

`Agent.Core\Agent.Core.csproj`: `netstandard2.0`, `RootNamespace`/`AssemblyName` = **`Agent.Rpa`**.

Состав (студийные расширения, которых **нет** в upstream `Agent.Core`):
- `AgentTools\` — форк `AgentPromptBuilder`, `AgentRunner`, `AgentTool`, `AgentToolRegistry`, `IAgentTool`, `ToolSchemaConverter` (в т.ч. WF-доработки).
- `Services\Documents\*` — `DocumentText`, `OpenXmlExcelReader`, `OpenXmlWordReader`, `PdfTextReader`, `PlainTextReader`.
- `Services\` — `DocumentationIndex`, `DocumentationViewer`, `MarkdownContent`, `MsBuildLocator`, `PromptDefaults`, `PromptPaths`, `ThemeColorMath`, `WebProxyBuilder`.
- `Models\` — `StudioTheme`, `WorkflowWriteWay`.
- `Contracts\` — `BuildToolchainContracts`.

### 1.4. Потребители пространства имён `Agent.Rpa.*`

| Проект | Файлов с `using Agent.Rpa.*` |
|---|---|
| `Agent.Core` (сам форк) | 6 |
| `RPA.Chat` | 12 |
| `AiRpaStudio` | 14 |
| `AiRpaStudio.Tests` | 62 |
| `Rpa.RobotPackaging` | 1 (`RobotProjectBuild.cs`) |

`ProjectReference` на `Agent.Core\Agent.Core.csproj` остаются в: `AiRpaStudio`, `AiRpaStudio.Tests`, `Rpa.RobotPackaging`; проект `Agent.Core` всё ещё в `AiRpa.sln`.

### 1.5. Что уже частично интегрировано

`RPA.Chat\ViewModels\ChatViewModel.cs` уже одновременно использует:
- пакетные пространства имён: `Agent.Core.Context`, `Agent.Core.Contracts`, `Agent.Core.Models`, `Agent.Core.Services`, `Agent.Models` (в т.ч. `ContextUsageBinder`), `Agent.Toolkit.Wpf.Markdown`;
- форковые: `Agent.Rpa.AgentTools`, `Agent.Rpa.Services`, `Agent.Rpa.Services.Documents`, `Agent.Rpa.Models`.

`Agent.Mvvm` широко используется в `AiRpaStudio` (ViewModels/Views) — это пакетные типы.

### 1.6. Что НЕ интегрировано (сравнение с поверхностью AgentShared 1.17.0)

| Возможность AgentShared | В AI-RPA | Комментарий |
|---|---|---|
| `SettingsControl` (общие настройки) | 0 вхождений | настройки свои |
| `AgentThemeManager` + `Theme.Dark/Light.xaml` (переключение темы) | 0 вхождений | своя тема `ChatTheme.Dark/Light` + `StudioTheme.BaseTheme` |
| `IChatHost` / общий `ChatControl` | 0 в проде (только страж-тест) | свой `RPA.Chat\Views\ChatPanel.xaml` |
| Каталог AI-провайдеров (`AiProviderCatalog`, `ProviderId/ProviderIndex`) | 0 вхождений | — |
| `ThemeIndex` в `SettingsModel` | 0 вхождений | — |
| `ContextIndicator`, детекция потери prompt-кэша | не используется | — |

### 1.7. Несогласованность (важно)

- Страж `AiRpaStudio.Tests\AgentCoreRenameGuardTests.cs` ожидает `<AssemblyName>Agent.Core</AssemblyName>` / `<RootNamespace>Agent.Core</RootNamespace>`, а в `Agent.Core.csproj` сейчас `Agent.Rpa`. Тест **устарел/красный**.
- Тот же страж читает `RPA.Chat\Controls\ChatMessagesControl.xaml`, которого **нет** по этому пути (контрол живёт в пакете `AgentToolkit.Wpf`). Файл-путь устарел.
- `Agent.Core\AGENTS.md` всё ещё описывает проект как `Agent.Core` (не `Agent.Rpa`) — документ не синхронизирован с фактом.

---

## 2. Часть A — Обновление пакетов (1.9.0 → 1.17.0)

Зависимости пакетов между 1.9.0 и 1.17.0 **не изменились** (снято из `.nuspec` 1.17.0):
- `AgentToolkit`: `DiffPlex 1.9.0`, `DocumentFormat.OpenXml 3.0.2`, `Markdig 1.4.2`, `NPOI 2.7.6`, `Newtonsoft.Json 13.0.3`, `System.Reflection.MetadataLoadContext 8.0.0`.
- `AgentToolkit.Wpf`: `Markdig 1.4.2`, `Material.Icons 3.0.2`, `Microsoft.Web.WebView2 1.0.4258.31`.

Поэтому апдейт — в основном «поднять версию + перепроверить», без каскада новых зависимостей.

### A1. Подготовить фид
1. Скопировать `AgentToolkit.1.17.0.nupkg` и `AgentToolkit.Wpf.1.17.0.nupkg` из
   `C:\Users\Maxxx\source\repos\AgentShared\feed` в `AI-RPA\nuget_local\`
   (либо добавить источник `AgentShared\feed` в `packageSourceMapping` — решение владельца).
2. Убедиться, что `nuget.config` резолвит `AgentToolkit*` из нужного источника.

### A2. Поднять версии
1. `RPA.Chat\RPA.Chat.csproj`: `AgentToolkit` 1.9.0 → **1.17.0**, `AgentToolkit.Wpf` 1.9.0 → **1.17.0**.
2. `Agent.Core\Agent.Core.csproj` (форк): `AgentToolkit` 1.9.0 → **1.17.0** — как промежуточный шаг, если Часть B идёт не в этом же заходе.
3. Прочие проекты до Части B получают пакет транзитивно через `ProjectReference` на форк.

### A3. Убрать legacy-артефакты
1. Удалить/не использовать `packages\AgentToolkit.1.8.0`, `packages\AgentToolkit.1.9.0`, `packages\AgentToolkit.Wpf.1.8.0` (после перехода на `PackageReference 1.17.0` и зелёной сборки).

### A4. Проверка
- `MSBuild AiRpa.sln /p:Configuration=Release` (VS 2026 **и** VS 2022) → 0 ошибок.
- Полный прогон тестов (MSBuild + `vstest.console.exe`, см. `AGENTS.md`) → зелёные, число тестов не меньше контрольной точки.

---

## 3. Часть B — Доинтеграция: завершение миграции форка `Agent.Rpa`

> Прямое продолжение `AgentShared_Migration_Plan.md`, но с целевой версией **1.17.0** вместо 1.10.0.

### B1. Инвентаризация и дифф
1. Построить реестр `тип Agent.Rpa.* → новое место` и разбить на группы:
   - **A. Есть в upstream `Agent.Core.*`** → смена `using` (механическая).
   - **B. Студийные расширения** (`Documents\*`, `DocumentationIndex`, `DocumentationViewer`, `MarkdownContent`, `MsBuildLocator`, `ThemeColorMath`, `WebProxyBuilder`, `StudioTheme`, `WorkflowWriteWay`, `BuildToolchainContracts`, `PromptDefaults`, `PromptPaths`) → перенести в принадлежащие студии проекты под свои имена (`RPA.Chat.*` / `RPA.Studio.*`).
   - **C. Доработанные общие типы** (WF-части `AgentTools`) → решить: заапстримить в AgentShared или оставить локальной обёрткой.
2. Зафиксировать реестр отдельным файлом.

### B2. Перенос студийных расширений
1. Определить проекты-владельцы (кандидаты: `RPA.Chat` и/или `AiRpaStudio`, при необходимости — новый студийный проект без ссылки на форк).
2. Перенести код группы B, переименовать в собственные пространства имён.
3. Покрыть перенос тестами (TDD-правило: функциональность не должна измениться).

### B3. Миграция `using Agent.Rpa.* → Agent.Core.*`
1. По реестру заменить `using` во всех потребителях (~95 файлов: `RPA.Chat` 12, `AiRpaStudio` 14, `AiRpaStudio.Tests` 62, `Rpa.RobotPackaging` 1).
2. Собирать итеративно: `RPA.Chat` → `AiRpaStudio` → `AiRpaStudio.Tests` → `Rpa.RobotPackaging`.

### B4. Переключение ссылок
1. `AiRpaStudio\AiRpaStudio.csproj`: снять `ProjectReference` на `Agent.Core`, добавить `PackageReference AgentToolkit 1.17.0` (+ `AgentToolkit.Wpf`, если нужно напрямую).
2. `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj`: снять `ProjectReference` на `Agent.Core`, добавить `PackageReference AgentToolkit 1.17.0`.
3. `Rpa.RobotPackaging\Rpa.RobotPackaging.csproj`: снять `ProjectReference` на `Agent.Core`, заменить на `AgentToolkit 1.17.0` либо на локальный код после B1/B2.

### B5. Удаление форка
1. Удалить проект `Agent.Core` из `AiRpa.sln`.
2. Убедиться, что ни один `.csproj` не ссылается на `Agent.Core\Agent.Core.csproj` и в коде нет `Agent.Rpa.*`.
3. Удалить каталог `Agent.Core\` (отдельным разрешением после зелёной сборки/тестов).

### B6. Починить несогласованность
1. Обновить/удалить `AgentCoreRenameGuardTests.cs` (ожидания `Agent.Core` → факт миграции; путь `ChatMessagesControl.xaml`).
2. Синхронизировать `Agent.Core\AGENTS.md` (или удалить вместе с каталогом) и корневой `AGENTS.md` (индекс ссылается на `Agent.Core\AGENTS.md`).
3. Проверить, что в выходных каталогах нет `Agent.Rpa.dll`, а есть только `Agent.Core.dll`, `Agent.Models.dll`, `Agent.Mvvm.dll`, `Agent.Toolkit.Wpf.dll` из пакетов.

---

## 4. Часть C — Интеграция новых возможностей AgentShared (кандидаты, требуют решения)

Эти пункты — отдельные карточки; «интегрировать всё подряд» не обязательно.

| # | Возможность | Что даёт | Замечания/риски |
|---|---|---|---|
| C1 | Тема Dark/Light (`AgentThemeManager`, `Theme.Dark/Light.xaml`, `SettingsModel.ThemeIndex/BaseTheme`) | Единый механизм переключения темы на лету | **Решено:** оставить свою тему (`ChatTheme.Dark/Light`, `StudioTheme.BaseTheme`). Тема AgentToolkit остаётся самоуправляемой (переключатель в `SettingsControl`); переключение темы из приложения-хоста — отдельная будущая карточка. |
| C2 | Каталог AI-провайдеров (`AiProviderCatalog`, `ProviderId/ProviderIndex`) | Единый реестр провайдеров/моделей | Проверить, как студия сейчас конфигурирует провайдеров. |
| C3 | Общий `ChatControl` / `SettingsControl` / `IChatHost` | Переиспользование общего UI чата/настроек | Сейчас свой `RPA.Chat\Views\ChatPanel.xaml`. Переход большой (host-контракт, слоты). |
| C4 | `ContextIndicator`, детекция потери prompt-кэша | Индикация контекста и предупреждение о потере кэша | Малая связность, можно точечно. |
| C5 | Новые MCP-транспорты (`Sse`/`StreamableHttp`) | Расширение MCP-подключений | В студии свой `AiRpaStudio\Mcp` для `xaml.*`. Оценить пересечение. |

---

### Решения по Части C (снято 19.11.2026)

- **C1 — решено:** оставить свою тему (`ChatTheme.Dark/Light`, `StudioTheme.BaseTheme`); тему AgentToolkit не перенимаем. Тема AgentToolkit остаётся самоуправляемой — переключатель внутри настроек AgentToolkit (`SettingsControl`), приложение-хост тему пока не переключает. Переключение темы из приложения-хоста — отдельная будущая карточка.

---

## 5. Риски

- **R1. Пространства имён.** `Agent.Rpa.* ≠ Agent.Core.*`; ~95 файлов, часть типов не имеет прямого аналога в upstream (студийные расширения) — самый объёмный шаг.
- **R2. Студийные расширения без «дома».** `Documents\*`, `DocumentationIndex`, `MsBuildLocator`, `WebProxyBuilder`, `ThemeColorMath`, `StudioTheme`, `WorkflowWriteWay`, `BuildToolchainContracts` должны получить новый дом внутри AI-RPA, иначе функциональность потеряется.
- **R3. `Material.Icons 3.0.2` (из `AgentToolkit.Wpf`) vs `Material.Icons.WPF 2.4.1` (в `RPA.Chat`/`AiRpaStudio`).** Разные ID пакетов, но схожая область имён иконок — проверить отсутствие конфликтов.
- **R4. Разные способы ссылок.** `RPA.Chat` — `PackageReference`, `AiRpaStudio`/`Rpa.RobotPackaging` — `ProjectReference` на форк. Приведение к единому `PackageReference 1.17.0` — аккуратно, чтобы не осталось «висячих» DLL из `packages\`.
- **R5. TFM.** `AgentToolkit` = `netstandard2.0`, `AgentToolkit.Wpf` = `net472`; потребители `net48`/`net472`/`netstandard2.0` — совместимо (правило `CP-10`), проверить на живом решении.
- **R6. `Rpa.RobotPackaging`** ссылается на форк ради `Agent.Rpa.Services` в `RobotProjectBuild.cs` — определить, что именно используется, и заменить на пакетный `Agent.Core.Services` либо локальный код.
- **R7. Устаревший страж.** `AgentCoreRenameGuardTests` сейчас красный из-за несоответствия `Agent.Rpa`/`Agent.Core` — до старта B зафиксировать это как известное состояние.
- **R8. `nuget.config`.** `AgentShared\feed` не замаплен — без правки фида 1.17.0 не зарезолвится.

---

## 6. Порядок работ (рекомендуемый)

1. **Фаза 0** — контрольная точка: полная сборка + прогон тестов по регламенту `AGENTS.md`, зафиксировать число тестов и текущие красные (в т.ч. `AgentCoreRenameGuardTests`).
2. **Фаза A** — фид + поднятие версий до 1.17.0 + сборка/тесты (самостоятельный, малый риск).
3. **Фаза B1–B2** — реестр типов + перенос студийных расширений (с тестами).
4. **Фаза B3–B4** — миграция `using` + переключение ссылок на `PackageReference`.
5. **Фаза B5–B6** — удаление форка, починка стража/документации.
6. **Фаза C** — отдельными карточками по решению владельца.

---

## 7. Критерии готовности

- Все потребители ссылаются на `AgentToolkit 1.17.0` / `AgentToolkit.Wpf 1.17.0` (где применимо) через `PackageReference`; в решении нет `ProjectReference` на `Agent.Core` и нет кода `Agent.Rpa.*`.
- `nuget.config`/фид даёт резолв 1.17.0.
- Сборка решения: 0 ошибок (VS 2026 и VS 2022).
- Полный прогон тестов зелёный; число тестов не меньше контрольной точки.
- Доменные RPA/WF-инструменты (чат, `spec_*`, MCP `xaml.*`) работают как раньше.

---

## 8. Открытые вопросы (нужны решения владельца до старта)

1. Целевая версия — подтвердить **1.17.0** (а не автоинкремент `1.17.0.x`).
2. Фид: копировать `.nupkg` в `nuget_local` или замапить `AgentShared\feed` в `nuget.config`?
3. Куда переносить студийные расширения форка: в `RPA.Chat`, в `AiRpaStudio` или в новый проект?
4. WF-доработки `AgentTools` — апстримить в AgentShared или оставить локальной обёрткой?
5. Перенимать ли общий `ChatControl`/`SettingsControl`/`IChatHost` (Часть C3) или оставить свой `ChatPanel`?
6. ~~Перенимать ли тему `AgentThemeManager` (C1) или оставить `ChatTheme`/`StudioTheme`?~~ — **решено (19.11.2026):** оставить свою тему; тема AgentToolkit остаётся самоуправляемой через `SettingsControl`; переключение темы из приложения-хоста — отдельная будущая карточка.
7. Перенимать ли каталог AI-провайдеров (C2)?
8. Разрешить ли удаление каталога `Agent.Core\` сразу после миграции или оставить на стабилизацию (не ссылаясь на него)?


