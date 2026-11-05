# Волна 5 «Headless-раннер `RpaRunner.exe`» — журнал выполнения

| Поле | Значение |
|---|---|
| Документ | `AiRpaStudio\docs\Wave5_Execution_Plan.md` — журнал по карточке `OR-02` (`ROADMAP.md` §5, Ц5; критерий владельца — «прогнать сценарий») |
| Нумерация | журналы идут подряд (`Wave1` → волна 1, `Wave2` → волна 6, `Wave3` → волна 7, `Wave4` → волна 8), поэтому пятый журнал — `Wave5`, а волна — 5 (`ROADMAP.md` §8.4 «Оркестратор: фундамент») |
| Область волны 5 | выполнена **одна карточка** — `OR-02` (headless-раннер). Остальные карточки волны (`OR-01`, `OR-03`, `OR-04`, `OR-05`, `OR-10`) не начаты: без раннера их нечем наполнять |
| Дата | 06.11.2026 |
| Статус | **сделано, ждёт приёмки** — см. §7 (прогон 2032/2032, Release 0 ошибок из VS 2022 и VS 2026, живой прогон раннера по эталону `Net472Robot`) |
| Базовая линия на входе | полный прогон `AiRpaStudio.Tests` — **1969/1969**; движок прогона жил в студии (`AiRpaStudio\Core\WorkflowRunEngine.cs`), роль раннера играл устаревший прототип `WorkflowHost` (`MessageBox`, `Console.ReadKey()`, `EnvDTE80`) |
| Правило | пункт закрывается только с тестом, полным зелёным прогоном `AiRpaStudio.Tests` (MSBuild из VS + `vstest.console.exe`, **не** `dotnet test`) и сборкой `AiRpa.sln` Release из VS 2022 **и** VS 2026 |

## 1. Состав работ и итог

| # | Шаг | Суть | Итог |
|---|---|---|---|
| `W1` | Общий рантайм `Rpa.Workflow.Runtime` (`net472`) | движок прогона и формат ошибки вынесены из студии в отдельный проект; студия и раннер используют **один** `WorkflowRunEngine` | ✅ |
| `W2` | Каркас `RpaRunner` (`net472`, Console, AnyCPU) | `Program.cs` (консоль, Ctrl+C, код возврата), `RunnerOptions` (12 параметров, JSON-аргументы), `RunnerUsage` (справка = таблица кодов), `RunnerExitCode(s)` | ✅ |
| `W3` | Поиск робота и прогон | `RobotLocator` (четыре раскладки `bin`, `AssemblyName`/`x:Class`, выбор свежей сборки, перечень кандидатов, код 6), `RobotDependencyProbe` (зависимости робота из каталога его сборки), `WorkflowRunnerHost` (загрузка, прогон, `--validate`) | ✅ |
| `W4` | Лог и отчёт | `RunnerJournal` (JSON-lines, `run-id` в каждой строке), `RunnerOutcome` (`result.json`, `Summary`, `Status`), запись отчёта даже при падении | ✅ |
| `W5` | Отмена и предел времени | `CancellationToken` из Ctrl+C (первый и второй — штатная отмена, процесс не умирает), `--timeout`, коды 3 и 4 | ✅ |
| `W6` | Тесты и стражи | 63 тест-случая в 5 файлах + 4 стража (нет UI/ожидания ввода, нет MSBuild и студии, один движок, TFM 4.7.2) | ✅ |
| `W7` | Документация и доска | этот журнал, `AGENTS.md` (раздел про раннер), `ROADMAP.md`, карточка `OR-02` на доске оркестратора | ✅ |

**Оценка:** 8 рабочих дней по плану волны. Основное время ушло не на перенос движка, а на локатор робота, таблицу кодов возврата и приёмку на живом бинарнике: именно там обнаружились дефекты, которые тесты «на бумаге» не ловят (см. §5).

## 2. Решения (приняты до кода)

