# Запуск вложенных XAML-процессов по собранной сборке, выбор стартового скрипта и точки останова во всех скриптах — план и журнал

| Поле | Значение |
|---|---|
| Документ | `dev-docs\LooseWorkflowRun_Plan.md` — план доделки прогона WF-проектов с вложенными процессами (`RPA-1`, `SubProcessN`), выбор стартового скрипта (Ф1) и отладка во всех скриптах процесса (Ф2) |
| Связанный план | `dev-docs\LooseXamlTypeService_Plan.md` — loose-открытие/сохранение в дизайнере (без сборки) |
| Цель | (1) прогон WF-проекта с вложенными процессами по собранной сборке (**вариант A**), дизайнер остаётся loose; (2) кнопка «Старт» запускает скрипт активной вкладки либо скрипт по умолчанию; (3) точки останова срабатывают во всех скриптах процесса, закрытый скрипт открывается на точке |
| Дата заведения | 14.11.2026 (вариант A); актуализировано 13.11.2026 (Ф1/Ф2) |
| Правило приёмки | каждый шаг закрывается тестом (включая негатив), полным зелёным прогоном `AiRpaStudio.Tests` и сборкой `AiRpa.sln` Release из VS 2022 и VS 2026; production-правки — только по TDD (red → green → refactor) |
| Reference source | `C:\Users\Maxxx\source\repos\.NET-Framework-4.8-RTM-Reference-Source` |

## 1. Контекст и причина

### 1.1. Прогон по сборке (вариант A) — уже реализовано

При запуске `RPA-1` раннер падал с:

```
System.InvalidOperationException: CacheMetadata для действия "RPA_1.SubProcess1" вызвало
System.Xaml.XamlObjectWriterException: Нельзя создать неизвестный тип "{clr-namespace:RPA_1}SubProcess2".
```

Причина — вложенные ссылки `<local:SubProcess2/>` в `RPA-1\SubProcess1.xaml`:

```xml
<Activity x:Class="RPA_1.SubProcess1" ... xmlns:local="clr-namespace:RPA_1">
  <Sequence>
    <local:SubProcess2 ... />
    <local:SubProcess3 ... />
  </Sequence>
</Activity>
```

`RPA_1.SubProcess2` — CLR-тип, который появляется только **после сборки** проекта
(`XamlAppDef` → `XamlBuildTask` → partial class → `RPA-1.dll`). Прогон грузил XAML **без сборки**
(`WorkflowRunEngine.LoadActivityFromText`), поэтому штатный `XamlSchemaContext` не находил тип и падал
на компиляции выражений.

Это подтверждается reference source: `ActivityXamlServices.Load(stream, settings)` жёстко использует
штатный `DynamicActivityReaderSchemaContext` (`ActivityXamlServices.cs:26,149,189,350`,
`FuncFactory.cs:88/91–94`), точки подмены schema context в этом API нет. Поэтому для прогона выбран
**вариант A** — исполнять собранную `RPA-1.dll`, как это делает `RpaRunner`; дизайнер остаётся loose.

### 1.2. Две новые проблемы

**П1 — «Старт» запускает не тот скрипт.**
`Main.CmdWorkflowRun()` → `CustomWfDesigner.WorkflowRun()` запускает
`ActiveWorkflowDesignerEx.CurrentWorkflowFile` — последнюю активную вкладку **дизайнера WF**, а не то,
что реально видит пользователь. Если активна вкладка кода/XAML-редактора (или не-WF файл), запускается
последний WF-файл — на практике почти всегда `Main.xaml`. Отдельной настройки «скрипт по умолчанию» нет.

**П2 — точки останова не срабатывают во вложенных процессах.**
Дизайнер открыт loose (фасады без тел), прогон идёт по собранной сборке (реальные тела).
`TrackHandler.GetSourceLocation()` ищет `SourceLocation` по `Activity.Id` только в
`DesignerSourceLocationMapping` верхнего дизайнера `Main.xaml`, поэтому тела `SubProcessN` не находятся —
в логе студии появляются `Not found SourceLocation for 1.5.1: Sequence`, `... for 1.5.3.1: Sequence` и т.д.,
а подсветка/точка останова внутри вложенных скриптов не работают.

