# План: генерация валидного XAML, который сразу открывается в конструкторе

Статус: этапы 1–6 сделаны (стенд конструктора, канонический XAML и гейт валидации, открытие вкладки в конструкторе, инструмент палитры, Code-First через Roslyn, JSON AST, контракт для агента); этап 8 — план работ
Область: `AiRpaStudio` (студия, WPF + WF rehosted designer), `AiRpaStudio\Mcp`, `Agent.Core`, `AiRpaStudio.Tests`
Связанные документы: `AGENTS.md`, `docs\MCP_Architecture.md`, `docs\MCP_Workflow_Tools_Guide.md`, `docs\Workflow_Json_Ast_Schema.md`, `docs\Workflow_Agent_Contract.md`, `docs\AI_Activity_Integration_Architecture.md`

---

## 1. Главное требование и что оно меняет

Формулировка требования, принятая за основу:

> Результат работы ИИ-агента — **валидный XAML**, который **сразу отображается в конструкторе** студии.
> Смысл инструментов не в «генерации текста похожего на XAML», а в том, что пользователь видит готовый
> рабочий процесс на диаграмме и может продолжить его редактировать руками.

Из этого следуют три жёстких вывода, которые отменяют часть решений из предыдущего обсуждения.

### 1.1. XAML — не текст, а результат сериализации объектной модели

Любая схема «агент пишет XAML-строку → пишем её в файл» отбрасывается: текстовый XAML, собранный
конкатенацией или шаблоном, не содержит того, что дизайнер считает обязательным (ViewState, атрибуты
редактора выражений, коллекции пространств имён и ссылок на сборки, `x:Class`, `AssemblyReference` проекта),
и в конструкторе даёт либо «пустую» диаграмму, либо модальное окно «Конструктор рабочих процессов».

Единственный надёжный путь:

```
вход агента (C#-код или JSON AST)
        ↓  (Roslyn / транспатор, в песочнице)
объектная модель WF: ActivityBuilder / Activity
        ↓  (сериализатор WF, не наш шаблон)
канонический XAML (тот же формат, что сохраняет сама студия)
        ↓  (гейт валидации, п.5)
открытие вкладки в конструкторе (WorkflowDesigner.Load)
```

Канонический XAML обязан выглядеть как файл, который сохраняет сама студия — эталон:
`RPA-1\Main.xaml`. Там уже есть всё, что ожидает конструктор:

- корневой `<Activity>` c `x:Class="RPA_1.Main"` и `mc:Ignorable="sap sap2010 sads"`;
- `sap2010:ExpressionActivityEditor.ExpressionActivityEditor="C#"` — без него выражения не редактируются
  Roslyn-редактором студии;
- `xmlns:mca="clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities"` — пространство имён
  C#-выражений;
- `sap2010:WorkflowViewState.IdRef` на каждом элементе — состояние дизайнера;
- `TextExpression.NamespacesForImplementation` / `TextExpression.ReferencesForImplementation`
  (в `ReferencesForImplementation` обязательна ссылка на сборку проекта, например `RPA-1`);
- переменные как `Variable x:TypeArguments="x:String"` внутри `Sequence.Variables`.

### 1.2. C#-код агента не становится XAML напрямую

Вариант «Code-First» сохраняется как **способ описания логики**, но не как способ сериализации:
C#-код компилируется Roslyn в сборку, из неё получается `ActivityBuilder`, и только потом WF-сериализатор
пишет XAML. Лямбды (`InArgument` с делегатом) при этом запрещены так же жёстко, как и раньше: они не
переживают сериализацию и ломают не только persistence, но и отображение (выражение в дизайнере
превращается в пустой тег / невалидный узел). Только `CSharpValue<T>("...")` и `CSharpReference<T>("...")`.

### 1.3. Показ в конструкторе — часть контракта инструмента, а не «дело пользователя»

Инструмент считается успешным только если после его вызова:
1. файл `.xaml` записан и повторно загружается как WF-активность (`ActivityXamlServices.Load`);
2. `WorkflowDesigner.Load(path)` не бросил исключение и построил визуальное дерево;
3. `ActivityValidationServices.Validate` даёт **0 ошибок** (warnings допустимы, но возвращаются агенту);
4. в студии открыта вкладка конструктора с этим процессом, и она активна;
5. студия осталась работоспособной (никаких модальных WF-окон, блокирующих UI).

---

## 2. Целевая архитектура

### 2.1. Три уровня, один выход

