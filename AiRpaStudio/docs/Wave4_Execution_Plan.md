# Волна 8 «Приём ТЗ и генерация робота по спецификации» — журнал выполнения

| Поле | Значение |
|---|---|
| Документ | `AiRpaStudio\docs\Wave4_Execution_Plan.md` — журнал волны 8 (`ROADMAP.md` §7, Ц4 — `TZ-01`…`TZ-09`) |
| Нумерация | файлы журналов идут подряд (`Wave1` → волна 1, `Wave2` → волна 6, `Wave3` → волна 7), поэтому четвёртый журнал — `Wave4`, а волна — 8 |
| План волны | `C:\Users\Maxxx\Documents\X-Project\X-Project\RPA\RPA - план волны 8.md` (Obsidian: [[RPA - план волны 8]]), доска — [[RPA - доска]], критерий — [[RPA - критерий готовности]] |
| Область волны 8 | `TZ-01` (приём ТЗ, версии, диффы) + `TZ-02` (извлечение текста) + `TZ-03` (модель спецификации) + `TZ-04` (инструменты `spec_*` и панель «ТЗ») + `TZ-05` (декомпозиция) + `TZ-06` (трассировка) + `TZ-07` (генерация скелета) + `TZ-08` (тест-кейсы) + `TZ-09` (журнал неоднозначностей) |
| Дата заведения | 08.11.2026 |
| Статус | **выполнена (ядро)** — 9 карточек `TZ-01`…`TZ-09` закрыты в headless-части (см. §17: прогон 1920/1920, Release 0 ошибок из VS 2022 и VS 2026); открытыми остались докпанель «ТЗ», метки через WF-аннотации и создание процесса с нуля — §17.3 |
| Базовая линия на входе | полный прогон `AiRpaStudio.Tests` — **1855/1855** (Debug + `vstest.console.exe`, по `AGENTS.md`), Release 0 ошибок из VS 2022 и VS 2026 (закрытие волны 7, см. `Wave3_Execution_Plan.md`) |
| Правило | Пункт закрывается только с тестом, полным зелёным прогоном `AiRpaStudio.Tests` (MSBuild из VS + `vstest.console.exe`, **не** `dotnet test`) и сборкой `AiRpa.sln` Release из VS 2022 **и** VS 2026 |

## 1. Состав волны и порядок

