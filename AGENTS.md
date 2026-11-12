# Правила разработки (AiRpaStudio)

## Язык общения в чате - русский

## Терминология
- «студия» = WPF-проект `AiRpaStudio` (решение `AiRpa.sln`, проект `AiRpaStudio\AiRpaStudio.csproj`,
  `RootNamespace`/`AssemblyName` = `RPA`).

## TDD — обязательное правило
- Любая разработка идёт только через тестирование (red → green → refactor).
- Сначала пишется падающий тест, затем минимальная реализация, затем рефакторинг.
- Каждая новая возможность должна быть покрыта юнит-тестами в проекте `AiRpaStudio.Tests`.
- Перед завершением задачи все тесты обязаны проходить.
  ⚠️ `dotnet test` в этом репозитории НЕ работает: legacy WPF-проекты не собираются
  SDK-шным MSBuild. Правильная команда — MSBuild из Visual Studio + vstest:
  `MSBuild.exe AiRpaStudio.Tests\AiRpaStudio.Tests.csproj -t:Build`, затем
  `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll`.

## Соглашения проекта
- Целевая платформа: .NET Framework 4.8, WPF-приложение (`OutputType=WinExe`),
  `RootNamespace`/`AssemblyName` = `RPA`.
- UI-стек: WPF (полоса заголовка своя — `Controls\StudioTitleBar.xaml`, MahApps.Metro в решении нет), AvalonDock (докинг-панели), AvalonEdit (редактор кода), MaterialDesignThemes.Wpf (база контролов),
  Material.Icons.WPF, LogControlLibrary (просмотр логов).
- Целевые платформы (правило, `CP-10`): **студия — .NET Framework 4.8**, а **всё, что попадает к роботу, — 4.7.2**
  (`net472`). Новый проект, на который может ссылаться робот, обязан целиться в `net472`/`netstandard2.0`;
  исключение допустимо только с записью в белом списке `AiRpaStudio.Tests\TfmMatrixGuardTests.cs` (ID задачи + причина).
  Генерируемые студией роботы и библиотеки активностей по умолчанию создаются на **4.7.2**
  (`Consts.defaultTargetFrameworkVersion`), шаблон VS подставляет TFM через `$targetframeworkversion$`.
- Сборка: `MSBuild` из **VS 2026** (MSBuild 18) **и** из **VS 2022** (MSBuild 17.13) собирает решение полностью:
  `MSBuild AiRpa.sln /p:Configuration=Release` → 0 ошибок (`TD-14` закрыт 04.11.2026; проверено `-t:Rebuild`
  для `AvalonEdit` обоими тулчейнами). Пин .NET SDK — `AvalonEdit\global.json` = `9.0.100` + `rollForward: minor`
  (SDK 10.x требует MSBuild 18 или новее и ломал сборку из VS 2022); страж — `AiRpaStudio.Tests\SdkPinGuardTests.cs`.
  После смены TFM у `AvalonDock` (`TD-13`, закрыт 04.11.2026) сборку решения запускайте с `/restore`
  (иначе `NETSDK1005`); параллельную сборку из VS 2022 не запускать — `/m:1`.
- Дизайнер рабочих процессов: `System.Activities.Presentation.WorkflowDesigner`
  (Windows Workflow Foundation).
- MVVM: модель-центричный шаблон на `FW.EasyMvvm` (см. раздел ниже).
- Команды привязываются по имени метода модели через reflection (см. раздел ниже).
- Локализация: RU/EN через `ResourceManager` + `Properties.Settings.Language` (см. `App.xaml.cs`).
- Настройки сохраняются через `Properties.Settings` (метод `Save()`).
- Логирование: `RPA.Logging.Log` (запись в `log.txt`) + просмотр через `LogControlLibrary`.
- AI-интеграция: MCP-сервер — папка `AiRpaStudio\Mcp`, пространство имён `RPA.Mcp` (stdio/http).
- Ядро AI-агента — проект `Agent.Core` (`Agent.Core\Agent.Core.csproj`, `netstandard2.0`,
  `RootNamespace`/`AssemblyName` = `Agent.Core`). Прежнее имя сборки — `VibeCoder.Core` — больше не используется;
  оно осталось единственной легаси-константой `PromptPaths.LegacyAppDataFolderName` (чтение `%AppData%\VibeCoder\Prompts`
  последним кандидатом, чтобы у существующих установок не пропали промпты). Страж — `AiRpaStudio.Tests\AgentCoreRenameGuardTests.cs`
  (скан `.cs`/`.xaml`/`.csproj`/`.sln`: строковое имя сборки компилятор не проверяет, поэтому рефлексия и `assembly=` в XAML
  ломаются молча).

## Панель NuGet (`RPA.NuGet`)

- Дизайн — как менеджер пакетов Visual Studio 2026, но **без скруглений**: во всех `Border` и
  шаблонах `CornerRadius="0"` (панели студии плоские, ср. `Helpers\PropertyWindowStyleHelper.cs`).
  Токены `StudioPalette` (`8/4/6`) не меняются — они про окно, вкладки и меню.
- Цвета — только ключами `Studio.*` из `StudioBrushes` (`Surface/SurfaceAlt/Hover/Selected/
  Separator/SeparatorStrong/Foreground/ForegroundMuted` + `Studio.Semantic.Validation`), шрифт —
  только `{DynamicResource Studio.FontFamily}`, иконки — векторные `Path` (новых зависимостей в
  `RPA.NuGet` нет). Стражи: `AiRpaStudio.Tests\NuGetPanelThemeTests.cs`.
- Оверлей «Package sources» (`SourcesOverlay`) накрывает строки 0–2 корневого `Grid` (`Grid.RowSpan="3"`), а не строку состояния: её непрозрачная полоса с 1px-разделителем рисовалась поверх оверлея и «резала» подпись «Project sources are stored in AiRpaStudio…». Страж — `AiRpaStudio.Tests\NuGetPanelLayoutTests.cs` (`SourcesOverlay_FootnoteIsNotClippedByStatusBar`).

- Выбор источника в комбобоксе персистится **на воркспейс** в настройке `NuGetSelectedSources`
  (JSON-словарь `workspaceFolder → sourceUrl`); парсинг/запись — `Core\NuGetSourceSelectionStore.cs`,
  панель читает/пишет через хуки `NuGetPanelViewModel.LoadSelectedSourceUrl`/`SaveSelectedSourceUrl`
  (сам `RPA.NuGet` на студию не ссылается). Стражи — `NuGetSourceSelectionStoreTests`,
  `NuGetSourcesAndSearchTests.SelectedSource_*`.
- Панель открывается **вкладкой панели документов** (`MainView.ShowNuGetPanel`, `ContentId = "NuGet"`),
  а не боковым доком: там помещается двухколоночный макет «список | детали». Узкая ширина
  (< 640) включает режим, где детали показываются вместо списка. Ветка восстановления раскладки
  для `"NuGet"` обязана стоять до отмены `LayoutDocument`. Стражи:
  `AiRpaStudio.Tests\NuGetPanelDocumentTabTests.cs`.
- Правила состояния пакета (какое действие предлагает кнопка, подписи версий и downloads) живут в
  `Services\NuGetPackageStateResolver.cs` без WPF и сети — там же их тесты
  (`NuGetPackageStateTests.cs`). План и таблица этапов — `dev-docs\NuGet_Panel_Redesign_Plan.md`.
- Шеврон блока «Options» в панели деталей — **кнопка без фона** с иконкой Material
  (`materialIcons:MaterialIcon Kind="ChevronRight"` → `ChevronDown` по `IsChecked`). В шаблоне шапки
  запрещены триггеры `IsMouseOver`/`IsPressed`: наведение не меняет ни фон, ни цвет иконки.
  Стражи — `AiRpaStudio.Tests\NuGetOptionsChevronTests.cs`.
- README пакета читается из `.nupkg`, скачанного **из выбранного источника**
  (`NuGetSearchService.DownloadNupkgAsync`), с запасной версией и кешем `id|version`; причина неудачи
  остаётся в `NuGetPackageDetailsService.LastReadmeError` и показывается в строке состояния.
  Молчаливый `catch` вокруг README запрещён: из-за него вкладка README «пропадала у всех пакетов».
  Стражи — `AiRpaStudio.Tests\NuGetReadmeLoadTests.cs`.

- Шапка правой колонки берёт картинку пакета **только** через `PackageIcon`/`HasPackageIcon`/`PackageMonogram`
  (`NuGetPackageDetailsViewModel`): прямая привязка `Item.Icon` не обновляется, потому что `Details` — один
  экземпляр на всю панель, `Item` присваивался без уведомления, а иконка приходит позже (`LoadIconAsync`).
  Фолбэк — монограмма (первая буква идентификатора). Стражи: `NuGetPackageStateTests.Details_HeaderShowsIconOrMonogramFallback`,
  `NuGetPanelDocumentTabTests.Panel_DetailsHeaderShowsPackageIcon`.
- Кнопки плашки списка: колонка **37px** (прежние 56px, суженные в 1,5 раза), кнопки 16×16, глифы 10×10;
  удаление — `MaterialIcon Kind="Close"` (крестик), установка/обновление — `Kind="Download"`; 16 + 16 + 5 = 37.
  Стражи: `NuGetPanelLayoutTests.RowIconButtons_AreSizedForNarrowColumn`, `...Panel_RowButtonsFitNarrowColumn`.

- Фокус в поле поиска ставится **отложенно** (`FocusSearchBoxDeferred` → `DispatcherPriority.Input`) и только
  при живом `PresentationSource`: синхронный `Focus()` в `Loaded` падал `NullReferenceException` в
  `HwndKeyboardInputProvider.AcquireFocus`, потому что `Loaded` приходит из `HwndTarget.OnResize` (WM_SIZE —
  разворачивание окна, восстановление раскладки доков). Автофокус однократный (переезд вкладки в боковой док
  фокус пользователя не перебивает), Ctrl+L идёт через ту же защищённую точку.
  Страж — `AiRpaStudio.Tests\NuGetPanelFocusTests.cs`.

- Список источников пакетов проекта — **собственное per-project хранилище AiRpaStudio**, а не
  `NuGet.Config` проекта: JSON-словарь `workspaceFolder → [sources]` в `%AppData%\AiRpaStudio\NuGet\nuget_sources.json`
  (`Core\NuGetProjectSourcesStore.cs` + `Core\NuGetPaths.cs`), панель читает/пишет хуками
  `LoadProjectSources`/`SaveProjectSources`. Студия **никогда** не создаёт и не правит `NuGet.Config`
  в каталоге проекта автоматически.
- Внешние источники из `NuGet.Config` (машина/пользователь/решение) читаются **только на чтение**
  (`NuGetSourcesService.LoadMachineAndUserSources`/`LoadSolutionSources`) и показываются в оверлее
  отдельной секцией; их можно импортировать копией в источники проекта (`ImportSourceCommand`).
- Канонические места конфигов NuGet: машинные — `%ProgramFiles(x86)%\NuGet\Config\*.config` и
  `%ProgramFiles%\NuGet\Config\*.config`; пользовательский — `%AppData%\NuGet\NuGet.Config` (или
  `NUGET_CONFIG_FILE`, если задан); уровня решения — `NuGet.Config`/`nuget.config` в папке решения
  и всех родительских папках до корня диска. Источник помечается `Origin` (Machine/User/Solution),
  `SourceConfigPath` хранит путь к файлу, в оверлее импорта источники сгруппированы по `OriginLabel`.