| Уровень | Назначение | Риск для дизайнера |
|---|---|---|
| `xaml.create_workflow_from_code` (C#/Roslyn) | сложная логика: условия, циклы, вложенность | средний (ошибки типов, лямбды) |
| `xaml.add_activity` / `xaml.set_argument` (палитра) | простые RPA-шаги, точная вставка в контейнер | минимальный |
| `xaml.create_workflow_from_json` (JSON AST) | детерминированная сборка по строгой схеме | минимальный |

Все три уровня обязаны заканчиваться **одним** конвейером: `WorkflowBuildService` → канонический XAML →
`WorkflowValidationGate` → `DesignerOpenService`. Дублировать сериализацию и открытие вкладки нельзя:
именно расхождение путей даёт «работает в одном инструменте, ломается в другом».

### 2.2. Компоненты (новые классы)

| Компонент | Ответственность | Куда |
|---|---|---|
| `WorkflowBuildService` | принять `Activity`/`ActivityBuilder`, собрать `ActivityBuilder` с `x:Class`, коллекции `TextExpression`, сохранить XAML сериализатором WF | `AiRpaStudio\Core\Generation\` |
| `WorkflowCanonicalizer` | привести готовый `ActivityBuilder` к формату студии: имя класса, expression editor `C#`, namespace/assembly-ссылки, `AssemblyReference` проекта | там же |
| `WorkflowValidationGate` | `ActivityXamlServices.Load` + `ActivityValidationServices.Validate` + отчёт `{ok, errors[], warnings[]}` | там же |
| `DesignerOpenService` | открыть/переиспользовать вкладку конструктора на UI-потоке, вернуть `workflowId`/`filePath`, дождаться отрисовки | `AiRpaStudio\Core\Generation\` (поверх `LoadActivityToDesignerHelper`) |
| `RoslynSandbox` | компиляция кода агента: whitelist сборок/пространств имён, лимиты времени и размера, запрет `System.IO`/`Reflection`/`Net`/`Diagnostics` | `Agent.Core` или `AiRpaStudio\Core\Generation\` |
| `ActivityPaletteRegistry` | метаданные активностей: имя, тип, аргументы (тип/направление/обязательность), допустимые родительские контейнеры | `AiRpaStudio\Core\Generation\` |
| `XamlExpressionPolicy` | правило «нет лямбд»: поиск делегатов внутри `InArgument`/`OutArgument`/`Variable` до сохранения | `AiRpaStudio\Core\Generation\` |

### 2.3. Требования к среде конструктора (иначе «валидный» XAML всё равно не отрисуется)

Уже выполнено в `LoadActivityToDesignerHelper.LoadActivityToDesigner`, но должно быть зафиксировано тестами
и не должно теряться при новых путях открытия:

- `DesignerConfigurationService.TargetFrameworkName = .NETFramework 4.5`;
- `LoadingFromUntrustedSourceEnabled = true`;
- `ExpressionTextBox.RegisterExpressionActivityEditor(CSharpValue<string>.Language, typeof(RoslynExpressionEditor), ...)`
  и `Publish<IExpressionEditorService>(new RoslynExpressionEditorService())`;
- `Publish<IValidationErrorService>(new DebugValidationErrorService())`;
- `AssemblyContextControlItem` (набор referenced assembly + имя локальной сборки проекта) — без ссылки на
  сборку проекта активности из `RPA-1` не резолвятся, конструктор показывает ошибку загрузки;
- открытие строго на STA/UI-потоке (MCP-вызовы приходят из фонового потока → `Dispatcher.InvokeAsync`).

---

## 3. Этапы работ

Оценки — в рабочих днях одного разработчика. Порядок жёсткий: каждый этап закрывается тестами
(TDD по `AGENTS.md`: сначала падающий тест в `AiRpaStudio.Tests`, затем реализация).

### Этап 1. Тестовый стенд для конструктора (2 дня) — СДЕЛАНО

Без него бессмысленно двигаться: «XAML валиден» проверяется только прогоном через настоящий дизайнер.

- [x] `DesignerTestHost` в `AiRpaStudio.Tests`: STA-поток с циклом диспетчера +
      создание `WorkflowDesignerEx` без показа окна (поверхность живёт в невидимом `HwndSource`).
      `Application` стенд намеренно не создаёт: он привязывается к диспетчеру своего потока, и после этого
      чужой тест, читающий `Application.Current.Resources`, падает с исключением о другом потоке (проверено).
- [x] `DesignerRenderProbe`: после `Load` дождаться `LayoutUpdated`/`DesignerView` и утверждать, что
      (а) дерево содержит хотя бы одну активность, (б) нет ошибок валидации WF и самого дизайнера,
      (в) нет нераспознанных активностей (`ErrorActivity`: WF не считает неизвестный тип ошибкой загрузки),
      (г) не появилось модальных окон. Страж окон закрывает диалоги WF и не даёт им заблокировать прогон;
      ошибки валидации дизайнера перехватывает записывающий `IValidationErrorService`.
- [x] Прогон по эталону `RPA-1\Main.xaml` — стенд обязан показать «зелёно» на заведомо хорошем файле.

**Тесты:** `DesignerTestHostTests` — 13 тестов: стенд поднимается (STA, маршалинг вызовов), эталон
загружается, рендерится, валиден и без модальных окон; стенд умеет говорить «нет» (неизвестный тип,
битый XML, `Assign` без обязательных аргументов); стражи — студия настраивает дизайнер через общий
`WfDesignerEnvironment`, среда не теряет обязательные установки (п. 2.3).

**Реализация:** `AiRpaStudio.Tests\Design\` — `DesignerTestHost` (STA-поток + диспетчер),
`DesignerRenderProbe` (`Ok`/`errors`/`warnings`/`modalWindows` в отчёте), `DesignerModalWindowGuard`,
`RecordingValidationErrorService`, `DesignerBenchProxy` — отрисовка идёт в отдельном домене приложения:
кэши шаблонов и цветов WF статические на процесс, и без изоляции 11 тестов темы панели «Активности»
падали на чужом потоке. Среда дизайнера вынесена в `AiRpaStudio\Helpers\WfDesignerEnvironment.cs` и
используется и студией (`LoadActivityToDesignerHelper`), и стендом; в тестовый проект добавлена ссылка
на `System.Reflection.Metadata` — без неё `RoslynExpressionEditorService` падал при построении поверхности.

### Этап 2. Канонический XAML и его стражи (2–3 дня) — СДЕЛАНО

- [x] `WorkflowCanonicalizer`: сборка `ActivityBuilder` с обязательными элементами (см. п.1.1).
- [x] `WorkflowBuildService.Save`: сохранение сериализатором WF, а не строковым шаблоном.
- [x] Гейт валидации `WorkflowValidationGate` + единый формат ответа
      `{ok, workflowId, filePath, xaml, errors[], warnings[], hints[]}`.
- [x] Структурные тесты XAML (регекс-стражи по образцу `ThemeXamlTests`): наличие `sap2010:ExpressionActivityEditor="C#"`,
      `xmlns:mca`, `TextExpression.ReferencesForImplementation`, `x:Class`, `IdRef` на корне.
- [x] Тест «сгенерированный XAML ≠ сломанный»: мутационный набор — убрать атрибут/пространство имён/ссылку
      на сборку → гейт обязан вернуть ошибку, а не «ок».

**Тесты:** `WorkflowCanonicalizerTests` (12), `WorkflowValidationGateTests` (15), `GeneratedXamlShapeTests` (13) —
40 тестов: канонизация по объектной модели (`x:Class`, редактор `C#`, наборы пространств имён и ссылок, IdRef
корня и каждой активности, идемпотентность), гейт (битый XML, неизвестная активность, активность без
обязательных аргументов, отсутствие ссылки на сборку проекта, отключение проверок формы), откат файла при
провале гейта, понятная ошибка при файле, открытом в конструкторе, мутационный набор (11 мутаций + контрольная
точка «немутированный XAML проходит») и прогон сгенерированного файла через настоящий конструктор
(`DesignerBenchProxy`).