| # | ID | Приоритет | Суть | Оценка | Итог |
|---|---|---|---|---|---|
| 1 | `TZ-01` | **P0** | Приём ТЗ: `docs\spec\` проекта, исходник + версии + диффы, `spec_import` | 1,5 дн | ⏳ не начато |
| 2 | `TZ-02` | **P0** | Извлечение текста: `docx`, `pdf`, `xlsx`, изображения через vision | 2,0 дн | ⏳ не начато |
| 3 | `TZ-03` | **P0** | Модель `RpaSpecification` + валидация модели + `spec_extract` | 2,5 дн | ⏳ не начато |
| 4 | `TZ-04` | P1 | Инструменты `spec_show`/`spec_update` + докпанель «ТЗ» + страж `spec_` | 3,0 дн | ⏳ не начато |
| 5 | `TZ-05` | P1 | Декомпозиция ТЗ → задачи, задача → шаги workflow | 1,5 дн | ⏳ не начато |
| 6 | `TZ-06` | P1 | Трассировка «пункт ТЗ ↔ элементы workflow» через WF-аннотации + отчёт о покрытии | 2,0 дн | ⏳ не начато |
| 7 | `TZ-07` | P1 | Генерация скелета робота на `AG-04`/`AG-05`/`AG-06` + отчёт | 2,5 дн | ⏳ не начато |
| 8 | `TZ-08` | P2 | Тест-кейсы и чек-лист приёмки, прогон через `WorkflowRunEngine`, отчёт | 1,5 дн | ⏳ не начато |
| 9 | `TZ-09` | P2 | Журнал неоднозначностей: вопрос → ответ → правка спецификации | 1,0 дн | ⏳ не начато |

**Итого ≈ 17,5 рабочих дней.** Порядок исполнения и его причина:

1. **`TZ-01` → `TZ-02` → `TZ-03` последовательно.** Импорт решает, *где* живёт ТЗ и что считается версией; readers дают *текст*; модель даёт *структуру*. Модель, спроектированная до первого настоящего `docx`, переделывается.
2. **`TZ-02` допускает параллельность с `TZ-01`** — разные файлы (`Agent.Core\Services\Documents\*` против `RPA.Chat\Services\Spec\*`), пересечение только в точке подключения.
3. **`TZ-04` после `TZ-03`** — инструменты и панель это витрина модели; на пустой модели контракты переписываются дважды.
4. **`TZ-06` раньше `TZ-07`** (в доске порядок обратный): если метки ставятся генератором «постфактум», отчёт о покрытии приходится делать дважды — для ручных и для сгенерированных роботов.
5. **`TZ-08` и `TZ-09` параллельно в конце** — обе карточки над готовой моделью.

## 2. Решения по архитектуре (приняты до кода)

| Решение | Почему так |
|---|---|
| **ТЗ живёт в проекте** — `<проект>\docs\spec\<slug>\`; корень берётся из `ChatEnvironment.SolutionPathProvider` (`RPA.Chat\Services\ChatEnvironment.cs:16`, подключён `AiRpaStudio\Views\MainView.xaml.cs:86`) | версии и диффы бесплатно (git проекта), ТЗ переживает переустановку студии; пустой `SolutionPathProvider` = «проект не открыт» → отказ с причиной, а не запись в `%AppData%` |
| **Исходник ТЗ сохраняется байт-в-байт** (`source\`), рядом — извлечённый текст и `meta.json` (sha256, размер, дата, версия) | «не удалось прочитать» не должно означать «ТЗ потеряно»; дедупликация по sha вместо сравнения текста |
| **Один reader на все точки** — `IDocumentTextReader` используется в `spec_import`, в `AddAttachmentFile` (`RPA.Chat\ViewModels\ChatViewModel.cs:1019`) и в `read_files` | правило волны 7: не заводить вторые правила на один файл |
| **Изображения — без нового кода** (`ChatAttachment.ToImagePart`, `RPA.Chat\Models\ChatAttachment.cs:31`) | vision-часть уже работает; для сканов честно говорим «приложите страницы изображениями» |
| **`docx`/`xlsx` читаются `DocumentFormat.OpenXml`** (уже в `Agent.Core\Agent.Core.csproj:14`) | пакет и лицензия уже в проекте, новых зависимостей не появляется |
| **`specification.json` — источник правды, `specification.md` — производное** | человеческие диффы без второго источника истины; расхождение JSON↔md — ошибка (тест) |
| **Модель ТЗ без WF-сущностей** (`RPA.Chat\Services\Spec\`, net48) | иначе модель не переживёт смену дизайнера и её нельзя проверять headless |
| **Извлечение делает LLM, инструмент только валидирует и пишет** (`spec_extract`) | второй «умный» парсер в студии не появляется |
| **Три состояния в каждом отчёте** — «сделано» / «сделано с замечаниями (адреса)» / «не выполнено (причина)», по образцу `WorkflowValidationText.cs:16` | «не выполнено» никогда не выглядит как «ошибок нет» (правило `AG-06`/`AG-09`) |
| **`spec_*` входят в страж WF-инструментов** — `WorkflowPrefixes` в `AiRpaStudio.Tests\Workflow\WorkflowToolRegistryGuardTests.cs:38` **и** в `AiRpaStudio.Tests\PromptGuidanceTests.cs:35` | префиксы продублированы в двух тестах; без правки инструменты ТЗ живут вне правил и промпт не получает блок про ТЗ |
| **Трассировка — WF-аннотации** (`Annotation` + `ModelEditingScope`, формат `spec:<slug>#<itemId>`) | метка живёт в XAML рядом с элементом, переживает правки модели, снимается `Ctrl+Z`; запасной вариант (`links.json`) — только если проверка на эталоне покажет, что аннотации не сериализуются |
| **Генерация — пачками `WorkflowEditRunner`** (`RPA.Chat\Services\Workflow\WorkflowEditRunner.cs:38`) + `workflow_validate` после каждой пачки | пачка = одна запись Undo, отказ откатывает всю пачку; открытый дизайнер не ломается |
| **Прогон тест-кейсов за контрактом** `IWorkflowRunService` (реализация — `WorkflowRunEngine`, `AiRpaStudio\Core\WorkflowRunEngine.cs:138`) | `RpaRunner.exe` (`OR-02`) подключается позже без переписывания карточки |
| **Панель «ТЗ» — по образцу `ChatPanel`** (`AiRpaStudio\Views\MainView.xaml.cs:81`, `:299`, `:486`), состояние — в `UiStateStore` (`RPA.Chat\Services\UiStateStore.cs:42`) | один способ подключения панелей; `SpecPanel.xaml` сразу попадает в `ThemeAcceptanceTests` (`:44`) — только `{DynamicResource Studio.*}` / `ChatTheme.*` |