- Создание/редактирование/экспорт `NuGet.Config` — только по явным кнопкам оверлея
  (`CreateNuGetConfigCommand`, `ExportProjectSourcesToConfigCommand`, `OpenConfigFileCommand`), через
  `NuGetSourcesService.WriteConfig`/`Save`. Жёстких URL nuget.org в рабочем коде нет: поиск/версии/
  скачивание идут только через выбранный источник, всё логируется через `NuGetLog`.
  Стражи — `NuGetProjectSourcesStoreTests`, `NuGetSourcesConfigTests`,
  `NuGetPanelViewModelTests.Sources_WithoutConfig_DoesNotCreateNuGetConfig`,
  `NuGetPanelLayoutTests.IconUrl_NoFallbackWhenSourceHasNoIconUrl`.

## 4.7.2-робот на 4.8-хосте (`CP-08`)

- Студия (.NET Framework 4.8) запускает робота, собранного на **4.7.2**, в своём AppDomain: одна и та же
  версия CLR (v4.0.30319), сборки 4.7.2 исполняются на 4.8-рантайме. Доказательство —
  `AiRpaStudio.Tests\Net472OnNet48HostTests.cs`: эталон `TestProcess\Net472Robot` грузится через
  `Activator.CreateInstanceFrom` и отдаёт события в `ActivitiesTracker` (тоже 4.7.2) — Sequence, If, TryCatch,
  WriteLine, аргумент `InputText = max`, результат `Result = MAX`.
- **Зависимости робота** (`Newtonsoft.Json` робота, его библиотеки активностей) ищутся в его выходном каталоге
  `bin\<Config>` и `bin\<Config>\<TFM>`: `AiRpaStudio\Helpers\RobotAssemblyResolver.cs`. Правила: сборки хоста
  (`System.*`, WF, `Activities*`, `ActivitiesTracker`, `RPA*`) из каталога робота **никогда** не обслуживаются,
  понижение версии запрещено, чужой открытый ключ запрещён, обработчик не бросает исключений, подписка
  идемпотентна. Порядок: сначала `AssemblyBindingUnifier` (сверка версий внутри домена), затем этот резолвер.
  Тесты — `AiRpaStudio.Tests\RobotAssemblyResolverTests.cs` (проверены негативом).
- **Прогон** — один движок на студию и на раннер: `Rpa.Workflow.Runtime\Engine\WorkflowRunEngine.cs`
  (проект `Rpa.Workflow.Runtime`, `net472`; студия ссылается на него и своей копии не держит —
  страж `AiRpaStudio.Tests\Runner\RunnerSourceGuardTests.cs`).
  (`LoadActivityFromText`/`FromFile`/`FromAssembly`, `CreateApplication`, `Run`). Итог —
  `WorkflowRunResult`: состояние (`Completed`/`Faulted`/`Aborted`/`Cancelled`), выходные аргументы,
  ошибка со стеком (`ErrorText.Of`), `TimedOut`, `ExitCode` (0 — успех) и события трекера. Загрузка
  идёт с `CompileExpressions = true` — как в студии. Движок обязан оставаться headless
  (`System.Windows`, `MessageBox`, `DispatcherTimer`, `CustomWfDesigner` — запрещены), а
  `ActivityRunner` не имеет права ни грузить XAML, ни создавать `WorkflowApplication` сам: оба
  правила — стражи `AiRpaStudio.Tests\WorkflowRunEngineTests.cs`.
- **Студийный путь прогона эталона** — `AiRpaStudio.Tests\LegacyRobotStudioRunTests.cs`: `Main.xaml`
  робота 4.7.2 грузится движком и прогоняется (`InputText = max` → `Result = MAX`), проверены ветка
  `If.Else` и негатив «в пустом каталоге сборка робота не находится». Вне автотестов остаётся
  UI-состояние `ActivityRunner` (read-only дизайнера, точки останова, `Locals`): ему нужен живой
  `CustomWfDesigner` и `TrackHandler` с окном — проверяется вручную отладкой в студии.
- `WorkflowHost` — **устаревший прототип** (зашитый путь к `Main.xaml`, `MessageBox`, `Console.ReadKey()`,
  `EnvDTE80`): собирается в решении, но никем не подключается — страж `AiRpaStudio.Tests\WorkflowHostLegacyGuardTests.cs`.
  Роль раннера исполняет `RpaRunner.exe` (`OR-02`, раздел ниже); `WorkflowHost` не воскрешать.

## Headless-раннер `RpaRunner.exe` (`OR-02`)

- **Раннер ничего не собирает.** Он находит уже собранного робота: `--project <csproj>` (сборка из
  `bin\<Config>`, `bin\<Config>\<TFM>`, `bin\<Platform>\<Config>`, `bin\<Platform>\<Config>\<TFM>`;
  имя сборки — `AssemblyName`, тип — `x:Class` из `Main.xaml`), `--workflow <xaml>` (без сборки),
  `--assembly <dll> --type <Тип>` (готовый бинарник). Сборка — работа студии, упаковка — `OR-03`.
  Из этого запрета следует остальное: в `RpaRunner` не может быть ссылок на `Microsoft.Build`,
  `MsBuildLocator`, `AiRpaStudio`, `RPA.Chat`, `Agent.Core`, `EnvDTE` — страж
  `AiRpaStudio.Tests\Runner\RunnerSourceGuardTests.cs` (он же держит «нет UI и нет ожидания ввода»,
  «один `WorkflowRunEngine` на студию и раннер», `net472`).
- **Коды возврата — контракт для оркестратора (`OR-05`) и запускатора (`LN-02`):** `0` успех,
  `1` робот упал, `2` неверные аргументы, `3` отменён, `4` предел времени, `5` workflow не загрузился
  или не прошёл проверку, `6` робот не найден / не собран. Таблица и справка (`RunnerUsage`) сверяются
  тестом — менять код без правки справки нельзя.
- **`--validate` — это WF-валидация**, а не прогон: ошибки, невидимые загрузке (например `Assign`
  без `To`/`Value`), дают код 5 и называются словами. «Не выполнено» никогда не выглядит как «ошибок нет».
- **Отмена и предел времени штатные:** первый и второй Ctrl+C отменяют прогон через
  `CancellationToken` (процесс не умирает, отчёт успевает записаться), `--timeout` (по умолчанию 300 с)
  даёт код 4. `result.json` пишется в `finally` — он нужен и при падении.
- **Лог** — JSON-lines (`--log`), в каждой строке `runId`; отчёт — `result.json` (`--out`): `status`,
  `exitCode`, `state`, `outputs`, `error`, источник и длительность.