**Реализация:** `AiRpaStudio\Core\Generation\` — `WorkflowCanonicalizationOptions` (имя класса, сборка проекта,
дополнительные пространства имён/ссылки, санитайзер `RPA-1` → `RPA_1` и `Main.xaml` → `Main`), `WorkflowCanonicalizer`
(формат студии по объектной модели: `ActivityBuilder.Name`, `ExpressionActivityEditor` = `C#`,
`TextExpression.NamespacesForImplementation`/`ReferencesForImplementation` как `sco:Collection` — а не `scg:List`
с `Capacity`; IdRef корня `Класс_1` и `Тип_N` на каждой активности через `WorkflowInspectionServices.GetActivities`,
с обрезкой арности `ForEach`1 → `ForEach`), `WorkflowXamlSerializer` (сериализатор WF
`ActivityXamlServices.CreateBuilderWriter` + `XamlServices.Save`; DOM-правка — только `xmlns:mc` и
`mc:Ignorable="sap sap2010"`, которые сериализатор не выводит; `xmlns:sap`/`xmlns:sap2010` при этом объявляются
обязательно — `mc:Ignorable` с недекларированным префиксом даёт `XmlException` при загрузке), `WorkflowValidationGate`
(корректный XML + каноническая форма + `ActivityXamlServices.Load` + `ActivityValidationServices.Validate`),
`WorkflowBuildService` (единый конвейер: сборка → временный файл → гейт → атомарная замена целевого файла;
ответ `{ok, workflowId, filePath, xaml, errors[], warnings[], hints[]}`). Файл пишется как файлы студии:
UTF-8 без BOM, CRLF, без XML-декларации, ссылка на сборку проекта — последней.

### Этап 3. Открытие в конструкторе (2 дня) — СДЕЛАНО

- [x] `DesignerOpenService.OpenOrActivate(filePath)` поверх `LoadActivityToDesignerHelper`, с проверкой
      `GetDesignerByFile` (не открывать дважды один файл).
- [x] Маршалинг на UI-поток: инструмент, вызванный из MCP-потока, не должен создавать `WorkflowDesigner`
      в фоновом потоке (иначе WPF-исключения и «пустая» диаграмма).
- [x] Учесть ограничение из `CustomWfDesigner.WorkflowOpen`: файл, уже открытый в конструкторе, держится
      `FileShare.Read`, поэтому перезапись открытого файла даёт `IOException` — нужен явный сценарий
      (закрыть/перезагрузить вкладку, либо генерация в новый файл).
- [x] Интеграция с отслеживанием изменений (`WorkflowEditTracking`): сгенерированный процесс помечается как
      изменённый корректно, без «фантомного» dirty-состояния.

**Тесты:** `DesignerOpenServiceTests` (20) + `DesignerUiThreadTests` (3) + `DesignerOpenRealDesignerTests` (2) — 25 тестов:
открытие и активация вкладки; повторный вызов переиспользует ту же вкладку (в том числе при другом написании
пути) и не создаёт второй конструктор; свежая вкладка не помечается «изменённой», а правки уже открытой
вкладки не сбрасываются; `workflowId` берётся из `x:Class`; отказы — файл не найден, не `.xaml`, сбой
загрузчика (ошибка вместо исключения); работа уходит на UI-поток; занятый UI-поток даёт таймаут с понятным
текстом; `CheckWrite`/`Close`/`Reload` (включая запрет закрывать вкладку с несохранёнными правками); связка с
`WorkflowBuildService` — генерация в открытый файл отклоняется до записи, а после закрытия вкладки проходит.
Плюс прогон на живом дизайнере (`DesignerBenchProxy.OpenTabScenario`): сгенерированный процесс открывается,
вкладка активна и в дереве есть активности, повторный вызов переиспользует вкладку, перезапись запрещена,
генерация в открытый файл отклонена.