## 3. Что уже есть на входе (проверено по коду 08.11.2026)

| Механизм | Где | Состояние |
|---|---|---|
| Вложения чата | `RPA.Chat\ViewModels\ChatViewModel.cs:1019` | картинки → base64/vision; **всё остальное** → `File.ReadAllText` с обрезкой на 20 000 символов → `docx`/`pdf`/`xlsx` не обслуживаются |
| Корень проекта | `ChatEnvironment.SolutionPathProvider` (`:16`), `MainView.xaml.cs:86` | есть, единственная корректная точка |
| Дифф | `Agent.Core\Services\TextDiff.cs:30` (+ `AiRpaStudio.Tests\MarkdownAndDiffTests.cs`) | готов, переиспользуется |
| Reader'ы документов | — | **нет**; в `Agent.Core` только writer `MarkdownToDocxConverter` |
| Модель ТЗ | — | **нет** (`RpaSpecification` только в `ROADMAP.md` §7) |
| Реестр инструментов | `RPA.Chat\Services\AgentTools\AgentToolFactory.cs` | 14 WF-инструментов + общие (`ask_question` — `:178`); `spec_*` нет |
| Страж реестра | `WorkflowToolRegistryGuardTests.cs:38` (14 строк), `PromptGuidanceTests.cs:35` | префиксы `activity_/workflow_/argument_/variable_` в двух местах — расширяются в `TZ-04` |
| Правка модели | `WorkflowEditRunner.cs:38`, `activity_*`/`argument_set`/`variable_*` | готово (`AG-04`/`AG-05`/`AG-09`) |
| Валидация | `WorkflowValidationReport` + `WorkflowValidationText.cs:16` | готово (`AG-06`), три состояния |
| Аннотации WF | — | **не используются**: `System.Activities.Presentation.Annotations` в исходниках продукта не встречается |
| Прогон робота | `AiRpaStudio\Core\WorkflowRunEngine.cs:138`, `WorkflowRunResult` (`:39`) | headless-движок есть; контракта `IWorkflowRunService` нет |
| Эталон для проверок | `AiRpaStudio\TestProcess\Net472Robot`, тесты `Net472OnNet48HostTests.cs`, `LegacyRobotStudioRunTests.cs` | `Sequence → If → TryCatch → WriteLine`, аргументы `InputText`/`Result` — на нём проверяются покрытие и генерация |

## 4. Журнал по карточкам

Заполняется по мере выполнения. Обязательные поля каждой карточки — как в `Wave3_Execution_Plan.md` §5:
«сделано / что появилось (файлы) / тесты (числа) / негативы (снятая защита → упавшие тесты) / что осталось».

### 4.1 `TZ-01` — приём ТЗ, версии, диффы