- **Запуск руками:** `RpaRunner\bin\Debug\net472\RpaRunner.exe --project <робот.csproj> --arg InputText=max --out result.json`.
  В PowerShell кавычки внутри `--args {"InputText":"max"}` съедаются оболочкой — используйте `--arg имя=значение`.
  Тесты — `AiRpaStudio.Tests\Runner\` (63 тест-случая), журнал волны — `dev-dev-docs\Wave5_Execution_Plan.md`.

## Пакет робота `robot.zip` (`OR-03`)

- **Пакет самодостаточен:** `robot.zip` = `manifest.json` + `lib/` (сборка робота и все зависимости из
  её каталога) + `robot/` (XAML). На машине прогона не нужны ни проект, ни студия, ни MSBuild —
  поэтому пакет и является единицей публикации для оркестратора (`OR-05`) и запускатора (`LN-06`).
- **Собирает `RpaPack.exe`** (`RpaPack\`), логика — `Rpa.RobotPackaging` (`RobotPackager`): чтение проекта
  (имя сборки, TFM, версия, `x:Class`, аргументы из `x:Members`) → сборка проекта через MSBuild **тем же**
  локатором, что у студии (`CP-04`) → сбор файлов из `bin\<Config>` → пофайловые sha256 → манифест →
  детерминированный zip → размещение в кэше. Запуск:
  `RpaPack --project <csproj> [--version 1.2.3] [--configuration Release] [--out robot.zip] [--cache <каталог>] [--no-build] [--json]`.
  Коды возврата: `0` создан или переиспользован, `1` сборка не прошла, `2` аргументы, `3` проект непригоден,
  `4` пакет не прошёл правила формата, `5` версия уже есть с другим содержимым, `6` ошибка ввода-вывода,
  `7` робот не собран (а сборку не просили).
- **Формат — контракт, а не «файл упаковщика»:** `Rpa.Orchestrator.Contracts` (`netstandard2.0`, **без единой
  зависимости**, страж `ContractsHaveNoDependencies`) — `RobotPackageManifest`, JSON
  (`DataContractJsonSerializer`, есть и в 4.7.2, и в netstandard2.0), хэши, правила годности, читатель, кэш.
  Имена полей в JSON зафиксированы: `formatVersion`, `id`, `version`, `targetFramework`, `entryType`,
  `entryAssembly`, `entryWorkflow`, `arguments`, `dependencies`, `files`, `contentHash`, `warnings`.
- **`contentHash` считается по манифесту файлов** (путь + размер + sha256, сортировка по пути), а не по байтам
  архива: порядок файлов и архиватор на него не влияют, а пофайловые sha256 проверяются при **каждом**
  открытии пакета — подменённый файл даёт отказ с указанием пути и обоих хэшей.
- **Неизменяемость:** пакет той же версии с другим содержимым не перезаписывается — отказ с текстом
  «пакет неизменяем, поднимите версию». Тот же бинарник → статус `Reused` (не новая версия).
- **Кэш:** `<root>\<id>\<version>\<id>-<version>.zip`, распаковка — `…\<contentHash>\` с маркером `.extracted`
  (недописанная распаковка за готовую не принимается). Корень — `%LOCALAPPDATA%\RPA\packages` или
  переменная окружения `RPA_PACKAGE_CACHE` (перекрывается параметром).
- **Прогон пакета:** `RpaRunner --package <robot.zip> [--package-cache <каталог>]` — пакет проверяется по
  манифесту, распаковывается в кэш, тип берётся из `entryType`, зависимости — из `lib`, `--bin` не нужен.
  «Пакета нет» → код `6`, «пакет есть, но битый/подменённый/не по правилам» → код `5`: это разные действия
  (публиковать заново или чинить робота).
- **Общие правила проекта** живут в `RobotProjectLayout` (имя сборки, TFM, четыре раскладки `bin`,
  `x:Class`, аргументы): упаковщик и раннер обязаны искать сборку одинаково, иначе пакет собирался бы из
  `bin\Release\net472`, а прогон искался в `bin\Debug`.
- **Стражи** (`AiRpaStudio.Tests\Runner\RunnerSourceGuardTests.cs`): раннер и рантайм не ссылаются на
  упаковщик (упаковка тянет MSBuild, прогон — нет), упаковщик не ссылается на студию/`RPA.Chat`/`AvalonDock`,
  контракты без зависимостей и `netstandard2.0`, все три проекта есть в `AiRpa.sln`.
- Тесты: `AiRpaStudio.Tests\Packages\` (формат, правила, хэши, читатель, кэш, разбор проекта),
  `AiRpaStudio.Tests\Packaging\` (упаковщик и живой `RpaPack.exe`), `AiRpaStudio.Tests\Runner\RunnerPackageSourceTests.cs`
  (параметр `--package`, коды 5/6, прогон эталона из пакета). Журнал волны — `dev-dev-docs\Wave6_Execution_Plan.md`.

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

## Ошибки и падения приложения
- Любая ошибка в студии показывается окном `AiRpaStudio\Views\ErrorView.xaml` через
  `AiRpaStudio\Helpers\ErrorDialog.cs` (`Show` / `ShowException` / `ShowCrash`). `MessageBox` для ошибок
  и сообщений модели (`IView.OnSendMessage`) запрещён: он не следует теме и не даёт скопировать текст.
  `MessageBox` остаётся только для вопросов-подтверждений (например, «удалить папку?»).
- Окно ошибки: readonly `TextBox` (`IsReadOnly=True` + `IsReadOnlyCaretVisible=True`) — текст выделяется
  и копируется по Ctrl+C; кнопка «Копировать» кладёт текст в буфер (`Clipboard.SetText`), при недоступном
  буфере текст остаётся выделенным; цвета только из палитры (`Studio.Surface`/`SurfaceAlt`/`SeparatorStrong`,
  красный — только семантика `Studio.Semantic.Stop`); `WindowCorner.Enable(this)` из `OnSourceInitialized`.
- Падения: подписки живут в конструкторе `App` (`AppDomain.UnhandledException` +
  `DispatcherUnhandledException`) — они работают ещё до разбора `App.xaml`. Обработчик
  `Log.ReportExceptionAndShowMessage` пишет `log.txt` и открывает то же окно с заголовком «Критическая
  ошибка» (версия + время + стек). WinForms-событие `Application.ThreadException` для WPF не используется.
- `ErrorDialog` не бросает исключений и сам выбирает путь показа: UI-поток → модальное окно поверх владельца;
  фоновый поток → `Dispatcher.Invoke` с таймаутом; диспетчера нет (падение на старте/после `Shutdown`) →
  отдельный STA-поток со своей копией палитры (`StudioBrushes.ApplyPalette`), последняя страховка —
  системный `MessageBox`. Окно не создаётся через `MvvmFactory` намеренно: во время падения ни модели,
  ни `DataContext` уже нет.
- Локализация подписей — ключи `lang_error_title`, `lang_error_crash_title`, `lang_error_hint`,
  `lang_error_copy`, `lang_error_copied` (+ существующий `lang_close`) в `Main.resx` и `Main.ru-RU.resx`;
  в XAML — `{DynamicResource lang_*}`, в code-behind — fallback через `LocalizeHelper` (старт приложения,
  когда словарь ресурсов ещё не создан).
- Тесты: `AiRpaStudio.Tests\ErrorWindowTests.cs` — поведение окна (readonly, выделение, копирование, тема)
  и стражи по точкам показа ошибок (ни один файл студии не должен вернуться к `MessageBox.Show` для ошибок).
- В тексте ошибки обязателен стек вызовов. Формат один на всю студию — `AiRpaStudio\Helpers\ErrorText.cs`
  (`Of(ex)` — полный `Exception.ToString()`; `Describe(context, ex)` — «контекст: сообщение» + стек);
  им же пользуется `ErrorDialog.BuildExceptionMessage`. В местах показа/логирования (`OnSendMessage`,
  `ErrorDialog`, лог дизайнера, `Debug`/`Console`, ответы MCP, `result.Errors`) пишется полное исключение,
  а не `ex.Message`: интерполяция — `$"...: {ex}"`, аргумент — `ErrorText.Of(ex)`. Причина — инцидент
  «Не удалось загрузить в дизайнер: «…RowDefinition» не является Visual или Visual3D»: по тексту без стека
  невозможно найти место падения. Страж — `AiRpaStudio.Tests\ErrorStackTraceTests.cs`.
- Обход визуального дерева — только через `Helpers\XamlHelper.cs` (`IsVisual`, `FindVisualChildren<T>`,
  `FindVisualParent<T>`). `LogicalTreeHelper.GetChildren` отдаёт для `Grid`/`Panel` не только элементы,
  но и `RowDefinition`/`ColumnDefinition`, а `VisualTreeHelper` на них бросает `ArgumentException`
  («…не является Visual или Visual3D»). Страж — `AiRpaStudio.Tests\VisualTreeWalkTests.cs`.


## Версии пакетов и привязка сборок
- В решении должна быть **одна** версия каждой транзитивной зависимости. `System.Threading.Tasks.Extensions`
  закреплена на 4.5.4 (сборка 4.2.0.1): в legacy-проектах через `packages.config`, в SDK-проектах — явным
  `PackageReference`. Пакет 4.5.2 даёт сборку 4.2.0.0 (её тянули транзитивно `RPA.Chat` и `Agent.Core`).
- Симптом нарушения: при загрузке проекта WF-панель «Пространства имён» (`ImportDesigner`) перечисляет типы
  сборок (`Assembly.GetExportedTypes`), привязка зависимости падает с `FileLoadException` / HRESULT 0x80131040,
  и дизайнер показывает модальное окно «Конструктор рабочих процессов», блокирующее студию.
- Страховка: `AiRpaStudio\Helpers\AssemblyBindingUnifier.cs` (`Install(AppDomain.CurrentDomain)` в конструкторе
  `App`) — на `AppDomain.AssemblyResolve` возвращает уже загруженную сборку того же имени и открытого ключа с
  версией **не меньше** запрошенной. Сателлиты `*.resources`, запросы с культурой, без версии и без открытого
  ключа, а также понижение версии не подменяются. Тесты: `AiRpaStudio.Tests\AssemblyBindingUnifierTests.cs`
  (включая стражи: единая версия пакета, redirect в `App.config`, версии сборок в папках вывода).
- Запускать студию можно только из `AiRpaStudio\bin\Debug|Release`: в `AiRpaStudio.Tests\bin\Debug\net48` копия
  `AiRpaStudio.exe` лежит без ~20 зависимостей студии (WF-сборки, `System.Reflection.Metadata`), и запуск оттуда
  даёт ложные ошибки Roslyn/дизайнера.

## Сборка и тесты
- Решение: `AiRpa.sln` (корень репозитория).
- Проект приложения: `AiRpaStudio\AiRpaStudio.csproj`.
- Тесты: `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` (SDK-style, net48 + NUnit).
  Запуск — через MSBuild из Visual Studio + `vstest.console.exe` (см. выше), т.к.
  `dotnet test` не собирает legacy WPF-проекты решения.
- Полная сборка решения: MSBuild из Visual Studio.

## Темы (MaterialDesignThemes.Wpf) и хранилище исходников
- Темы студии строятся на `MaterialDesignThemes.Wpf` (`BundledTheme` + `MaterialDesign2.Defaults.xaml`
  + алиасы `Studio.*`, создаваемые в рантайме `Helpers\StudioBrushes.cs`); переключение Light/Dark — через
  `MaterialDesignThemes.Wpf.PaletteHelper`.
- ⚠️ Кисти `Studio.*` и `SystemColors.*BrushKey` НЕЛЬЗЯ объявлять в XAML как
  `<DynamicResource x:Key=... ResourceKey=... />`: в словарь попадает `ResourceReferenceExpression`,
  и WPF падает с `XamlParseException` на Style-сеттерах (проверяется тестом
  `AiRpaStudio.Tests\StudioBrushesTests.cs`). Только кодом — `Helpers\StudioBrushes.cs`
  (`ApplyToApplication()` при старте приложения и после каждой смены темы).
- План темизации всех визуальных контролов (единственный актуальный): `dev-dev-docs\Vs2026_Theme_Plan.md`
  (этапы 1-10 и приложение A — пошаговый чеклист ручной приёмки).
- Единая палитра студии (в стиле Visual Studio 2026): `AiRpaStudio\Helpers\StudioPalette.cs`
  (`Surface`/`SurfaceAlt`/`Hover`/`Selected`/`Separator`/`SeparatorStrong`/`Foreground`/`ForegroundMuted`,
  ровно две темы, ноль акцента, скругления 8/4/6). `StudioBrushes` и `DockingThemeBrushes` берут значения
  только оттуда: `MaterialDesign.Brush.Primary`/`Secondary` больше не источник цветов.
  Тесты: `AiRpaStudio.Tests\StudioPaletteTests.cs`.
- Шрифты: **Roboto для всего проекта**. Единственный источник — `AiRpaStudio\Helpers\StudioFonts.cs`
  (`FamilyName = "Roboto"`, `UiFont`, `CodeFont`, ключи `Studio.FontFamily`, `Studio.CodeFontFamily`,
  `Studio.FontFamilyName`). Начертания встроены в сборку (`Resources\Roboto\*.ttf`), поэтому установленный
  Roboto не требуется. В XAML — только `FontFamily="{DynamicResource Studio.FontFamily}"`
  (`Studio.CodeFontFamily` для редакторов кода), в C# — только `StudioFonts.UiFont`/`CodeFont`.
  Строки `Consolas`/`Segoe UI` в XAML и `new FontFamily("Consolas")` в коде запрещены;
  WF-контролы получают шрифт ключами `FontFamilyKey`/`FontSizeKey`/`FontWeightKey`
  (`WfDesignerTheme.CreateResourceValues`). Стражи: `AiRpaStudio.Tests\StudioFontTests.cs`.
- Редакторы кода и XAML: цвета — только из `AiRpaStudio\Helpers\StudioEditorPalette.cs` (схема
  Visual Studio 2022, dark/light), оформление и подсветка — через `AiRpaStudio\Helpers\StudioEditorTheme.cs`
  (XSHD перекрашивается по именам цветов `<Color name="…">`, поэтому один файл подсветки работает в обеих
  темах). В `Resources\Highlighting\Xaml.xshd` зашитых цветов быть не должно.
  Стражи: `AiRpaStudio.Tests\StudioEditorThemeTests.cs`.
- Инспектор свойств WF: `PropertyWindowStyleHelper.SetPropertyStyles` сливает словарь палитры в ресурсы
  самого инспектора (его шаблоны читают собственные `TextBrushKey`/`BackgroundBrushKey`/`FontFamilyKey`)
  и подписывается на `LayoutUpdated` (`Attach` + отпечаток дерева): WF пересоздаёт строки свойств, и новые
  элементы обязаны перекрашиваться повторно. Стражи: `AiRpaStudio.Tests\WfThemeTests.cs`.
- Кисти AvalonDock: студия пишет нейтральные ключи `AvalonDock_*` кодом
  (`Helpers\DockingThemeBrushes.cs`, единственная точка — `AppSettings.ReapplyDockManagerResources`),
  а в изолированные ресурсы плавающих окон / Ctrl+Tab / оверлея их прокидывает
  `AvalonDock\Themes\ThemeBrushBridge.cs` (вызывается из их `UpdateThemeResources`).
  `AvalonDock_ThemeMetro_BaseColor1..7` — только legacy-алиасы, в шаблонах темы не используются.
  Тесты: `AiRpaStudio.Tests\DockingThemeBrushesTests.cs`, `AvalonDockThemeBridgeTests.cs`,
  `ThemeXamlTests.cs` (регекс-стражи по `AvalonDock.Themes.Metro\Theme.xaml` + полный разбор XAML).
  Полоса вкладок документов (P-08): состояния рисует `Border x:Name="TabBorder"` в шаблоне контейнера
  вкладки (`AvalonDock_Base` → hover `AvalonDock_Hover` → выбранная `AvalonDock_Active` — светлее неактивной, + hairline
  `AvalonDock_Separator`). Сеттеры `Background`/`BorderBrush` в триггерах обязаны быть адресованы
  `TargetName` элемента шаблона: `BorderBrush` у самого `TabItem` ничем не отрисовывается — из-за
  этого вкладки выглядели «без темы». Кнопка закрытия вкладки —
  `AvalonDock_ThemeMetro_DocumentCloseButtonStyle`: hover — нейтральная подсветка фона
  (`AvalonDock_SelectionHover`, нажатие — `AvalonDock_Selection`), глиф остаётся `AvalonDock_Foreground`;
  красные `AvalonDock_CloseHover*` в шапке вкладок не используются (токены остаются для кнопки закрытия
  окна и валидации WF). Стражи: `ThemeXamlTests.ThemeXaml_DocumentTabTemplateDrawsStates`,
  `ThemeXaml_DocumentTabCloseButtonHoversNeutral`, `DocumentTabStyle_RendersPaletteBrushes`.
- Скругления окон: `AiRpaStudio\Helpers\WindowCorner.cs` — DWM
  (`DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)`, Windows 11) с
  fallback на region через `WindowChrome.CornerRadius` (радиус — `StudioPalette.WindowCornerRadius`,
  Windows 10). Окна студии зовут `WindowCorner.Enable(this)` из `OnSourceInitialized`; окна AvalonDock
  (откреплённые панели/документы, Ctrl+Tab) — `AvalonDock\Themes\WindowCornerBridge.cs`, провайдер в него
  ставит `WindowCorner.EnableAvalonDockWindows()` в `App.Application_Startup`. Хелпер никогда не бросает
  исключений: без DWM окно просто остаётся прямоугольным.
  Тесты: `AiRpaStudio.Tests\WindowCornerTests.cs` (контракт DWM, идемпотентность, мост, стражи по точкам вызова).


- WF-контролы (F-01…F-03): `Helpers\WfThemeStyles.cs` — общие примитивы (шрифт Segoe UI вместо Consolas,
  замороженные кисти палитры, маркер «стиль уже наш» + `Wrap()` для сохранения шаблонов Toolbox через
  `BasedOn`). `Helpers\WfVisualTreeThemer.cs` берёт цвета только из `StudioPalette` (акцент
  `MaterialThemePalette` в WF больше не используется), ставит `ItemContainerStyle` с триггерами
  `IsMouseOver` → `Hover` и `IsSelected` → `Selected` — **только если стили элементов не выбирает
  `ItemContainerStyleSelector`**, — и умеет `ApplyWhenLoaded()` (дерево `OutlineView` строится
  шаблоном при загрузке). Выставлять `ItemContainerStyle` поверх селектора запрещено: в стиле
  активности `ToolboxControl` лежит `EventSetter MouseMove → OnToolMouseMove` (вызов
  `DragDrop.DoDragDrop`), и подмена стиля молча отключала перетаскивание активностей в дизайнер
  (категория оставалась видимой, поэтому баг выглядел как «перестало перетаскиваться»).
  Страж — `AiRpaStudio.Tests\ToolboxDragContractTests.cs` (стили WF не подменяются, `EventSetter`
  `MouseMove`/`MouseDoubleClick` на месте, `SelectedTool` появляется при выделении).
  Светлые плашки шаблонов WF (регресс «белый фон под названием категории в панели Активности») гасятся
  не кистями системных цветов, а проходом по эффективным значениям — `WfThemeStyles.NeutralizeLightBrushes`
  (площади → `Surface`, линии → `Transparent`, тёмный текст → `Foreground`, стрелка экспандера →
  `ForegroundMuted`; кисти предыдущей темы пересчитываются по токену палитры: цвет зашит в WF-стиль,
  и `SystemColors.*BrushKey` на него не влияют). Состояния элементов с WF-стилем ведёт
  `WfVisualTreeThemer.HookItemStates` (Hover/Selected палитры вместо системных `#FFE8A6`/`#E5C365`),
  плашка шаблона перепривязывается к `Background` элемента, а контейнеры, созданные после темизации
  (разворачивание категории, «Развернуть всё»), достраивает `ToolboxPanel.HookContainers` по
  `ItemContainerGenerator.StatusChanged` и `TreeViewItem.ExpandedEvent`. Стражи —
  `AiRpaStudio.Tests\ToolboxThemeTests.cs`. Обход дерева — только через `XamlHelper`
  (`Children`/`FindVisualChildren`): в `WfVisualTreeThemer` `VisualTreeHelper.GetChildrenCount` запрещён.
  `Helpers\PropertyWindowStyleHelper.cs` ищет элементы по именам
  (`_propertyToolBar`, `PART_ByCategoryViewButton`, `PART_AlphaViewButton`, `PART_SearchClearButton`),
  состояния кнопок — триггеры стиля (не обработчики), повторное применение перекрашивает контрол без
  пересоздания. Тесты: `AiRpaStudio.Tests\WfThemeTests.cs`.