**Реализация:** `AiRpaStudio\Core\Generation\` — `DesignerUiThread`/`DispatcherDesignerUiThread` (маршалинг на
`Dispatcher` студии; вызов с самого UI-потока выполняется напрямую, ожидание чужого потока ограничено
таймаутом — занятый диспетчер возвращает ошибку инструменту, а не вешает MCP-вызов), `DesignerTab` +
`IWorkflowDesignerHost` (вкладка: путь, «изменён», живой дизайнер), `StudioWorkflowDesignerHost` (открытие —
только `LoadActivityToDesignerHelper`, активация — `ActiveWorkflowDesignerEx`, закрытие —
`RemoveDesignerByLoadedFile`, сброс «изменён» — `IsEdited`), `DesignerOpenService`
(`OpenOrActivate`/`Reload`/`Close`/`CheckWrite`; ответ `{ok, filePath, workflowId, alreadyOpen, reloaded,
activated, errors[], hints[]}`; свежая вкладка помечается «не изменённой» — нет фантомного dirty-состояния).
`WorkflowBuildService.Save` получил необязательную проверку `IDesignerWriteCheck`: файл, открытый в
конструкторе, отклоняется ошибкой с подсказкой **до** записи, а не `IOException` после валидации; сценарий
«перезаписать открытый файл» — это `Close` (когда правок нет) или `Reload`. Открытие переведено на сервис в
`McpIntegration.AddNewActivityToProject` (MCP-поток), `Main.OpenCreatedActivity`, `Main.CmdTryLoadFile`,
`CustomWfDesigner.WorkflowOpen`. Попутно: `WorkflowDesignerEx.DetachValidation` останавливает таймер
`DesignerValidator` при закрытии вкладки — раньше он продолжал тикать после закрытия и при остановке
приложения пытался показать модальное окно конструктора (наблюдалось падением при выгрузке домена стенда).

### Этап 4. Инструмент палитры (Вариант 3) — базовый MVP (3–4 дня) — СДЕЛАНО

- [x] `ActivityPaletteRegistry`: набор активностей с аргументами и допустимыми контейнерами
      (источник — существующий тулбокс/`WFToolboxHelper`, чтобы не расходиться с реальной палитрой студии).
- [x] `xaml.create_workflow`, `xaml.add_activity`, `xaml.set_argument`, `xaml.get_workflow`.
- [x] Вставка через `ModelService`/`ModelItem` внутри уже открытого дизайнера — тогда ViewState и IdRef
      проставляет сам конструктор, и отображение гарантировано.
- [x] Регистрация инструментов: `RpaMcpServer._tools` сейчас **пустой** (в `Mcp` есть только каркас
      `IMcpTool`/`McpModels`), поэтому нужен реестр инструментов и регистрация на старте.
- [x] Каждый вызов инструмента завершается гейтом + открытием вкладки (п.1.3).

**Тесты:** `ActivityPaletteRegistryTests` (11), `ActivityPaletteFactoryTests` (12), `WorkflowModelReaderTests` (7),
`PaletteWorkflowServiceTests` (14), `PaletteToolsTests` (14), `PaletteDesignerTests` (1) — 59 тестов: реестр описывает
аргументы (тип, направление, обязательность) и контейнеры, находит обобщённые активности в любом написании
(`ForEach`, `ForEach<>`, `ForEach`1`) и покрывает всё, что есть в панели «Активности»; фабрика собирает
`CSharpValue<T>`/`CSharpReference<T>`, вкладывает активности в `Activities`/`Then`/`Body` и проходит гейт; чтение
модели даёт путевые идентификаторы (`root/Activities[1]`), выражения аргументов и подписи; сервис открывает вкладку,
вставляет активность через модель, сохраняет штатным сериализатором, прогоняет гейт и **откатывает** правку при
провале (файл остаётся валидным); инструменты проверяют аргументы и отвечают единым форматом
`{ok, filePath, activityId, errors[], hints[]}`; прогон на живом дизайнере (`DesignerBenchProxy.PaletteScenario`) —
после `add_activity` вкладка открыта и активна, активность видна в дереве модели, валидация без ошибок,
`set_argument` меняет выражение в файле.

