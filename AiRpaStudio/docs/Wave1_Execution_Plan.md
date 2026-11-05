# Волна 1 «Блокеры целей» — пошаговый план и сверка с кодом

| Поле | Значение |
|---|---|
| Документ | `AiRpaStudio\docs\Wave1_Execution_Plan.md` — детализация волны 1 из `AiRpaStudio\docs\ROADMAP.md` (§11) |
| Дата сверки с кодом | 03.11.2026 |
| Область | `CP-01`, `CP-02`, `CP-03`, `CP-04`, `CP-09`, `TD-12`, `AG-01`, `AG-02`, `AG-03`, `AG-12` (каркас), `TD-08` |
| Правило | Пункт закрывается только с тестом (кроме чисто визуальных), сборкой MSBuild из VS 2022 **и** VS 2026 и полным зелёным прогоном `AiRpaStudio.Tests` |
| Статус документа | поддерживается, пока волна 1 не закрыта |

## 1. Состояние пунктов волны: сверено с кодом 03.11.2026

| Пункт | Факт из кода | Доказательство |
|---|---|---|
| `CP-01` | Стража не было. **Реализован 03.11.2026**: `AiRpaStudio.Tests\TfmMatrixGuardTests.cs`, 4 теста | `vstest` по фильтру `FullyQualifiedName~TfmMatrixGuardTests` — 4/4 зелёные |
| `CP-02` | **Закрыт 03.11.2026.** TFM `Rpa.Activities.Primitives` понижен `v4.8.1` → `v4.7.2`; проект собирается MSBuild VS 2026; запись из `TfmMatrixGuardTests.KnownProjects` удалена | `MSBuild Rpa.Activities.Primitives.csproj` → exit 0; `TfmMatrixGuardTests` 4/4 зелёные без записи |
| `CP-03` | **Закрыт 03.11.2026 (вариант «а»).** TFM `v4.8` → `v4.7.2`, проект остаётся вне сборки решения, к роботам не подключается; запрет перенесён из белого списка в собственные тесты `AiRpaStudio.Tests\VsProjectLayoutGuardTests.cs` | `VsProjectLayoutGuardTests` 2/2 зелёные; негативная проверка (добавленная `.Build.0` строка валит тест) |
| `CP-04` | **Закрыт 03.11.2026.** Дубль поиска устранён: `Agent.Core\Services\MsBuildLocator.cs` — единый поиск (`Auto` / `Vs2022` / `Vs2026`), откат виден в логе, тулчейн пишется в `VsBuildResult`, защита от `MSB4236` | `MsBuildLocatorTests` — 39/39 зелёные, включая реальную сборку шаблонного проекта обоими тулчейнами; полный прогон 1458/1458 |
| `CP-09` | **Закрыт 03.11.2026.** `AiRpaStudio\TestProcess\Net472Robot` (legacy WF, `v4.7.2`) и `Net48Robot` (SDK-style `net48` + `PackageReference`) + фикстура трипа `BrokenRobot\BrokenMain.xaml`; проекты намеренно вне `AiRpa.sln` | `TestProcessRegressionTests` 5/5: открытие, сборка с restore, headless-прогон `WorkflowInvoker` (`Result = MAX`), «трип» с `boom` |
| `TD-12` | **Закрыт 03.11.2026 (вариант A).** `ProxyChecker` починен; `RPA.CodeEditor` снят с сборки решения; 4 отсутствующих проекта + папка решения `Activities.WinUI` убраны из `.sln`; `RPA-1\Main.xaml` исправлен, висячий `ProjectReference` удалён | `MSBuild AiRpa.sln` Release и Debug → exit 0; `vstest` → 1136/1136 |
| `AG-01` | **Закрыт 04.11.2026 (кроме иконок).** `RPA.Chat\Services\Workflow\` — DTO (`ActivityInfo`/`ActivityArgumentInfo`/`ActivityContainerInfo`), контракт `IActivityCatalog` + `NullActivityCatalog`, формат ответов `ActivityCatalogText`; реализация `AiRpaStudio\Core\Workflow\StudioActivityCatalog.cs` поверх `ActivityPaletteRegistry` (тот же источник, что панель «Активности»); точка подключения хоста — `ChatEnvironment.ActivityCatalog` (`MainView`); инструменты `activity_search`/`activity_describe` в `AgentToolFactory` (read-only, `ReadFiles`, видны в Plan). Иконки не отдаются: в палитре нет источника иконок | `ActivityCatalogTests` 28 + `ActivityToolsTests` 13 зелёные; негатив проверен (отключённое имя категории → 4 теста падают); полный прогон 1574/1574 |
| `AG-02` | **Закрыт 05.11.2026.** Контракт и DTO — `RPA.Chat\Services\Workflow\` (`IWorkflowEditingService`, `IWorkflowEditSession`, `WorkflowInsertRequest`, `WorkflowMoveRequest`, `WorkflowEditResult`, `WorkflowEditText`, `NullWorkflowEditingService` + `UnavailableWorkflowEditSession`); реализации `AiRpaStudio\Core\Workflow\StudioWorkflowEditingService.cs` (вкладка по пути через `IWorkflowDesignerHost`, `undo`/`redo` через `UndoEngine`, структура несохранённого документа) и `StudioWorkflowEditSession.cs` (вставка/удаление/перенос через `ModelService`/`ModelItem`, каждая правка — свой `ModelEditingScope`, пачка `Complete`/`Revert`); маршалинг на UI-поток дизайнера через `IDesignerUiThread`; хост подключает мост через `ChatEnvironment.WorkflowEditing`, а `MainView` связывает `StudioWorkflowOutlineService.LiveOutlineSource` с живой моделью (структура несохранённого документа — остаток `AG-03`). Не сделано: `Select` (визуальное выделение агенту не нужно, адресация — по `id`); инструменты `activity_*` — карточка `AG-04` | `WorkflowEditingTests` 23 (headless) + `WorkflowEditBridgeTests` 7 (живой конструктор: вставка в начало/середину/конец, адреса, `complete`, `undo`/`redo`, `revert` пачки, удаление/перенос, отказ «файл не открыт») зелёные; негатив проверен (`AddressFor` → `[0]` роняет тест адресов); полный прогон 1641/1641; `MSBuild AiRpa.sln` Release — 0 ошибок из VS 2022 и VS 2026 |
| `AG-03` | **Закрыт 04.11.2026.** Контракт и DTO — `RPA.Chat\Services\Workflow\` (`WorkflowOutline`, `WorkflowOutlineNode`, `WorkflowOutlineArgument`, `WorkflowOutlineVariable`, `WorkflowOutlineIssue`, `IWorkflowOutlineService` + `NullWorkflowOutlineService`, `WorkflowOutlineText`); реализация `AiRpaStudio\Core\Workflow\StudioWorkflowOutlineService.cs` поверх `WorkflowModelReader` (адреса узлов те же, что у `xaml.*`), ошибки WF привязываются к узлам по `ValidationError.Source`; ридер доучен читать переменные, литеральные значения и граф блок-схемы; инструмент `workflow_outline` (`path`, `depth`, `include_arguments`) — read-only, `ReadFiles`, виден в Plan; хост отдаёт службу через `ChatEnvironment.WorkflowOutline`. Не сделано: структура несохранённого открытого документа (придёт с `AG-02`) | `WorkflowOutlineServiceTests` 21 + `WorkflowOutlineToolTests` 12 зелёные; негатив проверен (снятая привязка ошибки к узлу роняет тест); полный прогон 1610/1610 |
| `AG-04` | **Закрыт 06.11.2026.** Инструменты `activity_insert`/`activity_delete`/`activity_move`/`activity_wrap` (+`activity_undo`/`activity_redo`) в `AgentToolFactory`: `mutates: true`, `AgentPermissions.EditFiles`, в Plan не видны; оркестрация вынесена из инструментов в `RPA.Chat\Services\Workflow\WorkflowEditRunner.cs` (BeginEdit → правки → валидация → Complete/Revert), поэтому проверяется без конструктора; контракт расширен `WorkflowWrapRequest` + `IWorkflowEditSession.Wrap`, реализация — `StudioWorkflowEditSession.WrapCore` (обёртка занимает место элемента, элемент становится её первым ребёнком, `If` требует `container`); пачка `activities`/`elementIds`/`moves` = одна атомарная правка. Не сделано: пакет `wrap` (одна обёртка за вызов) | `WorkflowEditToolTests` 25 (фейковая сессия: атомарность, strict/report/off, отказы, undo/redo) + `WorkflowWrapBridgeTests` 6 (живой конструктор: обёртка в Sequence и If.Then, адреса, неоднозначный контейнер, неизвестный тип, `undo`) зелёные; негатив проверен (`Strict && false` роняет тест strict-отката); полный прогон 1672/1672 |
| `AG-05` | **Закрыт 06.11.2026.** Контракт и DTO — `RPA.Chat\Services\Workflow\WorkflowArguments.cs`: `WorkflowArgumentSetRequest` (литерал `value` **или** `expression`, плюс `typeName`/`language`), `WorkflowVariableAddRequest`, `WorkflowVariableRemoveRequest`, `WorkflowExpressionLanguage` и чистая политика `WorkflowExpressionPolicy` (пустое выражение, незакрытые скобки/кавычки, обрыв на операторе, лямбды, путаница C#/VB). Контракт сессии расширен `SetArgument`/`AddVariable`/`RemoveVariable`, оркестрация — `WorkflowEditRunner` (`argument_set`/`variable_add`/`variable_remove`: пачка = одна атомарная правка, `validate=report/strict/off`, адреса в ответе). Реализация `StudioWorkflowEditSession`: тип и направление аргумента берутся из дескриптора палитры, литерал приводится к типу в `ActivityPaletteFactory` (`PaletteValue`, добавлен `PaletteExpressionLanguage` → `CSharpValue`/`VisualBasicValue`), выражение проверяется политикой и разбором Roslyn (`AiRpaStudio\Core\Workflow\WorkflowExpressionSyntax.cs`, C# 7.3 и VB 15.5), язык документа определяется по XAML (`WorkflowExpressionLanguageDetector`); переменные: тип из списка студии (`PaletteTypeNames`), значение по умолчанию литералом или выражением, проверка идентификатора и дубля, предупреждение о перекрытии внешней области. Инструменты `argument_set`/`variable_add`/`variable_remove` — `mutates: true`, `AgentPermissions.EditFiles`, в Plan не видны | `WorkflowArgumentToolTests` 13 (реестр, разбор JSON, пачка, атомарность, `validate=off`) + `WorkflowExpressionTests` 19 (политика, Roslyn C#/VB, определение языка) + `WorkflowArgumentBridgeTests` 7 (живой конструктор: литерал и выражение в `WriteLine.Text`, неизвестный аргумент, `value`+`expression`, битое выражение, объявление/удаление переменной, дубль, неизвестный тип, неверное значение, активность без `Variables`) зелёные; негатив проверен дважды (снятая проверка синтаксиса и снятая проверка дубля валят bridge-тесты); полный прогон 1711/1711; `MSBuild AiRpa.sln -t:Rebuild` Release — 0 ошибок из VS 2022 и VS 2026 |
| `AG-09` | **Закрыт 06.11.2026.** Один вызов инструмента = одна атомарная пачка: отказ любой правки откатывает всю пачку и модель возвращается к состоянию до вызова (`WorkflowEditRunner.Abort` + `Dispose` незакрытой пачки); валидация после правки — `WorkflowValidationDelta.Compare` (сравнение ошибок **по тексту**, потому что вставка сдвигает индексы адресов) отдаёт только новые ошибки; режимы `validate=report` (по умолчанию: правка остаётся, ошибки приходят с адресами), `strict` (правка откатывается при новой ошибке), `off`; короткая сводка `правок: N; валидация: ошибок X, новых Y` в ответе инструмента вместе со структурой процесса | Тесты: `WorkflowEditToolTests` — `Insert_BatchSecondFails_NothingApplied`, `Strict_RevertsWhenNewValidationErrorAppears`, `Report_KeepsEditAndListsOnlyNewErrors`, `Report_ExistingErrorIsNotCountedAsNew`, `Validation_Off_SkipsValidation`, `ValidationDelta_*`; негатив проверен; полный прогон 1672/1672 |
| `AG-12` | **Начат 04.11.2026 (часть 1 — каркас):** фикстуры `AiRpaStudio.Tests\Fixtures\Workflow\` (`sequence`, `if`, `trycatch`, `flowchart`, `variables`, `sequence-broken`, `malformed`), `WorkflowFixtureTests` (загрузка WF, корень, чтение модель-ридером, round-trip через `WorkflowXamlSerializer` + `WorkflowCanonicalizer`, негативы валидации и битого XML), `ActivityToolsTests` — страж реестра WF-инструментов (read-only, Plan, нет MCP), негативы волны 2 в `WorkflowOutlineServiceTests`/`WorkflowOutlineToolTests`. Осталось: негативы волн 5–8, реестр инструментов целиком | `WorkflowFixtureTests` 25 + `WorkflowEditingTests` 23 + `WorkflowEditBridgeTests` 7 + `WorkflowEditToolTests` 25 + `WorkflowWrapBridgeTests` 6 зелёные; полный прогон 1672/1672 (Debug + `vstest`, как в `AGENTS.md`) |
| `TD-08` | Аудит `TODO.txt` не начат | — |

## 2. Что закрыто/сдвинуто в этой итерации

### `CP-01` — страж матрицы TFM (закрыт)

Файл: `AiRpaStudio.Tests\TfmMatrixGuardTests.cs`. Четыре теста:

1. `EveryProject_IsCompatibleWithNet472_OrListedInKnownProjects` — сканирует все `*.csproj` репозитория (кроме `bin/obj/packages/temp/tmp`, `*_wpftmp.csproj`, `VSTemplates`) и требует, чтобы проект либо целился в `net472`/`net462`/`netstandard2.0`, либо был в белом списке `KnownProjects` с ID задачи и причиной.
2. `KnownProjects_ExistAndMatchRecordedTfm` — белый список не должен протухать: после закрытия `CP-02`/`CP-03`/`CP-05` запись обязана быть обновлена или удалена.
3. `Scan_FindsKeyProjects` — страховка от «пустого» сканирования (проверяет, что 16 эталонных проектов роботов найдены).
4. `RobotProjects_TargetExactlyNet472` — эталонные роботы обязаны целиться ровно в `net472`.

Проверено негативным сценарием: при порче записи (`net481` → `net480`) падают 2 теста с сообщением
«`Rpa.Activities.Primitives`: в KnownProjects записан TFM net480, а в проекте net481 — обновите запись (CP-02)».

### `CP-04` — явный выбор MSBuild (закрыт 03.11.2026)

Новый файл `Agent.Core\Services\MsBuildLocator.cs` (`netstandard2.0`, поэтому его сможет переиспользовать и
4.7.2-раннер `OR-02`):

1. `enum MsBuildToolchain { Auto, Vs2022, Vs2026 }` и `MsBuildLocation` — путь, версия `MSBuild.exe`, тулчейн, источник
   (`vswhere` / `well-known` / `framework`), признак отката `IsFallback`, `Describe()` одной строкой для лога.
2. Порядок поиска: `vswhere` с `-version «[17.0,18.0)»` для 2022 и `«[18.0,19.0)»` для 2026 (обе с `-prerelease`),
   затем well-known каталоги нужного поколения, затем MSBuild из .NET Framework (с пометкой, что SDK-style он не умеет).
   Явно запрошенный и неустановленный тулчейн → `Found=false` с диагностикой; `allowFallback=true` даёт откат на `Auto`,
   но с `IsFallback=true` и текстом «requested Visual Studio 2022, not installed» — откат никогда не молчит.
3. `VsProjectBuilder.FindMSBuild()` делегирует в локатор; добавлен `BuildWithToolchain(path, configuration, toolchain, log)` —
   `Describe()` пишется **первой строкой лога**; `VsBuildResult` получил `MsBuildPath`, `ToolchainName`, `ToolchainVersion`,
   `ToolchainFallback`. `MsBuildFeedbackService` больше не содержит своей копии поиска: берёт локатор, реализует
   `IBuildToolchainAware` и ставит строку тулчейна в начало build output (видно в `read_build_output` и в логе студии).
4. Выбор тулчейна: настройка `MsBuildToolchain` (`Auto`/`Vs2022`/`Vs2026`) в `Settings.settings` + `Main.CmdVsBuild(path, toolchain)`;
   агент может передать аргумент `toolchain` инструменту `build`.
5. Тесты `AiRpaStudio.Tests\MsBuildLocatorTests.cs` — 39: поиск на подставном окружении (диапазоны версий, откаты,
   well-known, .NET Framework, тексты диагностики), `Classify`, `ParseToolchain`, переопределения окружения,
   реальный поиск на машине и **реальная сборка шаблонного проекта обоими тулчейнами** + проверка, что путь попал в лог.
   Плюс `AiRpaStudio.Tests\MsBuildToolchainSettingTests.cs` (3 теста) — настройка `MsBuildToolchain` не расходится между
   `Settings.settings`, `Settings.Designer.cs` и `App.config` (проверено негативным сценарием: испорченное значение по
   умолчанию валит тест). Отдельно `MsBuildFeedbackServiceTests.BuildAsync_RealProject_*` собирает шаблонный проект
   через сервис чата и проверяет, что вывод начинается со строки тулчейна.

**Поправка к пункту 6 плана §5.1.** Гипотеза «MSBuild 2022 без VS-окружения падает с `MSB4236`» подтвердилась лишь частично:
причина — **не** отсутствие VS-окружения, а недоступность .NET SDK. Проверено 03.11.2026: MSBuild 2022, запущенный напрямую
(без `VsDevCmd`), собирает `AiRpaStudio.csproj`, `RPA.Chat.csproj`, `Agent.Core.csproj` и SDK-style `net472` с `UseWPF` —
0 ошибок. Но если убрать `C:\Program Files\dotnet` из `PATH`, тот же MSBuild падает с `MSB4236`/`MSB4276`; с `DOTNET_ROOT`
и `dotnet` в `PATH` (ровно то, что применяет локатор) restore и build проходят. Поэтому вместо подмешивания `VsDevCmd`
локатор выставляет `DOTNET_ROOT` и дополняет `PATH` **только если SDK не виден**, не перетирая пользовательские значения.


### `TD-12` — закрыт 03.11.2026 (вариант A)

`AiRpa.sln` собирается с **0 ошибок** в Release и Debug (MSBuild VS 2026). Что сделано:

1. **`RPA.CodeEditor` снят с сборки решения** — в `AiRpa.sln` удалены все строки `.Build.0` для GUID `{8FCD1308-…}`
   (остались `ActiveCfg`). Проект остался в решении как источник для `CP-03`, но не компилируется.
   Вариант «починить одной строкой» по-прежнему не годится: удаление `using OpenRPA.Input;` даёт 8 ошибок вместо 1.
2. **4 отсутствующих проекта убраны из `.sln`** — `CookiesManager`, `Activities.WinUI`, `Activities.WinUI.Tests`, `WinUIExplorer`;
   вместе с ними удалена папка решения `Activities.WinUI` и все строки `ProjectConfigurationPlatforms`/`NestedProjects` для их GUID.
   Именно они валили `Restore` с `MSB3202`, из-за чего сборка решения не доходила до компиляции.
3. **`RPA-1\Main.xaml` починен** — добавлен `Assign.To` в каноничной для C#-проектов форме
   `<mca:CSharpReference x:TypeArguments="x:String">output</mca:CSharpReference>` (форма `VariableReference` с атрибутом `Name`
   не принимается: `XC1033`), мусорное выражение `епрк` заменено валидным `"Hello from RPA-1"`.
4. **Удалён висячий `ProjectReference`** из `RPA-1\RPA-1.csproj` на `..\..\Activities.WinUI\...` — путь вёл на **два уровня выше корня
   репозитория**, MSBuild давал `MSB9008`.
5. Удалён мусорный `ProxyChecker\ProxyChecker_a5dbcita_wpftmp.csproj` (в git не отслеживался).

Доказательство: `MSBuild AiRpa.sln /t:Restore,Build /p:Configuration=Release` → exit 0; то же для `Debug` → exit 0;
`vstest.console.exe AiRpaStudio.Tests\bin\Release\net48\AiRpaStudio.Tests.dll` → 1136/1136 зелёные. Остались только
предупреждения `MSB3277` (`TD-11`) и `CS0067`.

Важно для приёмки: VS держит модель решения в памяти — после правки `.sln` нужен **Reload Solution**, иначе Error List
покажет устаревшие ошибки `RPA.CodeEditor` и `MSB9008` по `RPA-1`.

## 3. Попутные находки (в роадмэпе их не было)

| Находка | Влияние | Куда |
|---|---|---|
| В `AiRpa.sln` 4 проекта без файлов: `CookiesManager\CookiesManager.csproj`, `..\Activities.WinUI\Activities.WinUI.csproj`, `..\Activities.WinUI\Activities.WinUI.Tests.csproj`, `..\Activities.WinUI\WinUIExplorer.csproj` | полная сборка решения падает с `MSB3202` (4 ошибки), т.е. «собрать всё решение» в Ц2 сейчас невозможно в принципе | `TD-12` |
| `RPA-1\Main.xaml`: имя `епрк` (`CS0103`) и не задан обязательный аргумент `To` | ошибки при сборке проекта-робота в Release; в VS Error List они приходят как Warning | `TD-12` |
| `Activities.WinUI` лежит **вне** репозитория (`..\Activities.WinUI` не существует) | решение зависит от чужой папки; `RPA-1` ссылается на несуществующий проект | `TD-12` |
| В репозитории валяется `ProxyChecker\ProxyChecker_a5dbcita_wpftmp.csproj` | мусор от прерванной WPF-сборки (в `.gitignore` шаблон есть, файл остался) | гигиена |
| Поиск MSBuild продублирован в двух сборках | ✅ устранено 03.11.2026: оба места используют `MsBuildLocator` | `CP-04` |
| Прямой `MSBuild.exe` из VS 2022 без VS-окружения → `MSB4236` (`Microsoft.NET.Sdk.WindowsDesktop`) на SDK-style проектах; MSBuild 2026 в тех же условиях собирает | ✅ уточнено 03.11.2026: причина — недоступность .NET SDK (`dotnet` не в `PATH`), а не VS-окружение; лечится `DOTNET_ROOT` + `PATH`, что и делает локатор | `CP-04` |
| `AvalonEdit\global.json` пиннил .NET SDK `10.0.100` (`rollForward: major` → `10.0.401`), а он требует MSBuild 18 или новее | ✅ **устранено 04.11.2026**: пин → `9.0.100` + `rollForward: minor` (резолвится в `9.0.200`); `ICSharpCode.AvalonEdit` собирается `-t:Rebuild` обоими тулчейнами, `AiRpa.sln` Release — 0 ошибок из VS 2022 и VS 2026; страж — `SdkPinGuardTests` (5 тестов, проверены негативом) | `TD-14` |



**Устранено 03.11.2026 (`TD-12`, вариант A):** 4 отсутствующих проекта убраны из `.sln`; `RPA-1\Main.xaml` исправлен;
зависимость от папки вне репозитория снята (`RPA-1` больше не ссылается на `..\..\Activities.WinUI`); мусорный
`*_wpftmp.csproj` удалён. Осталось актуальным: при следующих прерванных WPF-сборках `*_wpftmp.csproj` появляются снова
(шаблон в `.gitignore` есть) — это гигиена, а не блокер.

## 4. Порядок работ внутри волны

```
TD-12 ✅ (03.11.2026) ──► CP-04 ✅ (03.11.2026) ──► CP-09 ──► AG-01 ──► AG-03 ──► AG-02 ──► AG-12 (каркас)
                                   │
        CP-02, CP-03 (решения)     └── TD-08 (параллельно, не блокирует)
