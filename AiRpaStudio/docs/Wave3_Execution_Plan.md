# Волна 7 «Агент и инструменты workflow, часть 3» — журнал выполнения

| Поле | Значение |
|---|---|
| Документ | `AiRpaStudio\docs\Wave3_Execution_Plan.md` — журнал волны 7 из `AiRpaStudio\docs\ROADMAP.md` (§6) |
| Область волны 7 | `AG-07` (нормализация XAML) + `AG-08` (создание активности) + `AG-10` (разрешения и страж реестра) + `AG-11` (промпт, `DsmlNormalizer`, контекст) + `AG-12` (сводные негативы волны) |
| Дата | 07.11.2026 |
| Правило | Пункт закрывается только с тестом, полным зелёным прогоном `AiRpaStudio.Tests` (MSBuild из VS + `vstest.console.exe`, не `dotnet test`) и сборкой `AiRpa.sln` Release из VS 2022 **и** VS 2026 |

## 1. Состав волны и порядок

| # | ID | Приоритет | Суть | Оценка | Итог |
|---|---|---|---|---|---|
| 1 | `AG-10` | P1 | Маска разрешений в одной точке + таблица-страж всех WF-инструментов + инвариант «нет MCP» | 0,5 дн | ✅ 07.11.2026 |
| 2 | `AG-07` | P1 | Канонический вид XAML внутри `editor`, гейт «примет ли конструктор», шапка метаданных в `read_files` | 1,0 дн | ✅ 07.11.2026 |
| 3 | `AG-08` | P1 | Создание активности в существующем проекте библиотеки: файлы, `.csproj`, сборка, загрузка в домен, палитра | 3,0 дн | ✅ 07.11.2026 |
| 4 | `AG-11` | P1 | Промпт по реестру, восстановление маркапа в Native-ветке, подсказка при усечении `workflow_outline` | 1,5 дн | ✅ 07.11.2026 |
| 5 | `AG-12` | **P0** | Сводные негативы волны: повторная проверка саботажем, журнал, приёмка | 1,0 дн | ✅ 07.11.2026 |

**Итого ≈ 7 рабочих дней.** Порядок исполнения и его причина:

1. **`AG-10` первым** — рамка для остальных: новый инструмент (`activity_create`) сразу получает разрешение,
   режим и строку в страже; иначе страж пришлось бы переписывать дважды.
2. **`AG-07` перед `AG-08`** — генератор дизайнера активности пишет XAML; канонический вид должен быть один
   на `editor` и на сериализатор, иначе два пути дают разные файлы.
3. **`AG-11` после инструментов** — промпт описывает то, что уже зарегистрировано, и только если реестр
   для этого режима действительно содержит WF-инструменты.
4. **`AG-12` последним**, но негативы писались внутри каждой карточки; `AG-12` закрывает сводную проверку.

## 2. Решения по архитектуре