## 2. Описание фич

### Ф1. Запуск скрипта из открытой вкладки + настройка «Стартовый скрипт»

**Поведение.**
- Если активная вкладка — открытый WF-скрипт (вкладка дизайнера) → «Старт» запускает **этот** скрипт.
- Если активная вкладка — не скрипт (код/XAML-редактор, `.cs`/`.json`/`.xml`, панель и т.п.) или вкладки
  нет → «Старт» запускает **скрипт по умолчанию**, заданный настройкой.
- Настройка `DefaultRunScript` по умолчанию равна `Main.xaml`. Значение может быть именем файла или
  относительным путём от каталога проекта.
- Фолбэк (если настройка не задана/пуста или файл не найден): `Main.xaml` → `main.xaml`
  (регистронезависимый поиск в каталоге проекта). Это сохранённое ожидаемое поведение.

**Правила выбора (чистый резолвер, без UI).**

`RunTargetResolver.Resolve(projectDir, configuredDefault, projectMainActivity, activeDocumentPath, getOpenWorkflowPath)`:

1. `activeDocumentPath` — путь активной вкладки (дизайнер или редактор кода).
2. Если активная вкладка — открытый WF-дизайнер (`getOpenWorkflowPath(activeDocumentPath)` вернул путь),
   возвращается её файл.
3. Иначе перебираются кандидаты без дублей и регистронезависимо:
   `configuredDefault` → `projectMainActivity` → `"Main.xaml"` → `"main.xaml"`.
   Путь: если кандидат уже корневой — как есть, иначе `Path.Combine(projectDir, candidate)`.
4. Возвращается первый существующий `.xaml`; если ничего не найдено — `null` (студия показывает понятное
   сообщение и не падает).

**Настройка и UI.**
- Новая настройка `DefaultRunScript` (тип `System.String`, `User`), значение по умолчанию `Main.xaml`:
  `Properties\Settings.settings`, `Properties\Settings.Designer.cs`, `App.config`.
- Рекомендуется пункт меню «Настройки» → «Стартовый скрипт…»: тематизированный диалог ввода имени файла
  (по образцу `StudioTitleBar`/`ErrorDialog`), сохраняющий `DefaultRunScript` и вызывающий `Save()`.

**Точки интеграции.**
- `CustomWfDesigner.WorkflowRun()` должен вызывать `RunTargetResolver.Resolve(...)` и запускать полученный
  `runPath` (а не `ActiveWorkflowDesignerEx.CurrentWorkflowFile`).
- `ActivityRunner.Start(pathToXaml)` уже умеет открыть/активировать нужную вкладку, если она ещё не открыта,
  поэтому отдельного «предварительного открытия» не требуется.
- `CustomWfDesigner.GetDesignerByFileIgnoreCase(path)` — поиск открытого дизайнера по `LoadedFile`
  без учёта регистра (нужен и для Ф1, и для Ф2).

### Ф2. Точки останова во всех скриптах по ходу процесса

**Поведение.**
- Подсветка текущей активности и точки останова срабатывают во **всех** скриптах процесса: `Main.xaml` и
  вложенные `SubProcessN`, включая их тела.
- Если скрипт уже открыт вкладкой — подсветка/останов идут на ней.
- Если скрипт **закрыт**, но содержит точки останова — вкладка открывается автоматически, и выполнение
  останавливается на точке.
- Если символ отладки отсутствует или маппинг построить не удалось — остаётся прежний фолбэк
  (маппинг верхнего дизайнера), то есть верхний уровень продолжает работать как раньше.

**Решение (по reference source).**
Скомпилированный XAML несёт `sads:DebugSymbol.Symbol` на **теле** x:Class-активности
(`<sads:DebugSymbol.Symbol>...</sads:DebugSymbol.Symbol>` внутри `Sequence`). Цепочка:

`DebugSymbol.GetSymbol(activity)` → `WorkflowSymbol.Decode(symbol)` →
`SourceLocationProvider.GetSourceLocations(activity, symbol, translateInternalActivityToOrigin: false)`.