- Плоские кнопки окна «Свойства» (правка F-03). `PropertyWindowStyleHelper.CreateIconHost` принимает
  `iconSize`/`framed`: у переключателей «По категориям»/«Алфавитный» `CornerRadius = 0` (hairline-рамка
  `Separator` осталась, кнопка 28×28 и глиф 20 px не менялись), у кнопки очистки поиска рамки нет вовсе
  (`framed: false`), а крестик уменьшен в 1,5 раза — `ClearIconSize = 11` вместо 16 при том же размере
  кнопки `ClearButtonSize` 24×24. Кнопка «…» строк свойств (WF `EditModeSwitchButton`) полностью плоская:
  `ApplyEditModeButtons` → `MakeFlatButtonChrome` ставит прозрачные `Background`/`BorderBrush`,
  `BorderThickness = 0`, `Effect = null` и обнуляет `CornerRadius`/рамку/фон у `Border` шаблона; если
  хромировку рисует системный `ButtonChrome` (Border-потомка нет) — ставится плоский шаблон на
  `ContentPresenter`. Глиф «…» не теряется: WF отдаёт его через `Content` (триггер
  `TargetEditMode = Dialog` → `Content = "..."`, проверено по `stylescore.editmodeswitchbutton.baml`).
  Ховер-плашки у «…» больше нет — это следствие «плоскости». Проход идемпотентен, строки, созданные
  после выбора другой активности, подхватывает тот же `Attach`/`LayoutUpdated`. Тесты:
  `WfThemeTests.PropertyHelper_StylesButtonsByName`, `.PropertyHelper_SearchClearButton_IsFramelessWithSmallerGlyph`,
  `.EditModeButton_IsFlatWithoutCornerRadiusAndShadow`,
  `.EditModeButton_WithoutBorderChrome_GetsFlatTemplateAndKeepsGlyph`, `.PropertyHelper_FlattensEditModeButtons`.
- Тема — перечисление `Agent.Core.Models.StudioTheme` (`Dark`/`Light`), единственный источник истины.
  Активное значение хранит `StudioPalette.Current` (палитра темы — `StudioPalette.Active`); ставится только
  из `AppSettings.ApplyTheme(StudioTheme)` и **до** пересборки кистей (`StudioPalette.ApplyCurrent`).
  `bool dark` в API темы и «угадывание» темы по яркости кисти MaterialDesign в рабочих путях запрещены:
  `StudioPalette.Detect` удалён, остались `StudioPalette.DetectTheme` (чужой словарь: изолированные
  ресурсы плавающих окон, отдельный STA-поток окна ошибки) и `DetectDark` (распознавание в тестах).
  Перечисление лежит в `Agent.Core`, потому что тема нужна сразу двум сборкам — студии и `RPA.Chat`;
  `AiRpaStudio.csproj` ссылается на проект напрямую. Персист темы чата — строка `"Dark"`/`"Light"`
  (`SettingsData.Theme`), старый `{"UseDarkTheme": bool}` переносится при загрузке и больше не пишется;
  `ChatViewModel.Theme` — источник истины, `IsDarkTheme` — проекция для двухпозиционного тумблера,
  `ThemeApplying` — `Action<StudioTheme>`. Стражи — `AiRpaStudio.Tests\ThemeApiGuardTests.cs`,
  `StudioThemeTests.cs`, `ChatThemeSettingsTests.cs`.