| Решение | Почему так |
|---|---|
| **Один нормализатор XAML** — `RPA.Chat\Services\Workflow\WorkflowXamlText.cs`, `WorkflowXamlSerializer` делегирует в него | Два набора правил на один файл дают разные ответы «валидно» у генерации процесса и у `editor` |
| **Гейт через контракт** — `IWorkflowXamlGate` + `NullWorkflowXamlGate` в `RPA.Chat`, реализация `StudioWorkflowXamlGate` поверх существующего `WorkflowValidationGate` | Второй проверки «примет ли конструктор» не пишется; отсутствие гейта отвечает «валидация не выполнена», а не «ошибок нет» |
| **Отступы XAML не переиндентируются** | В XAML значимый текст лежит внутри элементов (`mca:CSharpValue`, `TextExpression.*`); добавленный отступ стал бы частью выражения. Отступ только читается и показывается агенту |
| **Нормализация включается по расширению `.xaml`** (и не трогает `.xaml.cs`) | Иначе «умный» `editor` переписывает `.cs`/`.json`/`.md`; проверено негативом `E06` |
| **Реестр WF-инструментов — таблица-страж**, сверка множествами | До `AG-10` разрешения проверялись группами по отдельности, и новый инструмент можно было зарегистрировать вообще без строки в страже |
| **Промпт выводится из реестра** — `AgentPromptBuilder.AppendWorkflowGuidance` вставляет WF-блок только если для режима есть WF-инструменты (Plan — читающая часть, Act — правки и примеры) | Промпт не описывает то, что нельзя вызвать; проверено негативами `E21`/`E22` |
| **Маска разрешений в одной точке** — `RPA.Chat\Services\AgentPermissionsPolicy.Resolve(SettingsData)`; `null` настроек = ничего не разрешено | Пока правило жило приватным методом `ChatViewModel`, «выключил правку файлов, а мутирующие WF-инструменты остались» ловилось только руками |
| **Видимость палитры — «вариант B»**: загрузка сборки в домен со сверкой имени → `RegisterUserLibrary` → сброс **обоих** кэшей → пересборка панели на UI-потоке | Тип, не загруженный в домен, не виден ни каталогу агента, ни панели; UX панели (скролл, иконки, кнопка «Обновить») остаётся `UI-08` |
| **Регистрация в `.csproj` — текстовой вставкой**, а не перезаписью `XDocument` | Перезапись меняет BOM, CRLF и порядок `Include` — diff на весь файл; вывод `AG-07` переносится на проект |
| **`%AppData%\Agent\Prompts\*.txt` не перезаписываются** (решение владельца) | Пользовательские промпты перекрывают defaults; миграция — отдельной карточкой после волны 7 |

## 3. Что появилось

| Файл | Карточка | Что добавлено |
|---|---|---|
| `RPA.Chat\Services\Workflow\WorkflowXamlText.cs` | `AG-07` | `WorkflowXamlKind` (`Workflow`/`OtherXml`/`Malformed`), `Normalize(text, options)` (CRLF, без XML-декларации, `xmlns:mc/sap/sap2010/mca`, `mc:Ignorable`, идемпотентность), `Describe(...)` для шапки `read_files`, `IsWorkflowXamlFile`, `IsWorkflowRoot`, `StripBom` |
| `RPA.Chat\Services\Workflow\WorkflowXamlGate.cs` | `AG-07` | `WorkflowXamlGateResult` с тремя состояниями (`Validated`/`Errors`/`Reason`), `IWorkflowXamlGate`, `NullWorkflowXamlGate` |
| `AiRpaStudio\Core\Generation\StudioWorkflowXamlGate.cs` | `AG-07` | Обёртка над `WorkflowValidationGate`; сбой проверки → «не выполнена», а не «ошибок нет» |
| `AiRpaStudio\Core\Generation\WorkflowXamlSerializer.cs` | `AG-07` | Переведён на единый нормализатор |
| `RPA.Chat\Services\AgentTools\AgentToolFactory.cs` | `AG-07`, `AG-08` | `editor` для `.xaml`: нормализация, сохранение BOM из файла, отказ без записи (битый XML, файл открыт в конструкторе), режимы `validate` `off`/`report`/`strict` с откатом; `read_files` для `.xaml` отдаёт шапку (корень, размер, BOM, EOL, отступ, `xmlns`, «в конструкторе»); инструмент `activity_create` (14-й WF-инструмент) |
| `RPA.Chat\Services\Workflow\ActivityCreation.cs`, `ActivityCreationService.cs`, `ActivityCreateText.cs` | `AG-08` | DTO запроса/результата и служба: `.cs` с аргументами и атрибутами, `Designer.xaml` + code-behind, регистрация в `.csproj`, сборка через `IBuildFeedbackService`, поиск сборки по `OutputPath`/`AssemblyName`, побайтный откат |
| `RPA.Chat\Services\Workflow\ActivityPaletteNotifier.cs`, `ActivityCatalogText.cs`, `ActivityInfo.cs`, `IActivityCatalog.cs` | `AG-08` | Контракт `IActivityPaletteNotifier` + null-реализация с причиной; текстовые ответы каталога и `activity_create` |
| `AiRpaStudio\Core\Workflow\StudioActivityCatalog.cs`, `StudioActivityPaletteNotifier.cs`, `AiRpaStudio\Core\Generation\ActivityPaletteRegistry.cs` | `AG-08` | Сброс обоих кэшей (`InvalidateDefault`), загрузка сборки в домен со сверкой имени, `RegisterUserLibrary`, пересборка панели на UI-потоке |
| `AiRpaStudio\Models\ActivityCreator.cs` | `AG-08` | Диалог переведён на службу — ушло чтение `Main.xaml` из рабочего каталога процесса |
| `RPA.Chat\Services\AgentPermissionsPolicy.cs` | `AG-10` | Единственная точка перевода переключателей настроек в маску разрешений; `null` настроек — пустая маска |
| `RPA.Chat\ViewModels\ChatViewModel.cs` | `AG-10` | Делегирует в `AgentPermissionsPolicy` |
| `Agent.Core\Services\PromptDefaults.cs` | `AG-11` | Из ACT-инструкций убраны `edit_open_document`/`apply_fix`/`write_file`, добавлены `editor`/`run_commands`/`build`/`read_errors`; `WorkflowReadInstructions`, `WorkflowEditInstructions`, `WorkflowExamplesJson`, `WorkflowExamplesNative` |
| `Agent.Core\AgentTools\AgentPromptBuilder.cs` | `AG-11` | `AppendWorkflowGuidance`: WF-блок только при наличии WF-инструментов в реестре для этого режима |
| `Agent.Core\AgentTools\AgentRunner.cs` | `AG-11` | Native-ветка восстанавливает маркап и **исполняет** вызов (`ExecuteJsonToolCallAsync`, те же проверки режима и разрешений, лимит 2 попытки); усечение `workflow_outline` несёт подсказку про `depth`/`include_arguments`/один элемент |