Метод `SourceLocationProvider.GetSourceLocations(Activity, WorkflowSymbol, bool)` — internal static;
`translateInternalActivityToOrigin: false` обязателен для отладки, чтобы ключами словаря остались
рантайм-активности, а не их `Origin`. Символ лежит на теле x:Class-активности, поэтому для скомпилированного
XAML `symbolRoot = activity.Parent` (см. `SourceLocationProvider.GetSourceLocations(Activity)`:
`if (rootActivity.RootActivity != rootActivity) rootActivity = rootActivity.Parent;`).

**Важный нюанс нормализации.** `SourceLocation.FileName` хранится в `ToUpperInvariant()`, а
`SourceLocation.Equals` учитывает `Checksum`. Loose-дизайнер строит маппинг с `Checksum == null`, рантаймный
символ даёт `Checksum != null`. Перед сопоставлением с точками останова/дизайнером SourceLocation надо
нормализовать: `new SourceLocation(fileName, startLine, startColumn, endLine, endColumn)` (сбрасывает checksum).

**Компоненты.**
- `RuntimeSourceLocationMap.Build(Activity rootActivity)` — рекурсивно обходит
  `WorkflowInspectionServices.GetActivities` (корень + потомки, защита от циклов через `HashSet<Activity>`),
  для каждой активности читает `DebugSymbol`, декодирует и через reflection вызывает
  `SourceLocationProvider.GetSourceLocations(activity, symbol, translateInternalActivityToOrigin: false)`,
  сливает записи в `Dictionary<object, SourceLocation>`. Ошибки глушатся в пустой словарь + диагностика.
- `ActivityRunner` хранит `_runtimeSourceLocationMapping`, заполняет его после `ValidateForRun(activity)`
  и передаёт в `TrackHandler.HandleTrackingRecord(...)`.
- `TrackHandler.HandleTrackingRecord(...)` получает `runtimeMapping` и ищет SourceLocation сначала в нём,
  затем (фолбэк) в `workflowDesignerEx.DesignerSourceLocationMapping`.
- `TrackHandler.ShowDebug(...)`: по `SourceLocation.FileName` определяется целевой дизайнер —
  текущий → уже открытый (`GetDesignerByFileIgnoreCase`) → если закрыт, открыть через
  `LoadActivityToDesignerHelper.LoadActivityToDesigner(file, GetLooseRegistryFor(file))` и активировать.
  Затем нормализованный SourceLocation ставится в `DebugManagerView.CurrentLocation`, проверяется
  `BreakpointList` целевого дизайнера, при попадании — лог `breakpoint hit`, `CustomWfDesignerState = Breakpoint`,
  `resumeRuntimeFromHost.WaitOne()`.

**Границы.**
- Для `Activities.Flow.RunScript` каждый дочерний скрипт уже запускается отдельным `ActivityRunner` со своим
  дизайнером (`LaunchSubWorkflow`), поэтому «открыть закрытый скрипт и остановиться» там работает тем же путём.
- Отдельная карточка беклога: возобновление после точки останова внутри `RunScript`-субпроцесса — сейчас
  `CustomWfDesigner.WorkflowRun()` резюмирует только главный `_activityRunner` (см. §7).

## 3. Что уже сделано (вариант A, шаги 1–6)

`AiRpaStudio\Core\ActivityRunner.cs`:

- `Start` грузит активность через `LoadRunActivity(pathToXaml)`: WF-проект (`IsWorkflowProject` +
  `ProjectFilePath`) — **из собранной сборки** (`RobotLocator.TryResolve` →
  `WorkflowRunEngine.LoadActivityFromAssembly`); обычный робот — прежний XAML-путь
  (`WorkflowRunEngine.LoadActivityFromText`).
- `TryLoadOrBuildFromProject` — автосборка через `VsProjectBuilder` (MSBuild), если сборки нет или она устарела
  (`IsProjectOutdated`); сборка только в студии, в `RpaRunner` запрещена стражем.
- `ValidateForRun` — `ActivityValidationServices.Validate` до старта.
- Loose-реестр передаётся в дизайнер при запуске (`GetLooseRegistryFor`).
- Зависимости собранной сборки резолвятся `RobotAssemblyResolver.InstallForProject`.
- Страж `ActivityRunner_UsesSharedEngineForLoadingAndApplication` соблюдён.