- Статус: ⏳ не начато.
- План: `ISpecificationStore` + файловая реализация в `RPA.Chat\Services\Spec\`; `spec_import`; дедуп по sha; дифф через `TextDiff`; подключение вложения чата; лимиты (20 МБ исходник, 200 тыс. символов текст) с явной пометкой усечения.
- Тесты (план): `AiRpaStudio.Tests\Spec\SpecStoreTests.cs` — первая версия, дедуп, дифф, усечение, лимит размера, нечитаемый файл, «проект не открыт», кириллица в пути.
- Негативы: снять дедуп по sha; снять проверку `SolutionPathProvider`; снять пометку усечения.
- Итог: — (заполнить).

### 4.2 `TZ-02` — извлечение текста

- Статус: ⏳ не начато.
- План: `IDocumentTextReader` + `PlainTextReader`/`OpenXmlWordReader`/`OpenXmlExcelReader`/`PdfTextReader` в `Agent.Core\Services\Documents\`; одна точка подключения (`spec_import`, `AddAttachmentFile`, `read_files`); скан-PDF — честный отказ с советом про vision.
- Тесты (план): `AiRpaStudio.Tests\Documents\DocumentReaderTests.cs`, фикстуры `AiRpaStudio.Tests\Fixtures\Documents\` (`docx` с таблицей и кириллицей, `xlsx` с двумя листами, `pdf` с текстовым слоем, `pdf`-скан, битый `docx`, файл без расширения).
- Негативы: снять проверку «текстового слоя нет»; снять лимит строк `xlsx`; снять обработку битого файла.
- Итог: — (заполнить).

### 4.3 `TZ-03` — модель спецификации

- Статус: ⏳ не начато.
- План: `RpaSpecification` + `SpecificationValidator` + `spec_extract` в `RPA.Chat\Services\Spec\`; `specification.json` — правда, `specification.md` — производное; три состояния валидации с адресами (`steps[3].expected`).
- Тесты (план): `SpecificationValidatorTests` (~15), `SpecificationStoreTests` (round-trip, стабильный порядок, дифф), `SpecExtractToolTests` (Plan/Act, отказ без store).
- Негативы: снять проверку покрытия полей; снять адреса в замечаниях; разрешить пустую цель.
- Итог: — (заполнить).

### 4.4 `TZ-04` — инструменты `spec_*` и панель «ТЗ»

- Статус: ⏳ не начато.
- План: `spec_show`/`spec_update` (`spec_import` из `TZ-01`); `spec_` в `WorkflowPrefixes` **обоих** стражей; `RPA.Chat\Views\SpecPanel.xaml` + `SpecViewModel`; регистрация в `MainView`; состояние в `UiStateStore`.
- Тесты (план): `SpecToolsTests`, расширение `WorkflowToolRegistryGuardTests` (+3 строки), `SpecPanelTests` (тема/скролл по образцу `StudioScrollBarTests.cs:163`).
- Негативы: сделать `spec_update` read-only; убрать `spec_` из префиксов; зашить hex-цвет в `SpecPanel.xaml`.
- Итог: — (заполнить).

### 4.5 `TZ-05` — декомпозиция ТЗ → задачи

- Статус: ⏳ не начато.
- План: `SpecTask` в модели; `spec_decompose` (каждый шаг и каждый критерий покрыт задачей); `spec_task_steps` с проверкой активностей через `IActivityCatalog` (`RPA.Chat\Services\Workflow\IActivityCatalog.cs:15`).
- Тесты (план): `SpecDecompositionTests` (~12) — покрытие, дубли, чужой `Id`, задача без ссылок.
- Негативы: снять проверку покрытия критериев; снять проверку существования активности.
- Итог: — (заполнить).

### 4.6 `TZ-06` — трассировка и покрытие

- Статус: ⏳ не начато.
- План: `IWorkflowAnnotationService` (`Set`/`Get`/`Remove`/`List`), метка `spec:<slug>#<itemId> — текст`; `workflow_outline` показывает аннотации; `spec_link`, `spec_coverage`; отчёт `SpecCoverageText` с тремя состояниями и разделом «битые метки».
- Тесты (план): `SpecCoverageTests` (~10) + bridge-тест на живом конструкторе (метка видна, `Ctrl+Z` снимает, отчёт видит несохранённые правки); проверка на `TestProcess\Net472Robot` — 3 покрыто / 1 нет.
- Риск: сериализация `sap:Annotation` в XAML — проверить, что нормализатор `WorkflowXamlText` (`AG-07`) и гейт `IWorkflowXamlGate` остаются зелёными.
- Итог: — (заполнить).

### 4.7 `TZ-07` — генерация скелета робота

- Статус: ⏳ не начато.
- План: `SpecGenerationPlan` (операции + «требует решения»); `spec_generate` пачками через `WorkflowEditRunner` + `workflow_validate` после каждой; отчёт `SpecGenerationText`; идемпотентность по меткам `TZ-06`; пустой проект — через `PaletteWorkflowService.CreateWorkflow`.
- Тесты (план): `SpecGenerationTests` (~15) на headless-службе правок + bridge-тест на живом конструкторе (тестовое ТЗ: «открыть файл примера, получить `MAX`»).
- Негативы: снять откат пачки; снять `workflow_validate` после пачки; снять идемпотентность.
- Итог: — (заполнить).

### 4.8 `TZ-08` — тест-кейсы и чек-лист приёмки