## 4. Тесты волны 7 (фактические числа прогона)

| Класс | Тестов | Что покрыто |
|---|---|---|
| `WorkflowToolRegistryGuardTests` | 17 | таблица ожиданий на 14 WF-инструментов, сверка множествами, Plan/Act, категории разрешений, настройки → маска → реестр, инвариант «нет MCP» (имена, описания, исходники `RPA.Chat`/`Agent.Core`), 5 мета-тестов `Guard_*` |
| `ChatViewModelTests` | 13 | в т.ч. `PermissionsPolicy_NullSettings_DoesNotThrow` — `null` настроек не превращается в полный доступ |
| `WorkflowXamlTextTests` | 17 | BOM/EOL/декларация/`mc:Ignorable`/`xmlns`, сохранение текста выражений и отступов, идемпотентность, «не рабочий процесс → не переписывается», `IsWorkflowXamlFile` по расширению |
| `WorkflowEditorXamlTests` | 15 | нормализация при записи, `preserve_bom`, не-`.xaml` не трогаем, битый XML, файл открыт в конструкторе, `report`/`strict`/`off`, «валидация не выполнена» ≠ «ошибок нет», откат существующего файла и удаление созданного |
| `WorkflowXamlBridgeTests` | 2 | живой конструктор: отказ `editor` для открытого `.xaml`; нормализация + настоящий гейт для закрытого |
| `ActivityCreationServiceTests` | 17 | успешное создание файлов и регистрация в `.csproj`, `withDesigner=false`, TFM `net472`, отсутствующий/чужой проект, дубль класса, негодное имя, откат при исключении и при ошибках сборки, побайтное сравнение `.csproj`, поиск сборки по `OutputPath`/`AssemblyName` |
| `ActivityCreateToolTests` | 11 | реестр и режимы, обязательные аргументы, не-`.csproj`, дубль, `build=true` без службы сборки, откат, «служба не подключена», palette-notifier только при успехе |
| `ActivityPaletteRefreshTests` | 9 | загрузка сборки в домен, тип появляется в каталоге и палитре без перезапуска, сброс обоих кэшей, сверка имени сборки, пустая сборка, повторная регистрация |
| `PromptGuidanceTests` | 13 | ACT-инструкции не упоминают несуществующие инструменты, каждое упомянутое имя зарегистрировано, WF-блок появляется только при WF-инструментах, Plan получает только читающую часть, 4 примера в двух редакциях |
| `DsmlToleranceTests` | 8 | терпимость нормализатора: пустые и битые аргументы, мусор до и после, кавычки внутри JSON, `>` в значении, пробелы в имени, «не маркап → текст как есть» |
| `NativeMarkupRecoveryTests` | 4 | Native-ветка исполняет восстановленный маркап, лимит 2 попытки, блокировка мутирующего инструмента в Plan, затем текст как есть |
| `WorkflowContextLoadTests` | 2 | усечение `workflow_outline` несёт подсказку, свежий outline остаётся в окне целиком |

