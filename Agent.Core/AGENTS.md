# Правила AI-агента (Agent.Core / RPA.Chat)

> Фрагмент иерархии правил решения `AiRpa.sln`. Глобальные правила и общий индекс —
> в корневом `AGENTS.md`. Этот файл поднимается в контекст только при работе с функциями
> AI-агента (`Agent.Core`, `RPA.Chat`, WF-инструменты, `spec_*`, промпты).

## ТЗ и спецификация (`spec_*`, волна 8)
- ТЗ живёт **в проекте**: `<проект>\docs\spec\<slug>\` — `spec.md` (текущая версия), `versions\vNNNN-<sha8>.md`,
  `source\` (исходник байт-в-байт), `meta.json`, `specification.json` (**источник правды**),
  `specification.md` (производное, перегенерируется), `runs\` (прогоны тест-кейсов). В `%AppData%` ТЗ не пишется:
  пустой корень проекта — это отказ с причиной (`SpecificationStore.Import`).
- Извлечение текста `docx`/`xlsx`/`pdf` — только через `Agent.Core\Services\Documents\DocumentTextReaders`
  (`IDocumentTextReader`). Вторых правил чтения одного формата не заводить: reader'ом пользуются `spec_import`,
  вложения чата и `read_files` (`IsBinaryDocument` отличает контейнеры от текста, поэтому `.xaml` идёт прежним путём
  с шапкой метаданных). Скан-PDF без текстового слоя — честный `NoTextLayer`, а не пустая строка.
- Инструменты с префиксом `spec_` входят в **промпт-страж** WF-инструментов (`PromptGuidanceTests.WorkflowPrefixes`):
  (Plan видит только `spec_show`/`spec_coverage`, мутирующие в Plan-промпт не попадают) и
  `AgentPromptBuilder.AppendSpecGuidance` (`SpecReadInstructions` / `SpecEditInstructions`). Таблица «имя → mutates → permission» (`WorkflowToolRegistryGuardTests`) покрывает только четыре WF-префикса — `activity_`, `workflow_`, `argument_`, `variable_`; набор `spec_*` держат промпт-страж и `SpecToolTests`.
- Отчёты печатают три состояния явно: «готово» / «есть замечания с адресами» / «не выполнено (причина)».
  «Не запускался» никогда не выглядит как «пройден» (`SpecRunReport`, `SpecValidationReport.Describe`).
- Прогон тест-кейсов — за контрактом `RPA.Chat\Services\Spec\IWorkflowRunService` (в студии —
  `AiRpaStudio\Core\Workflow\StudioWorkflowRunService` поверх `WorkflowRunEngine`). Исполнитель робота один:
  контракт закрывает студия (`StudioWorkflowRunService`); `RpaRunner.exe` исполняет робота тем же `WorkflowRunEngine`, но `IWorkflowRunService` пока не реализует — отдельная карточка. Дублировать исполнение в UI запрещено.

## WF-инструменты агента: реестр, разрешения, режимы (`AG-10`)
- **Новый WF-инструмент** (`activity_*`, `workflow_*`, `argument_*`, `variable_*`) появляется вместе со строкой
  в таблице `AiRpaStudio.Tests\Workflow\WorkflowToolRegistryGuardTests.cs` (`имя → mutates → permission`).
  Страж сверяет **множества** в обе стороны, поэтому «зарегистрировал и забыл описать» не проходит.
- **Правила, которые держит страж:** read-only → `AgentPermissions.ReadFiles` и виден в Plan; мутирующий →
  `EditFiles`, в Plan не виден (Plan = только чтение при `AgentPermissions.All`); категории MCP больше нет —
  `RPA.Chat`/`Agent.Core` не ссылаются на `AiRpaStudio\Mcp`, упоминание MCP в имени или описании
  WF-инструмента запрещено. Негативы — мета-тесты `Guard_*` в том же файле (подкладывают инструмент с
  неверными флагами и требуют, чтобы проверка сработала).
- **Маска разрешений — одна точка правды:** `RPA.Chat\Services\AgentPermissionsPolicy.cs`
  (`Resolve(SettingsData)`); `ChatViewModel.BuildAllowedPermissions` только делегирует в неё. `null` настроек
  даёт **пустую** маску: отсутствие настроек не превращается в полный доступ.
- **Решение по режимам (`AG-10`):** при `AllowReadFiles=false` + `AllowEditFiles=true` правки разрешены
  (мутирующие инструменты видны), чтение структуры запрещено — вместе с `workflow_outline`. Промпт (`AG-11`)
  требует `workflow_outline` первым шагом, поэтому такой режим неудобен, но не запрещён — так и закреплено
  тестом `SettingsWithoutReadFiles_RemoveEveryReadOnlyWorkflowTool`.

## Текстовая правка XAML: `editor` и один набор правил канонического вида (`AG-07`)
- **Отдельного `xaml_write`/`xaml_read` нет** (решение владельца): текст пишут `editor`/`read_files`.
  Правила канонического вида живут в `RPA.Chat\Services\Workflow\WorkflowXamlText.cs` — один набор и для
  `editor`, и для `WorkflowXamlSerializer`: UTF-8 без BOM, CRLF, без XML-декларации,
  `xmlns:mc/sap/sap2010/mca` + `mc:Ignorable="sap sap2010"`. **Второй нормализатор не заводится:**
  иначе один и тот же файл канонизируется двумя наборами правил.
- **Отступы не переиндентируются — намеренно.** В XAML значимый текст лежит внутри элементов
  (`mca:CSharpValue`, `TextExpression.NamespacesForImplementation`): переиндентация дописала бы отступ внутрь
  выражения. Отступ только читается (`WorkflowXamlText.Describe`) и показывается агенту в шапке `read_files`;
  писать надо в стиле файла.
- **`editor` для `.xaml`:** нормализация перед записью; BOM берётся из фактического файла (новый файл — без BOM);
  отказ без записи, если XML не разбирается или файл открыт в конструкторе (там правят `activity_*`/`argument_set`);
  после записи — валидация через `IWorkflowXamlGate` (`off` / `report` по умолчанию / `strict` с откатом файла).
  Чужой XML (корень не `Activity` рабочего процесса) не переписывается, не-`.xaml` не трогается вообще.
- **«Валидация не выполнена» ≠ «ошибок нет»** и здесь: `WorkflowXamlGateResult.Validated == false` печатается
  отдельной строкой, null-object (`NullWorkflowXamlGate`) называет причину. Хост студии подключает гейт в
  `MainView` (`ChatEnvironment.WorkflowXamlGate = StudioWorkflowXamlGate.Default`), проверка идёт тем же
  `WorkflowValidationGate`, что и у генерации процессов.
- **Тесты-стражи:** `WorkflowXamlTextTests` (правила), `WorkflowEditorXamlTests` (файл, BOM, режимы, откат),
  `WorkflowXamlBridgeTests` (живой конструктор: отказ открытому файлу, настоящий гейт). Негатив обязателен:
  снятие любой проверки должно ронять тест — проверено для расширения, «открыт в конструкторе», CRLF,
  «не выполнена» ≠ «нет ошибок», отката в `strict`, BOM и битого XML.

## Создание активности агентом: служба, инструмент, видимость в палитре (`AG-08`)
- **Создаём только в существующем проекте** библиотеки активностей (решение владельца): проект с нуля из
  шаблона Visual Studio — карточка `UI-08`. Единственный обязательный вход — `projectPath` (`.csproj`);
  пространство имён берётся из `RootNamespace`, конфигурация — из запроса.
- **Служба — `RPA.Chat\Services\Workflow\ActivityCreationService.cs`** (+ DTO `ActivityCreation.cs`). Она не
  зависит от WF/WPF: пишет текст, правит XML проекта, дергает `IBuildFeedbackService`. Шаблоны собираются в
  коде, а не читаются из рабочего каталога: `ActivityCreator.CreateActivityFromTemplate` делал
  `File.ReadAllText("Main.xaml")`, то есть искал файл в CWD процесса, и в студии это падало.
- **`Designer.xaml` не тянет `ActivityLibraryBase`:** в новом проекте этой сборки нет, поэтому дизайнер
  собирается только из `System.Activities.Presentation` (`sapv:ExpressionTextBox` +
  `sapc:ArgumentToExpressionConverter`).
- **`.csproj` правится текстовой вставкой, не перезаписью `XDocument`:** перезапись меняет форматирование
  всего файла (BOM, CRLF, отступы) — это тот же вывод, что в `AG-07` про XAML. Вставка идёт в `ItemGroup`
  с такими же элементами, в алфавитном порядке, с сохранением отступа и BOM; результат обязан разбираться
  как XML (иначе откат).
- **Атомарность — часть контракта:** перед первой записью запоминается `.csproj` побайтно и список созданных
  файлов; исключение или ошибки компиляции при `build=true` → файлы удалены, `.csproj` восстановлен, в ответе
  коды ошибок. «Полуготовой» активности в проекте не остаётся.
- **TFM обязателен `net472`** (`v4.7.2` для legacy, `net472` для SDK-style): иначе робот на 4.7.2 не загрузит
  библиотеку (правило `CP-10`). «Сборка прошла, но собранной сборки нет по ожидаемому пути» — отдельное
  состояние (`RequiresDecision`), а не успех.
- **Видимость результата (вариант B):** `AiRpaStudio\Core\Workflow\StudioActivityPaletteNotifier.cs` —
  1) `Assembly.LoadFrom` со сверкой `assembly.GetName().Name` с именем файла, 2) `StudioActivityPalette.RegisterUserLibrary`
  (список пользовательских категорий рядом с `LibraryAssemblyNames`), 3) сброс **обоих** кэшей:
  `ActivityPaletteRegistry.InvalidateDefault()` и `StudioActivityCatalog.InvalidateDefault()` (оба были
  `readonly Lazy` — до `AG-08` сброса не существовало), 4) пересборка панели на UI-потоке
  (`MainView.RefreshActivityPalette` → `new WFToolboxHelper().Build()` + `ApplyTheme`). `WFToolboxHelper.Build`
  теперь сбрасывает `ActivitiesCount` (было «только прибавляется»), а панель получает пользовательские
  категории через `StudioActivityPalette.UserCategories()`.
- **Инструмент `activity_create`** — 14-й WF-инструмент: `mutates`, `AgentPermissions.EditFiles`, в Plan не
  виден; строка в таблице `WorkflowToolRegistryGuardTests` (правило `AG-10`). Ответ — `ActivityCreateText`:
  создано/изменено, «сборка не запрашивалась» / «сборка прошла, файла нет» / «сборка ошиблась», состояние
  палитры отдельной строкой, затем «требует решения».
- **Тесты:** `ActivityCreationServiceTests` 17, `ActivityCreateToolTests` 9, `ActivityPaletteRefreshTests` 9.
  В тесте видимости кэши **прогреваются до** создания активности: иначе `Lazy` инициализировался бы уже
  после регистрации типов и сброс кэшей не проверялся бы вовсе (это поймал негатив: снятый
  `InvalidateDefault` не ронял тест, пока кэш не стали прогревать заранее).
- **Негативы (проверены снятием):** `RegisterUserLibrary` → 3 падения; сброс кэшей → 1; откат файлов → 1;
  `activity_create` как read-only → 4.
- **Граница с `UI-08` по панели «Активности»:** здесь механика «видно без перезапуска» (домен, кэши, подмена
  содержимого); в `UI-08` остаётся UX — сохранение скролла/выделения/свёрнутости, кнопка «Обновить палитру»,
  иконки (с `UI-05`), диалог создания активности и проект из шаблона.

## Промпт агента: имена инструментов, блок про процесс, маркап в Native (`AG-11`)
- **Имя инструмента в промпте обязано существовать в реестре.** Страж —
  `AiRpaStudio.Tests\PromptGuidanceTests.cs`: он собирает все snake_case-токены из строк `PromptDefaults` и
  требует, чтобы каждый был зарегистрированным инструментом `AgentToolFactory` (исключение — короткий
  задокументированный список параметров и подстановок: `start_line`, `end_line`, `include_arguments`,
  `old_text`, `new_text`, `tool_call`, `mode_instructions`). Отдельный тест запрещает «инструменты» VSIX-эпохи
  `edit_open_document` / `apply_fix` / `write_file` и отклонённые владельцем `xaml_write` / `xaml_read`.
  Правило: **новый инструмент описывается в промпте своим настоящим именем**, иначе сборка тестов краснеет.
- **Промпты по умолчанию — только `Agent.Core\Services\PromptDefaults.cs`** (решение владельца): файлы
  `%AppData%\Agent\Prompts\*.txt` не перезаписываются, `PromptLibrary.Load` перекрывает defaults. Следствие:
  там, где такие файлы есть, правка defaults не подействует — известное ограничение, миграция пользовательских
  промптов отдельной карточкой после волны 7.
- **Блок про процесс добавляется только когда WF-инструменты реально есть в реестре** —
  `AgentPromptBuilder.AppendWorkflowGuidance`: Plan получает `WorkflowReadInstructions`, Act —
  `WorkflowReadInstructions` + `WorkflowEditInstructions` + примеры. Признак WF-инструмента — префикс имени
  (`activity_`, `workflow_`, `argument_`, `variable_`), то же правило, что в страже реестра (`AG-10`). Промпт
  не описывает то, что нельзя вызвать: реестр без WF-инструментов не получает ни блока, ни примеров.
- **Две редакции примеров:** `WorkflowExamplesJson` и `WorkflowExamplesNative`. Нативный промпт не показывает
  JSON action objects, JSON-промпт не показывает «call … with arguments». Примеры правятся вместе: тест
  требует четыре примера и покрытие `activity_insert`, `argument_set`, `workflow_validate`, `activity_create`.
- **Правила, которые промпт обязан повторять (они же в коде):** цикл `workflow_outline` → правки модели →
  `workflow_validate` → `build` → `read_errors`; одни и те же адреса `root/Activities[0]` у чтения, правки и
  валидации; «валидация не выполнена» ≠ «ошибок нет»; XAML руками — только через `editor`, только когда правок
  модели не хватает (и `editor` откажется, если файл открыт в конструкторе, `AG-07`); `activity_create` — только
  в существующем проекте (`AG-08`).
- **Маркап в Native-режиме исполняется, а не показывается:** если модель вернула `<invoke>`/`<parameter>` вместо
  нативного вызова, `AgentRunner` разбирает ответ через `AgentResponseParser` (внутри — `DsmlNormalizer`) и
  выполняет восстановленные вызовы через `ExecuteJsonToolCallAsync` — значит проверки режима и разрешений те же,
  что у обычного вызова. Лимит — `ProtocolRecoveryLimit` (2 попытки), после чего текст возвращается
  пользователю как есть: выбрасывать ответ модели нельзя.
- **Усечение структуры объясняет, как запросить меньше:** `TruncateToolResult` дописывает к `workflow_outline`
  подсказку про меньший `depth` / `include_arguments` / один элемент. Молча укороченное дерево агент принимает
  за полное и правит не в том месте.
- **Тесты:** `PromptGuidanceTests` 13, `DsmlToleranceTests` 8, `NativeMarkupRecoveryTests` 4,
  `WorkflowContextLoadTests` 2 (контекст на WF-нагрузке: большая история конденсируется, свежий outline
  остаётся в окне целиком). Негативы — снятие 5 защит: `write_file` в промпте → 2 падения, блок WF не
  добавляется → 4, WF-блок попал и в Plan → 3, Native без восстановления маркапа → 3, усечение без подсказки → 1.
