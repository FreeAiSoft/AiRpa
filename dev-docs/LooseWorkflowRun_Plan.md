# Запуск вложенных XAML-процессов по собранной сборке — план и журнал

| Поле | Значение |
|---|---|
| Документ | `dev-docs\LooseWorkflowRun_Plan.md` — план доделки прогона WF-проектов с вложенными процессами (`RPA-1`, `SubProcessN`) |
| Связанный план | `dev-docs\LooseXamlTypeService_Plan.md` — loose-открытие/сохранение в дизайнере (без сборки) |
| Цель | закрыть `System.InvalidOperationException` при запуске `RPA-1`: прогон WF-проекта с вложенными процессами по собранной сборке (**вариант A**), дизайнер остаётся loose |
| Дата заведения | 14.11.2026 |
| Правило приёмки | каждый шаг закрывается тестом (включая негатив), полным зелёным прогоном `AiRpaStudio.Tests` и сборкой `AiRpa.sln` Release из VS 2022 и VS 2026; production-правки — только по TDD (red → green → refactor) |
| Reference source | `C:\Users\Maxxx\source\repos\.NET-Framework-4.8-RTM-Reference-Source` |

## 1. Контекст и причина

При запуске `RPA-1` раннер падает с:

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

`RPA_1.SubProcess2` — это CLR-тип, который появляется только **после сборки** проекта
(`XamlAppDef` → `XamlBuildTask` → partial class → `RPA-1.dll`). Прогон в студии грузил XAML
**без сборки** (`WorkflowRunEngine.LoadActivityFromText`), поэтому стандартный `XamlSchemaContext`
не находил тип и падал на компиляции выражений.

### Почему это подтверждается reference source

`ActivityXamlServices.Load(stream, settings)` (`System.Activities.XamlIntegration`) жёстко
использует штатный `DynamicActivityReaderSchemaContext`:

- `ActivityXamlServices.cs:26` — `static readonly XamlSchemaContext dynamicActivityReaderSchemaContext = new DynamicActivityReaderSchemaContext();`
- `ActivityXamlServices.cs:149` — `new XamlXmlReader(xmlReader, dynamicActivityReaderSchemaContext)` — **точки подмены schema context в этом API нет**;
- `ActivityXamlServices.cs:189` — при `CompileExpressions = true` вызывается `Compile(...)`;
- `ActivityXamlServices.cs:350` — `RequiresCompilation` зовёт `ActivityUtilities.CacheRootMetadata(...)` — ровно тот `CacheMetadata`, что в стеке ошибки;
- `FuncFactory.cs:88/91–94` — `Evaluate()` исполняет `XamlServices.Transform(...)`, который и доходит до `XamlObjectWriter` → `Cannot create unknown type`.

Вывод: **пропустить loose-типы через штатный `ActivityXamlServices.Load(..., CompileExpressions=true)`
нельзя** — schema context зашит, а `CacheRootMetadata` всё равно развернёт вложенные ссылки.
Поэтому для прогона выбран вариант A — исполнять собранную `RPA-1.dll`, как это уже делает `RpaRunner`.

## 2. Что уже сделано (вариант A)

`AiRpaStudio\Core\ActivityRunner.cs`:

- `Start` загружает активность через новый `LoadRunActivity(pathToXaml)`.
- Для WF-проекта (`Project.IsWorkflowProject` + есть `Project.ProjectFilePath`) активность грузится
  **из собранной сборки**: `RobotLocator.TryResolve` → `WorkflowRunEngine.LoadActivityFromAssembly`.
- Для обычного робота (project.json / однофайловый XAML) оставлен прежний путь
  `WorkflowRunEngine.LoadActivityFromText`.
- Если сборки нет — понятное сообщение «соберите проект» вместо стека резолва XAML.
- Страж `ActivityRunner_UsesSharedEngineForLoadingAndApplication` соблюдён:
  `WorkflowRunEngine.LoadActivityFromText` / `CreateApplication` есть, `new WorkflowApplication(` /
  `ActivityXamlServices.Load` отсутствуют.

Release-сборка решения проходит, ошибок компиляции нет.

## 3. План доделки