- Поверхность дизайнера WF (F-04…F-06): `Helpers\WfDesignerTheme.cs` — объявляет ключи
  `DesignerViewBackground`/`DesignerViewForeground` (на первый ссылается
  `CSharpExpressionEditor\RoslynExpressionEditor.xaml`) и ключи шаблонов WF (поверхность, элементы
  активности, shell bar, breadcrumb, MiniMap, контекстные меню, `PropertyInspector` — имена берутся из
  публичных `WorkflowDesignerColors.*Key`). Словарь палитры кладётся и в `Application.Resources`, и в
  `DesignerView.Resources` (локальные `ColorResources.xaml` дизайнера перекрывают приложение) и
  мутируется на месте при смене темы. Кисти, пришедшие через `{StaticResource}`, заменяются по точному
  значению дефолта WF; пользовательские кисти не трогаются. Адаптер ошибки — `Validation.ErrorTemplate`
  с hairline `CloseHoverBackground` (красный только для ошибок). Контекстное меню дизайнера —
  `WfDesignerTheme.CreateEditingContextMenu` (им пользуется `DesignerVariableContextMenuService`).
  Оформление меню вынесено в `Helpers\WfContextMenuTheme.cs`: меню — **popup**, а не потомок
  `DesignerView`, поэтому проход темы по дереву дизайнера до него не доходит ни разу, а локальных
  `Background`/`Foreground` недостаточно — рисует чужой шаблон (`MaterialDesignContextMenu`/
  `MaterialDesignMenuItem` из `App.xaml`, в чистом процессе Aero2), и его подсветка строится от
  `Foreground` (белые буквы на светлой накладке). `WfContextMenuTheme.Apply` ставит собственные
  `Template` меню, `ItemsPanel` и `ItemContainerStyle` пунктов (рамка палитры, `Hover`, `ForegroundMuted`
  для недоступного пункта, `PART_Popup` подменю), а `Hook()` — общий проход по `ContextMenu.OpenedEvent`
  для меню самого дизайнера (по `Loaded` popup-ребёнку обработчик класса не доставляется). Проход
  ограничен цепочкой родителей/`PlacementTarget` до `DesignerView` и `System.Activities.Presentation`,
  а палитру, заданную фабрикой, не перебивает. Отдельно меню правого клика темизируется **до показа** —
  `ContextMenuService.ContextMenuOpeningEvent` (`PrepareOpen`): иначе генератор контейнеров падает на
  разделителе, если стиль пунктов ещё чужой (`Стиль, заданный для типа "MenuItem", не может применяться
  к типу "Separator"`), поэтому стиль раздаётся `ItemContainerStyleSelector`, а `Separator` получает
  свой hairline-стиль. Стражи — `AiRpaStudio.Tests\WfContextMenuThemeTests.cs`.
  Механизм этого меню зафиксирован документально (`dev-docs\Designer_Variable_ContextMenu_Principles.md`):
  правый клик в ячейке `variableDataGrid`/`argumentsDataGrid` открывает меню программно (штатное меню
  завершает правку и закрывает панель), а завершение правки отменяется флагом, взведённым на
  `DataGrid.PreviewMouseRightButtonDown`. Инварианты — порядок туннелирования, отмена consume-once,
  `RecolorEditingContextMenu` на каждое открытие, снятие глобальных хуков в `Closed`, все обработчики
  на `DataGrid` (в `CellEditingTemplate` и его `Resources` писать нельзя — шаблоны запечатаны,
  дефект 1.0.0.0 «ResourceDictionary доступен только для чтения»; палитра ставится полю ввода
  локальными значениями в `PreparingCellForEdit`); решения вынесены в
  `Core\Services\DesignerVariableContextMenuPolicy.cs` и
  `DesignerContextMenuState.cs`. Стражи — `AiRpaStudio.Tests\DesignerVariableContextMenuTests.cs` и
  `DesignerVariableContextMenuPolicyTests.cs`.
  Ручной чеклист приёмки и карта тестов — разделы «Ручной чеклист приёмки» и «Тесты» этого документа.
  Вызовы: `DocGroupHelper.DesignerView_Loaded`, `MainView.ReapplyWorkflowControlThemes`,
  `App.Application_Startup`, `AppSettings.ApplyTheme`.
  Полоса поверхности вокруг активностей (`designerViewGrid`, хосты таблиц, `designerPresenterBorder`)
  перекрашивается отдельным последним проходом `WfDesignerTheme.RepaintSurfaceBand`. Причина: у полосы
  несколько «хозяев» (хост таблиц `WfDataGridTheme.PaintHost`, общий `WfVisualTreeThemer`, плиты
  `WfActivityTheme`), и в режиме кэша словаря WF ни один из них не узнавал белый в тёмной теме — в тёмной
  палитре чистый белый объявлен как `CloseHoverForeground`, поэтому проверка «цвет входит в активную
  палитру» отбрасывала его (regress: light → dark оставлял белую полосу вокруг активностей, хотя сами
  активности перекрашивались). Признак прохода — «цвет противоположной палитры»
  (`WfThemeStyles.TryMapStalePaletteColour`, сверяется только с токенами площади и текста —
  `WfThemeStyles.BackgroundTokens`), поэтому пользовательские кисти и цвета активной темы не трогаются;
  внутрь `WorkflowViewElement` и `AdornerLayer` проход не заходит (там связанные кисти
  `WfColorResourcesOverride` и адаптеры ошибок). Стражи — `WfDesignerSurfaceTests`
  (`Apply_AfterLightAndBack_RepaintsTheBandAroundActivities`, `.Apply_KeepsPaletteStatesAndTransparentBands`,
  `.SurfacePass_DoesNotEnterActivitiesOrAdornerLayer`); сквозная проверка — пробник
  `tools\designer-ui-diagnostics` с аргументом `cache` (см. `README.md` рядом).

  Хлебные крошки (`breadCrumbListBox` — имена активностей `Main › Sequence › Assign`) темизируются
  отдельным проходом `ThemeBreadCrumb`: контейнеры получают `ItemContainerStyle` палитры, содержимое
  уже созданных крошек перекрашивается, а новые (появляются при «провале» в активность) достраиваются
  по `ItemContainerGenerator.StatusChanged`. Светлые кисти внутри крошки гасятся по яркости: цвет
  детей `DataTemplate` (кнопка Aero2, `TextBlock`) приходит из шаблона, а не локально, поэтому прежний
  проход по локальным значениям оставлял белые плашки в тёмной теме. Страж —
  `WfDesignerThemeTests.Apply_BreadCrumbHasNoLightBackgrounds`. «Один белый пиксель» на стыке «крошки/дизайнер» — угловой `Rectangle` **1×1** из шаблона `ScrollViewer` Aero2
  (светло-серый фон темы): при скрытых полосах прокрутки он вырождается в точку в правом нижнем углу
  `ListBox`, а ширина списка равна ширине крошек — поэтому точка «сдвигалась» вместе с последней крошкой.
  Он не разделитель и не шеврон: `WfDesignerTheme.IsTemplateCorner` сливает его с `Surface`
  (`PaintBreadCrumbBand` и проход по содержимому). Стражи —
  `WfDesignerThemeTests.Apply_BreadCrumbJunctionHasNoLightPixel` (полоса рендерится в битмап: в нижних
  строках, где лежит hairline, не должно быть пикселей светлее поверхности) и
  `.Apply_BreadCrumbTemplateCornerDotMergesWithSurface`. Сохраняются и вспомогательные правила: гасятся
  **все** свойства типа `Brush` (`WfDesignerTheme.BrushPropertiesOf`, кисти палитры неприкосновенны),
  полоса обрезает содержимое (`ClipToBounds` + `UseLayoutRounding`), светлая площадь гасится в цвет
  поверхности, а не в `Transparent`, подложка полосы красится `Surface` (`PaintBreadCrumbBand`), элементы,
  созданные после применения темы, достраиваются по `ItemContainerGenerator.StatusChanged` и
  `LayoutUpdated`. Стражи — `WfDesignerThemeTests.Apply_BreadCrumbHasNoLightBrushAnywhere`,
  `.Apply_BreadCrumbClipsArtifactsOverflowingOnTheDivider`,
  `.Apply_BreadCrumbArtifactsCreatedAfterApplyAreNeutralized`,
  `.Apply_BreadCrumbBandStaysOpaqueSurfaceUnderWhiteHost`,
  `.Apply_BreadCrumbBackdropBehindNamesIsSurface`.
  Активности (плита, рамка, подпись, контролы внутри) темизирует отдельный проход
  `Helpers\WfActivityTheme.cs`. Цвета активности берутся из **WF-словаря** `ColorResources.xaml`, который
  WF подмешивает в `ControlTemplate.Resources` **своего** шаблона
  (`WorkflowElementDesignerDefaultTemplate.xaml`), поэтому ключи `WorkflowViewElementBackground` /
  `DesignerViewBackground` из `WfDesignerTheme` до активностей не доходят: плита оставалась белой, плашка
  заголовка `#E9ECEE`, рамка `#D0D7E2`. Кисти WF-словаря заморожены, а сам словарь после загрузки шаблонов
  становится read-only (`InvalidOperationException` на записи) — поэтому тема ставится **локальными
  значениями** на элементы шаблона: плита → `Surface`, полоса заголовка → `SurfaceAlt`,
  рамка и линии → `SeparatorStrong`, глифы → `ForegroundMuted`, буквы → `Foreground`, а `RestoreStale`
  переносит кисти предыдущей темы (иначе возврат к светлой оставил бы тёмные плашки). Активности, созданные
  позже (перетаскивание из панели, «провал», отмена), достраиваются по `LayoutUpdated` (`Marks` + `IsStale`:
  один проход на цикл раскладки, повторный проход идемпотентен). Привязки модели не разрываются
  (`BindingOperations.IsDataBound`), поэтому общий проход по `TextBlock` в `WfDesignerTheme.Apply` больше не
  перезаписывает привязанный `Foreground`.
  Неявные стили MaterialDesign из `App.xaml` внутри активностей и в шапке дизайнера гасятся пустыми
  стилями-заглушками (`UseNativeControlStyles`, список `NativeStyleTypes`). Регресс, ради которого это
  сделано: `CheckBox` из `ActivityTop` получал стиль MaterialDesign 29×29, `Button#AnnotationIndicator` —
  шрифт 14, кнопки «Развернуть все»/«Свернуть все» — `Width = 34`, из-за чего подпись не влезала. Форма и
  шрифт контролов остаются нативными (WF/Aero2), цвета ставит темизатор; `ScrollBar` в списке заглушек нет
  намеренно (плоские скроллы 10 px задаёт `Themes\StudioScrollBar.xaml`). Подпись активности в `ActivityTop`
  больше не красится конвертером: цвет даёт палитра, выключенное состояние показывается `Opacity = 0.55`, а
  сам `BoolToColorConverter` берёт `Studio.Foreground`/`Studio.ForegroundMuted` по строковым ключам (сборки
  активностей не ссылаются на `RPA.Helpers`). Проверка на настоящем `WorkflowDesigner` — пробник
  `tools\designer-ui-diagnostics` (проход «DARK (первое включение)»: `фон чужой поверхности` = 0 и для
  пустой активности, и для workflow с вложенными активностями, и для кастомного дизайнера
  `Activities.Debug.Logger`). Стражи: `AiRpaStudio.Tests\WfActivityThemeTests.cs`.
  Цвета WF ставятся **до первого кадра** — `Helpers\WfColorResourcesOverride.cs`: словарь
  `ColorResources.xaml` подменяется в статическом кэше `CachedResourceDictionaryExtension.Cache` при старте
  (`App.StartupCore`, до `new EntryPoint().Run()`), кисти — связанные (`Color` привязан к источнику токена,
  поэтому смена темы не требует обхода дерева). Режимы взаимоисключающие: при успешной установке
  `WfActivityTheme` плиту и рамку не красит: подпись наследует `Foreground` от контрола активности, а тот
  ставит class handler `Loaded` (`HookCaptionForeground`) связанной кистью палитры до первого кадра — для
  всех типов дизайнеров и без обхода дерева; при сбое работает прежняя пост-покраска (аварийный switch
  `Studio.WfLegacyTheming`). Панель «Свойства» и Toolbox
  подменой не покрыты. Страж: `AiRpaStudio.Tests\WfColorResourcesOverrideTests.cs`; живая проверка обоих
  режимов — пробник `tools\designer-ui-diagnostics` (аргумент `cache` / `legacy`).
  Тесты: `AiRpaStudio.Tests\WfDesignerThemeTests.cs`.
  Комбобоксы WF-поверхностей (в дизайнере — масштаб в shell bar, `zoomPicker`) темизируются
  `WfVisualTreeThemer.ThemeComboBoxes` (вызывается из `WfVisualTreeThemer.Apply` и из
  `WfDesignerTheme.ThemeShellBar` — путь `TryApplyShellBarStyle` полного обхода не делает): кисти,
  шрифт и `ItemContainerStyle` палитры, гашение белых плашек шаблона Aero2, `ForegroundMuted` для
  стрелки раскрытия. Причина: WF-стиль `statusBarComboBoxStyle` задаёт только шрифт/`Foreground`/
  прозрачный фон, а шаблон Aero2 рисует под текстом опаковую белую плашку
  (`Border#templateRoot` кнопки раскрытия и `Border#Border` редактируемой части), пока `Foreground`
  наследуется от shell bar — в тёмной теме это белые буквы на белом фоне. Элементы списка получают
  собственный шаблон (в Aero2 подсветку рисует `Border#Bd` светлыми плашками триггерами шаблона),
  а список раскрывается в popup вне визуального дерева комбобокса — он темизируется по
  `DropDownOpened` с отложенным повтором (шаблон popup и полосы прокрутки применяются при раскладке).
  Белые плашки гасит `PaintTemplatePlates`, а не общий `NeutralizeLightBrushes`: чистый белый —
  токен палитры (`CloseHoverForeground`), поэтому здесь цвет сопоставляется с токенами прежней темы.
  Стили, созданные темизатором, помнят палитру (`WfThemeStyles.MarkThemed(style, palette)`): при смене
  темы обёртка пересоздаётся от исходного стиля WF, а не от стиля прежней темы. Стражи —
  `WfThemeTests.Apply_ThemesComboBoxes`, `.Apply_ThemesComboBoxItems`, `.Apply_MakesComboBoxGlyphVisible`,
  `.Apply_ComboBoxThemeIsIdempotent`, `.Apply_SwitchesComboBoxPalette`,
  `WfDesignerThemeTests.Apply_ThemesShellBarZoomCombo`, `.Apply_ShellBarZoomComboIsReadableInBothThemes`,
  `.Apply_ThemesShellBarZoomComboList`.
  Тот же механизм обслуживает комбобоксы таблиц (столбцы `Scope`/`Modifiers` — `DataGridComboBoxColumn`)
  и строк панели «Свойства» (enum-свойства): `WfDataGridTheme.Refresh` вызывает `ThemeComboBoxes`
  последним проходом, `WfDataGridTheme.HookGrid` ставит `PreparingCellForEdit` (палитра ставится до
  первой отрисовки, а не следующим циклом раскладки), `PropertyWindowStyleHelper.ApplyValueEditors`
  отдаёт комбобоксы строк тому же механизму, а `WfVisualTreeThemer.ApplyEditorPalette` — общий вход для
  редактора ячейки (поля ввода + комбобоксы) из таблицы и из `DesignerVariableContextMenuService`.
  Прежние самописные обходы `ComboBox`/`ComboBoxItem` внутри инспектора были мёртвым кодом: элементов
  списка в его визуальном дереве нет — список живёт в popup. Подписка на `Loaded` в `ThemeComboBoxes`
  ставится всегда (в shell bar к моменту применения темы `zoomPicker` уже есть, поэтому прежнее условие
  «комбобоксов нет» не выполнялось никогда, и позже созданные комбобоксы не темизировались). Стражи —
  `WfDesignerTableThemeTests.Apply_LateCreatedCellComboBoxIsThemed`,
  `.Apply_CellComboBoxItemStyleUsesPalette`, `.Sources_TableThemeThemesComboBoxes`,
  `WfThemeTests.SetPropertyStyles_ThemesInspectorComboBoxes`,
  `.SetPropertyStyles_SwitchesInspectorComboBoxPalette`,
  `DesignerVariableContextMenuTests.PreparingCellForEdit_ComboBoxGetsPalette`.

  Панель «Свойства активности» (`DesignTimeVariable`: строки `Modifiers` и `Scope`) потребовала второго
  захода: каркас панели темизировался, а до комбобоксов строк проход не доезжал — WF создаёт строки
  позже (выбор активности, разворот категории), и `ThemeComboBox` срабатывал до реализации шаблона, когда
  внутреннего поля правки `PART_EditableTextBox` ещё нет (отсюда белое поле в тёмной теме), а элементы
  popup (у `Modifiers` это чекбоксы `ReadOnly`/`Mapped`) получали системный чёрный текст. Теперь
  `ThemeComboBox` вызывает `comboBox.ApplyTemplate()` до покраски, `PaintItemForeground` локально красит
  текст popup (включая привязки, которые `NeutralizeLightBrushes` пропускает), подписки
  `ItemContainerGenerator.StatusChanged` и `IsKeyboardFocusWithinChanged` доводят список и режим правки, а
  `PropertyWindowStyleHelper.Attach` вешает доводку комбобоксов на `PreviewMouseDown`/`GotKeyboardFocus`
  панели. Стражи — `WfDesignerTableThemeTests.ThemeComboBoxes_PaintsEditableFieldOfCollapsedComboBox`,
  `.ThemeComboBoxes_PaintsDropDownCheckBoxTextWithPalette`,
  `.SetPropertyStyles_ThemesComboBoxOfRowCreatedAfterSweep`.

  Третье дополнение (WF создаёт редактор значения уже после прохода темы): на запущенной студии левый край
  редакторов `Modifiers` (`FlagEditor`, `autoId=ComboBox_1`) и `Scope` (`autoId=scopeEditoComboBox`) при
  выборе переменной был `#FFFFFF`/`#EFEFEF` до первого клика и становился `#272727` после него. Причина:
  `ThemeComboBoxes` к этому моменту уже прошёл, а WF подставляет редактор значения позже, и отпечаток
  дерева инспектора (`PropertyWindowStyleHelper.ComputeSignature`) при этом не меняется. Решение:
  `WfVisualTreeThemer.HookLayoutSweep` — по `LayoutUpdated` корня ищутся комбобоксы без отметки палитры
  (`ComboBoxMarks`) и темизируются только они (отложенно, флаг `Pending` — одна проверка на цикл
  раскладки), плюс `ScheduleComboBoxRetry` — один повторный проход, если шаблон ещё не материализован
  (`Template == null` или нет `PART_EditableTextBox` при `IsEditable`), с пометкой на палитру от
  бесконечной цепочки. Стражи —
  `WfDesignerTableThemeTests.ThemeComboBoxes_ComboBoxCreatedAfterSweep_IsThemedOnLoad`,
  `.ThemeComboBoxes_ComboBoxOutsideThemedRootsKeepsSystemChrome`,
  `.Sources_ThemerThemesLateCreatedComboBoxesOnLayout`.

  Панели «Переменные», «Аргументы», «Пространства имён» (вкладки shell bar, таблицы и их контекстное
  меню) темизирует `Helpers\WfDataGridTheme.cs` (F-04). Причина отдельного прохода: WF объявляет таблицы
  стилем `defaultDataGridStyle`, который берёт `{StaticResource}` на дефолтные цвета WF
  (`GridViewRowUnderlineColor`, `GridViewRowSelectedColor`, `GridViewRowHoverColor`) — `StaticResource`
  не пересчитывается, поэтому шапка, сетка и выделение оставались светлыми; плашки шапки, угла
  «выделить всё» и строк рисует шаблон Aero2, а триггер шаблона сильнее сеттера и триггера стиля.
  Отсюда: стили шапки/строки/ячейки — обёртки стилей WF (`WfThemeStyles.Wrap`, шаблон WF сохраняется в
  `BasedOn`), цвет материализованных контейнеров ставится локально через `WfThemeStyles.WatchState`,
  светлые кисти гасит `NeutralizeLightBrushes`, проход повторяется по
  `ItemContainerGenerator.StatusChanged` и отпечатку `LayoutUpdated` (строки виртуализованы), а
  панель-контейнер получает непрозрачный `Surface`. Вкладки shell bar — `ThemeShellBarItems` (активная —
  `Selected`, hover — `Hover`, неактивная подпись — `ForegroundMuted`). Контекстное меню обслуживает
  **все** таблицы дизайнера: `DesignerVariableContextMenuService.RefreshTables` ищет их по типу
  (`WfDataGridTheme.FindTables`), а не по именам панелей, поэтому «Пространства имён»
  (`importedNamespacesDataGrid`) больше не остаются без меню. Стражи —
  `AiRpaStudio.Tests\WfDesignerTableThemeTests.cs`, `DesignerVariableContextMenuTests.RefreshTables_*`,
  `Sources_TableTheme*`.