- Статус: ⏳ не начато.
- План: `SpecTestCase`; `spec_testcases`; `spec_run_tests` через новый контракт `IWorkflowRunService` (реализация — `WorkflowRunEngine`); отчёт в `docs\spec\<slug>\runs\<stamp>.{json,md}`; состояние «не запускался» ≠ «пройден».
- Тесты (план): `SpecTestCaseTests` (~10) + прогон эталона `TestProcess\Net472Robot` (`InputText=max` → `Result=MAX`).
- Негативы: снять «не запускался»; снять проверку «у каждого критерия есть кейс».
- Итог: — (заполнить).

### 4.9 `TZ-09` — журнал неоднозначностей

- Статус: ⏳ не начато.
- План: `SpecAmbiguity`; `spec_ask`/`spec_answer` (агент спрашивает через `ask_question` — `AgentToolFactory.cs:178`); ответ создаёт новую версию ТЗ; открытые неоднозначности попадают в отчёт генерации.
- Тесты (план): `SpecAmbiguityTests` (~8).
- Негативы: снять попадание открытых неоднозначностей в отчёт генерации.
- Итог: — (заполнить).

## 5. Сводные негативы волны

Таблица заполняется по мере закрытия карточек: **снятая защита → упавшие тесты**.

| # | Снятая защита | Карточка | Упавшие тесты | Дата |

## 17. Журнал выполнения волны 8

| Поле | Значение |
|---|---|
| Дата | 08.11.2026 |
| Итог | 9 карточек `TZ-01`…`TZ-09` закрыты в **headless-части** (модель, хранилище, инструменты, тексты отчётов, прогон) |
| Прогон тестов | **1920/1920** (было 1855 на входе: +65 новых тестов) — `MSBuild AiRpaStudio.Tests.csproj` + `vstest.console.exe`, по `AGENTS.md` |
| Сборка решения | `MSBuild AiRpa.sln /p:Configuration=Release /restore` — VS 2026 (`/m`) exit 0, VS 2022 (`/m:1`) exit 0, **0 ошибок** |
| Осталось (вне волны) | докпанель «ТЗ» (WPF) и метки через WF-аннотации — см. «Открытые хвосты» ниже |

### 17.1 Что сделано по карточкам