**Реализация:** `AiRpaStudio\Core\Generation\` — `ActivityPaletteRegistry` (описатели активности/аргумента/контейнера:
аргументы с типом, направлением и обязательностью по `[RequiredArgument]`; контейнеры `Activities`/`Then`/`Body`;
элементы диаграммы `FlowDecision`/`State` активностями не считаются), `StudioActivityPalette` — **единственный**
список состава палитры: из него строятся и панель «Активности» (`WFToolboxHelper`), и реестр инструментов,
`ActivityPaletteFactory` (значения → `CSharpValue<T>`/`CSharpReference<T>`, обобщённые активности закрываются по
`typeArguments`, `ActivityAction<T>` для `Body`), `WorkflowModelReader` (идентификатор активности — путь в дереве,
а не `Activity.Id`: WF присваивает свои id только при материализации дерева), `PaletteWorkflowService` (единый
конвейер: проверка запроса → открытие вкладки → правка модели → `WorkflowDesigner.Save` → гейт → откат через
`UndoEngine`), `DesignerModelSession` (вставка через `ModelTreeManager`/`ModelItemCollection`, адресация `ModelItem`
по тому же путевому идентификатору). Инструменты — `AiRpaStudio\Mcp\Tools\` (`PaletteToolBase` + четыре инструмента) и
`Mcp\McpToolRegistry` (регистрация на старте в `RpaMcpServer`; сервис палитры и UI-поток подставляются лениво,
поэтому старт MCP-сервера не зависит от готовности конструктора). Попутно: `WorkflowXamlSerializer` объявляет
`xmlns:mca` всегда — гейт проверяет форму корня, и процесс без выражений обязан быть готов к первому выражению
(иначе пустой процесс не создавался); `WFToolboxHelper` берёт категории из `StudioActivityPalette`, чтобы панель и
инструменты не расходились.



### Этап 5. Code-First через Roslyn (4–5 дней) — СДЕЛАНО

- [x] `RoslynSandbox`: whitelist сборок (WF, mscorlib, System.*, сборки активностей студии), запрет
      `System.IO`/`System.Reflection`/`System.Net`/`System.Diagnostics`, лимиты размера кода и времени
      компиляции, запрет `unsafe`/`P-Invoke`.
- [x] Трансляция «сборка → `ActivityBuilder`»: найти тип с `[ActivityBuilder]`/`Activity`, подставить
      имя класса проекта.
- [x] `XamlExpressionPolicy`: до сохранения отклонить лямбды в аргументах с подсказкой
      «используйте `CSharpValue<T>("...")`».
- [x] Машиночитаемая диагностика компиляции (код, строка, столбец, подсказка) — чтобы агент исправлялся сам.
- [x] Разрешение имён: `TextExpression.NamespacesForImplementation` строится из `using` кода, ссылки — из
      реально использованных сборок; иначе конструктор покажет нерезолвленные выражения.

**Тесты:** `RoslynSandboxTests` (16): код компилируется в загруженную сборку; `using` кода даёт и пространства имён,
и сборки для XAML; каждый запрет проверяется отдельно — `System.IO`, `System.Reflection`, `System.Net`,
`System.Diagnostics` (в `using` и в полном имени), `unsafe`, `[DllImport]`; текст в комментарии запретом не считается;
ошибка синтаксиса приходит с кодом Roslyn, строкой, столбцом и подсказкой; неизвестный тип — с подсказкой про `using`;
предупреждение не блокирует сборку; лимиты размера кода и времени компиляции дают ошибку инструмента, а не зависание.
`CodeFirstWorkflowTests` (10): простой процесс и процесс с `If`+`ForEach`+`While`+`TryCatch` дают канонический XAML
(`x:Class`, редактор `C#`, `xmlns:mca`, `ReferencesForImplementation`) без предупреждений WF; `using` кода попадают
в `NamespacesForImplementation`, ссылки — из использованных сборок; вход через класс-`Activity` берётся из
`Implementation`; ошибки — «нет входа», «активность из кода», «не тот путь», «нет проекта» — с подсказками; сбой
компиляции возвращает диагностику и **не** пишет файл; сохранённый файл повторно загружается
`ActivityXamlServices.Load`. `LambdaRejectionTests` (7): лямбды (`LambdaValue`/`LambdaReference`, в том числе
во вложенной активности) отклоняются политикой, `CSharpValue`/`CSharpReference` проходят; `Build`/`Save` не создают
файл при лямбде; Code-First отклоняет лямбду в коде агента. `CodeFirstToolsTests` (6): регистрация
`xaml.create_workflow_from_code`, обязательные `path`/`code`, разбор аргументов, ответ с `entryPoint`,
`activityCount`, `diagnostics[]` и XAML только при ошибке. `CodeFirstDesignerTests` (1): процесс, собранный из кода,
открывается в настоящем конструкторе (`DesignerBenchProxy`) — активности в дереве, валидация без ошибок,
модальных окон нет.