- Сторонние визуальные сборки (этап 8): `LogControlLibrary` и `RPA.Chat` не ссылаются на `RPA.Helpers`,
  поэтому палитра приходит в них строковыми ключами.
  * Панель «Log» (`LogControlLibrary\Controls\LogViewerControl.xaml`) использует `{DynamicResource Studio.*}`
    (ключи пишет `StudioBrushes.ApplyToApplication`), а цвета уровней — семантика в конвертерах
    (`LogLevelToColorConverter`/`...HoverConverter`/`...BackgroundConverter`): средние тона и
    полупрозрачные накладки, чтобы читались и в тёмной, и в светлой теме.
    Иконки фильтров Debug…Fatal берут цвет из семантики `Studio.Semantic.Log.*` (входят в
    `SemanticKeys`): приглушённые тона, согласованные с буквами уровня, цвета непрозрачные
    (alpha 255) и одинаковы в обеих темах. Trace — не цвет, а основной текст панели: иконка и буквы
    уровня красятся `Studio.Foreground` (белый `#F1F1F1` в тёмной теме, чёрный `#1F1F1F` в светлой),
    базовый цвет букв — `{DynamicResource Studio.Foreground}` в стиле ячейки (реагирует на смену темы), а Debug…Fatal — DataTrigger-ами на кисти `LogLevelToColorConverter`; строки
    Trace фона не получают вовсе — ни в покое, ни под курсором. Стражи — `LogAndChatThemeTests.
    LogViewer_FilterIconsUseSemanticLevelKeys`, `..._AreClassicSemanticsAndOpaque`, `..._DoNotDependOnTheme`,
    `LogViewer_TraceIconAndLettersUseStudioForeground`, `LogViewer_TraceRowsHaveNoBackgroundAtAll`.
  * Чат (`RPA.Chat`): все цвета — только в `Themes\ChatTheme.Dark.xaml` / `ChatTheme.Light.xaml`
    (ни одного `<SolidColorBrush>` в контролах, иначе переключение темы не доходит до элемента).
    В студии словарь на лету перекрывает мост `RPA.Chat\Themes\ChatStudioBridge.cs`: таблица
    «ключ чата → ключ `Studio.*`» + `Apply(ResourceDictionary, ResourceDictionary)` (зовётся из
    `ChatPanel.ApplyTheme`), без ресурсов студии мост ничего не меняет. Поверхностные акценты
    нейтральны (`Dark.Accent`/`Dark.Primary`/`Dark.ControlPressed` → `Selected`/`Foreground`);
    режимы Plan/Act/Help — голубой/зелёный/красный в `ChatViewModel`: активная кнопка залита акцентом с белым текстом, неактивные прозрачные (при наведении — мягкая серая подсветка) (`SegmentedToggle`), рамка поля ввода красится в цвет режима.
    Тесты: `AiRpaStudio.Tests\ChatThemeBridgeTests.cs`, `LogAndChatThemeTests.cs`.
  * Порядок смены темы (обязателен, иначе чат отстаёт на один переключатель): `ChatViewModel.IsDarkTheme`
    сначала вызывает хук `ThemeApplying` (на него подписана студия в `MainView.InitializeControls`:
    `Services.AppSettings.ApplyTheme`), который пересобирает палитру `Studio.*`, и только затем событие
    `ThemeChanged` → `ChatPanel.ApplyTheme` перекрывает словарь чата мостом. Повторный `ApplyTheme` в
    `MainView.OnChatThemeChanged` не нужен — он красил бы студию дважды за клик. Если палитра `Studio.*`
    изменилась уже после создания контрола (сохранённая светлая тема на старте), вызывается
    `ChatPanel.ReapplyTheme()`. Стражи: `ThemeAcceptanceTests.MainView_AppliesPaletteBeforeTheChatSwapsItsDictionary`,
    `ChatThemeBridgeTests.ThemeApplying_RunsBeforeThemeChanged_SoThePaletteIsFresh`.

- Заголовок окон: все окна студии рисуют его сами — `Controls\StudioTitleBar.xaml` (высота 32,
  `Studio.TitleBarBackground`/`TitleBarForeground`, кнопки «свернуть»/«закрыть» 46×32 на `MaterialIcon`
  16×16, hover закрытия `Studio.CloseHoverBackground`, перетаскивание `DragMove()`, DP `ShowMinimize`/
  `ShowClose`). Системный caption Windows не используется: он белый в любой теме и не скругляется.
  Окно с `StudioTitleBar` обязано иметь `WindowChrome` (`CaptionHeight="0"`, `GlassFrameThickness="0"`,
  `CornerRadius="8"`) — иначе `WindowCorner.ApplyCornerRadiusFallback` не скруглит его на Windows 10.
  Заголовок полосы берётся из `Title` окна (`RelativeSource AncestorType=Window`), поэтому `ErrorDialog`
  может менять его без DataContext. Тесты: `AiRpaStudio.Tests\StudioTitleBarTests.cs`.

- Диалоги студии («Создать активность», «Создать проект», «Открыть VS-проект», «Из VS-шаблона») задают
  `Background="{DynamicResource Studio.Surface}"` + `Foreground="{DynamicResource Studio.Foreground}"`,
  внешний отступ 12, кнопку обзора 28px; `Grid.Row` обязан существовать среди `RowDefinition`.
  Базовая тема приложения — `Dark`, а `AppSettings.ApplyMaterialDesignTheme` создаёт тему MaterialDesign
  нейтральными `Primary`/`Secondary` из палитры (акцент в фокус и скроллбары не пробивается).
  Тесты: `AiRpaStudio.Tests\StudioDialogWindowTests.cs`.