| Решение | Почему так |
|---|---|
| **Раннер ничего не собирает** — только находит уже собранного робота | «Чистая машина» не имеет Visual Studio и MSBuild; сборка — работа студии (`OR-03` упакует результат). Отсюда же запрет ссылок на `Microsoft.Build`/`MsBuildLocator` в `RpaRunner` (страж) |
| **Общий рантайм `Rpa.Workflow.Runtime` вместо копии движка** | второй `WorkflowRunEngine` разошёлся бы со студийным молча; страж `ThereIsExactlyOneRunEngineSource` не даёт завести копию |
| **TFM `net472`** (`CP-10`) | раннер кладётся рядом с роботом, а робот по правилу 4.7.2; студия 4.8 грузит его сборку без пересборки (`CP-08`) |
| **JSON — `JavaScriptSerializer` из .NET Framework** | ноль новых пакетов в поставке: `System.Web.Extensions` уже есть на любой Windows с .NET Framework |
| **Три источника прогона:** `--project` / `--workflow` / `--assembly --type` | проект — обычный путь; XAML — быстрый прогон без сборки; сборка — для оркестратора и чужого кода |
| **Локатор не «угадывает»**: свежая сборка побеждает, но все кандидаты и причина выбора уходят в лог | «не удалось загрузить workflow» вместо «робот не собран» — самая дорогая ошибка эксплуатации |
| **Код 6 отделён от кода 5** | оркестратору (`OR-05`) и запускатору (`LN-02`) нужно различать «робота нет / не собран» и «робот есть, но сломан» |
| **`--validate` — это WF-валидация (`ActivityValidationServices`), а не прогон** | ошибки, невидимые загрузке (`Assign` без `To`/`Value`), ловятся до старта и дают код 5 |
| **Ctrl+C — штатная отмена, а не убийство процесса** | иначе не записать `result.json` и не сообщить причину; `eventArgs.Cancel = true`, робот получает `CancellationToken` |
| **`result.json` пишется в `finally`** | отчёт нужен и при падении, и при таймауте: оркестратор читает выходные аргументы и код оттуда |
| **Стражи вместо «посмотреть глазами»** | запреты «нет UI», «нет сборки», «один движок», «TFM» нарушаются одной строкой в `csproj` — поэтому проверяются тестом |
| **`--arg имя=значение` рядом с `--args {json}`** | PowerShell съедает кавычки внутри JSON (`--args {"InputText":"max"}` доходит как `{InputText:max}`) — это воспроизведено на приёмке, поэтому простой путь обязателен |

## 3. Таблица кодов возврата (контракт для `OR-05`/`LN-02`)

| Код | Значение | Что делает клиент |
|---|---|---|
| `0` | прогон завершён, выходные аргументы получены (в том числе `--validate` без ошибок) | успех |
| `1` | робот упал на необработанном исключении | чинить робота |
| `2` | неверные аргументы командной строки | чинить вызов |
| `3` | прогон отменён | перезапуск по решению оператора |
| `4` | предел времени (`--timeout`) | повтор с большим пределом или разбор зависания |
| `5` | workflow не загрузился или не прошёл проверку | чинить робота, прогон бессмысленен |
| `6` | робот не найден или не собран (нет сборки в `bin` проекта) | собрать проект, затем повторить |

## 4. Артефакты