Суммарно по классам волны — **156 тестов**. Полный прогон `AiRpaStudio.Tests`: **1855/1855**
(было 1736 на закрытии волны 6, затем 1757 после `AG-10`, 1791 после `AG-07`, 1828 после `AG-08`, 1855 после `AG-11`).

## 5. Повторная проверка саботажем (`AG-12`) — 24 эксперимента

Метод: production-код правится на 1–3 строки, тестовый проект собирается, прогоняется **фильтром по классам**,
записываются упавшие тесты, файл восстанавливается из копии, проверяется **побайтная** идентичность
(`SHA256` файла и `git diff` по `RPA.Chat`/`Agent.Core`/`AiRpaStudio`). Полный прогон — только в начале и в конце.

### 5.1 `AG-10` — разрешения, режимы, страж реестра

| # | Снятая защита | Упавшие тесты |
|---|---|---|
| E01 | `argument_set` помечен `mutates: false` | `PlanMode_HidesEveryMutatingWorkflowTool`, `Registry_WorkflowToolFlags_MatchTable` |
| E02 | `variable_remove` переведён в `ReadFiles` | `MutatingWorkflowTools_RequireEditFiles`, `Registry_WorkflowToolFlags_MatchTable`, `SettingsWithoutEditFiles_RemoveEveryMutatingWorkflowTool`, `SettingsWithoutReadFiles_RemoveEveryReadOnlyWorkflowTool` |
| E03 | в реестр добавлен фиктивный `activity_fake` | `ActMode_ShowsEveryWorkflowTool`, `ReadOnlyWorkflowTools_AreVisibleWithReadFilesOnly`, `Registry_WorkflowToolSet_EqualsTable`, `SettingsWithoutEditFiles_RemoveEveryMutatingWorkflowTool` |
| E04 | `AgentPermissionsPolicy.Resolve(null)` → полный доступ | `PermissionsPolicy_NullSettings_DoesNotThrow` |
| E05 | в `RPA.Chat` добавлена ссылка на `AiRpaStudio\Mcp` | `RpaChatAndAgentCore_DoNotReferenceStudioMcp` |

### 5.2 `AG-07` — нормализация XAML, гейт, режимы записи

| # | Снятая защита | Упавшие тесты |
|---|---|---|
| E06 | снята проверка расширения `.xaml` | `IsWorkflowXamlFile_DependsOnExtension("Main.xaml.cs",False)`, `("Activity.cs",False)`, `("main.txt",False)` |
| E07 | снят отказ «файл открыт в конструкторе» | `Editor_RefusesWorkflowOpenInDesigner` |
| E08 | нормализатор пишет LF вместо CRLF | `Normalize_RemovesXmlDeclarationAndUsesCrLf`, `Normalize_PreservesExpressionTextAndFileIndentation`, `Normalize_DoesNotDuplicateAlreadyDeclaredPrefixes` |
| E09 | «валидация не выполнена» выдана за «ошибок нет» | `Editor_NotPerformedValidationIsNotReportedAsNoErrors` |
| E10 | снят откат файла в `strict` | `Editor_StrictModeDeletesNewFileOnValidationErrors`, `Editor_StrictModeRestoresPreviousContentOnValidationErrors` |
| E11 | нормализатор теряет BOM существующего файла | `Editor_PreservesBomOfExistingFile` |
| E12 | снят отказ при битом XML | `Editor_RefusesMalformedXamlWithoutWriting` |