Готово и покрыто тестами: шаги 1–6 (см. §6, журнал). Открытый хвост — шаг 7 «приёмка и документация»
(перенесён в общий план приёмки Ф1/Ф2).

## 4. План задач

| № | Задача | Состояние |
|---|---|---|
| **Ф1. Стартовый скрипт** | | |
| 1 | Добавить настройку `DefaultRunScript` (`Settings.settings` + `Settings.Designer.cs` + `App.config`), default `Main.xaml`. Тест `DefaultRunScriptSettingTests` (по образцу `MsBuildToolchainSettingTests`). | ✅ готово (14.11.2026) |
| 2 | Новый чистый резолвер `RunTargetResolver.Resolve(...)` без UI: активная вкладка-скрипт → настройка → `Main.xaml`/`main.xaml`, регистронезависимо, первый существующий `.xaml`, иначе `null`. | ✅ готово (14.11.2026) |
| 3 | `CustomWfDesigner.GetDesignerByFileIgnoreCase(path)` + `WorkflowRun()` использует резолвер (вместо `ActiveWorkflowDesignerEx.CurrentWorkflowFile`). | ✅ готово (14.11.2026) |
| 4 | UI: пункт «Настройки» → «Стартовый скрипт…» + тематизированный диалог (рекомендуется). | ✅ готово (14.11.2026) |
| 5 | Тесты `RunTargetResolverTests`: активная вкладка побеждает; вкладка-код/нет вкладки → настройка; фолбэк `Main.xaml`→`main.xaml`; регистр; несуществующий файл → `null`. | ✅ готово (14.11.2026) |
| **Ф2. Точки останова во всех скриптах** | | |
| 6 | `RuntimeSourceLocationMap.Build(activity)` — маппинг `Activity → SourceLocation` по `DebugSymbol` через reflection к `SourceLocationProvider.GetSourceLocations(activity, symbol, false)`. | ✅ готово (14.11.2026) |
| 7 | `ActivityRunner` заполняет `_runtimeSourceLocationMapping` после `ValidateForRun` и передаёт его в `TrackHandler`. | ✅ готово (14.11.2026) |
| 8 | `TrackHandler`: приоритет runtime-маппинга + фолбэк дизайнерского; нормализация SourceLocation (сброс checksum); выбор целевого дизайнера по `FileName`; `BreakpointList` целевого дизайнера; `resumeRuntimeFromHost.WaitOne()`. | ✅ готово (14.11.2026) |
| 9 | Открытие закрытого скрипта на точке: `LoadActivityToDesignerHelper.LoadActivityToDesigner(file, GetLooseRegistryFor(file))` + регистронезависимый поиск фактического пути. | ✅ готово (14.11.2026) |
| 10 | Тесты `RuntimeSourceLocationMapTests` (эталон с `Main.xaml` + `SubProcess1.xaml`, в т.ч. тела `SubProcess2/3`) и дополнение `TrackHandlerTests` (runtime-маппинг приоритетнее; пустой runtime → старый путь). | ✅ готово (14.11.2026) |
| **Приёмка** | | |
| 11 | Полный зелёный прогон `AiRpaStudio.Tests`; Release `AiRpa.sln` из VS 2022 и VS 2026 (0 ошибок). | ✅ готово (14.11.2026) |
| 12 | Ручная приёмка на `RPA-1`: (а) открыта вкладка `SubProcess2.xaml` → Старт запускает её; (б) открыта вкладка `.cs`/`.json` → Старт запускает `Main.xaml`; (в) точка в `SubProcess1.xaml`/`SubProcess2.xaml`, вкладка закрыта → Старт `Main.xaml` открывает скрипт и останавливается на точке. | ⏳ не начато |
| 13 | Документация: `AGENTS.md` (CP-08/OR-02), `ROADMAP.md` §3, `LooseXamlTypeService_Plan.md`. | ⏳ не начато |

## 5. Определения «готово»