```

Обоснование: без собирающегося решения нельзя ни зафиксировать эталон (`CP-09`), ни упаковать робота (`OR-03`).
`AG-01` (каталог) даёт данные, `AG-03` (outline) — дешёвое чтение, `AG-02` (мост) — мутации; `AG-12` растит тесты на фикстурах
параллельно `AG-01`…`AG-03`, чтобы к `AG-04` уже был полигон.

## 5. Пошаговые планы

### 5.1 `CP-04` — явный выбор MSBuild (P0) — ✅ закрыт 03.11.2026 (что сделано — §2)

1. Новый файл `AiRpaStudio\VsProject\MsBuildLocator.cs`: `enum MsBuildToolchain { Auto, Vs2022, Vs2026 }`,
   `MsBuildLocation Resolve(MsBuildToolchain preference)` → `{ Path, ToolchainName, Version, Found }`.
2. Внутри: `vswhere` с `-version "[17.0,18.0)"` для 2022 и `-version "[18.0,19.0)"` для 2026, `-prerelease` для 2026;
   порядок «явная версия → Auto (`-latest`) → well-known пути → PATH»; понятная диагностика, если не найдено.
3. `VsProjectBuilder.FindMSBuild()` делегирует в `MsBuildLocator`; `RPA.Chat\Services\MsBuildFeedbackService.FindMSBuild()`
   удаляется и тоже вызывает локатор (либо локатор переезжает в `Agent.Core`, чтобы 4.7.2-раннер мог его переиспользовать).
4. `VsBuildResult` дополняется `MsBuildPath`/`ToolchainName`; выбранный тулчейн пишется в лог сборки и виден в UI (панель сборки).
5. Тесты (`AiRpaStudio.Tests\MsBuildLocatorTests.cs`): путь существует и оканчивается на `MSBuild.exe`;
   `Auto` содержит `\18\` или `\17\`; явные режимы дают соответствующую ветку или `Found=false` с сообщением;
   `BuildArguments` не меняется (регресс уже есть в `VsProjectBuilderTests`).
6. Учесть 03.11.2026: прямой вызов `MSBuild.exe` из VS 2022 **без** VS-окружения падает с `MSB4236` («не удалось найти пакет SDK `Microsoft.NET.Sdk.WindowsDesktop`») на SDK-style проектах студии, а MSBuild 2026 в тех же условиях собирает. Значит локатор обязан либо предпочитать 2026, либо подмешивать окружение VS (`VsDevCmd`/переменные), иначе «сборка робота» из студии будет падать при выборе тулчейна 2022.


**Приёмка:** тесты зелёные, сборка студии обеими VS, в логе видно, каким MSBuild собирали.

### 5.2 `CP-02` — TFM `Rpa.Activities.Primitives` (P0) --- ЗАКРЫТ 03.11.2026 (итог: §1, §5.10)

Факт: ссылок на проект нет, роботы от него не зависят. Рекомендация — **понизить до `v4.7.2`**, чтобы матрица была однородной
и проект можно было подключить к роботу, не переписывая TFM.

1. В `Rpa.Activities.Primitives\Rpa.Activities.Primitives.csproj` заменить `<TargetFrameworkVersion>v4.8.1</TargetFrameworkVersion>` на `v4.7.2`.
2. Проверить `<Reference>` на `System.Activities.Presentation` (в 4.7.2 это GAC-сборка) и дизайнер `InvokeMethodDesigner`.
3. Собрать проект MSBuild обеих VS.
4. Обновить `KnownProjects` в `TfmMatrixGuardTests`: убрать запись `Rpa.Activities.Primitives` (теперь `net472` и совместим).

**Приёмка:** проект собирается на 4.7.2, стража зелёная без записи в белом списке.

### 5.3 `CP-03` — судьба `RPA.CodeEditor` (P0) --- ЗАКРЫТ 03.11.2026 (итог: §1, §5.10)

Факт: сборка не используется ни одним проектом, не компилируется без внешних `OpenRPA.*`.
Варианты (по возрастанию цены): **(a)** снять с сборки решения (убрать `.Build.0` в `AiRpa.sln`, проект остаётся в VS, но не строится);
**(b)** исключить проект из решения и перенести в `temp\`; **(c)** восстановить зависимости OpenRPA и починить.
Рекомендация — (a) + пометка в шапке проекта «не для роботов, не собирается».

1. В `AiRpa.sln` для GUID `{8FCD1308-94BA-4FD9-A1A7-33E86486033E}` убрать строки `<cfg>.Build.0` во всех конфигурациях.
2. Обновить `KnownProjects` (запись остаётся, ID `CP-03`) и строку матрицы TFM в роадмэпе.
3. Тест-страж уже проверяет, что к роботам проект не подключён.

**Приёмка:** сборка решения не содержит ошибок `RPA.CodeEditor`; в VS проект виден, но не участвует в сборке.

### 5.4 `CP-09` — эталонный регресс-набор (P0) --- ЗАКРЫТ 03.11.2026 (итог: §1, §5.10)

1. Создать `TestProcess\Net472Robot\` (legacy `csproj`, `v4.7.2`, `Main.xaml` с `Sequence` → `If` → `TryCatch` → `WriteLine`)
   и `TestProcess\Net48Robot\` (та же логика, `v4.8`).
2. Тест `TestProcess\RegressionTests.cs` (или скрипт `TestProcess\Run-Regression.ps1`): открыть проект парсером (`VsProjectOpener`),
   собрать через `MsBuildLocator` (`Auto`, `Vs2022`, `Vs2026` — три прогона), проверить `result.Success`.
3. Запуск робота — через `WorkflowHost`/`RpaRunner` (после `CP-08`), пока допускается проверка только сборки.
4. Зафиксировать в роадмэпе, что набор существует, иначе `OR-03` нечего упаковывать.

**Приёмка:** оба проекта собираются тремя прогонами, тест не требует сети (restore — из локального кэша).

### 5.5 `AG-01` — каталог активностей как данные (P0)

1. `RPA.Chat\Services\Workflow\ActivityCatalog.cs` (+ `ActivityDescriptor`, `ActivityArgumentDescriptor`).
2. Наполнение из загруженных сборок: `System.Activities`, `Activities*`, `ActivityLibraryBase`, `Rpa.Activities.Primitives`;
   тип, `DisplayName`, категория (`CategoryAttribute`/тулбокс), аргументы (`ArgumentDirectionAttribute`, `Argument.Type`, required, default), иконка.
3. Кэш на процесс + инвалидация при перезагрузке Toolbox (`WFToolboxHelper`).
4. Инструменты `activity_search(query, category, limit)` и `activity_describe(typeName)` в `AgentToolFactory` (read-only, `AgentPermissions`).
5. Тесты: `Sequence`, `If`, `WriteLine`, `InvokeMethod` — категория есть, направления аргументов верные; поиск по подстроке;
   негатив — неизвестный тип даёт структурированную ошибку, а не исключение.

**Приёмка:** тесты + `CreateRegistry` содержит инструменты; стража «нет MCP-категории» сохранена.

### 5.6 `AG-03` → `AG-02` — outline, затем мост к дизайнеру (P0)

1. `AG-03`: расширить `IStudioDocumentService` методом `GetWorkflowOutline(документ)` → узлы `{ Id, TypeName, DisplayName, Arguments, Variables, Errors }`.
   Id — путь индексов от корня + тип (стабилен между вызовами). Ошибки — из `DesignerValidator`.
2. `AG-02`: `IWorkflowEditingService` — `Insert(parentId, index, activityType)`, `Delete(id)`, `Move(id, parentId, index)`,
   `BeginEdit/Complete/Revert`, всё через `ModelItem`/`ModelEditingScope` дизайнера (Undo/Redo штатные).
3. Реализация в студии поверх `CustomWfDesigner`/`WorkflowDesignerEx`; регистрация — там же, где сейчас `IStudioDocumentService`.
4. Тесты: вставка в `Sequence` (индекс 0/середина/конец), удаление, откат по ошибке (`AG-09`), `Undo` возвращает состояние.

**Приёмка:** «правка через инструмент → Ctrl+Z» работает; outline совпадает с деревом дизайнера.

### 5.7 `TD-12` — закрыт 03.11.2026 (P0) ✅

Выполнено по варианту A:

1. `RPA.CodeEditor`: снят с сборки решения (`.Build.0` удалены, проект оставлен источником). Решение из §5.3
   (переписать или удалить порт) остаётся открытым по `CP-03`.
2. `AiRpa.sln`: 4 отсутствующих проекта удалены из решения (в репозиторий не возвращены) вместе с папкой решения `Activities.WinUI`.
3. `RPA-1\Main.xaml`: задан `Assign.To` через `mca:CSharpReference`, мусорное выражение заменено валидным; `RPA-1` остаётся
   в решении как эталонный 4.7.2-робот.
4. Удалён `ProxyChecker\ProxyChecker_a5dbcita_wpftmp.csproj`.
5. Проверка пройдена: `MSBuild AiRpa.sln` Release — 0 ошибок, Debug — 0 ошибок; `AiRpaStudio.Tests` — 1136/1136.

Остаток на будущее (не блокирует волну): `AiRpaStudio.Tests` не входит в `AiRpa.sln`, прогон тестов — отдельной командой (§8);
внести проект в решение отдельной задачей (`CP-05`).

### 5.8 `AG-12` — каркас тестов на фикстурах (P0)

1. `AiRpaStudio.Tests\Fixtures\Workflow\` — `sequence.xaml`, `if.xaml`, `trycatch.xaml`, `flowchart.xaml` (+ `flowchart-broken.xaml`).
2. `WorkflowFixtureTests`: загрузка фикстуры → outline → сохранение → повторная загрузка (round-trip без потерь и без изменения текста).
3. `AgentToolRegistryTests`: новые WF-инструменты видны в реестре, у всех корректные `AgentPermissions`, MCP-категории нет.
4. Негативные сценарии: битый XAML, вставка в несуществующий `parentId`, неверный тип аргумента.

### 5.9 `TD-08` — аудит `TODO.txt` (P1, параллельно)

1. Прочитать `AiRpaStudio\TODO.txt`, разбить на пункты.
2. По каждому — поиск реализации в коде (`RPA.NuGet`, `GitService`, дизайнер, тема и т.д.).
3. Результат — таблица в `ROADMAP.md` §12 с фактическим статусом; завести задачи только на реально отсутствующее.

### 5.10 Итоги итерации 03.11.2026 — `CP-02`, `CP-03`, `CP-05`, `CP-06`, `CP-07`, `CP-09`

Все шесть карточек колонки «В работе» доски `RPA - доска` закрыты подряд (сначала дешёвые P0-разблокираторы, затем формат/решение/эталон).

| Карточка | Что изменено | Тесты |
|---|---|---|
| `CP-02` | `Rpa.Activities.Primitives.csproj`: `v4.8.1` → `v4.7.2`; запись из `TfmMatrixGuardTests.KnownProjects` удалена | `TfmMatrixGuardTests` 4/4 |
| `CP-03` | `RPA.CodeEditor.csproj`: `v4.8` → `v4.7.2` + шапка «вне сборки, к роботам не подключать»; запись из белого списка удалена; запрет перенесён в `VsProjectLayoutGuardTests` | 2 новых теста (проверены негативом) |
| `CP-05` | `VsProjectParser`: 3 формы SDK-style, `TargetFrameworks`, `packages.config`/`PackageReference`, центральные версии; `VsProjectBuilder.RestoreArguments` + двухпроходная сборка; `VsBuildResult.Restore*` | `VsProjectFormatsTests` 8/8 |
| `CP-06` | `VsSolutionParser`: заголовок, поколение VS → тулчейн, конфигурации, папки решения, `NestedProjects`; отсутствующие проекты — предупреждение + узел `IsMissing`; `FSNodeType.SolutionFolder` | `VsSolutionLayoutTests` 6/6 |
| `CP-07` | Шаблон `ActivityLibrary2010`: 6 недостающих ссылок WF/дизайнера; плейсхолдер TFM сохранён; `Consts.defaultTargetFrameworkVersion` = `4.7.2` (умолчание роботов, пересечение с `CP-10`) | `VsTemplateTfmGuardTests` 5/5 |
| `CP-09` | `TestProcess\Net472Robot` (legacy WF 4.7.2), `TestProcess\Net48Robot` (SDK net48 + `PackageReference`), `TestProcess\BrokenRobot\BrokenMain.xaml`; известная запись `Net48Robot` в белом списке TFM (`CP-09`) | `TestProcessRegressionTests` 5/5 |

Итог прогона: `MSBuild AiRpaStudio.Tests.csproj` (VS 2026, `Debug`) — 0 ошибок; `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll` — **1458 тестов, все зелёные**.

Отклонение от плана (осознанное): эталонные роботы **не добавлены** в `AiRpa.sln` — иначе они попадают в сборку студии,
а на момент той итерации решение строилось только VS 2026 из-за `TD-14` (снят 04.11.2026). Открытие/сборка эталона проверяются тестами напрямую.
Ограничение приёмки снято 04.11.2026: `TD-14` закрыт (пин .NET SDK `9.0.100` + `rollForward: minor`), поэтому
полная сборка `AiRpa.sln` из **обеих** VS достижима и проверена; приёмка `CP-*` больше не сужается до затронутых проектов.


### 5.11 Итерация 04.11.2026 — `TD-14`, `TD-13`, `CP-08` (закрыт)

| Карточка | Что изменено | Тесты |
|---|---|---|
| `TD-14` | `AvalonEdit\global.json`: `10.0.100`/`rollForward: major` → `9.0.100`/`rollForward: minor`; резолвится установленный SDK `9.0.200`, который понимает MSBuild 17.13 (VS 2022) | `SdkPinGuardTests` 5/5 (негатив: возврат `10.0.100` валит 2 теста) |
| `TD-13` | `AvalonDock`/`AvalonDock.Themes.Metro`: `net5.0-windows` → `net5.0-windows7.0`; `NU1012` в `NuGet.Build.Tasks.Pack` больше не воспроизводится | `MSBuild AiRpa.sln /restore` Release: VS 2022 `/m:1` и VS 2026 `/m` — exit 0 |
| `CP-08` (A, B) | Паспорт совместимости и трекинг: `Net472OnNet48HostTests` — робот `v4.7.2` и `ActivitiesTracker` `v4.7.2` в net48-процессе, события Sequence/If/TryCatch/WriteLine, `InputText = max`, `Result = MAX` | 3 теста |
| `CP-08` (C) | `AiRpaStudio\Helpers\RobotAssemblyResolver.cs`: зависимости робота из `bin\<Config>[\<TFM>]`; сборки хоста (`System.*`, WF, `Activities*`, `ActivitiesTracker`, `RPA*`) не подменяются, понижение версии и чужой ключ запрещены, обработчик не бросает, подписка идемпотентна; проводка в `ActivityRunner` до десериализации XAML | `RobotAssemblyResolverTests` 30/30 (негатив: снятие запрета валит 12 тестов, комментарий вместо вызова валит сторож проводки) |
| `CP-08` (F) | `WorkflowHost` объявлен устаревшим прототипом (зашитый путь, `MessageBox`, `Console.ReadKey()`, `EnvDTE80`): собирается в решении и никем не подключается; роль раннера — `OR-02` | `WorkflowHostLegacyGuardTests` 2/2 |
| `CP-08` (D) | Головной движок `AiRpaStudio\Core\WorkflowRunEngine.cs`: `LoadActivityFromText`/`FromFile`/`FromAssembly`, `CreateApplication`, `Run` → `WorkflowRunResult` (состояние, выходные аргументы, ошибка со стеком, `TimedOut`, `ExitCode`, события трекера), `CompileExpressions = true` как в студии, предел времени с отменой и прерыванием; `ActivityRunner` грузит XAML и создаёт `WorkflowApplication` только через движок | `WorkflowRunEngineTests` 11/11: успех/падение/таймаут/отмена, битый XAML, страж «движок без UI», страж «`ActivityRunner` ходит через движок» |
| `CP-08` (E) | Прогон эталона штатным путём: `Main.xaml` робота 4.7.2 грузится движком при установленном `RobotAssemblyResolver` и прогоняется на 4.8-хосте | `LegacyRobotStudioRunTests` 3/3: `Result = MAX` + трекинг Sequence/If/TryCatch/WriteLine/Assign, ветка `If.Else`, негатив «в пустом каталоге сборка робота не находится» |

Итог прогона 04.11.2026: `MSBuild AiRpaStudio.Tests.csproj` (VS 2026, `Debug`) — 0 ошибок;
`vstest.console.exe` — **1512 тестов, все зелёные** (было 1458; +14 в этой итерации — 11 `WorkflowRunEngineTests` и 3 `LegacyRobotStudioRunTests`). Сборка `AiRpa.sln` Release: VS 2026 — exit 0;
VS 2022 — exit 0. После смены TFM у `AvalonDock` (`TD-13`) сборку решения запускать с `/restore` — иначе `NETSDK1005`.

`CP-08` закрыт 04.11.2026: головной движок выделен (`WorkflowRunEngine`) и эталон прогоняется штатным путём
(`LegacyRobotStudioRunTests`). Вне автотестов остаётся UI-состояние `ActivityRunner` (read-only дизайнера,
точки останова, окно `Locals`): ему нужен живой `CustomWfDesigner` и `TrackHandler` с окном — проверяется
вручную отладкой в студии (в колонке «В работе» карточки больше нет, ссылка — `ROADMAP.md` §5).

Движок `WorkflowRunEngine` — основа раннера `OR-02`: ему останется только хост (аргументы, лог, код возврата).

## 6. Definition of Done волны 1

- `MSBuild AiRpa.sln /p:Configuration=Release` — 0 ошибок из обеих VS (2022 и 2026).
- Полный прогон `AiRpaStudio.Tests` (MSBuild + `vstest.console.exe`) зелёный.
- Новые тесты: матрица TFM, локатор MSBuild, каталог активностей, outline, мост с Undo, фикстуры workflow.
- `TD-08` — таблица статусов заполнена; в `TODO.txt` не осталось пунктов «невыполнено», которые на самом деле сделаны.
- `ROADMAP.md` обновлён: снятые записи из белого списка TFM, актуальные статусы `CP-*`, `TD-12`.

## 7. Риски

| Риск | Митигация |
|---|---|
| Восстановление OpenRPA-зависимостей в `RPA.CodeEditor` притащит чужие сборки в поставку | вариант (a) — снять с сборки решения, проект оставить как источник |
| `Activities.WinUI` физически вне репозитория — решение не должно от него зависеть | убрать из `AiRpa.sln`, вести отдельно |
| Понижение TFM `Rpa.Activities.Primitives` может сломать внешнего потребителя | в репозитории ссылок нет; зафиксировать в роадмэпе |
| Тесты, тянущие restore/MSBuild, флакуют без сети (`NU1900`) | restore из локального кэша, `--no-restore` в тестах, тег `[Explicit]` для сетевых |
| `TD-04`/`TD-07`-подобные «визуальные» пункты нельзя закрыть тестом | ручной чек-лист, как в `Vs2026_Theme_Acceptance.md` |

## 8. Команды проверки

```powershell
# путь к MSBuild (как в этом документе)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msb = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