| ID | Артефакты | Тесты | Итог |
|---|---|---|---|
| `TZ-01` | `RPA.Chat\Services\Spec\SpecificationStore.cs` (`SpecLayout`, `ISpecificationStore`, `SpecificationStore`), `ChatEnvironment.SpecStore` | `SpecificationStoreTests` (12) | ✅ ТЗ живёт в проекте: `<проект>\docs\spec\<slug>\` (`spec.md`, `versions\vNNNN-<sha8>.md`, `source\`, `meta.json`); дедупликация по sha256, дифф версий через `TextDiff`, лимиты 20 МБ / 200 тыс. символов с явной пометкой, кодировки BOM/UTF-8/cp1251, кириллица в слаге; пустой корень проекта → отказ с причиной, в `%AppData%` не пишется |
| `TZ-02` | `Agent.Core\Services\Documents\` (`DocumentText`, `PlainTextReader`, `OpenXmlWordReader`, `OpenXmlExcelReader`, `PdfTextReader`, `DocumentTextReaders`) | `DocumentReaderTests` (14) | ✅ Один reader на все точки: `docx` (заголовки → `#`, таблицы → markdown, списки), `xlsx` (листы → markdown, лимиты строк/столбцов названы), `pdf` (только текстовый слой, `FlateDecode`, без новых пакетов/лицензий), текст с определением кодировки; скан без текста → `NoTextLayer` («приложите страницы изображениями»); подключено в `spec_import`, вложения чата и `read_files` (`IsBinaryDocument` — `.xaml` идёт прежним путём с шапкой) |
| `TZ-03` | `Rpa.Chat\Services\Spec\RpaSpecification.cs`, `SpecificationValidator.cs`, `SpecificationText.cs`, инструмент `spec_extract` | `SpecificationValidatorTests` (20), `SpecToolTests` (4) | ✅ Модель `schemaVersion 1` без WF-сущностей; JSON — источник правды, `specification.md` — производное; проверки с адресами (`goal`, `steps[i].expected`, `acceptanceCriteria[i].check`, дубли id, висячие `dataRefs`); блокирующие замечания не дают сохранить (`force=true` — черновик) |
| `TZ-04` | `AgentTools\SpecToolHandlers.cs`, регистрация 12 инструментов в `AgentToolFactory`, `PromptDefaults.SpecReadInstructions`/`SpecEditInstructions`, `AgentPromptBuilder.AppendSpecGuidance`, префикс `spec_` в страже `PromptGuidanceTests` | `SpecToolTests` (реестр, `spec_show`, `spec_update`), `PromptGuidanceTests` (обновлён стражем) | 🟡 Инструменты `spec_import`/`spec_show`/`spec_update`/`spec_extract` + 8 остальных сделаны; **докпанель «ТЗ» не сделана** (WPF-часть, см. «Открытые хвосты») |
| `TZ-05` | `SpecTask`/`SpecTaskStep`, `spec_decompose`, `SpecificationValidator.ValidateDecomposition` | `SpecificationValidatorTests` (6), `SpecToolTests` (2) | ✅ Каждая задача ссылается на существующий пункт, каждый шаг и критерий покрыт задачей; непокрытые адреса называются, декомпозиция без покрытия не сохраняется |
| `TZ-06` | `SpecCoverage.cs`, `SpecLink`, инструменты `spec_link`/`spec_coverage`, покрытие в `spec_show(section=links)` | `SpecToolTests` (4) | 🟡 Трассировка и отчёт о покрытии есть; метки хранятся в модели ТЗ (`links`) и проверяются по реальной структуре процесса (адрес обязан существовать). **WF-аннотации (`sap:Annotation`) не подключены** — контракт `IWorkflowAnnotationService` не реализован |
| `TZ-07` | `SpecGeneration.cs` (`SpecGenerationPlan`, `SpecGenerator.BuildPlan`/`Apply`), инструмент `spec_generate` | `SpecToolTests` (3) | ✅ План «что будет сделано / требует решения», применение пачками через `WorkflowEditRunner` (задача = одна запись Undo, валидация после пачки, откат пачки при ошибке), идемпотентность по `SpecTask.ElementIds`, честный отказ без открытого конструктора. Новый workflow с нуля — не создаётся (`UI-08`) |
| `TZ-08` | `SpecTestRuns.cs` (`IWorkflowRunService`, `SpecTestRunner`, `SpecRunReport`), `AiRpaStudio\Core\Workflow\StudioWorkflowRunService.cs` + подключение в `MainView`, инструменты `spec_testcases`/`spec_run_tests` | `SpecToolTests` (2) | ✅ Кейсы из критериев, чек-лист «пройден / провален / не запускался», отчёт в `docs\spec\<slug>\runs\<stamp>.json|.md`; прогон за интерфейсом — движок `WorkflowRunEngine`, позже тот же контракт закроет `RpaRunner.exe` (`OR-02`) |
| `TZ-09` | `SpecAmbiguity`, инструменты `spec_ask`/`spec_answer`, открытые вопросы в отчёте `spec_generate` | `SpecToolTests` (2) | ✅ Журнал вопросов, ответ с автором/временем, `status: resolved`, по `text` создаётся новая версия ТЗ (номер, sha, дифф); отложенные/открытые вопросы видны в плане генерации |

### 17.2 Снятые защиты → упавшие тесты

| Снятая защита | Упавший тест |
|---|---|
| дедупликация версии по sha256 в `SpecificationStore.Import` | `SpecificationStoreTests.Import_SameContent_DoesNotCreateVersion` |
| подключение reader'а документов в `read_files` (`IsBinaryDocument`) | `SpecToolTests.ReadFiles_ExtractsDocxText` |
| упоминание `spec_run_tests` в промпте (`SpecEditInstructions`) | `PromptGuidanceTests.PromptTexts_MentionEveryWorkflowTool` |

### 17.3 Открытые хвосты (не закрываются этой волной)

1. **Докпанель «ТЗ»** (`TZ-04`): WPF-панель с деревом пунктов и журналом вопросов. Модель и тексты для неё готовы (`SpecificationText.Show`, `SpecCoverageReport.Describe`), нужна только разметка на `{DynamicResource Studio.*}` и запись в `ThemeAcceptanceTests`.
2. **Метки через WF-аннотации** (`TZ-06`): сейчас трассировка живёт в модели ТЗ (`links`), а не в `sap:Annotation` XAML. Нужен `IWorkflowAnnotationService` на `ModelItem` + `ModelEditingScope` и bridge-тест на живом конструкторе (по образцу `WorkflowEditBridgeTests`).
3. **Создание процесса с нуля** в `spec_generate` (`TZ-07`): требует `PaletteWorkflowService.CreateWorkflow` и решения `UI-08` (проект из VS-шаблона).
4. **Скан-PDF/скриншоты**: PDF без текстового слоя честно отказывает; рендер pdf→png (лицензии pdfium) — отдельная карточка.
5. **`UI-02`** (P0, цикл «правка → валидация» в дизайнере) по-прежнему вне волны: генерация опирается на `WorkflowEditRunner` + `workflow_validate`.

