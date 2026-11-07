# Волна 2 «Агент и инструменты построения workflow» — журнал выполнения

| Поле | Значение |
|---|---|
| Документ | `AiRpaStudio\docs\Wave2_Execution_Plan.md` — журнал волн 2 из `AiRpaStudio\docs\ROADMAP.md` (§6) |
| Область волны 6 | `AG-06` (`workflow_validate`) + `AG-12` (негативы волны 6) |
| Дата | 06.11.2026 |
| Правило | Пункт закрывается только с тестом, полным зелёным прогоном `AiRpaStudio.Tests` и сборкой `AiRpa.sln` Release из VS 2022 **и** VS 2026 |

## 1. Волна 6 — `workflow_validate` (`AG-06`)

### 1.1 Решение по архитектуре

**Новой службы валидации не появилось.** Валидация WF уже выполняется в
`StudioWorkflowOutlineService.AddValidationIssues` (`ActivityValidationServices.Validate` по тому же дереву,
которое читает `WorkflowModelReader`), ошибки уже привязаны к адресам, а живой документ уже отдаётся через
`LiveOutlineSource`. Поэтому `workflow_validate` — это **отчёт над `IWorkflowOutlineService`**:

- один источник адресов и текстов с `workflow_outline` и с инструментами правки (`activity_*`, `argument_set`),
  иначе агент получал бы два разных ответа про один элемент и правил не тот;
- инструмент остаётся read-only (`ReadFiles`), виден в Plan;
- новая точка подключения в `ChatEnvironment`/`MainView` не нужна — правок в студии минимум.

### 1.2 Что появилось

| Файл | Что добавлено |
|---|---|
| `RPA.Chat\Services\Workflow\WorkflowValidationReport.cs` (новый) | `WorkflowValidationReport`: `FromOutline(outline, elementId, maxIssues)`, `Available`, `UnavailableReason`, `Source`/`SourceName`, `Errors`, `Warnings`, `LoadIssues` (только `xaml.path`/`xaml.load`), `ValidationIssues` (`wf.validate`), `Issues`/`TotalIssues`/`HiddenIssues`, `AddressNotFound` + `KnownAddresses`, `ScopeId`, `Ok`; статические `IsLoadIssue`, `IsValidationIssue`, `InScope` |
| `RPA.Chat\Services\Workflow\WorkflowValidationText.cs` (новый) | формат ответа: `валидация: ошибок X, предупреждений Y; источник: конструктор/файл`, `область: …`, строки `ошибка root/Activities[0]: текст (код: wf.validate, свойство: Text)`, блоки «валидация не выполнена» и «адрес не найден», лимит `max_issues` + «ещё N не показаны», подсказка про цикл «правка → валидация → правка» |
| `RPA.Chat\Services\Workflow\WorkflowOutline.cs` | `WorkflowOutlineSource` (`Unknown`/`File`/`Designer`), `Source`, `ValidationAvailable`, `ValidationUnavailableReason`, `MarkValidationAvailable()`, `MarkValidationUnavailable(reason)` — аддитивно, существующие тесты не менялись |
| `AiRpaStudio\Core\Workflow\StudioWorkflowOutlineService.cs` | `AddValidationIssues` помечает валидацию выполненной или недоступной (с причиной); `BuildOutline`/`FromDesigner`/`GetOutline` проставляют источник (файл или живой конструктор) |
| `RPA.Chat\Services\AgentTools\AgentToolFactory.cs` | инструмент `workflow_validate` (`path`, `elementId`, `max_issues`), `mutates: false`, `ReadFiles`, тот же `OutlineService`/`ResolvePath` и то же сообщение «службы нет», что у `workflow_outline` |

### 1.3 Три состояния вместо «пусто = хорошо»

| Состояние | Как выглядит в ответе |
|---|---|
| валидация выполнена, ошибок нет | `валидация: ошибок 0, предупреждений N; источник: …` + `ошибок нет: валидация WF выполнена` |
| валидация выполнена, ошибки есть | счётчики + список с адресами, кодами и свойствами + «ещё N не показаны» |
| **валидация не выполнена** | `валидация: не выполнена` + `причина:` + «не выполнена ≠ ошибок нет» + замечания чтения |
| адрес не найден (фильтр) | `валидация: адрес не найден — …` + известные адреса + подсказка прочитать структуру |

Ключевое: `WorkflowOutline.ValidationAvailable` = `false`, если `ActivityValidationServices.Validate` бросил
исключение. Раньше сбой валидации превращался только в предупреждение `xaml.validate`, и отчёт мог выглядеть
зелёным — теперь это невозможно.

### 1.4 Тесты