| Шаг | Что делаем | Состояние |
|---|---|---|
| 1 | **Передать loose-реестр в дизайнер при запуске.** `ActivityRunner.Start` сейчас зовёт `LoadActivityToDesignerHelper.LoadActivityToDesigner(pathToXaml)` без реестра. Если `Main.xaml` ещё не открыт через `CustomWfDesigner.LoadProject`, штатный `designer.Load` упадёт на том же `local:SubProcessN`. Для WF-проекта передавать `CustomWfDesigner.GetInstance().GetLooseRegistryFor(pathToXaml)`. Тест: страж вызова / живой стенд «запуск до открытия вкладки». | ✅ 14.11.2026 |
| 2 | **Тесты нового пути загрузки.** `LoadRunActivity`/`TryLoadFromBuiltAssembly` сейчас без тестов. Кейсы: (а) WF-проект + собранная dll → тип из `x:Class`, прогон работает; (б) WF-проект без dll → понятное исключение с причиной `RobotLocator`; (в) не-WF project.json → прежний XAML-путь (`DynamicActivity`); (г) из нескольких `bin` выбирается самая свежая сборка. | ✅ 14.11.2026 |
| 3 | **Отладка: рассинхрон `Id` дизайнер(loose) ↔ прогон(сборка).** Дизайнер открыт loose (фасады без тел), прогон идёт по сборке (реальные тела) → `TrackHandler.GetSourceLocation` по `Activity.Id` не находит подсветку/точки останова внутри `SubProcessN`. **Решение:** принят вариант (а) на первый приём — ограничение «точки останова/подсветка только на верхнем уровне (вне тел `SubProcessN`)» задокументировано в `AGENTS.md` (CP-08) и закреплено тестом `TrackHandlerTests`; вариант (б) «дизайнер из сборки для отладки» заведён отдельной карточкой беклога (§6). | ✅ 14.11.2026 |
| 4 | **Зависимости собранной сборки.** Проверить, что сторонние зависимости `RPA-*.dll` (не host-owned, например свой `Newtonsoft.Json`) резолвятся при прогоне по сборке: `RobotAssemblyResolver.InstallForProject` уже стоит в `Start`, но нужен тест-эталон с внешней зависимостью. При необходимости унифицировать с `RobotDependencyProbe` раннера. | ✅ 14.11.2026 |
| 5 | **Автосборка перед запуском (вариант C).** Прогон WF-проекта теперь сам собирает проект через `VsProjectBuilder` (MSBuild), если сборка отсутствует или устарела относительно XAML проекта. Сборка только в студии; в `RpaRunner` она по-прежнему запрещена стражем. | ✅ 14.11.2026 |
| 6 | **Валидация перед запуском.** Перед прогоном прогоняется `ActivityValidationServices.Validate` (как `--validate` раннера): ошибки, невидимые загрузке, ловятся до старта с понятным сообщением. | ✅ 14.11.2026 |
| 7 | **Приёмка и документация.** Обновить `AGENTS.md` (CP-08/OR-02), `ROADMAP.md` §3 и журнал `LooseXamlTypeService_Plan.md`; полный прогон `AiRpaStudio.Tests`; Release VS 2022 + VS 2026; ручная приёмка в студии (`RPA-1`: сборка → запуск → вывод `Hello from RPA-1` + выполнение `SubProcess*`). | ⏳ не начато |

## 4. Определения «готово» на шаг

- Шаг 1: `ActivityRunner.Start` открывает WF-файл loose-путём и до прогона; запуск не падает на `local:SubProcessN`, даже если вкладка ещё не была открыта.
- Шаг 2: зелёные тесты на `LoadRunActivity`/`TryLoadFromBuiltAssembly` (включая негатив «сборки нет»); тесты ссылаются на production-класс (`InternalsVisibleTo("AiRpaStudio.Tests")` уже есть).
- Шаг 3: поведение подсветки/точек останова зафиксировано тестом или задокументировано; не осталось «тихой» потери отладки внутри вложенных процессов.
- Шаг 4: робот с внешней зависимостью (не host-owned) прогоняется по сборке без `FileNotFoundException`.
- Шаг 5: после правки XAML без ручной сборки запуск сам пересобирает dll и прогоняет актуальную версию (тесты `ActivityRunnerLoadPathTests`); запрет сборки в `RpaRunner` не нарушен (страж `RunnerSourceGuardTests`).
- Шаг 6: ошибки, невидимые загрузке (`Assign` без `To`/`Value`), ловятся до запуска с понятным сообщением; есть тест на негативном workflow (`ActivityRunner.ValidateForRun`).
- Шаг 7: `RPA-1` запускается по сборке; полный прогон тестов зелёный; Release 0 ошибок из VS 2022 и VS 2026; документация обновлена.