| Артефакт | Что внутри |
|---|---|
| `Rpa.Workflow.Runtime\Rpa.Workflow.Runtime.csproj` | `net472`, ссылки `System.Activities`, `System.Xaml`, `System.Web.Extensions`, проект `ActivitiesTracker`; WPF и студии нет |
| `Rpa.Workflow.Runtime\Engine\WorkflowRunEngine.cs` | движок прогона (перенесён из студии, логика не менялась) |
| `Rpa.Workflow.Runtime\Helpers\ErrorText.cs` | формат ошибки со стеком (перенесён из студии) |
| `Rpa.Workflow.Runtime\Runner\RobotLocator.cs` | `RobotLocation` + `RobotLocator`: `AssemblyName`/`RootNamespace`, `x:Class`, четыре раскладки `bin`, нормализация TFM, выбор свежей сборки, текст отказа с перечнем каталогов |
| `Rpa.Workflow.Runtime\Runner\RobotDependencyProbe.cs` | подключение каталога зависимостей робота (обработчик `AssemblyResolve`, запрет на подмену сборок хоста) |
| `Rpa.Workflow.Runtime\Runner\WorkflowRunnerHost.cs` | исполнение прогона: локатор → зависимости → загрузка → `--validate` / прогон → `RunnerOutcome` → `result.json`; ни одного исключения наружу |
| `Rpa.Workflow.Runtime\Runner\RunnerOptions.cs` / `RunnerUsage.cs` / `RunnerExitCode.cs` | разбор командной строки (ошибки — код 2 и справка), справка, таблица кодов |
| `Rpa.Workflow.Runtime\Runner\RunnerJournal.cs` / `RunnerOutcome.cs` | JSON-lines лог с `run-id` в каждой строке; итог прогона, модель `result.json`, `Summary`, `Status` |
| `RpaRunner\Program.cs` / `RpaRunner.csproj` | оболочка: кодировка консоли, Ctrl+C, код возврата, последняя страховка `catch` → код 1 |
| `AiRpaStudio.Tests\Runner\` | `RobotLocatorTests` (10), `RunnerOptionsTests` (15), `WorkflowRunnerHostTests` (9), `RunnerSourceGuardTests` (8), `RpaRunnerProcessTests` (4) — **63 тест-случая** |

## 5. Дефекты, найденные доводкой (и почему их не видно «на глаз»)

| Симптом | Причина | Правка | Тест, который это держит |
|---|---|---|---|
| Робот на `v4.8` «не собран», хотя `bin\Debug\net48` есть | `NormalizeFramework("v4.8")` склеивал третий компонент `Version` (-1) → каталог `net48-1` | третий компонент добавляется только при `Build >= 0` | `NormalizeFramework_ConvertsBothSpellings` |
| Успешный прогон отдавал `ErrorText = null` | инициализация в конструкторе отсутствовала, а `Fail` заполняет ошибку только на неуспехе | `ErrorText = string.Empty` в конструкторе | `Execute_WorkflowWithArgument_ReturnsZeroAndOutputs` |
| Правило «для `--assembly` обязателен `--type`» в локаторе ни разу не исполнялось | `RunnerOptions.TryParse` отсекает такой вызов раньше (и это правильно: код 2) — проверка локатора осталась недостижимой | внутренняя сборка параметров `RunnerOptions.FromValues` для теста публичного API локатора | `Resolve_AssemblyWithoutType_IsError` |
| Стражи падали на собственных пояснениях | в XML-документации прямо написано «ни `MessageBox`, ни `Console.ReadKey()`», в `ErrorText` — пример с `System.Windows.Controls` | страж читает код без комментариев (`CodeOnly`, строковые литералы сохраняются) | `RunnerSources_HaveNoUiOrBlockingInput`, `RuntimeSources_HaveNoUi` |
| `--validate` не находил ошибок в «невалидном» XAML | `Delay` с отрицательной длительностью WF валидирует без ошибок — фикстура проверяла не то | фикстура `Assign` без `To`/`Value` (проверено: 2 ошибки валидации) | `Execute_ValidateOnlyWithInvalidWorkflow_ReturnsLoadFailed` |

## 6. Негативы (снятая защита → упавший тест)

| Снятая защита | Упавший тест |
|---|---|
| проверка «`--assembly` без `--type`» в локаторе | `Resolve_AssemblyWithoutType_IsError` |
| нормализация framework как до правки (`Build >= -1`) | `NormalizeFramework_ConvertsBothSpellings` |
| инициализация `ErrorText` убрана | `Execute_WorkflowWithArgument_ReturnsZeroAndOutputs` |
| в `Program.cs` добавлено ожидание ввода (`Console.ReadKey()`) | `RunnerSources_HaveNoUiOrBlockingInput` |
| в `Program.cs` добавлен `System.Windows.MessageBox` | сборка раннера **не проходит** (нет ссылки на WPF) — запрет держится не только стражем |

## 7. Приёмка (живой раннер, не только тесты)

| Сценарий | Команда | Результат |
|---|---|---|
| успех по проекту | `RpaRunner --project AiRpaStudio\TestProcess\Net472Robot\Net472Robot.csproj --arg InputText=max --run-id acc-02 --out tmp\acceptance-result.json` | код **0**, `Completed`, `Result=MAX`, `result.json` записан |
| тот же вызов Release-бинаником | `RpaRunner\bin\Release\net472\RpaRunner.exe --arg InputText=release` | код **0**, `Result=RELEASE` |
| падение робота | `--workflow AiRpaStudio\TestProcess\BrokenRobot\BrokenMain.xaml` | код **1**, `Faulted`, `System.Exception: boom` |
| ошибка аргументов | `--args {"InputText":"max"}` (PowerShell съел кавычки) | код **2**, «параметр «--args» не разобран как JSON», справка |
| предел времени | `--workflow tmp\Wait.xaml --timeout 1` | код **4**, `таимаут`, `TimeoutException` |
| ошибка загрузки | `--validate tmp\BrokenSyntax.xaml` | код **5** |
| несобранный робот | `--project tmp\nb\NotBuiltRobot.csproj` (каталога `bin` нет) | код **6**, перечислены 12 каталогов поиска и подсказка «Соберите проект» |
| `--validate` на исправном роботе | `--workflow AiRpaStudio\TestProcess\Net472Robot\Main.xaml --validate` | код **0**, состояние `Validated`, прогон не запускался |
| отмена | `Execute_Cancelled_ReturnsCancelledThree` (Ctrl+C из скрипта не нажать) | код **3**, `cancelled`; отдельно: второй Ctrl+C не убивает процесс |

`result.json` приёмки (сокращённо):

```json
{"runId":"acc-02","status":"success","exitCode":0,"state":"Completed","timedOut":false,
 "source":"project","type":"Net472Robot.Main","bin":"...\\Net472Robot\\bin\\Debug",
 "outputs":{"Result":"MAX"},"error":""}