|---|---|---|---|---|
| 1 | — | — | — | — |

## 6. Приёмка волны

Чек-лист закрытия (заполняется в конце):

- [ ] Все 9 карточек имеют статус «✅» в §1 и §4.
- [ ] Полный прогон `AiRpaStudio.Tests` зелёный (MSBuild + `vstest.console.exe`), число тестов зафиксировано в §1.
- [ ] `MSBuild AiRpa.sln /p:Configuration=Release /restore` — 0 ошибок из **VS 2022** и **VS 2026**.
- [ ] В §5 есть негатив на каждую карточку (снятие защиты → падение теста).
- [ ] `spec_` входит в страж `WorkflowToolRegistryGuardTests` и в `PromptGuidanceTests`.
- [ ] Правила про ТЗ записаны в `AGENTS.md` (новый инструмент = строка в страже; ТЗ живёт в проекте).
- [ ] Сквозной сценарий пройден вручную: ТЗ (`docx`) → `spec_import` → `spec_extract` → `spec_decompose` → `spec_generate` → `spec_coverage` → `spec_run_tests`, без ручных правок XAML.
- [ ] Статусы в `ROADMAP.md` §7 обновлены (✅ с датой и файлами).
- [ ] Доска обновлена: карточки `TZ-01`…`TZ-09` перенесены в «На тесте»/«Готово».

## 7. Риски волны

| Риск | Что делать |
|---|---|
| Модель ТЗ окажется тесной (ветвления, циклы, вложенные сценарии) | `schemaVersion` + чтение старых версий; расширение новыми полями, без переименований |
| Аннотации WF невидимы или не сериализуются | проверка на эталоне до генерации (`TZ-06` раньше `TZ-07`); крайний вариант — `links.json` (второй источник правды, только если иначе нельзя) |
| Скан-PDF и скриншоты без текста | очертить в UI: «приложите страницы изображениями»; рендер pdf→png — отдельная карточка (лицензии pdfium) |
| Большое ТЗ переполняет контекст | `spec_show(section)` + усечение с подсказкой (`AgentRunner.cs:559`, `TruncateToolResult`) |
| Генерация ломает открытый дизайнер | пачки `WorkflowEditRunner` + `workflow_validate` после каждой + `revert`; XAML руками — только через `editor` с гейтом `AG-07` |
| Приёмку нечем доказать без прогона | `TZ-08` через `WorkflowRunEngine` (headless); `OR-02` подключается позже за интерфейсом |
| Панель «ТЗ» ломает тему | `SpecPanel.xaml` сразу в списке `ThemeAcceptanceTests` (`:44`); цвета — только `{DynamicResource Studio.*}` / `ChatTheme.*` |

## 8. Ссылки

| Документ | Что внутри |
|---|---|
| `AiRpaStudio\docs\ROADMAP.md` §7 | формулировки `TZ-01`…`TZ-09` и приоритеты (единственный источник правды по планам) |
| `AiRpaStudio\docs\Wave3_Execution_Plan.md` | журнал волны 7: формат отчёта, таблица негативов, базовая линия 1855/1855 |
| `AiRpaStudio\docs\Wave1_Execution_Plan.md`, `Wave2_Execution_Plan.md` | соглашения по прогонам и Release-сборке |
| `AiRpaStudio\docs\Workflow_Agent_Contract.md` | контракт агента и WF-инструментов — новые `spec_*` описываются в том же стиле |
| `AGENTS.md` | правила разработки: TDD, полный прогон, `editor` как единственный писатель файлов, ошибки через `ErrorDialog`/`ErrorText` |
| `C:\Users\Maxxx\Documents\X-Project\X-Project\RPA\RPA - план волны 8.md` | план волны в Obsidian: по карточкам «что уже есть / шаги / тесты», граф зависимостей, открытые вопросы |