- **Ф1.** Кнопка «Старт» запускает скрипт активной WF-вкладки; при не-WF вкладке — скрипт из `DefaultRunScript`
  (default `Main.xaml`, фолбэк `Main.xaml`/`main.xaml`). Настройка читается/пишется и хранится в трёх местах
  (`Settings.settings`, `Settings.Designer.cs`, `App.config`) согласованно. Резолвер покрыт тестами, включая
  негатив «ничего не найдено».
- **Ф2.** Для эталона `RPA-1` подсветка и точки останова работают внутри `SubProcessN` (тела вложенных
  процессов), а не только на верхнем уровне. Закрытый скрипт с точкой останова открывается и останавливается.
  `TrackHandler` сохраняет старый фолбэк при пустом runtime-маппинге. Поведение закреплено тестами
  (`RuntimeSourceLocationMapTests`, `TrackHandlerTests`).
- **Регрессия.** Полный прогон `AiRpaStudio.Tests` зелёный; Release `AiRpa.sln` VS 2022 + VS 2026 — 0 ошибок;
  страж «один движок на студию и раннер» и страж «сборка запрещена в раннере» не нарушены.

## 6. Журнал изменений

| Дата | Что сделано |
|---|---|
| 14.11.2026 | Фикс дефекта «задваивается жёлтая рамка в Main.xaml при возобновлении с точки останова»: в `ActivityRunner.Run()` ветка возобновления сначала очищает подсветку на всех открытых дизайнерах (`OnUi(ClearCurrentLocationOnAllDesigners)`) и только затем будит фоновый поток (`_resumeRuntimeFromHost.Set()`), иначе рамка предыдущей точки останова (`Assign`) оставалась при переходе на `SubProcess1`. Регрессионный тест `ActivityRunnerClearHighlightTests.Resume_ClearsCurrentLocationOnAllDesigners_BeforeWakingRuntime` (красный→зелёный, страж: очистка стоит до `_resumeRuntimeFromHost?.Set()`). Полный прогон `AiRpaStudio.Tests` — 2616/2616 зелёные; Release `AiRpa.sln` из VS 2022 и VS 2026 — 0 ошибок (exit 0). |
| 14.11.2026 | Фикс инцидента «MSB3027 + кракозябры в логе»: (1) `VsProjectBuilder`, `MsBuildRobotProjectBuilder`, `MsBuildFeedbackService`, `NuGetInstallService` читают вывод MSBuild как UTF-8 (`StandardOutputEncoding`/`StandardErrorEncoding` = `Encoding.UTF8`) — иначе cp1251-декодирование давало «РЅРµ…»; тест `VsProjectBuilderTests.Build_RussianError_IsDecodedAsUtf8_NotMojibake` (красный→зелёный, воспроизводит GUI-контекст через `Console.OutputEncoding = cp1251`). (2) `WorkflowRunEngine.LoadActivityFromAssembly` грузит сборку из байтов (`Assembly.Load(File.ReadAllBytes(path))`) вместо `Activator.CreateInstanceFrom`: это не блокирует `bin\*.dll` (MSB3021/MSB3027 при автосборке) и не отдаёт устаревшую копию из LoadFrom-контекста при повторном прогоне. Тесты `TryLoadFromBuiltAssembly_DoesNotLockBinDirectory`, `LoadRunActivity_RebuildAfterRun_DoesNotHitFileLock`; (3) `RobotAssemblyResolver` тоже не блокирует файлы: сначала возвращает уже загруженную сборку (`FindLoadedAssembly`), иначе грузит из байтов (`Assembly.Load(File.ReadAllBytes(path))`) вместо `Assembly.LoadFrom` — устраняет блокировку `bin\*.dll` со стороны резолвера зависимостей; тесты `ResolveAssembly_ReturnsAlreadyLoadedAssembly_WithoutLockingProbe`, обновлён `LegacyRobotStudioRunTests.Resolver_FindsRobotAssemblyOnlyFromRobotDirectory`. Release `AiRpa.sln` VS 2022 и VS 2026 — 0 ошибок. |
| 14.11.2026 | Ф2 шаги 7–10: `ActivityRunner` заполняет `_runtimeSourceLocationMapping` после `ValidateForRun` и передаёт в `TrackHandler`; `TrackHandler` ищет SourceLocation сначала в runtime-маппинге (фолбэк — дизайнерский), нормализует SourceLocation (сброс checksum через `new SourceLocation(...)` без checksum), выбирает целевой дизайнер по `FileName` (текущий → уже открытый по `GetDesignerByFileIgnoreCase` → закрытый через `LoadActivityToDesignerHelper.LoadActivityToDesigner(file, GetLooseRegistryFor(file))`) с регистронезависимым поиском фактического пути и проверяет `BreakpointList` целевого дизайнера; `LastBreakpoint` сбрасывается у всех открытых дизайнеров на старте. `TrackHandlerTests` дополнены (runtime приоритетнее; пустой/null runtime → старый путь) — 8/8 зелёные вместе с `RuntimeSourceLocationMapTests`; Release `AiRpaStudio.csproj` и `AiRpaStudio.Tests.csproj` — 0 ошибок. |
| 14.11.2026 | Ф2 шаг 6: добавлен `RuntimeSourceLocationMap.Build(Activity)` (Helpers) — рантайм-маппинг `Activity → SourceLocation` по `sads:DebugSymbol.Symbol` через reflection к internal `SourceLocationProvider.GetSourceLocations(Activity, WorkflowSymbol, bool)` (`translateInternalActivityToOrigin=false`); обход `WorkflowInspectionServices.GetActivities` с защитой от циклов, ошибки глушатся в диагностику, `Parent`/`RootActivity` читаются через reflection. Тест `RuntimeSourceLocationMapTests` (2) зелёный на эталоне RPA-1 (Main + SubProcess1 + SubProcess2); Release `AiRpaStudio.csproj` — 0 ошибок. |
| 14.11.2026 | Ф1 шаг 4: пункт меню «Настройки → Стартовый скрипт…» открывает тематизированный диалог `Views\StartScriptView.xaml` (StudioTitleBar + палитра `Studio.*`, по образцу ErrorView); диалог пишет `DefaultRunScript` и вызывает `Save()`. Ресурсы добавлены в `Main.resx`/`Main.ru-RU.resx`; `Page`/`Compile` внесены в `AiRpaStudio.csproj`; диалог включён в стражи `StudioDialogWindowTests` и `StudioTitleBarTests`. Новые тесты `StartScriptDialogTests` (5) и обновлённые стражи зелёные; Release `AiRpaStudio.csproj` и `AiRpaStudio.Tests.csproj` — 0 ошибок. |
| 14.11.2026 | Ф1 шаг 3: CustomWfDesigner.WorkflowRun() запускает путь из RunTargetResolver.Resolve (активная WF-вкладка -> DefaultRunScript -> Project.MainActivity -> Main.xaml/main.xaml), а не ActiveWorkflowDesignerEx.CurrentWorkflowFile; добавлен GetDesignerByFileIgnoreCase(path). Стражи RunTargetWiringTests (3) зелёные; RunTargetResolverTests (10) и DefaultRunScriptSettingTests (3) зелёные; Release AiRpaStudio.csproj и тест-проекта без ошибок. Шаги 2 и 5 (резолвер и его тесты) подтверждены зелёным прогоном. |
| 14.11.2026 | Ф1 шаг 1: добавлена настройка `DefaultRunScript` в трёх местах (`Settings.settings`, `Settings.Designer.cs`, `App.config`), default `Main.xaml`. Тест `DefaultRunScriptSettingTests` (3 теста) — зелёный (`vstest.console.exe`, Release). |
| 14.11.2026 | Заведён план. Зафиксирован вариант A (прогон по сборке). Диагноз подтверждён по reference source: `ActivityXamlServices.cs` (строки 26, 149, 189, 350), `FuncFactory.cs` (88–94). |
| 14.11.2026 | Реализован вариант A в `AiRpaStudio\Core\ActivityRunner.cs` (`LoadRunActivity`, `TryLoadFromBuiltAssembly`); Release-сборка решения проходит, ошибок компиляции нет. |
| 14.11.2026 | Шаг 1: `ActivityRunner.Start` передаёт `CustomWfDesigner.GetInstance().GetLooseRegistryFor(pathToXaml)` в `LoadActivityToDesignerHelper.LoadActivityToDesigner` — запуск WF-проекта до открытия вкладки идёт loose-путём. Страж `ActivityRunner_PassesLooseRegistryToDesignerOnRun`. |
| 14.11.2026 | Шаг 2: тесты нового пути загрузки `ActivityRunnerLoadPathTests` (5 тестов): сборка → тип из `x:Class` + прогон, свежая сборка из нескольких `bin`, негатив «сборки нет» с причиной `RobotLocator`, не-WF → `DynamicActivity`. `LoadRunActivity`/`TryLoadFromBuiltAssembly` сделаны internal для тестов. |
| 14.11.2026 | Шаг 3 (решение): принят вариант (а) — ограничение «точки останова/подсветка только на верхнем уровне» задокументировано в `AGENTS.md` (CP-08) и закреплено тестом `TrackHandlerTests`; вариант (б) «дизайнер из сборки для отладки» заведён карточкой беклога. |
| 14.11.2026 | Шаг 4: `RobotAssemblyResolver.GetProbeDirectories` теперь покрывает те же раскладки `bin`, что `RobotLocator`/упаковщик (включая `bin/Platform/Config`). Тест-эталон `RobotExternalDependencyTests` собирает MSBuild'ом робота с внешней зависимостью `RobotExternalLib` (не host-owned) и прогоняет его по сборке: `Result = MAX` без `FileNotFoundException`. Плюс тест `GetProbeDirectories_CoversPlatformLayouts`. |
| 14.11.2026 | Шаг 5 (автосборка): `ActivityRunner.TryLoadOrBuildFromProject` собирает проект через `VsProjectBuilder` (MSBuild), если сборки нет или она устарела (`IsProjectOutdated`). Тесты `ActivityRunnerLoadPathTests`: `LoadRunActivity_MissingAssembly_AutoBuildsAndRuns`, `LoadRunActivity_OutdatedAssembly_AutoRebuildsAndRunsUpdatedVersion`, `TryLoadOrBuildFromProject_UnbuildableProject_ReportsBuildAndLocatorReasons`. Release `AiRpa.sln` VS 2022 и VS 2026 — 0 ошибок; страж `RunnerSourceGuardTests` 12/12. |
| 14.11.2026 | Шаг 6 (валидация перед запуском): `ActivityRunner.ValidateForRun` прогоняет `ActivityValidationServices.Validate` после загрузки активности и до старта. Тесты `ActivityRunnerLoadPathTests`: `ValidateForRun_InvalidWorkflow_ThrowsWithClearMessage`, `ValidateForRun_BuiltAssembly_DoesNotThrow` (фикстура 10/10). |
| 13.11.2026 | Заведены Ф1 и Ф2. Диагноз П1/П2 подтверждён по коду (`CustomWfDesigner.WorkflowRun`, `TrackHandler`, `ActivityRunner`) и reference source (`SourceLocationProvider.GetSourceLocations(Activity, WorkflowSymbol, bool)`, `DebugSymbol`, `WorkflowSymbol`, `SourceLocation.Equals` с checksum). Документ переработан в актуальный план: разделы «Описание фич» (§2) и «План задач» (§4). Код не изменялся. |

## 7. Беклог

| Карточка | Критерий «готово» |
|---|---|
| **Дизайнер из сборки для отладки** (альтернатива Ф2, вариант б) | Открывать WF-файл в дизайнере по собранной сборке (реальные тела), чтобы `Id` дерева дизайнера совпадал с `Id` дерева прогона. Ф2 решает задачу «точки останова во вложенных процессах» через рантайм-маппинг по `DebugSymbol`, поэтому вариант (б) остаётся как альтернатива для случаев, когда нужен именно совпадающий `Id` дерева, а не только SourceLocation. |
| **Резюмирование самого глубокого остановленного субраннера (`RunScript`)** | После попадания в точку останова внутри `RunScript`-субпроцесса `CustomWfDesigner.WorkflowRun()` должен резюмировать самый глубокий остановленный `ActivityRunner`, а не только главный. Сейчас это отдельный дефект, не влияющий на сценарий `SubProcessN` (один процесс). |
