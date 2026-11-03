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
| `CP-02` | `Rpa.Activities.Primitives` = `v4.8.1`. **На неё не ссылается ни один `.csproj` решения** → роботы от неё не зависят; блокером для Ц2 не является | `Select-String` по `ProjectReference` — 0 совпадений на `Rpa.Activities.Primitives`/`RPA.CodeEditor` |
| `CP-03` | `RPA.CodeEditor` = `v4.8`, **никем не используется**, не компилируется: `GenericTools.cs` — порт из OpenRPA, нужны внешние типы `OpenRPA.Input`, `Log`, `AutomationHelper`, `IMainWindow` | `RPA.CodeEditor\GenericTools.cs:1`, ошибки сборки решения |
| `CP-04` | **Закрыт 03.11.2026.** Дубль поиска устранён: `VibeCoder.Core\Services\MsBuildLocator.cs` — единый поиск (`Auto` / `Vs2022` / `Vs2026`), откат виден в логе, тулчейн пишется в `VsBuildResult`, защита от `MSB4236` | `MsBuildLocatorTests` — 39/39 зелёные, включая реальную сборку шаблонного проекта обоими тулчейнами; полный прогон 1182/1182 |
| `CP-09` | Есть только `AiRpaStudio\TestProcess\` — песочница студии (`project.json` + 3 `.xaml`, без `.csproj`); эталонных проектов `net472`/`net48` нет | `Get-ChildItem AiRpaStudio\TestProcess` — 6 файлов, `.csproj` отсутствует |
| `TD-12` | **Закрыт 03.11.2026 (вариант A).** `ProxyChecker` починен; `RPA.CodeEditor` снят с сборки решения; 4 отсутствующих проекта + папка решения `Activities.WinUI` убраны из `.sln`; `RPA-1\Main.xaml` исправлен, висячий `ProjectReference` удалён | `MSBuild AiRpa.sln` Release и Debug → exit 0; `vstest` → 1136/1136 |
| `AG-01` | `ActivityCatalog`, `activity_search`, `activity_describe` — только в роадмэпе, кода нет | поиск по репозиторию — 0 совпадений в коде |
| `AG-02` | `IWorkflowEditingService` — кода нет; `ModelEditingScope` используется только внутри дизайнеров активностей | `Activities.Flow\Designers\RunScriptDesigner.xaml.cs`, `CSharpExpressionEditor\RoslynExpressionEditor.xaml.cs:256` |
| `AG-03` | `workflow_outline` — кода нет | поиск — 0 совпадений |
| `AG-12` | Фикстур XAML для workflow и реестра WF-инструментов нет | в `AiRpaStudio.Tests` нет каталога фикстур workflow |
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

Новый файл `VibeCoder.Core\Services\MsBuildLocator.cs` (`netstandard2.0`, поэтому его сможет переиспользовать и
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
(без `VsDevCmd`), собирает `AiRpaStudio.csproj`, `RPA.Chat.csproj`, `VibeCoder.Core.csproj` и SDK-style `net472` с `UseWPF` —
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
| `AvalonEdit\global.json` пиннит .NET SDK `10.0.100` (`rollForward: major` → `10.0.401`), а он требует MSBuild ≥ 18 | MSBuild VS 2022 (17.13) не собирает `ICSharpCode.AvalonEdit` → сборка студии из VS 2022 падает (замерено 03.11.2026; файл из первого коммита) | `TD-14` |



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
   удаляется и тоже вызывает локатор (либо локатор переезжает в `VibeCoder.Core`, чтобы 4.7.2-раннер мог его переиспользовать).
4. `VsBuildResult` дополняется `MsBuildPath`/`ToolchainName`; выбранный тулчейн пишется в лог сборки и виден в UI (панель сборки).
5. Тесты (`AiRpaStudio.Tests\MsBuildLocatorTests.cs`): путь существует и оканчивается на `MSBuild.exe`;
   `Auto` содержит `\18\` или `\17\`; явные режимы дают соответствующую ветку или `Found=false` с сообщением;
   `BuildArguments` не меняется (регресс уже есть в `VsProjectBuilderTests`).
6. Учесть 03.11.2026: прямой вызов `MSBuild.exe` из VS 2022 **без** VS-окружения падает с `MSB4236` («не удалось найти пакет SDK `Microsoft.NET.Sdk.WindowsDesktop`») на SDK-style проектах студии, а MSBuild 2026 в тех же условиях собирает. Значит локатор обязан либо предпочитать 2026, либо подмешивать окружение VS (`VsDevCmd`/переменные), иначе «сборка робота» из студии будет падать при выборе тулчейна 2022.


**Приёмка:** тесты зелёные, сборка студии обеими VS, в логе видно, каким MSBuild собирали.

### 5.2 `CP-02` — TFM `Rpa.Activities.Primitives` (P0)

Факт: ссылок на проект нет, роботы от него не зависят. Рекомендация — **понизить до `v4.7.2`**, чтобы матрица была однородной
и проект можно было подключить к роботу, не переписывая TFM.

1. В `Rpa.Activities.Primitives\Rpa.Activities.Primitives.csproj` заменить `<TargetFrameworkVersion>v4.8.1</TargetFrameworkVersion>` на `v4.7.2`.
2. Проверить `<Reference>` на `System.Activities.Presentation` (в 4.7.2 это GAC-сборка) и дизайнер `InvokeMethodDesigner`.
3. Собрать проект MSBuild обеих VS.
4. Обновить `KnownProjects` в `TfmMatrixGuardTests`: убрать запись `Rpa.Activities.Primitives` (теперь `net472` и совместим).

**Приёмка:** проект собирается на 4.7.2, стража зелёная без записи в белом списке.

### 5.3 `CP-03` — судьба `RPA.CodeEditor` (P0)

Факт: сборка не используется ни одним проектом, не компилируется без внешних `OpenRPA.*`.
Варианты (по возрастанию цены): **(a)** снять с сборки решения (убрать `.Build.0` в `AiRpa.sln`, проект остаётся в VS, но не строится);
**(b)** исключить проект из решения и перенести в `temp\`; **(c)** восстановить зависимости OpenRPA и починить.
Рекомендация — (a) + пометка в шапке проекта «не для роботов, не собирается».

1. В `AiRpa.sln` для GUID `{8FCD1308-94BA-4FD9-A1A7-33E86486033E}` убрать строки `<cfg>.Build.0` во всех конфигурациях.
2. Обновить `KnownProjects` (запись остаётся, ID `CP-03`) и строку матрицы TFM в роадмэпе.
3. Тест-страж уже проверяет, что к роботам проект не подключён.

**Приёмка:** сборка решения не содержит ошибок `RPA.CodeEditor`; в VS проект виден, но не участвует в сборке.

### 5.4 `CP-09` — эталонный регресс-набор (P0)

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