### 5.3 `AG-08` — создание активности, откаты, палитра

| # | Снятая защита | Упавшие тесты |
|---|---|---|
| E13 | `Rollback` стал пустым (нет отката файлов и `.csproj`) | `Create_RollsBackFiles_WhenBuildFails` |
| E14 | снят откат при ошибках компиляции | `Create_RollsBackFiles_WhenBuildFails` |
| E15 | снята проверка TFM `net472` | `Create_RejectsProjectWithoutNet472` |
| E16 | снята проверка дубля класса | `Create_RejectsDuplicateClassName` |
| E17 | кэши каталога и палитры не сбрасываются | `Notify_LoadsAssembly_AndTypeAppearsInCatalogWithoutRestart` |
| E18 | `activity_create` помечен read-only | `PlanMode_HidesEveryMutatingWorkflowTool`, `Registry_WorkflowToolFlags_MatchTable` |
| E19 | палитра уведомляется до успешной сборки | `ActivityCreate_RollsBackOnBuildFailure_AndDoesNotNotifyPalette`, `ActivityCreate_WithoutBuild_DoesNotNotifyPalette` |

### 5.4 `AG-11` — промпт, восстановление маркапа, усечение контекста

| # | Снятая защита | Упавшие тесты |
|---|---|---|
| E20 | `write_file` вернулся в ACT-инструкции | `PromptTexts_DoNotMentionLegacyTools`, `PromptTexts_EveryToolNameMentioned_IsRegisteredTool` |
| E21 | WF-блок не вставляется в промпт | `ActNativePrompt_DescribesWorkflowEditCycle`, `ActPrompt_ContainsFourWorkflowExamples`, `ActPrompt_ExamplesCoverInsertArgumentValidationAndCreation`, `ActPrompt_HandWrittenXamlRule_MentionsEditorAndDesignerRefusal` |
| E22 | WF-блок правок попал и в Plan | `PlanPrompt_DescribesWorkflowReadCycle`, `PlanPrompt_OffersOnlyReadOnlyWorkflowTools`, `PlanNativePrompt_OffersOnlyReadOnlyWorkflowTools` |
| E23 | Native-ветка не восстанавливает маркап | `RunAsync_NativeMarkupInsteadOfCall_ExecutesRecoveredCall`, `RunAsync_NativeMarkupRecovery_IsLimitedAndThenShownAsText`, `RunAsync_NativeMarkupForMutatingToolInPlanMode_IsBlocked` |
| E24 | усечение `workflow_outline` без подсказки | `RunAsync_LongWorkflowOutline_TruncationCarriesNarrowingHint` |

### 5.5 Вывод

**24 из 24 экспериментов пойманы**, ни одного «тихого» негатива: у каждой защиты есть конкретный тест,
который падает при её снятии. Все правки откачены, дерево вернулось к базовой линии побайтно
(`git diff` по `RPA.Chat`/`Agent.Core`/`AiRpaStudio` до и после кампании — одинаковый SHA256;
`robocopy /MIR /L` по этим каталогам — 0 изменённых записей).

## 6. Отклонения от плана и причины

1. **Отступы XAML не переиндентируются** — это не упущение, а решение: в XAML значимый текст лежит внутри
   элементов (`mca:CSharpValue`, `TextExpression.NamespacesForImplementation`), и переиндентация изменила бы
   смысл файла. Отступ читается и показывается агенту, чтобы он писал в том же стиле.
