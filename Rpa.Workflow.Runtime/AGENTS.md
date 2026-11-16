# Правила робота: рантайм, раннер и пакет

> Фрагмент иерархии правил решения `AiRpa.sln`. Глобальные правила и общий индекс —
> в корневом `AGENTS.md`. Этот файл поднимается в контекст только при работе с рантаймом,
> раннером и упаковкой робота (`Rpa.Workflow.Runtime`, `RpaRunner`, `RpaPack`,
> `Rpa.RobotPackaging`, `Rpa.Orchestrator.Contracts`).

## 4.7.2-робот на 4.8-хосте (`CP-08`)

- Студия (.NET Framework 4.8) запускает робота, собранного на **4.7.2**, в своём AppDomain: одна и та же
  версия CLR (v4.0.30319), сборки 4.7.2 исполняются на 4.8-рантайме. Доказательство —
  `AiRpaStudio.Tests\Net472OnNet48HostTests.cs`: эталон `TestProcess\Net472Robot` грузится через
  `Activator.CreateInstanceFrom` и отдаёт события в `ActivitiesTracker` (тоже 4.7.2) — Sequence, If, TryCatch,
  WriteLine, аргумент `InputText = max`, результат `Result = MAX`.
- **Зависимости робота** (`Newtonsoft.Json` робота, его библиотеки активностей) ищутся в его выходном каталоге
  `bin\<Config>`, `bin\<Config>\<TFM>`, `bin\<Platform>\<Config>`, `bin\<Platform>\<Config>\<TFM>`: `AiRpaStudio\Helpers\RobotAssemblyResolver.cs`. Правила: сборки хоста
  (`System.*`, WF, `Activities*`, `ActivitiesTracker`, `RPA*`) из каталога робота **никогда** не обслуживаются,
  понижение версии запрещено, чужой открытый ключ запрещён, обработчик не бросает исключений, подписка
  идемпотентна. Порядок: сначала `AssemblyBindingUnifier` (сверка версий внутри домена), затем этот резолвер.
  Тесты — `AiRpaStudio.Tests\RobotAssemblyResolverTests.cs` (поиск, правила, подписка; проверены негативом) и `AiRpaStudio.Tests\RobotExternalDependencyTests.cs` (эталон с внешней зависимостью `RobotExternalLib` прогоняется по сборке без `FileNotFoundException`).
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
- **Автосборка перед запуском** (`LooseWorkflowRun`, шаг 5): для WF-проекта (`IsWorkflowProject`) прогон
  сам собирает проект через `VsProjectBuilder` (MSBuild), если собранной `RPA-*.dll` нет или она устарела
  относительно XAML проекта (`ActivityRunner.TryLoadOrBuildFromProject`/`IsProjectOutdated`). Сборка —
  только в студии: раннер по-прежнему ничего не собирает (страж `RunnerSourceGuardTests`). Тесты —
  `AiRpaStudio.Tests\ActivityRunnerLoadPathTests.cs`.
- **Валидация перед запуском** (`LooseWorkflowRun`, шаг 6): после загрузки активности и до старта прогона
  `ActivityRunner.ValidateForRun` прогоняет `ActivityValidationServices.Validate` (как `--validate` раннера) —
  ошибки, невидимые загрузке (`Assign` без `To`/`Value`), дают понятное сообщение и не дают запустить прогон.
  Тесты — `AiRpaStudio.Tests\ActivityRunnerLoadPathTests.cs`.

- **Отладка при прогоне по сборке** (вложенные `SubProcessN`): дизайнер открывает workflow loose
  (фасады без тел), а прогон идёт по собранной сборке (реальные тела). `TrackHandler.GetSourceLocation`
  сопоставляет исполняемую активность с исходником по `Activity.Id` (`DesignerSourceLocationMapping`),
  а `Id` дерева loose-дизайнера не совпадает с `Id` дерева прогона внутри вложенных процессов. Поэтому
  подсветка текущей активности и точки останова гарантированно работают только **на верхнем уровне**
  (вне тел `SubProcessN`) — осознанное ограничение первого приёма (`dev-docs\LooseWorkflowRun_Plan.md`,
  шаг 3). Полная отладка вложенных процессов — отдельная карточка «дизайнер из сборки для отладки»
  (беклог, §6 того же плана).
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