- Нативные WF-контролы (`ToolboxControl`, `OutlineView`, `PropertyInspectorView`) стилизуются
  обходом визуального дерева; исходники для изучения их шаблонов:
  `C:\Users\Maxxx\source\repos\.NET-Framework-4.8-RTM-Reference-Source`
  (детали — `dev-dev-docs\Reference_Source_Storage.md`).

- Финальные правила темы (этап 9, чеклист — приложение A в `dev-dev-docs\Vs2026_Theme_Plan.md`):
  * В темизируемых XAML (`Views`, `Controls`, `CSharpExpressionEditor`, `Themes\StudioTheme.xaml`,
    `AvalonDock.Themes.Metro\Theme.xaml`, `AvalonDock\Themes\generic.xaml`, `LogControlLibrary`, `RPA.Chat`)
    запрещены зашитые hex и named-цвета (`White`, `Gray`, `Orange`, …): только `{DynamicResource Studio.*}`.
    Единственные исключения — `#E81123`/`#FFFFFF` кнопки закрытия в `Themes\StudioTheme.xaml`.
  * В студии запрещён `MaterialDesign.Brush.*`: значения берутся из `StudioPalette`
    (`MaterialDesign` остаётся только базой контролов, его `Primary`/`Secondary` нейтральны, см. M-04).
  * Семантика (не акцент!) — `StudioBrushes.SemanticKeys`: `Studio.Semantic.Run`/`Pause`/`Stop`/`Save`
    (тулбар), `Studio.Semantic.FolderIcon`/`XamlFileIcon`/`JsonFileIcon` (дерево проекта), `Studio.Semantic.Validation` (бейдж ошибки поля выражения) и `Studio.Semantic.Log.Debug`/`Info`/`Warning`/`Error`/`Fatal` (фильтры панели «Log») — всего 13 ключей. Значения
    фиксированы для обеих тем (`chroma > 6`), создаются кодом; декоративные иконки должны использовать
    `Studio.ToolbarForeground`, а не цвет.
  * «Третьих» палитр нет: `Themes\StudioTheme.Dark.xaml`, `Themes\StudioTheme.Light.xaml` и
    `AvalonDock.Themes.Metro\Brushes.xaml` удалены (содержали прежние фиолетовые/светлые значения).
    Fallback-кисти `MetroTheme.GetResourceDictionary()` нейтральны (метод `Neutral`), акцент оттуда убран.
  * Тени (`DropShadowEffect`) в popup не используются: границу даёт hairline (`Studio.Separator`),
    скругление popup/меню — 6.
  * Скроллы — плоские, 10px, без стрелок: неявный стиль `ScrollBar` в `AiRpaStudio\Themes\StudioScrollBar.xaml`
    (`Width/MinWidth/MaxWidth = 10`, у горизонтального — `Height/MinHeight/MaxHeight = 10`, бегунок
    `Studio.SeparatorStrong` → hover `Studio.ForegroundMuted` → drag `Studio.Foreground`, скругление 5).
    Словарь обязан подключаться в `App.xaml` ПОСЛЕДНИМ (после `MaterialDesign2.Defaults.xaml` и
    `Themes\StudioTheme.xaml`), иначе неявный стиль MaterialDesign побеждает по порядку словарей;
    `RepeatButton`/`Thumb` внутри шаблона задают `Style` явно с `OverridesDefaultStyle=True`, иначе их
    раздувают неявные стили MaterialDesign. Чат (`RPA.Chat`) держит свои копии размеров 10px — он не
    ссылается на `RPA.Helpers`. Страж — `AiRpaStudio.Tests\StudioScrollBarTests.cs` (включая runtime-проверку
    реального `ScrollViewer`: 10px по обеим ориентациям).
  * Внутренние `ToggleButton` в шаблонах (`OutlinedComboBoxStyle`, `ReasoningExpanderStyle`) обязаны задавать
    `Style` явно: неявный стиль MaterialDesign для `ToggleButton` — это переключатель с фиксированной
    `Width = 34`, из-за него «коробка» списка в настройках агента сжималась по горизонтали, хотя сам
    `ComboBox` растянут по столбцу. Страж — `StudioScrollBarTests.OutlinedComboBox_InnerToggleButtonHasExplicitStyle`.
    Тот же дефект был у панели NuGet — «комбобоксы не растягиваются по контенту»: `DropDownToggle` и
    `HeaderToggle` получили явный `Style` `NuGet.StretchedToggleButtonStyle` (`Width=Auto`, `MinWidth=0`),
    потолок ширины 150px — в `NuGet.ComboBoxStyle`. Стражи —
    `NuGetPanelLayoutTests.ComboBoxInnerToggleButtons_HaveExplicitStretchedStyle` и
    `.Panel_ComboBoxesSurviveImplicitMaterialDesignToggleStyle` (эмулирует неявный стиль MaterialDesign:
    без `App.xaml` в тестах дефект не воспроизводился, поэтому три прошлые правки были зелёными).
  * Каждая докпанель обведена рамкой 1px (`AvalonDock_Separator`, у активной — `AvalonDock_SeparatorStrong`):
    панель главного окна, панель документов, панель автоскрытия и панели внутри откреплённых окон. Рамку
    рисует ровно один элемент (в плавающем окне — `WindowBorder`, иначе панель), ресайзер в покое линии не
    рисует (`Transparent`), активность заливкой не передаётся. Правило 6 плана `dev-docs\Vs2026_Theme_Plan.md`,
    страж — `ThemeXamlTests.ThemeXaml_PaneFramesAreOnePixel`.
  * `App.xaml` не объявляет ресурсы через `{DynamicResource ...}` и не держит `Freezable` (`DrawingImage`
    и т.п.): в `Application.Resources` такая запись остаётся `ResourceReferenceExpression` (не `Brush`),
    заморозить `Freezable` нельзя, а Style-значение валит `XamlParseException`. Кисти здесь создаёт
    `StudioBrushes.ApplyToApplication()`. Страж — `ThemeAcceptanceTests.App_HasNoDynamicResourceLookupsInResourceValues`
    (регрессия этапа 9: мёртвый ресурс `Bulb`).
  * Шеврон панели документов и меню списка открытых документов (P-09): шеврон (`MenuDropDownButton`)
    отступает от правой кромки панели на 3 px (`Margin="0,0,3,0"`), а меню и пункты рисуют **собственные**
    шаблоны темы (`AvalonDock_ThemeMetro_DocumentPaneContextMenuStyle` /
    `AvalonDock_ThemeMetro_DocumentPaneMenuItemStyle`). Иначе работает неявный `MenuItem` MaterialDesign с
    `Padding="24,0,24,0"` и колонкой иконки — между кромкой меню и названием документа оставалась широкая
    пустая полоса. Инвариант: **10 px от внешней кромки меню до начала текста** = 1 px рамки меню +
    9 px `Padding` пункта (паддинг самого меню — 0). Менять эти два числа можно только вместе.
    Стражи — `ThemeXamlTests.ThemeXaml_DocumentPaneChevronHasThreePixelRightInset`,
    `ThemeXaml_DocumentPaneMenuUsesOwnTemplates`,
    `ThemeXaml_DocumentPaneMenuTextStartsTenPixelsFromOuterEdge` (сумма сеттеров) и
    `DocumentPaneMenu_TextStartsTenPixelsFromOuterEdge` (материализация: замер `TransformToAncestor`).
  * Кнопки «Развернуть все»/«Свернуть все» в шапке дизайнера WF (P-10): WF-шаблон
    (`ExpandAllCollapseAllTemplate`) своей ширины не задаёт, поэтому её задаёт неявный `ToggleButton`
    MaterialDesign — `Width = 34` (та же причина, что у «коробки» `OutlinedComboBoxStyle`). Подпись
    «Развернуть все» ≈90 px в такую кнопку не влезает и обрезалась правой кромкой панели — в шапке
    читались только «разв» и «свер». `WfDesignerTheme.ThemeHeaderButtons` (шаг 4б `Apply`) снимает
    `Width` (`NaN`) и `MaxWidth`, ставит `MinWidth` по `FormattedText` ширине подписи + отступы подписи,
    паддинг и рамку кнопки, а правой кнопке пары — `Margin.Right = 5` (сдвигается вся пара, зазор между
    кнопками не растёт). Проход идемпотентен (`Math.Max`), поэтому смена темы не накапливает отступ.
    Стражи — `WfDesignerThemeTests.Apply_HeaderButtonsFitTheirCaptions`,
    `.Apply_HeaderButtonsKeepFivePixelRightInset`; живая проверка — отчёт
    `tools\designer-ui-diagnostics` (`ReportHeaderButtons`, печатает ширину кнопки и «влезает=да/НЕТ»).
  Тесты: `AiRpaStudio.Tests\ThemeAcceptanceTests.cs` (сквозные стражи §9) + стражи этапов 1-8.

## Шаблон MVVM проекта AiRpaStudio (FW.EasyMvvm) — обязательное требование

Проект `AiRpaStudio` использует НЕ классический MVVM, а «модель-центричный» вариант
библиотеки `FW.EasyMvvm` (namespace `FW.EasyMvvm.Core`, проект `FW.EasyMvvm`),
подключённой в `AiRpaStudio.csproj`. В репозитории есть почти идентичная библиотека
`EasyMvvm` (используется в `ProxyChecker`) — в AiRpaStudio применять ТОЛЬКО `FW.EasyMvvm`.

### Распределение ответственности
- Model (`BaseModel`) — главный слой: данные (`INotifyPropertyChanged`) + бизнес-логика.
  Команды — обычные публичные методы модели (`CmdWorkflowSave()`, `CreateActivity()`,
  `Settings()`).
- ViewModel (`BaseViewModel`) — тонкий адаптер: `Model` + `View`, pass-through свойства,
  без бизнес-логики; настройка в `InitializeViewModel()`.
- View (`IView`) — `Window`/контрол с `Show`, `ShowDialog`, `OnSendMessage`, `Close`.

### Команды отправляются особым образом (по имени метода, через reflection)
- В XAML одна команда `Command="{Binding Cmd}"`, а `CommandParameter` — строка с именем
  публичного метода модели: `CommandParameter="CmdWorkflowSave"`.
- Цепочка: `RelayCommand.Execute` → `BaseViewModel.SendCommand` → `BaseModel.SendCommand`
  → `GetType().GetMethod(name)?.Invoke(this, args)`.
- Команда с параметрами: `CommandParameter` / `Cmd.Execute(...)` передаётся как
  `object[]`/`List<object>` вида `[имяМетода, arg1, arg2, arg3]` (до 3 аргументов),
  например `dc.Cmd?.Execute(new object[] { "CmdAddNewActivity", newFilePath });`.
- Правило: новое действие = публичный метод модели, имя совпадает со строкой
  `CommandParameter`; отдельные `ICommand`-свойства на ViewModel НЕ создаются.

### Сборка окна и жизненный цикл — только через `MvvmFactory`
- `MvvmFactory.CreateWindow(model, viewModel, view, viewMode, childModels)`:
  `model.Run()` → `viewModel.SetModel()` (подписка `SendCommand += model.SendCommand`,
  `Model.ViewModel = this`, `InitializeViewModel()`) → `view.DataContext = viewModel` →
  `model.SendMessage += view.OnSendMessage` → `view.Loaded += model.Loaded()` →
  регистрация дочерних моделей `RegisterModel` → `Show`/`ShowDialog` (`ViewMode`).
- Новые окна открываются из методов модели через `MvvmFactory.CreateWindow(...)`.
- Синглтоны-модели (`CustomWfDesigner.GetInstance()`, `ProjectManager.GetInstance()`,
  `AppSettings.GetInstance()`) подключаются через `childModels`/`RegisterModel` (см. `EntryPoint.cs`).