| Класс | Тестов | Что покрыто |
|---|---|---|
| `WorkflowValidationReportTests` | 9 | счётчики, разделение чтения и валидации, «валидация не выполнена», отсутствующий файл, фильтр по элементу (по сегментам пути: `root/Then` ≠ `root/Then2`, замечания без адреса не скрываются), неизвестный адрес, лимит `max_issues`, `null`-структура, единство адресов с `WorkflowValidationDelta` (`AG-09`) |
| `WorkflowValidateToolTests` | 12 | реестр (read-only, Plan и Act), обязательный `path`, неверный `max_issues`, отсутствие службы, передача `elementId` без чтения аргументов, источник «конструктор», валидная фикстура, `sequence-broken.xaml` (адреса), фильтр по элементу на реальной службе, неизвестный адрес, отсутствующий файл, `malformed.xaml` |
| `WorkflowValidationTextTests` | 2 | сводка «ошибок X, предупреждений Y; источник: …», различие «ошибок нет» и «валидация не выполнена» |
| `WorkflowEditBridgeTests` (+2) | 2 | живой конструктор: ошибка несохранённой правки (`Assign` без `To`) приходит с адресом `activity_insert`, источник — «конструктор»; глубина истории отмены до и после валидации совпадает (валидация ничего не меняет) |

Итого **25 новых тестов**; полный прогон `AiRpaStudio.Tests` — **1736/1736** (было 1711).

### 1.5 Негативы волны 6 (`AG-12`) — проверено снятием защиты

| Снятая защита | Упавшие тесты |
|---|---|
| игнорируется `ValidationAvailable` (валидация «всегда выполнена») | `Report_ValidationFailed_MarkedUnavailable` |
| снят фильтр по `elementId` | `Report_FiltersByElementId`, `Report_UnknownElementId_ReportsAddressNotFound`, `Report_AndEditDelta_UseTheSameAddresses` |
| ошибки чтения смешаны с замечаниями валидации | `Report_SeparatesLoadIssuesFromValidationIssues`, `Report_MissingFile_NotGreen`, `Tool_MissingFile_ExplainsInsteadOfThrowing`, `Tool_MalformedFile_ReportsLoadIssueOnly` |
| снята проверка `max_issues` | `Tool_RejectsInvalidMaxIssues` |
| снята обработка «служба вернула пустую структуру» | `Tool_WithoutService_ExplainsInsteadOfThrowing` |

Каждая правка «саботажа» откатывалась сразу после прогона; финальное состояние — без саботажа, полный прогон зелёный.

### 1.6 Отклонения от плана и причины

1. **`WorkflowValidationReport` совпал по имени с `RPA.Core.Generation.WorkflowValidationReport`** (отчёт гейта
   валидного XAML, `WorkflowValidationGate.cs`). Это разные сущности: гейт проверяет канонический вид XAML
   перед открытием, отчёт `AG-06` — вердикт агенту. В `DesignerBenchProxy` используется полное имя
   (`RPA.Chat.Services.Workflow.WorkflowValidationReport`) с комментарием, как это уже сделано для
   `WorkflowEditResult`. Переименование гейта не делалось, чтобы не трогать закрытые пункты конвейера генерации XAML.
2. **`depth = 0` и `include_arguments = false`** в вызове структуры: фильтр по адресу требует всё дерево,
   а аргументы вердикту не нужны и занимают контекст модели. Аргументы по-прежнему видны в `workflow_outline`.
3. **Замечания без адреса** (`ElementId` пуст) не скрываются фильтром по элементу: они относятся к процессу
   целиком, и их скрытие дало бы ложный зелёный отчёт.
4. **Фоновая валидация не делалась**: `workflow_validate` идёт существующим путём UI-потока конструктора
   (как `workflow_outline`). Вынос в фон — карточка `UI-02`.

### 1.7 Приёмка волны

- `MSBuild AiRpaStudio.Tests\AiRpaStudio.Tests.csproj -t:Build` — 0 ошибок; `vstest.console.exe` — **1736/1736**.
- `MSBuild AiRpa.sln /p:Configuration=Release` — 0 ошибок из VS 2026 (18) и VS 2022 (17).
- Живой конструктор: `activity_insert` (`Assign` без `To`) → `workflow_validate` показывает ошибку с адресом
  нового элемента и источником «конструктор», история отмены не растёт — цикл «правка → валидация → правка»
  замкнут внутри одного прогона.

### 1.8 Что осталось в волне 2

- `AG-07` (`xaml_read`/`xaml_write` с нормализацией), `AG-08` (создание активности), `AG-10` (разрешения и
  режимы для новых инструментов), `AG-11` (системный промпт: правила WF-XAML и примеры «запрос → tool-call»;
  сюда же описание `workflow_validate`).
- `AG-12`: негативы волн 7-8, откаты, страж реестра WF-инструментов целиком (вместе с `AG-10`).