## 5. Журнал изменений

| Дата | Что сделано |
|---|---|
| 14.11.2026 | Заведён план. Зафиксирован вариант A (прогон по сборке). Диагноз подтверждён по reference source: `ActivityXamlServices.cs` (строки 26, 149, 189, 350), `FuncFactory.cs` (88–94). |
| 14.11.2026 | Реализован вариант A в `AiRpaStudio\Core\ActivityRunner.cs` (`LoadRunActivity`, `TryLoadFromBuiltAssembly`); Release-сборка решения проходит, ошибок компиляции нет. |
| 14.11.2026 | Шаг 1: `ActivityRunner.Start` передаёт `CustomWfDesigner.GetInstance().GetLooseRegistryFor(pathToXaml)` в `LoadActivityToDesignerHelper.LoadActivityToDesigner` — запуск WF-проекта до открытия вкладки идёт loose-путём. Страж `ActivityRunner_PassesLooseRegistryToDesignerOnRun`. |
| 14.11.2026 | Шаг 2: тесты нового пути загрузки `ActivityRunnerLoadPathTests` (5 тестов): сборка → тип из `x:Class` + прогон, свежая сборка из нескольких `bin`, негатив «сборки нет» с причиной `RobotLocator`, не-WF → `DynamicActivity`. `LoadRunActivity`/`TryLoadFromBuiltAssembly` сделаны internal для тестов. |
| 14.11.2026 | Шаг 3 (решение): принят вариант (а) — ограничение «точки останова/подсветка только на верхнем уровне» задокументировано в `AGENTS.md` (CP-08) и закреплено тестом `TrackHandlerTests`; вариант (б) «дизайнер из сборки для отладки» заведён карточкой беклога (§6). |
| 14.11.2026 | Шаг 4: `RobotAssemblyResolver.GetProbeDirectories` теперь покрывает те же раскладки `bin`, что `RobotLocator`/упаковщик (включая `bin/Platform/Config` — иначе x64/x86-робот находился бы локатором, а его зависимости нет). Тест-эталон `RobotExternalDependencyTests` собирает MSBuild'ом робота с внешней зависимостью `RobotExternalLib` (не host-owned) и прогоняет его по сборке: `Result = MAX` без `FileNotFoundException`. Плюс тест `GetProbeDirectories_CoversPlatformLayouts`. |
| 14.11.2026 | Шаг 5 (автосборка): `ActivityRunner.TryLoadOrBuildFromProject` собирает проект через `VsProjectBuilder` (MSBuild), если сборки нет или она устарела (`IsProjectOutdated` — любой XAML проекта новее dll, без `bin`/`obj`); при неудачной сборке причина от `RobotLocator` сохраняется в сообщении. Тесты `ActivityRunnerLoadPathTests`: `LoadRunActivity_MissingAssembly_AutoBuildsAndRuns`, `LoadRunActivity_OutdatedAssembly_AutoRebuildsAndRunsUpdatedVersion`, `TryLoadOrBuildFromProject_UnbuildableProject_ReportsBuildAndLocatorReasons` (фикстура 8/8). Release `AiRpa.sln` VS 2022 и VS 2026 — 0 ошибок; страж `RunnerSourceGuardTests` 12/12 (запрет сборки в раннере не нарушен). |
| 14.11.2026 | Шаг 6 (валидация перед запуском): `ActivityRunner.ValidateForRun` прогоняет `ActivityValidationServices.Validate` после загрузки активности и до старта; ошибки, невидимые загрузке (`Assign` без `To`/`Value`), дают `InvalidOperationException` с перечнем замечаний (как `--validate` раннера). Тесты `ActivityRunnerLoadPathTests`: `ValidateForRun_InvalidWorkflow_ThrowsWithClearMessage`, `ValidateForRun_BuiltAssembly_DoesNotThrow` (фикстура 10/10). |


## 6. Беклог

| Карточка | Критерий «готово» |
|---|---|
| **Дизайнер из сборки для отладки** (из шага 3, вариант б) | Открывать WF-файл в дизайнере по собранной сборке (реальные тела), чтобы `Id` дерева дизайнера совпадал с `Id` дерева прогона и `TrackHandler.GetSourceLocation` подсвечивал/останавливал внутри `SubProcessN`. Готово: во вложенных `SubProcessN` эталона `RPA-1` работают жёлтая подсветка и точки останова при прогоне по сборке; поведение закреплено тестом. |
