# План перевода чат-агента на общий репозиторий AgentShared (только NuGet-пакеты ядра)

## 1. Цель

Заменить локальный форк ядра агента (`C:\Users\Maxxx\Desktop\AI-RPA\Agent.Core`,
собирается как `Agent.Rpa`, пространства имён `Agent.Rpa.*`) на готовые NuGet-пакеты из
`C:\Users\Maxxx\source\repos\AgentShared`:

- `AgentToolkit` — ядро без UI, `netstandard2.0`;
- `AgentToolkit.Wpf` — WPF-контролы чата, `net472`.

Доменные инструменты построения роботов **остаются в AI-RPA**: они зависят от студийных
сервисов (каталог активностей WF, конструктор, MSBuild-фидбек) и в AgentShared не входят.

## 2. Жёсткие ограничения

1. Использовать **только NuGet-пакеты ядра AgentShared** (`AgentToolkit`, `AgentToolkit.Wpf`) версии **1.10.0**.
2. **Никаких** `ProjectReference` на `..\source\repos\AgentShared\...` и никакого продолжения
   локального форка `Agent.Core` (сборки `Agent.Rpa`).
3. Пакеты берём **только из локального фида** `C:\Users\Maxxx\source\repos\AgentShared\feed` (не с nuget.org и не копированием исходников), версия — **1.10.0**.
4. **Проект (код, `.csproj`, `.sln`) не трогаем без отдельного явного запроса.** Этот документ —
   только план и не является разрешением на правки.

## 3. Факты о текущем состоянии

### 3.1. Что публикует AgentShared

Источник: `C:\Users\Maxxx\source\repos\AgentShared`.

| Пакет | TFM | Содержимое | Пространства имён |
|---|---|---|---|
| `AgentToolkit` | `netstandard2.0` | `Agent.Core.dll`, `Agent.Models.dll`, `Agent.Mvvm.dll` | `Agent.Core.*`, `Agent.Models`, `Agent.Mvvm` |
| `AgentToolkit.Wpf` | `net472` | `Agent.Toolkit.Wpf.dll` | `Agent.Toolkit.Wpf` (контролы чата) |

В `Directory.Build.props` сейчас задано `<Version>1.11.0</Version>`, но для миграции фиксируем локальные пакеты **1.10.0** из `feed`.