2. **`Designer.xaml` без зависимости от `ActivityLibraryBase`** — иначе библиотека активностей, созданная
   в чужом решении, просто не собирается. В дизайнере только `x:Class` и `sapv:ExpressionTextBox`.
3. **Регистрация в `.csproj` текстовой вставкой**, а не перезаписью `XDocument`: BOM, CRLF, отступы и порядок
   `Include` сохраняются, diff остаётся точечным.
4. **Native-ветка `AgentRunner` исполняет восстановленный маркап**, а не показывает разметку в чат — иначе
   «модель напечатала вызов текстом» остаётся тупиком для пользователя. Ограничение — 2 попытки.
5. **Фактическое число тестов 1855 против прогноза плана** (прогноз волны 7 сходился на 1790–1810):
   добавились `ActivityCatalogTests` (28) и расширения `WorkflowArgumentToolTests` (+4), `WorkflowExpressionTests`
   (−4 к прогнозу), `ActivityCreateToolTests` (+2 к §4.3), `ChatViewModelTests` (+9 к §2.3). Причина —
   тесты каталога активностей, без которых `activity_search` после `activity_create` нечем было проверить.
6. **Числа в карточках `AG-07`/`AG-08`/`AG-11` на доске — снимки на момент закрытия карточки** (1791 / 1828 / 1855),
   а не финальные: каждая следующая карточка добавляла тесты. Единая точка правды — §7 этого журнала.
7. **`WorkflowToolRegistryGuardTests` считает 17 тестов, а не 6–8 из плана**: 9 проверок реестра/режимов/категорий
   + 3 стража настроек и «нет MCP» + 5 мета-тестов `Guard_*`, которые подкладывают в реестр инструмент с
   намеренно неверными флагами. Мета-тесты — требование «страж нельзя успокоить, не тронув production-код».

## 7. Приёмка волны

| Проверка | Результат |
|---|---|
| `MSBuild AiRpaStudio.Tests\AiRpaStudio.Tests.csproj -t:Build` (VS 2026, 18) | 0 ошибок |
| `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll` | **1855/1855**, 1 мин 24 с |
| `MSBuild AiRpa.sln /p:Configuration=Release /restore` — VS 2026 (18) | 0 ошибок, 10 предупреждений |
| `MSBuild AiRpa.sln /p:Configuration=Release /m:1` — VS 2022 (17) | 0 ошибок, 140 предупреждений |
| Саботаж 24 негативов | 24/24 пойманы (§5) |
| Возврат дерева к базовой линии | `git diff` SHA256 совпадает, `robocopy /MIR /L` — 0 изменений |

**Release-прогон тестов не делается намеренно:** он падает по известному `StackOverflowException`
в `ToolboxDragContractTests` (см. правило прогонов в `AGENTS.md`). Приёмка — Release-**сборка**;
прогон — Debug + `vstest`, по правилу `AGENTS.md`.

## 8. Что осталось после волны 7

| Пункт | Кто закрывает |
|---|---|
| `UI-02` (P0) — валидация в дизайнере: запуск после загрузки XAML, освобождение валидатора при закрытии, фоновая валидация, валидация кастомных активностей | следующая волна, первым |
| `UI-08` (P1) — диалог «Создать активность», проект из VS-шаблона, UX панели «Активности» (скролл, выделение, иконки, кнопка «Обновить») | следующая волна |
| Миграция пользовательских промптов `%AppData%\Agent\Prompts\*.txt` | отдельная карточка (решение владельца) |
| `AG-13` / `TD-09` — судьба слоя `Mcp\*` (вынести WF-инструменты в MCP-сервер либо вычистить) | после волны 7 |
| `TD-07` (ручная приёмка темы VS2026), `TD-08` (аудит `TODO.txt`), `TD-11` (`MSB3277`) | отдельные карточки |
| `TZ-01`…`TZ-09` — приём ТЗ и генерация робота по спецификации | следующая волна (P0-цепочка) |