# решение целиком (TD-12)
& $msb 'AiRpa.sln' /nologo /m /v:minimal /p:Configuration=Release

# тесты
& $msb 'AiRpaStudio.Tests\AiRpaStudio.Tests.csproj' /nologo /m /v:minimal /p:Configuration=Debug
$vstest = & $vswhere -latest -find 'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe' | Select-Object -First 1
& $vstest 'AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll'

# только стража матрицы TFM
& $vstest 'AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll' /TestCaseFilter:"FullyQualifiedName~TfmMatrixGuardTests"
```

## 9. Отклонения от плана (`AG-01`, `AG-03`, `AG-12`) — 04.11.2026

1. **Каталог активностей реализован в студии, а не в `RPA.Chat`.** В плане (§5.5) предполагался
   `RPA.Chat\Services\Workflow\ActivityCatalog.cs`. Причина отклонения: данные обязаны совпадать
   с панелью «Активности», то есть с `ActivityPaletteRegistry`/`StudioActivityPalette`
   (`AiRpaStudio\Core\Generation\`), а у `RPA.Chat` нет ни WF-ссылок, ни библиотек активностей.
   Итог: в `RPA.Chat` живут контракт и DTO (`IActivityCatalog`, `ActivityInfo`, `ActivityArgumentInfo`,
   `ActivityContainerInfo`, `ActivityCatalogText`), реализация — `AiRpaStudio\Core\Workflow\StudioActivityCatalog.cs`,
   точка подключения — `ChatEnvironment.ActivityCatalog` (по образцу `IStudioDocumentService`).
   Плюс: инструменты тестируются без UI и без WF (фейковый каталог), а состав каталога не может
   разойтись с Toolbox.
2. **Иконки в каталог не попали.** В палитре нет источника иконок (их подставляет `WFToolboxHelper`
   при построении Toolbox). Пункт карточки вынесен в отдельную задачу вместе с `AG-08`.
3. **Фикстуры.** `FlowStep` — не `Activity` и не принимает `DisplayName`, поэтому узел блок-схемы
   описывается только `x:Name` + вложенной активностью. Корень файла без `x:Class` грузится как
   `DynamicActivity`, поэтому проверки сравнивают `DynamicActivity.Implementation`.
4. **Проверки.** `AiRpaStudio.Tests\Workflow\`: `WorkflowFixtureTests` 21, `ActivityCatalogTests` 28,
   `ActivityToolsTests` 13. Негатив проверен: отключение читаемого имени категории
   (`StudioActivityCatalog.CategoryNameOf`) роняет 4 теста. Полный прогон — 1574/1574 (было 1512).
   `MSBuild AiRpa.sln /p:Configuration=Release` — 0 ошибок из VS 2022 и из VS 2026.
5. **Структура строится по файлу, а не по живому конструктору.** В плане (§5.6) предполагалось
   `IStudioDocumentService.GetWorkflowOutline(документ)`. Причина: живая модель требует `ModelItem`-адресации
   и UI-потока, то есть того самого моста `AG-02`, которого ещё нет. Итог: `workflow_outline` читает файл через
   `WorkflowModelReader` (адреса совпадают с путями `ModelItem`, поэтому правки `AG-04` их примут), а структура
   несохранённого документа появится вместе с `AG-02`.
6. **`Ok` = «структура прочитана», а не «процесс валиден».** Ошибки WF возвращаются в `Issues` с адресом узла
   (`WorkflowOutlineIssue.ElementId`), дерево отдаётся всегда: иначе цикл «правка → валидация → правка»
   невозможен, а `AG-09` нечем откатывать.
7. **Ридер модели расширен, хотя формально это не `AG-03`.** Без этого структура теряла шаги: добавлены переменные
   (`Sequence.Variables`), литеральные значения аргументов (`Literal<T>` — иначе `Text="first"` выглядел
   как «аргумент задан, значение неизвестно»), тело обработчиков (`Catch` → `root/Catches[0]/Action/Handler`)
   и граф блок-схемы (`StartNode`/`Next`/`True`/`False`/`Cases` → `root/StartNode/Action`). Коллекция
   `Flowchart.Nodes` намеренно не разворачивается отдельно: она ссылается на те же узлы и дала бы дубли адресов.
8. **Текст ответа печатает только заданные аргументы.** Полный перечень (включая пустые служебные вроде
   `WriteLine.TextWriter`) занимает контекст модели; его отдаёт `activity_describe` из `AG-01`.
   Машинный DTO (`WorkflowOutlineNode.Arguments`) по-прежнему содержит все аргументы.
9. **Проверки.** `WorkflowOutlineServiceTests` 21, `WorkflowOutlineToolTests` 12, `WorkflowFixtureTests` 25
   (+ фикстура `variables.xaml`). Негатив проверен: снятая привязка ошибки к узлу (`IdOf` → пустая строка)
   роняет `Outline_BrokenByValidation_KeepsTreeAndBindsIssuesToNodes`. Полный прогон — 1610/1610 (было 1574).
   `MSBuild AiRpa.sln /p:Configuration=Release` — 0 ошибок из VS 2022 и из VS 2026.
10. **Что осталось по волнам:** `AG-12` — негативы волн 3–8, реестр инструментов целиком, откаты;
   далее `AG-02` (мост к дизайнеру в UI-потоке, требует `AG-03`-адресов — они готовы).

## 10. Итерация 05.11.2026 — `AG-02` (мост «агент ↔ открытый дизайнер»)

`AG-02` закрыт. Что появилось:

1. **Контракт (`RPA.Chat\Services\Workflow\`)** — `IWorkflowEditingService` (`BeginEdit`, `Undo`, `Redo`,
   `GetLiveOutline`, `IsOpenInDesigner`), `IWorkflowEditSession` (`Insert`, `Delete`, `Move`, `Complete`,
   `Revert`, `Outline`), запросы `WorkflowInsertRequest`/`WorkflowMoveRequest`, ответ `WorkflowEditResult`
   (адреса изменённого и появившегося элементов, состояние истории, структура процесса, замечания),
   формат ответа `WorkflowEditText`, null-object `NullWorkflowEditingService` и неактивная сессия
   `UnavailableWorkflowEditSession` (отказ с причиной и подсказкой — не исключение).
2. **Реализация (`AiRpaStudio\Core\Workflow\`)** — `StudioWorkflowEditingService` (вкладка по пути через
   `IWorkflowDesignerHost`, маршалинг на UI-поток через `IDesignerUiThread`, `undo`/`redo` через
   `UndoEngine`, структура несохранённого документа) и `StudioWorkflowEditSession` (вставка, удаление,
   перенос через `ModelService`/`ModelItem`; каждая правка — свой `ModelEditingScope`).
3. **Подключение** — `ChatEnvironment.WorkflowEditing`, `MainView` также связывает
   `StudioWorkflowOutlineService.LiveOutlineSource` с живой моделью: `workflow_outline` теперь видит
   несохранённые правки (закрыт остаток `AG-03` «структура несохранённого документа»).
4. **Проверки** — `WorkflowEditingTests` 23 (headless), `WorkflowEditBridgeTests` 7 (живой конструктор,
   домен стенда): вставка в начало/середину/конец с проверкой адресов, `complete`, `undo`/`redo`,
   `revert` пачки, отказ правки, удаление и перенос, отказ «файл не открыт». Полный прогон — 1641/1641
   (было 1611). `MSBuild AiRpa.sln /p:Configuration=Release` — 0 ошибок из VS 2022 и из VS 2026.

Отклонения от плана и решения, принятые по ходу:

1. **Мост стоит на существующей правке модели.** В плане (§5.6) предполагался новый путь
   `Insert/Delete/Move` поверх `ModelItem`. Причина отклонения: `DesignerModelSession` уже решает
   подбор контейнера, `ActivityAction` (`ForEach.Body`) и запрет неподдерживаемых контейнеров, и это
   проверено палитрой. Итог: `Insert` идёт через `DesignerModelSession.InsertActivity`, к нему добавлены
   `Delete`, `Move`, `Complete`, `Revert`; `ResolveItem`/`ChooseContainer`/`CreateItemForContainer`
   открыты как `internal`, чтобы адрес читался и правился одним кодом.
2. **Один `ModelEditingScope` на правку, а не на пачку.** План (§5.6/`AG-09`) читался как «один scope
   на пачку». Стенд показал, что до `Complete` конструктор держит изменения в буфере: вставки внутри
   одного scope не появлялись ни в дереве, ни в адресах (`ActivityCount` не менялся), а удаление не
   находило контейнер. Итог: правка = scope = одна запись истории (`Ctrl+Z` снимает ровно одну правку);
   пачка группирует правки и умеет отменить их все (`Revert` — N шагов истории).
3. **`Select` (визуальное выделение) не реализован.** Адресация правок — по `id` из `workflow_outline`;
   выделение ничего не добавляет агенту, а `Selection`-сервис конструктора потянул бы UI-зависимость.
4. **`DisplayName` при вставке — отдельная правка по адресу уже вставленного элемента.** В том же scope
   конструктор не отдаёт дерево с новой активностью, поэтому подпись некуда приложить; зато вставка не
   зависит от того, как конструктор назвал элемент.
5. **Адрес появившегося элемента считается по контейнеру до вставки**, а не «диффом» идентификаторов
   «до/после»: дифф ошибается, когда вставка сдвигает индексы соседей (проверено негативом —
   `AddressFor` → `[0]` роняет `Insert_ReturnsAddressesThatAppearInOutline`). Формат адреса тот же,
   что у `workflow_outline`: `parent/Container[индекс]`, `parent/Then`, `parent/Body/Handler`.
6. **Отказ правки откатывает только её.** Пачка остаётся открытой, чтобы агент исправил адрес и
   повторил правку; «половины правок» в модели не остаётся. `Revert` отменяет все правки пачки.
7. **UI-поток берётся у `View` дизайнера.** У `WorkflowDesignerEx` нет свойства `Dispatcher`; если
   поверхность не создана — это ошибка, а не тихая работа в потоке вызывающего.
8. **Что осталось по волнам:** `AG-12` — негативы волн 4–8, реестр инструментов целиком; далее `AG-04`
   (инструменты `activity_insert`/`activity_delete`/`activity_move`/`activity_wrap` поверх готового моста)
   и `AG-06` (валидация с адресами узлов).


## 11. Волна 4 — 06.11.2026: `AG-04` (инструменты правки) и `AG-09` (атомарность)

`AG-04` и `AG-09` закрыты. Что появилось:

1. **Инструменты (`RPA.Chat\Services\AgentTools\AgentToolFactory.cs`)** — `activity_insert`,
   `activity_delete`, `activity_move`, `activity_wrap`, `activity_undo`, `activity_redo`: `mutates: true`,
   `AgentPermissions.EditFiles`, в Plan не видны (фильтр `ForMode`), адреса — те же `id`, что у
   `workflow_outline`. Пачка (`activities`, `elementIds`, `moves`) уходит одной правкой.
2. **Оркестрация (`RPA.Chat\Services\Workflow\WorkflowEditRunner.cs`)** — `BeginEdit` → правки →
   валидация → `Complete`/`Revert`. Логика вынесена из инструментов, поэтому проверяется на фейковой
   сессии без конструктора: инвариант «отказ любой правки = модель не изменилась» держится в одном месте.
3. **Валидация (`WorkflowValidation.cs`, карточка `AG-09`)** — `WorkflowValidationDelta.Compare` отдаёт
   только новые ошибки; сравнение по тексту, а не по адресу: вставка сдвигает индексы, и
   `root/Activities[2]` после правки — уже другой элемент.
4. **Обёртка (`WorkflowWrapRequest`, `IWorkflowEditSession.Wrap`, `StudioWorkflowEditSession.WrapCore`)** —
   обёртка занимает место элемента, элемент становится её первым ребёнком; всё в одном `ModelEditingScope`,
   поэтому `Ctrl+Z` снимает обёртку целиком. `activity_wrap` возвращает два адреса: обёртки и обёрнутой
   активности (первый — сама обёртка, второй — `NewElementId`, по нему агент работает дальше).
5. **Проверки** — `WorkflowEditToolTests` 25 (headless) и `WorkflowWrapBridgeTests` 6 (живой конструктор:
   обёртка в `Sequence`, обёртка в `If.Then`, адреса, неоднозначный контейнер, неизвестный тип, `undo`).
   Полный прогон 1672/1672 (Debug + `vstest`, как в `AGENTS.md`). `MSBuild AiRpa.sln /t:Rebuild
   /p:Configuration=Release` — 0 ошибок из VS 2022 и VS 2026. Негатив проверен: `Strict && false` роняет
   `Strict_RevertsWhenNewValidationErrorAppears`, снятый `Revert` в `Abort` роняет проверку подсказки.

Отклонения от плана и решения, принятые по ходу:

1. **`validate=report` по умолчанию, а не «откат при любой ошибке валидации».** План (`AG-09`)
   предполагал автоматический откат при ошибке валидации. На практике шаг, у которого ещё не заданы
   аргументы (`argument_set` — карточка `AG-05`), валидацию не проходит всегда: откат сделал бы сборку
   робота невозможной. Поэтому откат включается режимом `strict`, а по умолчанию правка остаётся, и агент
   получает только новые ошибки с адресами — это и есть цикл «правка → валидация → правка».
2. **Сравнение ошибок по тексту, а не по `(адрес, текст)`.** Адреса меняются от самой правки; сравнение
   по адресу давало бы «новую» ошибку на каждом сдвиге индексов. Побочный эффект: правка, добавившая
   ошибку с тем же текстом, что уже была, новой не считается — принято осознанно.
3. **Оркестрация в `RPA.Chat`, а не в студийной сессии.** Валидация уже приходит в структуре
   (`IWorkflowEditSession.Outline`), поэтому `strict`/`report` не требуют новых членов контракта и
   тестируются без UI. Студийная сессия осталась механизмом правки модели.
4. **`activity_wrap` — одна обёртка за вызов.** Пакетная обёртка отложена: у обёрнутого элемента меняется
   адрес, и следующий запрос пачки ссылался бы на устаревший адрес. `insert`/`delete`/`move` пачкой идут —
   их адреса не переезжают.
5. **Разрешения — `AgentPermissions.EditFiles`.** Отдельная категория не вводилась: карточка `AG-10`
   пересмотрит матрицу целиком, вместе с системным промптом (`AG-11`).
6. **Полный прогон в Release падает по `StackOverflowException`** в `ToolboxDragContractTests` — так и до
   этой волны: прогон без новых фикстур падает так же, а тот же набор без namespace `Workflow` проходит
   (1487/1487). Основной прогон — Debug + `vstest` по `AGENTS.md` (1672/1672). Наблюдение внесено в риски:
   накопительное состояние домена конструктора в одном процессе, к карточке не относится.
7. **Не сделано:** `argument_set`/`variable_*` (`AG-05`), отдельный `workflow_validate` (`AG-06` —
   частично закрыт здесь: новые ошибки приходят в сводке правки), `Select` (агенту не нужен).

## 12. Волна 5 — 06.11.2026: `AG-05` (аргументы и переменные)

`AG-05` закрыт. Что появилось:

1. **Контракт** — `RPA.Chat\Services\Workflow\WorkflowArguments.cs`: `WorkflowArgumentSetRequest`
   (литерал `value` **или** выражение `expression`; `typeName` для необобщённых `InArgument`/`OutArgument`;
   `language` для явного языка), `WorkflowVariableAddRequest`, `WorkflowVariableRemoveRequest`,
   `WorkflowExpressionLanguage` и чистая политика `WorkflowExpressionPolicy`.
2. **Инструменты** — `argument_set`, `variable_add`, `variable_remove` в `AgentToolFactory`
   (`mutates: true`, `AgentPermissions.EditFiles`, в Plan не видны); пачки `arguments`/`variables`,
   `validate=report|strict|off`, `include_outline`.
3. **Оркестрация** — `WorkflowEditRunner.SetArgument/AddVariable/RemoveVariable`: один вызов инструмента =
   одна атомарная пачка (`AG-09`), отказ любой правки откатывает всю.
4. **Реализация в студии** — `StudioWorkflowEditSession.SetArgumentCore/AddVariableCore/RemoveVariableCore`:
   тип и направление аргумента из дескриптора палитры, литерал приводится к типу фабрикой палитры,
   выражение проверяется политикой и разбором Roslyn, язык документа определяется по XAML;
   переменные — тип из списка студии, значение по умолчанию, проверка идентификатора и дубля.
5. **Язык выражения в фабрике** — `PaletteValue.Language` + `PaletteExpressionLanguage`: C# даёт
   `CSharpValue<T>`/`CSharpReference<T>`, VB — `VisualBasicValue<T>`/`VisualBasicReference<T>`.

Отклонения от плана:

1. **Проверка выражения — синтаксис, а не семантика.** Разбор идёт Roslyn'ом (`WorkflowExpressionSyntax`)
   без привязки имён: на момент правки переменные и аргументы процесса ещё не собраны, а неизвестные имена
   и типы — это семантика `TextExpression`, которую реализует валидация WF. Её результат приходит
   в сводке правки (`AG-09`), поэтому цикл «правка → ошибка → правка» замкнут без дублирования компилятора.
   Граница зафиксирована тестом: `1 + * 2` разбор синтаксиса принимает (в грамматике Roslyn `*` —
   разыменование указателя) и такая ошибка доходит до валидации, а не отбрасывается инструментом.
2. **Язык выражения определяется по документу, явный `language` главнее.** XAML с `Microsoft.VisualBasic`/
   `VisualBasicValue` считается VB-документом; иначе C#. Смешение языков ловится политикой до правки
   («выражение записано в синтаксисе C#, а документ на Visual Basic»).
3. **Переменные только там, где есть коллекция `Variables`** (`Sequence`, `Flowchart`, корень процесса).
   Активность без неё — отказ с объяснением: «молча потерянная» переменная хуже отказа.
4. **Типы переменных — из белого списка `PaletteTypeNames`** (`String`, `Int32`, `Boolean`, `Object`, …):
   тип вне списка в XAML не попадает, поэтому агент получает отказ со списком известных имён.
5. **Перекрытие имени — предупреждение, а не отказ.** WF разрешает тень во вложенной области; отказ запретил
   бы законную конструкцию, поэтому агент получает предупреждение `shadowing` и знает, какую переменную правит.
6. **`Variable<T>.Default` читается через `BindingFlags.DeclaredOnly`.** Свойство объявлено дважды
   (`LocationReference.Default` типа `Activity` и `Variable<T>.Default` типа `Activity<T>`), и обычный
   `GetProperty("Default")` бросает `AmbiguousMatchException` — поймано тестом на живом конструкторе.
7. **Адрес результата у правок существующего элемента.** У `argument_set`/`variable_*` новых адресов нет,
   поэтому `WorkflowEditRunner.Apply` получил флаг `addressFromElement`: в ответе виден сам элемент —
   иначе агент не понимал, куда попала правка.
8. **Не сделано:** `workflow_validate` как отдельный инструмент (`AG-06`), `xaml_read`/`xaml_write`
   (`AG-07`), создание активностей (`AG-08`), отдельная категория разрешений (`AG-10`), системный промпт
   (`AG-11`).