**Реализация:** `AiRpaStudio\Core\Generation\` — `RoslynSandbox` + `RoslynSandboxOptions` (whitelist сборок:
WF, `mscorlib`, `System.*` и загруженные сборки активностей студии; запрет `System.IO`/`System.Reflection`/
`System.Net`/`System.Diagnostics`, `System.Runtime.InteropServices`, `System.Security`, `System.Management`,
`Microsoft.Win32`; запрет `unsafe`/`fixed`/`stackalloc`/`[DllImport]`; лимиты размера кода и времени компиляции —
`Task.Wait(timeout)`, чтобы занятая компиляция возвращала ошибку, а не вешала MCP-вызов), `RoslynCodePolicy`
(проверки по синтаксическому дереву, а не по тексту — комментарий и литерал не дают ложного срабатывания,
а `System.IO.File`, написанное с переносом строки, даёт), `RoslynDiagnostic`/`RoslynDiagnosticHints`
(машиночитаемая диагностика: код, строка, столбец, текст, подсказка по `CS0103`/`CS0246`/`CS1061`/синтаксису/`unsafe`),
`RoslynAssemblyCatalog` (поиск сборки по простому имени с пробой файла рядом со студией и в каталоге среды
выполнения — `Assembly.Load` для `System.Activities` в тестовом процессе не срабатывает, а путь нужен для
`MetadataReference`; отрицательный результат не кэшируется), `XamlExpressionPolicy` (лямбды ищутся по объектной
модели: аргументы каждой активности дерева, `LambdaValue<T>`/`LambdaReference<T>`; подключена в
`WorkflowBuildService.Build`, поэтому действует и для палитры, и для Code-First), `CodeFirstWorkflowTranslator`
(вход процесса: `public static Activity|ActivityBuilder`-метод без параметров — предпочтительно `Build`/`Create`/
`CreateWorkflow`/`GetWorkflow` — либо public-класс без параметров конструктора, наследующий `Activity`, у которого
берётся `Implementation`; активности, объявленные в коде агента, в дерево не допускаются — их типа нет в студии),
`CodeFirstWorkflowService` + `ICodeFirstWorkflowService` (общий конвейер: песочница → транслятор → `WorkflowBuildService`
(канонизация, сериализатор WF, временный файл, гейт) → `DesignerOpenService`; пространства имён `using` и сборки
из кода добавляются в параметры канонизации). Инструмент — `AiRpaStudio\Mcp\Tools\CreateWorkflowFromCodeTool.cs`
(`xaml.create_workflow_from_code`: `path`, `code`, `project`, `namespace`, `className`; ответ
`{ok, workflowId, filePath, entryPoint, activityCount, opened, activated, namespaces[], references[],
diagnostics[], errors[], warnings[], hints[]}`, XAML — только при ошибке) и регистрация в `McpToolRegistry`.
`PaletteToolBase` получил конструктор без сервиса палитры — Code-First использует те же разбор аргументов и формат
ответа, но палитра ему не нужна.
Итого 40 новых тестов (16 + 10 + 7 + 6 + 1): старые стражи (`PaletteToolsTests`, `DesignerTestHostTests`,
`WorkflowValidationGateTests`) остались зелёными, полный прогон стенда — 1367 тестов без падений.


### Этап 6. JSON AST (4–5 дней, если понадобится после MVP) — СДЕЛАНО

- [x] JSON-схема процесса (активности, переменные, аргументы, выражения как строки кода).
- [x] Строгая валидация по схеме **до** построения модели; whitelist активностей и типов переменных.
- [x] Транспатор JSON → `ActivityBuilder` → тот же конвейер этапов 2–3.

**Тесты:** `JsonAstSchemaTests` (22): схема строгая (`additionalProperties="false"`), enum активностей совпадает
с реестром палитры, пример из схемы проходит валидацию; разбор документа (проект, переменные, число активностей,
корневой `Sequence`); негативные — неизвестное поле (с путём и списком допустимых), неизвестная активность
(с подсказкой по палитре), неизвестный тип переменной, переменная без типа, дубль переменной, `variables`
у неконтейнера, литерал в аргументе-ссылке, `Assign` без типа, обобщённая активность без `typeArguments`
и с неизвестным типом, неизвестный контейнер, неизвестное поле значения, битый JSON (с позицией),
`activities` и `root` одновременно, пустой документ, слишком большое описание; вывод типа `Assign` из
объявленной переменной; полный путь до вложенного поля в ошибке. `JsonToWorkflowTests` (12): JSON даёт
канонический XAML (`x:Class`, редактор `C#`, `xmlns:mca`, `Variable`, `CSharpValue`/`CSharpReference`,
без `x:Object`); `If` + `ForEach` + `ActivityAction`; `Variable.Default`; имя класса и пространство имён
из документа перекрывают выведенные из пути; `namespaces`/`references` попадают в XAML; явный `root`;
`Save` даёт файл, который повторно загружается `ActivityXamlServices.Load`; негативные (неизвестная
активность, неверный тип переменной, `variables` у корня-неконтейнера, отсутствие `path`/`project`) —
с ошибкой и **без** записи файла. `JsonAstToolsTests` (9): регистрация `xaml.create_workflow_from_json`,
`describe: true` отдаёт схему, whitelist и пример, обязательные `path`/`ast`, приём описания объектом
и JSON-строкой, формат ответа (`workflowId`, `activityCount`, `variables`, XAML — только при ошибке),
сквозной прогон «инструмент → сервис → файл» на настоящем `JsonWorkflowService`.
`JsonAstDesignerTests` (1): процесс из JSON (переменные, `Assign`, `If`, `ForEach`) открывается
в настоящем конструкторе (`DesignerBenchProxy`) — активности в дереве, валидация без ошибок, модальных
окон нет.