Доступные версии в `AgentShared\feed\`:
`1.7.1`, `1.7.2`, `1.8.0`, `1.9.0`, **`1.10.0`** (целевая для миграции), `1.11.0` (не используем).

Зависимости `AgentToolkit`: `Markdig 1.4.2`, `DiffPlex 1.9.0`, `Newtonsoft.Json 13.0.3`,
`DocumentFormat.OpenXml 3.0.2`, `NPOI 2.7.6`, `System.Reflection.MetadataLoadContext 8.0.0`.

Зависимости `AgentToolkit.Wpf`: `Markdig 1.4.2`, `Material.Icons 3.0.2`,
`Microsoft.Web.WebView2 1.0.4258.31`.

### 3.2. Что сейчас использует AI-RPA

- `nuget.config`: источник `local` = `nuget_local`, с маппингом `AgentToolkit`/`AgentToolkit.Wpf`
  на local; всё остальное — `nuget.org`.
- В `nuget_local` сейчас лежат только `AgentToolkit 1.8.0/1.9.0` и `AgentToolkit.Wpf 1.8.0/1.9.0`.
  Версии **1.10.0 там нет** — её нужно добавить из `AgentShared\feed\` (только 1.10.0).
- Локальный форк `Agent.Core\Agent.Core.csproj`: `RootNamespace`/`AssemblyName` = `Agent.Rpa`,
  ссылается на пакет `AgentToolkit 1.9.0`. Содержит:
  - форк общих `AgentTools`/`Services`/`Models`/`Contracts` с переименованием в `Agent.Rpa.*`;
  - студийные расширения, которых **нет** в upstream-ядре AgentShared:
    `Services\Documents\*` (читатели docx/xlsx/pdf/txt), `Services\DocumentationIndex.cs`,
    `Services\DocumentationViewer.cs`, `Services\MarkdownContent.cs`, `Services\MsBuildLocator.cs`,
    `Services\ThemeColorMath.cs`, `Services\WebProxyBuilder.cs`, `Models\StudioTheme.cs`,
    `Models\WorkflowWriteWay.cs`, `Contracts\BuildToolchainContracts.cs`,
    а также доработки AgentTools под WF (`AppendWorkflowGuidance`, нативное исполнение tool-call).
- Потребители локального форка (`Agent.Rpa`):
  - `AiRpaStudio\AiRpaStudio.csproj` — `ProjectReference` на `Agent.Core` + legacy
    `HintPath`-ссылки на `packages\AgentToolkit.1.9.0\lib\netstandard2.0\*.dll`;
  - `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` — `ProjectReference` на `Agent.Core`;
  - `Rpa.RobotPackaging\Rpa.RobotPackaging.csproj` — `ProjectReference` на `Agent.Core`
    (`RobotProjectBuild.cs` использует `Agent.Rpa.Services`);
  - `RPA.Chat\RPA.Chat.csproj` — **уже** ссылается только на пакеты `AgentToolkit 1.9.0` +
    `AgentToolkit.Wpf 1.9.0`, но исходники используют `using Agent.Rpa.*` (см. риск R1).
- `Agent.Rpa.*` используется примерно в 150 файлах: `RPA.Chat\**`, `AiRpaStudio\**`,
  `AiRpaStudio.Tests\**`, `Rpa.RobotPackaging\RobotProjectBuild.cs`.

### 3.3. Доменные RPA/WF-инструменты (остаются в AI-RPA)

Это не инфраструктура чата, а сами «инструменты построения роботов». Их не переносим в AgentShared:

- `RPA.Chat\Services\AgentTools\AgentToolFactory.cs` — реестр инструментов чата студии:
  `activity_search`, `activity_describe`, `workflow_outline`, `workflow_validate`, `activity_create`,
  `editor` (с нормализацией/валидацией XAML), `build`, `run_commands`, `read_files`, `list_files`,
  `search_codebase`, `git_status`, `git_diff`, `git_commit`, `read_errors`, `read_output`,
  `search_docs`, `read_docs`, `ask_question`, `submit_and_exit`, `spawn_agent`.
- `RPA.Chat\Services\AgentTools\SpecToolHandlers.cs` — 12 инструментов `spec_*`
  (`spec_import`, `spec_show`, `spec_extract`, `spec_update`, `spec_decompose`, `spec_link`,
  `spec_coverage`, `spec_generate`, `spec_ask`, `spec_answer`, `spec_testcases`, `spec_run_tests`).
- `AiRpaStudio\Mcp\McpToolRegistry.cs` + `AiRpaStudio\Mcp\Tools\*` — MCP-инструменты `xaml.*`:
  `xaml.create_workflow`, `xaml.add_activity`, `xaml.set_argument`, `xaml.get_workflow`,
  `xaml.delete_activity`, `xaml.move_activity`, `xaml.wrap_activity`, `xaml.undo`, `xaml.redo`,
  `xaml.add_variable`, `xaml.remove_variable`, `xaml.create_workflow_from_code`,
  `xaml.create_workflow_from_json`, `xaml.describe_contract`.

## 4. Целевое состояние

1. Все проекты AI-RPA, которым нужно ядро агента, ссылаются только на пакеты
   `AgentToolkit 1.10.0` и `AgentToolkit.Wpf 1.10.0` (через `PackageReference`).
2. Локальный проект `Agent.Core` (сборка `Agent.Rpa`) и пространства имён `Agent.Rpa.*`
   удалены из решения; `ProjectReference` на него сняты.
3. Студийный код, которого нет в upstream-ядре, перенесён из форка в принадлежащие студии
   проекты (см. Фазу 2) и живёт там под своими именами (не `Agent.Rpa.*`).
4. Доменные RPA/WF-инструменты из раздела 3.3 работают без изменений логики — меняются
   только их зависимости на ядро (`Agent.Core.*` вместо `Agent.Rpa.*`).

## 5. Ключевые риски

- **R1. Пространства имён.** `Agent.Rpa.*` ≠ `Agent.Core.*`. Механическая миграция
  `using Agent.Rpa.X → using Agent.Core.X` затрагивает ~150 файлов и часть типов не имеет
  прямого аналога в upstream (студийные расширения). Это самый объёмный шаг.
- **R2. Студийные расширения в форке.** `Documents\*`, `DocumentationIndex`, `MsBuildLocator`,
  `WebProxyBuilder`, `ThemeColorMath`, `StudioTheme`, `WorkflowWriteWay`, `BuildToolchainContracts`
  и WF-доработки AgentTools не существуют в `Agent.Core` upstream. Им нужен новый «дом»
  внутри AI-RPA, иначе функциональность потеряется.
- **R3. Пакет `Material.Icons`.** `AgentToolkit.Wpf` тянет `Material.Icons 3.0.2`, а
  `RPA.Chat` использует `Material.Icons.WPF 2.4.1`. Нужно проверить, что это не конфликтует
  (разные ID пакетов, но схожая область имён иконок).
- **R4. Разные способы ссылок.** `AiRpaStudio` использует legacy `HintPath`-ссылки на DLL
  из `packages\`, `RPA.Chat` — `PackageReference`, а `Rpa.RobotPackaging` — `ProjectReference`.
  Приведение к единому `PackageReference 1.10.0` надо делать аккуратно, чтобы не осталось
  «висячих» DLL из старого `packages\`.
- **R5. Совместимость TFM.** `AgentToolkit` = `netstandard2.0`, `AgentToolkit.Wpf` = `net472`.
  Потребители: `net48` (студия/чат) и `net472`/`netstandard2.0` (робот-упаковщик) — совместимо,
  но проверить на живом решении (см. правило `CP-10` в `AGENTS.md`).
- **R6. `Rpa.RobotPackaging`.** Он ссылается на форк только ради `Agent.Rpa.Services`
  (в `RobotProjectBuild.cs`). Нужно определить, что именно используется, и заменить на
  `Agent.Core.Services` (пакет) либо на локальный код упаковщика.

## 6. План работ по фазам

### Фаза 0 — Подготовка фида и базовый прогон (без правок кода)

1. Снять контрольную точку: полная сборка решения и полный прогон тестов по регламенту
   `AGENTS.md` (`MSBuild AiRpa.sln /p:Configuration=Release`, затем `vstest.console.exe`).
   Зафиксировать число тестов (ориентир — 2105).
2. Скопировать из локального фида `C:\Users\Maxxx\source\repos\AgentShared\feed` в `nuget_local`
   только два пакета версии **1.10.0**: `AgentToolkit.1.10.0.nupkg` и `AgentToolkit.Wpf.1.10.0.nupkg`.
3. Проверить, что `nuget.config` продолжает резолвить `AgentToolkit*` из `local`
   (маппинг уже настроен; менять его не требуется).

### Фаза 1 — Инвентаризация и дифф форка (без правок кода)

1. Построить точный список типов в `Agent.Rpa.*` (форк) и сопоставить с типами
   `Agent.Core.*` из пакета `AgentToolkit 1.10.0`.
2. Разделить на три группы:
   - **A. Есть в upstream** → просто сменить `using` (механическая замена).
   - **B. Студийные расширения** → переносятся в студийные проекты (Фаза 2).
   - **C. Доработанные общие типы** (например, WF-части в AgentTools) → решить: заапстримить
     в AgentShared или оставить локальной обёрткой в студии.
3. Зафиксировать результат в таблице `тип → новое место/имя` (отдельный файл-реестр миграции).

### Фаза 2 — Перенос студийного кода из форка в проекты AI-RPA

1. Определить целевые проекты-владельцы (кандидаты: `RPA.Chat` и/или `AiRpaStudio`; при
   необходимости — новый студийный проект без ссылки на форк).
2. Перенести `Services\Documents\*`, `DocumentationIndex`, `DocumentationViewer`,
   `MarkdownContent`, `MsBuildLocator`, `ThemeColorMath`, `WebProxyBuilder`, `StudioTheme`,
   `WorkflowWriteWay`, `BuildToolchainContracts` и студийные WF-доработки AgentTools.
3. Переименовать их из `Agent.Rpa.*` в собственные пространства имён студии
   (например, `RPA.Chat.Services.*` / `RPA.Studio.*`), чтобы не путать с ядром.
4. Покрыть перенос тестами (TDD-правило из `AGENTS.md`): функциональность не должна
   измениться.

### Фаза 3 — Миграция пространств имён `Agent.Rpa.* → Agent.Core.*`

1. По реестру из Фазы 1 выполнить замену `using Agent.Rpa.*` на соответствующие
   `using Agent.Core.*` / `using Agent.Models` / `using Agent.Mvvm` во всех потребителях.
2. Студийные типы (группа B) переключить на их новые имена из Фазы 2.
3. Собирать итеративно по проектам: сначала `RPA.Chat`, затем `AiRpaStudio`, затем
   `AiRpaStudio.Tests`, затем `Rpa.RobotPackaging`.

### Фаза 4 — Переключение ссылок на пакеты 1.10.0

1. `RPA.Chat\RPA.Chat.csproj`: поднять `AgentToolkit` и `AgentToolkit.Wpf` с `1.9.0` до `1.10.0`.
2. `AiRpaStudio\AiRpaStudio.csproj`: убрать legacy `HintPath`-ссылки на
   `packages\AgentToolkit.1.9.0\lib\...` и `ProjectReference` на `Agent.Core`; заменить на
   `PackageReference` `AgentToolkit 1.10.0` (+ `AgentToolkit.Wpf 1.10.0`, если используется).
3. `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj`: снять `ProjectReference` на `Agent.Core`,
   оставить/добавить `PackageReference AgentToolkit 1.10.0`.
4. `Rpa.RobotPackaging\Rpa.RobotPackaging.csproj`: снять `ProjectReference` на `Agent.Core`,
   заменить на `PackageReference AgentToolkit 1.10.0` (или на локальный код после Фазы 2/3).

### Фаза 5 — Удаление локального форка

1. Удалить проект `Agent.Core` из `AiRpa.sln`.
2. Убедиться, что ни один `.csproj` больше не ссылается на `Agent.Core\Agent.Core.csproj`
   и что в коде не осталось `Agent.Rpa.*`.
3. Удалить каталог `Agent.Core\` (по отдельному разрешению, после зелёной сборки и тестов).

### Фаза 6 — Проверка и приёмка

1. `MSBuild AiRpa.sln /p:Configuration=Release` из VS 2026 **и** VS 2022 → 0 ошибок.
2. Полный прогон `vstest.console.exe AiRpaStudio.Tests\bin\...\AiRpaStudio.Tests.dll` → все
   тесты зелёные (не меньше контрольной точки из Фазы 0).
3. Смоук-проверка чата в студии: `activity_search`, `workflow_outline`, `workflow_validate`,
   `editor` на `.xaml`, `spec_*`, а также MCP `xaml.*` — поведение не изменилось.
4. Убедиться, что в выходных каталогах больше нет `Agent.Rpa.dll` и есть только
   `Agent.Core.dll`, `Agent.Models.dll`, `Agent.Mvvm.dll`, `Agent.Toolkit.Wpf.dll` из пакетов.

## 7. Файлы/проекты, которые потребуют правок (для последующего утверждения)

Только после явного разрешения:

- `nuget.config` — при необходимости (сейчас менять не требуется).
- `nuget_local\` — добавить `AgentToolkit.1.10.0.nupkg`, `AgentToolkit.Wpf.1.10.0.nupkg` (из `C:\Users\Maxxx\source\repos\AgentShared\feed`).
- `RPA.Chat\RPA.Chat.csproj` — версии пакетов.
- `RPA.Chat\**\*.cs` — `using Agent.Rpa.* → Agent.Core.*` / новые студийные пространства имён.
- `AiRpaStudio\AiRpaStudio.csproj` — снять `HintPath`-ссылки и `ProjectReference`, добавить пакеты.
- `AiRpaStudio\**\*.cs` — миграция `using`.
- `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` и `AiRpaStudio.Tests\**\*.cs` — то же.
- `Rpa.RobotPackaging\Rpa.RobotPackaging.csproj` и `RobotProjectBuild.cs` — то же.
- `AiRpa.sln` — удалить проект `Agent.Core`.
- Удаление `Agent.Core\` — отдельным разрешением после зелёного прогона.

## 8. Открытые вопросы (нужно решение до старта Фазы 2/3)

1. Куда класть студийные расширения форка: в `RPA.Chat`, в `AiRpaStudio` или в новый
   отдельный проект (`RPA.Chat.Infrastructure` / `Agent.Studio`)?
2. Доработки AgentTools под WF (WF-гайденс, нативное исполнение) — апстримить в AgentShared
   или оставить локальной обёрткой в студии?
3. `Material.Icons 3.0.2` (из `AgentToolkit.Wpf`) vs `Material.Icons.WPF 2.4.1` (в `RPA.Chat`) —
   оставить оба или унифицировать?
4. Разрешить ли удаление каталога `Agent.Core\` сразу после миграции или оставить на период
   стабилизации (не ссылаясь на него)?

## 9. Критерии готовности

- В решении нет `ProjectReference` на `Agent.Core` и нет кода с `Agent.Rpa.*`.
- Единственные источники ядра агента — пакеты `AgentToolkit 1.10.0` и `AgentToolkit.Wpf 1.10.0`.
- Сборка решения: 0 ошибок (VS 2026 и VS 2022).
- Полный прогон тестов зелёный; число тестов не меньше контрольной точки.
- Доменные RPA/WF-инструменты (чат и MCP `xaml.*`) работают как раньше.