```

| Проверка | Результат |
|---|---|
| Полный прогон `AiRpaStudio.Tests` (MSBuild VS 2022 + `vstest.console.exe`) | **2032/2032** (1969 на входе + 63 новых) |
| `MSBuild AiRpa.sln /p:Configuration=Release /restore` из **VS 2022** (`/m:1`, `/nodeReuse:false`) | exit **0**, **0 ошибок** (142 предупреждения) |
| то же из **VS 2026** (MSBuild 18) | exit **0**, **0 ошибок** |

## 8. Открытые хвосты (не закрываются этой карточкой)

| Хвост | Куда |
|---|---|
| `--package <robot.zip>` + манифест: третий источник прогона не меняет ни локатор, ни коды | `OR-03` |
| Отмена «сигнальный файл» и «именованный пайп» (в `ROADMAP` требование шире, сейчас штатно только Ctrl+C) | `OR-08` / `LN-02` — там есть чем подать сигнал |
| Tracking-записи в JSON-lines: сейчас это текст события в поле `message`, без структурных полей (`activityId`, `state`) | отдельная карточка беклога |
| `--validate` — WF-валидация; кастомные constrain'ы студии (`WorkflowValidationGate`) не подключены | по потребности `TZ-08`/`AG-*` |
| Раскладка раннера рядом с роботом, установщик, `appsettings` | `OR-12` |
| `Rpa.Orchestrator.Contracts` (DTO пакета и манифеста) раннер пока не использует | `OR-03` → `OR-05` |

## 9. Ссылки

- План: `ROADMAP.md` §5 (Ц5, `OR-02`), §8.4 (волна 5); доска — [[RPA оркестратор - доска]], критерий — [[RPA - критерий готовности]].
- Предыдущая волна: `AiRpaStudio\docs\Wave4_Execution_Plan.md` (волна 8, `TZ-01`…`TZ-09`).
- Устаревший прототип: `WorkflowHost` (`MessageBox`, `Console.ReadKey()`, `EnvDTE80`) остаётся в решении как исторический код под стражем `WorkflowHostLegacyGuardTests`.