**Реализация:** `AiRpaStudio\Core\Generation\` — `WorkflowJsonSchema` (единственное описание схемы: поля
документа/активности/переменной/значения, лимиты `MaxJsonChars`/`MaxDepth`/`MaxActivities`, whitelist
активностей из `ActivityPaletteRegistry.All` и типов из `PaletteTypeNames`, машиночитаемая JSON Schema
draft-07 `ToJsonSchema()` и рабочий пример `Example()`), `WorkflowJsonValidator` (строгая проверка **до**
построения модели: неизвестные поля, активность вне whitelist, тип переменной вне whitelist, аргументы по
метаданным палитры, контейнеры (узлы `Flowchart` отклоняются), дубли переменных, лимиты; каждая ошибка
с путём `$.activities[0].children.Then[1].type`; отдельное правило `Assign` — тип `To`/`Value` выводится из
объявленной переменной или соседнего аргумента, иначе ошибка вместо молчаливого `x:Object`),
`WorkflowJsonTranslator` (описание → `Activity` через тот же `ActivityPaletteFactory`, что у палитры),
`JsonWorkflowService` + `IJsonWorkflowService` (конвейер: валидация → транслятор → `WorkflowBuildService`
(канонизация, сериализатор WF, временный файл, гейт) → `DesignerOpenService`), `GenerationTarget` (общий
для Code-First и JSON AST вывод пути/проекта/класса — инструменты не расходятся), в `ActivityPaletteFactory` —
`PaletteVariableSpec`/`ApplyVariables` (`Sequence.Variables`; значение по умолчанию — `CSharpValue<T>` или
`Literal<T>`, потому что `Variable<T>.Default` имеет тип `Activity<T>`; `FindProperty`/`FindVariablesProperty`
вместо `Type.GetProperty` — WF прячет одноимённые свойства, и поиск по имени падает `AmbiguousMatchException`).
Инструмент — `AiRpaStudio\Mcp\Tools\CreateWorkflowFromJsonTool.cs` (`xaml.create_workflow_from_json`: `path`,
`ast`, `project`, `namespace`, `className`, `describe`; ответ `{ok, workflowId, filePath, activityCount, opened,
activated, variables[], namespaces[], references[], errors[], warnings[], hints[]}`, XAML — только при ошибке)
и регистрация в `McpToolRegistry`. Схема для агента описана в `docs\Workflow_Json_Ast_Schema.md`.
Итого 44 новых теста (22 + 12 + 9 + 1), полный прогон стенда — 1411 тестов без падений.



### Этап 7. Контракт для агента (1 день) — СДЕЛАНО

- [x] Промпт-правила: только `CSharpValue<T>`/`CSharpReference<T>`; только whitelisted активности и типы;
      выбор инструмента (простое → палитра, ветвления/циклы → Code-First, детерминизм → JSON).
- [x] Few-shot примеры, каждый проверен тестом-эталоном: `Sequence`+`Assign`, `If`, `ForEach<T>`, `While`,
      `TryCatch`, `Bookmark`+`Persist`.
- [x] Правило самокоррекции: ошибка гейта возвращается агенту как задача, а не как «сдайся».

**Тесты:** `AgentContractTests` (10): контракт содержит все обязательные сценарии (`Sequence`+`Assign`, `If`,
`ForEach<T>`, `While`, `TryCatch`, `Bookmark`+`Persist`), имена уникальны, у каждого примера есть сценарий,
пояснение и инструмент по форме; правила фиксируют `CSharpValue<T>`/`CSharpReference<T>`, запрет лямбд,
палитру и whitelist, «XAML — не текст» и конструктор как часть контракта; матрица выбора инструмента
покрывает палитру, Code-First, JSON AST, снимок дерева и сам контракт; самокоррекция — «это задача, а не
повод сдаться», читает `errors[]`/`hints[]`, запрещает правку XAML и понижение требований, лимит 3 итерации.
**Каждый пример** прогнан через настоящий конвейер (JSON AST — `JsonWorkflowService`, C# —
`CodeFirstWorkflowService`): `ok:true`, канонический XAML (`x:Class`, редактор `C#`, `xmlns:mca`,
`TextExpression.ReferencesForImplementation`), файл записан и повторно загружается `ActivityXamlServices.Load`,
лямбд в XAML нет; JSON-примеры используют только whitelisted активности и типы; `Describe()` отдаёт текст
промпта целиком; `Doc_MirrorsContract` — `docs\Workflow_Agent_Contract.md` совпадает с контрактом дословно
(правила, ситуации выбора, тела и пояснения примеров, самокоррекция). `AgentContractToolsTests` (5):
регистрация `xaml.describe_contract`, ответ `{ok, contract, rules[], toolSelection[], examples[],
selfCorrection[], hints[]}`, фильтр `example`, `format: "text"`, неизвестное имя → структурированная ошибка
со списком доступных примеров. `AgentContractDesignerTests` (6, по одному на пример): каждый пример
открывается в настоящем конструкторе (`DesignerBenchProxy`) — активности в дереве, валидация без ошибок,
нераспознанных активностей и модальных окон нет.

**Реализация:** `AiRpaStudio\Core\Generation\WorkflowAgentContract.cs` — единственный источник контракта
(`Rules`, `ToolSelection`, `Examples`, `SelfCorrection`, `Describe()` для промпта; `ContractToolNames` —
имена инструментов, а не строки «в воздухе»). Few-shot примеры лежат там же: JSON AST (`sequence-assign`,
`if`, `foreach`, `while`) и Code-First (`trycatch`, `bookmark-persist`), потому что `Catch` и `Persist` не
являются активностями палитры и JSON AST их не выражает. Инструмент —
`AiRpaStudio\Mcp\Tools\DescribeWorkflowContractTool.cs` (`xaml.describe_contract`: `example`, `format`;
ничего не собирает и не пишет на диск) и регистрация в `McpToolRegistry`. Документ для человека —
`docs\Workflow_Agent_Contract.md`, он обязан совпадать с кодом (страж `Doc_MirrorsContract`).
Попутно исправлен найденный примером дефект: `While`/`DoWhile` объявляют условие как `Activity<bool>`, а не
`InArgument<bool>`, поэтому JSON AST не мог задать `Condition` — активность была в whitelist, но недоступна
агенту. `ActivityPaletteRegistry.DescribeArgument` теперь описывает свойства-выражения
(`ActivityPaletteRegistry.IsExpressionPropertyType`), а `ActivityPaletteFactory` подставляет в них
`CSharpValue<T>`/`Literal<T>` (`CreateExpressionPropertyValue`).
Итого 21 новый тест (10 + 5 + 6), полный прогон стенда — 1432 теста без падений.