### Уведомления
- `BaseModel.OnPropertyChanged()` поднимает `PropertyChanged` и на модели, и на ViewModel —
  имена pass-through свойств ViewModel обязаны совпадать с именами свойств модели.
- Сообщения «модель → представление»: `BaseModel.OnSendMessage(string)` →
  `IView.OnSendMessage(string)` (MahApps в решении нет — сообщения рисует студия), а не `MessageBox`.

## Состояние вкладок, валидация WF и окна WF (три регресса одной задачи)
- **Состояние вкладок** хранится в одном месте — `AiRpaStudio\Core\WorkflowSessionState.cs`
  (формат: `{version, project, active, documents[{path, kind}]}` в настройке
  `Properties.Settings.Default.DesignerFiles`; старый JSON-массив путей читается как fallback).
  Владелец сохранения/восстановления — `Main.SaveSessionState` / `Main.RestoreSessionState`
  (`AiRpaStudio\Models\Main.cs`): подписка на `CustomWfDesigner.WorkflowDesigners.CollectionChanged` и
  `Main.OpenCodeDocuments.CollectionChanged` живёт в конструкторе `Main`, финальная запись —
  `MainView.OnClosing`. Восстанавливают **оба** режима проекта (project.json и .sln/.csproj).
  Запрещено возвращать сохранение в подписку внутри `CustomWfDesigner.LoadProject` (именно из-за этого
  в VS-проекте вкладки не сохранялись) и писать настройку из двух мест. На время восстановления
  `_isRestoringSession` глушит запись: промежуточное состояние затирало список вкладок.
  Стражи: `AiRpaStudio.Tests\WorkflowSessionStateTests.cs`, `StudioSessionWiringTests.cs`.
- **Какая вкладка активна после закрытия** — правило одно на всю студию:
  `AiRpaStudio\Core\DocumentTabPolicy.cs` (без WPF/AvalonDock, чистые функции; вкладки сравниваются
  **по ссылке**, индексы — по списку до закрытия). Как в Visual Studio: активной становится соседняя
  **справа** (для последней вкладки — **слева**), закрытие единственной вкладки оставляет панель без
  активной, закрытие **фоновой** вкладки активную не меняет. Единственная точка решения — панель
  документов `AiRpaStudio\Core\DocumentTabsCloseCoordinator.cs`: `Closing` запоминает соседа по
  фактическому порядку вкладок (там и дизайнеры, и редакторы кода), `ApplyPendingActivation`
  активирует его после того, как модель убрала документ. Модель активную вкладку не выбирает:
  она лишь перестаёт считать активной закрытую вкладку (`CustomWfDesigner.RemoveDesignerByLoadedFile`,
  `syncWFControls`/сохранение иначе работали бы с закрытым документом). Запрещено: выбирать вкладку
  через `WorkflowDesigners[0]` (дефект «при закрытии открывается не та вкладка»), «последнюю вкладку
  кода» (`OpenCodeDocuments.LastOrDefault()`) в `Main.ActiveDocumentPath` — активную вкладку знает только
  `MainView.GetActiveDocumentPath`. Раньше здесь активным считался дизайнер (даже когда на экране была
  вкладка кода/XAML), а без активного дизайнера — последняя открытая вкладка кода. Координатор
  `tabsCloseCoordinator` подписывается на `WorkflowDesigners.CollectionChanged` **после** `DocGroupHelper`,
  иначе модель дизайнеров перезапишет выбор панели документов. Стражи (все — `AiRpaStudio.Tests\`):
  `DocumentTabPolicyTests.cs`, `DocumentTabsCloseTests.cs`, `DocumentTabsCloseWiringTests.cs`.
- **Выбрана ровно одна вкладка** — инвариант панели документов (второй регресс того же бага: «после
  перезапуска активна не та вкладка, две вкладки подсвечены как активные»). Источник правды о выбранной
  вкладке — `LayoutDocumentPane`: `IsSelected` (видимая вкладка; к нему TwoWay-привязан `TabItem.IsSelected`
  в `AvalonDock.Themes.Metro\Theme.xaml`) обязан совпадать с `IsActive` (`LayoutRoot.ActiveContent`), а
  `SelectedContent` — быть валидным. Запрещено возвращать в вендорном `AvalonDock` две правки, ломавшие
  инвариант: `_autoFixSelectedContent = false` в `LayoutDocumentPane` и ветку
  `if (value && Parent is LayoutDocumentPane) SelectedContentIndex = -1;` в `LayoutContent.IsSelected`.
  Обнуление индекса не снимало `IsSelected` с предыдущей вкладки (подсвечивались две «активные») и
  оставляло `SelectedContent == null`, поэтому `LayoutDocumentPaneControl.OnSelectionChanged` никогда не
  переносил выбор в `IsActive`, а `TabControl` показывал последнюю добавленную вкладку. Восстановление
  сессии открывает вкладки по одной (каждая ставит `IsSelected = true`), поэтому
  `Main.RestoreSessionState → ActivateDocument → SetActiveDesigner/ActivateCodeDocument` обязан **снять**
  выбор с последней открытой — теперь это делает сам `IsSelected`/`IsActive`. При расхождении флагов
  (унаследованное состояние) `DocumentTabsCloseCoordinator.FindActive` считает правдой `IsActive`.
  Стражи (STA, настоящие модели AvalonDock, без окна): `AiRpaStudio.Tests\DocumentPaneSelectionTests.cs`,
  `DocumentPaneSelectionWiringTests.cs`.

- **Иконка валидации на активности** рисуется WF (триггеры `ModelItem.ValidationState` у
  `Border x:Name="validationVisuals"`). Студия (`DesignerValidator`) ставит свои значения **только**
  когда есть её собственные ошибки (выражения, типы аргументов), иначе обязана вызвать
  `ClearValue(VisibilityProperty/BackgroundProperty/ToolTipProperty)`: локальное значение сильнее
  триггера шаблона, и `Visibility = Collapsed` гасил нативную красную иконку (баг «ошибки валидации
  исчезают»). Решение и кисть ошибки — `AiRpaStudio\Core\ValidationIconState.cs`: `ErrorIconBrush()`
  отдаёт родной рисунок WF (`DesignerItems.ActivityErrorValidation`, красный круг с белым «!»), а
  сплошная кисть палитры — только резерв: заливка без глифа и была дефектом «красная рамка без иконки».
  Флаг `_isNeedInitValidation` сбрасывается после первого прохода,
  разбор каждой активности — в собственном `try/catch`. Стражи: `AiRpaStudio.Tests\ValidationIconTests.cs`.
- **Бейдж ошибки C#-выражения** (`CSharpExpressionEditor\RoslynExpressionEditor.xaml`) — глиф
  `materialIcons:MaterialIcon` (`Kind="Error"`) в семантическом голубом `Studio.Semantic.Validation` (ключ из `StudioBrushes.SemanticKeys`, `#29B6F6`, от темы не зависит).
  Стиль бейджа — `Style x:Key="errorStyle"`; красный `Studio.CloseHoverBackground` остаётся системной семантикой ошибки — он у иконки валидации активности и у кружка адаптера `Validation.ErrorTemplate`); пустая `Rectangle`-рамка запрещена —
  её и видел пользователь как «красная рамка без иконки». Видимость — `HasValidationError`,
  подсказка — `ValidationErrorMessage`. Проверка выражения запускается по изменению текста
  (`IExpressionEditorInstance.TextChanged`) с паузой-дребезгом: политика в
  `AiRpaStudio\Core\ExpressionValidationTiming.cs` (350 мс, резервный фон — 3000 мс вместо прежних
  10 000 мс, из-за которых ошибка появлялась «через десяток секунд»), таймер останавливается в
  `OnEditorClosing`/`OnEditorUnloaded`, подписка снимается вместе с редактором. `ValidateExpression`
  обязана звать `UpdateValidationError(null)` при отсутствии ошибки — иначе исправленное выражение
  оставляло бейдж. У адаптера `Validation.ErrorTemplate` (`WfDesignerTheme.CreateValidationErrorTemplate`)
  кроме hairline-рамки есть значок ошибки. Стражи: `AiRpaStudio.Tests\ErrorBadgeTests.cs`,
  `ExpressionValidationTimingTests.cs`, `ValidationIconGlyphTests.cs`, `ValidationBadgeColorTests.cs`.

- **Окна WF** (`WorkflowElementDialogWindow`, `EditorDialog` — редактор выражений по «…» в окне
  «Свойства») — обычные `Window` с системным caption, белым в любой теме; `WfDesignerTheme` до них не
  достаёт. Тема ставится `AiRpaStudio\Helpers\WfDialogWindowTheme.cs`: `WindowChrome` с
  `CaptionHeight = 0`, ключи палитры через `SetResourceReference`, содержимое оборачивается в `Grid`
  с полосой `StudioTitleBar` 32 px, скругление — `WindowCorner.Enable`. Точка вызова —
  `RoslynExpressionEditor.OnEditorLoadedForSize` (там же, где восстанавливается размер окна).
  Хелпер идемпотентен и никогда не бросает исключений. Стражи:
  `AiRpaStudio.Tests\WfDialogWindowThemeTests.cs`.

## Меню дерева проекта: одно правило видимости
- **Что видно у файла, решает `AiRpaStudio\Helpers\ProjectTreeMenuRules.cs`**, а не XAML и не
  `ProjectManagerControl.ContextMenu_Opened`. Имена пунктов — константы того же класса, они совпадают с
  `x:Name` в `ProjectManagerControl.xaml`: новый пункт меню добавляется в XAML и в правило, code-behind
  при этом не правится.
- Матрица правила: у `project.json` видны только «Открыть содержащую папку» и «Открыть в редакторе кода»;
  «Открыть в Visual Studio» и «Собрать с помощью MSBuild» — только у `.sln`/`.csproj`; остальным файлам
  видно всё. Сравнение — по имени файла (путь отбрасывается) и без учёта регистра; незнакомое имя пункта
  правило не скрывает, иначе меню папок и корня проекта ломались бы молча.
- «Открыть в редакторе кода» есть в трёх меню: файла (`ProjectFileContext`, `OpenInCodeEditorMenuItem`),
  корня проекта VS (`VsProjectRootContext`, `VsRootOpenInCodeEditorMenuItem`) и решения
  (`VsProjectContext`, `SolutionOpenInCodeEditorMenuItem`). Имена разные намеренно: `ContextMenu_Opened`
  прячет пункты по именам, и совпадающее имя скрыло бы «Открыть в VS» у корня проекта.
- Открывает `Main.CmdOpenInCodeEditor` (вкладка `code:<путь>`, повторный вызов активирует уже открытую).
  Двойной клик по `.sln`/`.csproj` по-прежнему идёт в `Main.CmdTryLoadFile` → `CmdVsOpenInVisualStudio`:
  в `CmdTryLoadFile` ветка `.sln`/`.csproj` обязана стоять раньше ветки редактора кода. Подсветки для
  `.csproj`/`.sln` нет (`CodeLanguageProvider` их не знает) — файл открывается как текст; это отдельная
  задача, а не регресс.
- Стражи: `AiRpaStudio.Tests\ProjectTreeCodeEditorMenuTests.cs` (наличие пунктов и обработчиков, матрица
  правила, делегирование из `ContextMenu_Opened`, загрузка `.csproj`/`.sln` в `CodeDocument`), уникальность
  имён пунктов держит `AiRpaStudio.Tests\VsProjectContextMenuTests.cs`.

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