### Этап 8. Приёмка (3–4 дня)

Чек-лист приёмки (все пункты обязательны и проверяются автотестами):

- [ ] Простой процесс (3–5 шагов RPA) — открывается в конструкторе, редактируется руками, сохраняется.
- [ ] Процесс с `If` + `ForEach` + `While` + `TryCatch` — открывается, валидация без ошибок.
- [ ] Процесс с `Bookmark` — persistence-тест: Persist → перезапуск хоста → Resume (ламбд нет, поэтому работает).
- [ ] Негативные: инъекция кода, лямбда в аргументе, забытая скобка, неверный тип переменной, ссылка на
      неизвестную сборку — все дают структурированную ошибку и **не** ломают студию.
- [ ] Страж «нет модального окна конструктора» после любой неудачной генерации.
- [ ] Страж единой версии транзитивных зависимостей остаётся зелёным (см. `AGENTS.md`:
      `System.Threading.Tasks.Extensions`, `AssemblyBindingUnifierTests`) — новые пакеты не должны его сломать.

---

## 4. Definition of Done для одной генерации

Генерация считается успешной, только если одновременно:

1. `XamlServices`/WF-сериализатор записал файл, `ActivityXamlServices.Load(path)` прошёл;
2. `ActivityValidationServices.Validate` → 0 errors;
3. `WorkflowDesigner.Load(path)` прошёл, дерево содержит ожидаемое число активностей;
4. вкладка конструктора открыта и активна, заголовок/файл соответствуют `workflowId`;
5. ответ инструмента содержит `{ok:true, workflowId, filePath, warnings[]}`;
6. в `log.txt` нет исключений, модальных WF-окон не появилось.

При любом нарушении — `{ok:false, errors[], hints[]}` и **откат**: недописанный/невалидный файл не остаётся
в проекте (пишем во временный файл, валидируем, затем атомарно переносим).

---

## 5. Риски и контрмеры

| Риск | Проявление | Контрмера |
|---|---|---|
| Expression editor не тот | выражения в дизайнере не редактируются, показываются как текст | канонизация `sap2010:ExpressionActivityEditor="C#"` + `xmlns:mca`, тест-страж |
| Нерезолвленные типы проекта | модальное окно «Конструктор рабочих процессов», студия блокируется | `AssemblyContextControlItem` + `AssemblyReference` проекта в XAML, страж на модальные окна |
| Лямбды в аргументах | узел невалиден / не сериализуется | `XamlExpressionPolicy` до сохранения |
| Сборка в фоновом потоке | WPF-исключения, пустая диаграмма | только UI-поток: `DesignerOpenService` + `DispatcherDesignerUiThread` (таймаут вместо вечного ожидания) |
| Перезапись открытого файла | `IOException` (`FileShare.Read`) | запрет заранее (`DesignerOpenService.CheckWrite`), сценарии `Close` (правок нет) и `Reload` |
| Инъекция через Roslyn | выполнение произвольного кода в студии | песочница: whitelist + запрещённые пространства имён + лимиты |
| «Шумный» XAML при глубокой вложенности | ошибки структуры при ручной сборке | только объектная модель + сериализатор WF |
| Конфликт версий пакетов | падение дизайнера при загрузке проекта | существующий `AssemblyBindingUnifier` + тесты-стражи |

---

## 6. Порядок и оценка

| Этап | Содержание | Дней |
|---|---|---|
| 1 | Тестовый стенд конструктора | 2 |
| 2 | Канонический XAML + гейт валидации | 2–3 |
| 3 | Открытие вкладки конструктора | 2 |
| 4 | Палитра активностей (MVP) | 3–4 |
| 5 | Code-First через Roslyn | 4–5 |
| 6 | JSON AST | 4–5 |
| 7 | Контракт для агента | 1 |
| 8 | Приёмка | 3–4 |
| | **MVP (этапы 1–5, 7, приёмка по 1–5)** | **≈ 15–18** |
| | **Полный объём (+ JSON AST)** | **≈ 21–26** |

Первый показываемый результат (демо «агент собрал процесс — он открылся в конструкторе») достижим уже
после этапов 1–4, то есть примерно на 9–11-й день: палитра даёт гарантированно валидный XAML без Roslyn.

---

## 7. Открытые вопросы (нужны решения до этапа 4)

1. Куда сохранять сгенерированный процесс: в открытый проект (`RPA-N\*.xaml`) или в отдельную папку
   «черновиков» с последующим «принять в проект»?
2. Открывать ли сгенерированный процесс принудительно (забирать фокус), или показывать ненавязчиво, если
   пользователь сейчас редактирует другой файл?
3. Разрешать ли агенту изменять процесс, который уже открыт и имеет несохранённые правки (конфликт
   «правки пользователя vs генерация»)?
4. Нужен ли persistence (`Bookmark` + `InstanceStore`) в MVP — от этого зависит обязательность
   запрета лямбд на уровне песочницы, а не только политики.
