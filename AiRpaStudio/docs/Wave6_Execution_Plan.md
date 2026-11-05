# Волна 6 «Пакет робота `robot.zip` и версии» — журнал выполнения

| Поле | Значение |
|---|---|
| Документ | `AiRpaStudio\docs\Wave6_Execution_Plan.md` — журнал по карточке `OR-03` (`ROADMAP.md` §8.2; Ц5) |
| Нумерация | журналы идут подряд: `Wave1`…`Wave5` — волны 1, 6, 7, 8, 5. Это шестой журнал и волна 6 (`ROADMAP.md` §11 «Оркестратор: фундамент») |
| Область волны | выполнена **одна карточка** — `OR-03` (пакет робота и версии). `OR-01`, `OR-04`, `OR-05`, `OR-10` не начаты: пакет был блокирующим для `OR-05` (нечего публиковать), модель данных — следующая по плану |
| Дата | 06.11.2026 |
| Статус | **сделано, ждёт приёмки** — §7: полный прогон **2105/2105**, Release 0 ошибок из VS 2022 и VS 2026, живая упаковка с настоящей сборкой через MSBuild и прогон пакета раннером |
| Базовая линия на входе | полный прогон `AiRpaStudio.Tests` — **2032/2032**; раннер (`OR-02`) умел прогонять `--project`, `--workflow`, `--assembly`, но пакета не существовало |
| Правило | пункт закрывается только с тестом, полным зелёным прогоном `AiRpaStudio.Tests` (MSBuild из VS + `vstest.console.exe`) и сборкой `AiRpa.sln` Release из VS 2022 **и** VS 2026 |

## 1. Состав работ и итог

| # | Шаг | Суть | Итог |
|---|---|---|---|
| `W1` | Формат пакета в `Rpa.Orchestrator.Contracts` (`netstandard2.0`) | `RobotPackageFormat` (раскладка `manifest.json` + `lib/` + `robot/`, безопасные пути), `RobotPackageManifest` (+ аргументы, зависимости, файлы, хэш), JSON через `DataContractJsonSerializer`, `RobotPackageHash` (пофайловые sha256 + хэш содержимого), `RobotPackageValidator` (12 правил), `RobotPackageReader` (открытие, сверка содержимого, распаковка), `RobotPackageCache` (раскладка, неизменяемость, атомарная распаковка) | ✅ |
| `W2` | Общие правила проекта `RobotProjectLayout` | имя сборки, TFM, четыре раскладки `bin`, `x:Class`, разбор аргументов из `x:Members` — **один** источник для упаковщика и раннера | ✅ |
| `W3` | Упаковщик `Rpa.RobotPackaging` (`netstandard2.0`) | `RobotProject` (чтение проекта), `RobotBuildOutput` (поиск сборки, подмена конфигурации с замечанием), `RobotProjectBuild` (`IRobotProjectBuilder` + MSBuild через общий локатор `CP-04`), `RobotPackageWriter` (детерминированный zip), `RobotPackager` (сборка → файлы → манифест → архив → размещение) | ✅ |
| `W4` | CLI `RpaPack.exe` (`net472`) | `--project`, `--version`, `--name`, `--configuration`, `--out`, `--cache`, `--no-build`, `--json`, `--help`; коды возврата 0–7; справка = таблица кодов | ✅ |
| `W5` | Прогон пакета раннером (`OR-02` → `OR-03`) | `RpaRunner --package <robot.zip>` + `--package-cache`; локатор проверяет пакет, распаковывает в кэш, берёт `entryType` из манифеста; **«пакета нет» = 6, «пакет битый» = 5**; в `result.json` добавлены `package`, `packageId`, `packageVersion`, `packageHash` | ✅ |
| `W6` | Тесты и стражи | 69 тест-случаев (`Packages` 32, `Packaging` 22, `Runner\RunnerPackageSourceTests` 9, сквозной процесс 3) + 4 новых стража; 4 негатива снятием защит | ✅ |
| `W7` | Документация и доска | этот журнал, `AGENTS.md` (раздел про пакет), `ROADMAP.md` (`OR-03` закрыт, снимок состояния, §8.3), карточка `OR-03` на доске оркестратора | ✅ |

**Оценка:** 6 рабочих дней по плану волны. Больше половины времени — не упаковка, а **проверка целостности и неизменяемость** (см. §5): именно там нашлись дефекты, которые «на глаз» не видны, потому что пакет открывался и даже запускался.

## 2. Решения (приняты до кода)

| Решение | Почему так |
|---|---|
| **Упаковка — отдельный проект и отдельный exe**, а не режим раннера | раннер по контракту `OR-02` ничего не собирает; упаковка требует MSBuild. Страж `RunnerAndRuntime_DoNotReferencePackaging` не даёт протащить MSBuild в прогон «по-тихому» |
| **Формат пакета — в `Rpa.Orchestrator.Contracts`, а не в упаковщике** | манифест читают четыре стороны (упаковщик, раннер, сервер `OR-05`, студия `OR-14`); формат в упаковщике заставил бы остальных тянуть MSBuild |
| **`netstandard2.0` и нулевые зависимости контрактов** | читаться должно и на 4.7.2 (раннер), и на сервере; `DataContractJsonSerializer` есть в BCL обеих сторон, поэтому JSON-пакет NuGet не нужен |
| **`contentHash` — по манифесту файлов, а не по байтам архива** | пересборка тем же бинарником другим архиватором не должна выглядеть как новая версия; подмена же байтов ловится пофайловыми sha256 при открытии |
| **Пофайловые sha256 проверяются при каждом открытии пакета** | пакет лежит в кэше и на диске агента; «открылся» и «тот самый» — разные утверждения. Цена — чтение архива, выигрыш — подмена видна до запуска |
| **Неизменяемость версии: то же содержимое → `Reused`, другое → отказ** | оркестратор и запускатор должны знать, что «1.0.0» — ровно те байты. Перезапись версии ломает историю запусков (`OR-04`) |
| **Распаковка кэша по `contentHash` + маркер `.extracted`** | оборванная распаковка не должна приниматься за готовую; пересобранный пакет той же версии не подменит файлы в кэше |
| **Детерминированный zip** (сортировка, фиксированная дата 1980-01-01) | «тот же пакет» = тот же хэш файла; иначе сравнение версий и диффы бессмысленны |
| **«Пакета нет» (6) отделено от «пакет битый» (5)** | действия разные: опубликовать заново или чинить робота. Один код заставил бы оператора смотреть в лог, чтобы различить случаи |
| **Пути внутри пакета только через `/`, только безопасные относительные** | zip-slip и разъезд Windows/Linux-инструментов; запрет `..`, абсолютных путей и файлов вне `lib/` и `robot/` — правило валидатора, а не «договорённость» |
| **`lib/` берётся из каталога сборки целиком** (`.dll`, `.exe`, `.config`, `.pdb`), XAML — из проекта без `bin`/`obj` | пакет должен запускаться на машине без проекта: зависимость, забытая «вручную», — самый частый способ получить «у меня работало» |
| **Аргументы робота — из `x:Members`** (`InArgument`/`OutArgument`/`InOutArgument`) | оркестратору и запускатору нужно знать входы робота **до** запуска (форма запуска, проверка параметров), а не после падения |
| **Сборка проекта — MSBuild-процессом через общий локатор `CP-04`** | «студия собрала одним тулчейном, упаковщик другим» дало бы невоспроизводимые пакеты; процесс, а не ссылка на сборки MSBuild, — чтобы упаковщик не зависел от их версий |
| **`--no-build` — штатный режим** | на агенте сборки нет (и не должно быть); пакет собирают на машине сборки, а `--no-build` позволяет упаковать уже собранное без MSBuild вообще |

## 3. Формат пакета и коды возврата

Раскладка `robot.zip`:

```
robot.zip
├── manifest.json          — манифест (formatVersion, id, version, targetFramework, entryType,
│                            entryAssembly, entryWorkflow, createdAt, builder, source, arguments,
│                            dependencies, files[], contentHash, warnings[])
├── lib/                    — сборка робота и всё, что лежало рядом в bin\<Config>
│   ├── Net472Robot.dll
│   └── Net472Robot.pdb
└── robot/                  — XAML сценария (точка входа — manifest.entryWorkflow)
    └── Main.xaml
```

Кэш: `<root>\<id>\<version>\<id>-<version>.zip` и распаковки `…\<contentHash>\` с маркером `.extracted`;
`<root>` — `%LOCALAPPDATA%\RPA\packages` или `RPA_PACKAGE_CACHE`.

Коды `RpaPack.exe` (контракт для скриптов сборки и агента):

| Код | Значение | Что делает клиент |
|---|---|---|
| `0` | пакет создан или переиспользован (содержимое совпало) | публиковать |
| `1` | сборка проекта не прошла | чинить проект/тулчейн |
| `2` | неверные аргументы командной строки | чинить вызов |
| `3` | из проекта нельзя получить робота (нет проекта, XAML или `x:Class`) | чинить проект |
| `4` | собранный манифест не прошёл правила формата | ошибка упаковщика — баг |
| `5` | версия уже есть с другим содержимым | поднять версию |
| `6` | ошибка ввода-вывода при записи пакета | права/место на диске |
| `7` | робот не собран, а сборка отключена (`--no-build`) | собрать проект |

Коды раннера для пакета — из таблицы `OR-02`: `0` успех, `5` пакет не прошёл проверку, `6` пакета нет.

## 4. Артефакты

| Артефакт | Что внутри |
|---|---|
| `Rpa.Orchestrator.Contracts\Rpa.Orchestrator.Contracts.csproj` | `netstandard2.0`, без зависимостей и без `PackageReference` (страж) |
| `Rpa.Orchestrator.Contracts\Packages\RobotPackageFormat.cs` | раскладка пакета, нормализация и проверка путей (`IsSafeEntryPath`, `IsAbsoluteEntryPath`) |
| `Rpa.Orchestrator.Contracts\Packages\RobotPackageManifest.cs` | манифест и DTO: аргументы, зависимости, файлы, сборочная информация |
| `Rpa.Orchestrator.Contracts\Packages\RobotPackageManifestJson.cs` | чтение/запись `manifest.json` (`DataContractJsonSerializer`, детерминированный порядок) |
| `Rpa.Orchestrator.Contracts\Packages\RobotPackageHash.cs` | sha256 файла/потока/байтов и `OfContent` (хэш содержимого) |
| `Rpa.Orchestrator.Contracts\Packages\RobotPackageValidator.cs` | 12 правил годности: формат, идентичность, точка входа, файлы, хэши, аргументы, зависимости |
| `Rpa.Orchestrator.Contracts\Packages\RobotPackageReader.cs` | `TryOpen` (манифест + правила + сверка содержимого + запрет лишних файлов), `TryExtract` |
| `Rpa.Orchestrator.Contracts\Packages\RobotPackageCache.cs` | раскладка кэша, `TryPlace` (неизменяемость), `TryEnsureExtracted` (атомарная распаковка + маркер) |
| `Rpa.Orchestrator.Contracts\Packages\RobotProjectLayout.cs` | имя сборки, TFM, каталоги `bin`, `x:Class`, аргументы — общие правила проекта |
| `Rpa.RobotPackaging\RobotProject.cs` / `RobotBuildOutput.cs` / `RobotProjectBuild.cs` | чтение проекта, поиск собранной сборки, сборка через MSBuild |
| `Rpa.RobotPackaging\RobotPackageWriter.cs` / `RobotPackager.cs` | детерминированный архив; сборка → файлы → манифест → размещение |
| `RpaPack\Program.cs` / `PackOptions.cs` / `RpaPack.csproj` | CLI упаковки, разбор параметров, справка, коды возврата, JSON-вывод |
| `Rpa.Workflow.Runtime\Runner\RobotLocator.cs` | `--package`: проверка пакета, распаковка в кэш, `entryType`, код отказа из локатора |
| `AiRpaStudio.Tests\Packages\*`, `...\Packaging\*`, `...\Runner\RunnerPackageSourceTests.cs` | **69 тест-случаев** (в том числе сквозной «упаковать → прогнать» и живой `RpaPack.exe`) |

## 5. Дефекты, найденные доводкой (и почему их не видно «на глаз»)

| Симптом | Причина | Правка | Тест, который это держит |
|---|---|---|---|
| Повторная упаковка того же бинарника падала с «манифест не читается: непредвиденный символ `P`» | существующий пакет читался как **текст** (`TryReadFile(destination)`), а `destination` — это `zip` (первые байты `PK`) | сверка идёт через `RobotPackageReader.TryOpen`: манифест извлекается из архива, заодно проверяется целостность | `Pack_SameContentTwice_ReusesPackage` |
| В списке зависимостей появился `Net472Robot.pdb` | в `dependencies` попадали все файлы из `lib/`, включая `pdb` и `config` | зависимости — только `.dll`/`.exe` (у остальных нет версии в метаданных) | `Pack_ReferenceRobotWithoutBuild_CreatesPackage` |
| Подменённый пакет открывался как исправный | сверка искала записи через `ZipArchive.GetEntry("lib/…")`, а перепакованный сторонним инструментом архив хранит имена с `\` — запись «не находилась», и проверка молча деградировала в «файла нет» (код 5, но по неверной причине) | `FindEntry` сравнивает нормализованные имена записей | `Reader_RejectsTamperedFile`, `Locator_TamperedPackage_IsLoadFailed` |
| Свойство `Directory` в `RobotProject` ломало статические методы | `Directory` затенял `System.IO.Directory` внутри класса | переименовано в `ProjectDirectory` | сборка проекта |
| `RpaPack` не собирался: «Условное выражение недопустимо в версии языка 8.0» | `options.Json ? null : line => …` без целевого типа | `(Action<string>)null` + `Console.WriteLine` | сборка `RpaPack` |

## 6. Негативы (снятая защита → упавший тест)

| Снятая защита | Упавший тест |
|---|---|
| сверка содержимого пакета с манифестом (ранний `return true` в `TryVerifyEntries`) | `Reader_RejectsTamperedFile`, `Reader_RejectsExtraEntry`, `Locator_TamperedPackage_IsLoadFailed` (3 падения) |
| проверка безопасных путей (`IsSafeEntryPath` → `true`) | `Format_ChecksEntryPaths` × 8 случаев + `Validator_RejectsUnsafePathInFiles` (9 падений) |
| неизменяемость версии (`if (false && File.Exists(destination))`) | `Pack_ChangedContentSameVersion_ReportsVersionConflict`, `Pack_SameContentTwice_ReusesPackage` (2 падения) |
| запрет ссылки раннера на упаковщик (добавлена `ProjectReference` на `Rpa.RobotPackaging` в `RpaRunner.csproj`) | `RunnerAndRuntime_DoNotReferencePackaging` |

Все защиты восстановлены из побайтных копий; после восстановления прогон снова **2105/2105**.

## 7. Приёмка (живые бинарники, не только тесты)

| Сценарий | Команда | Результат |
|---|---|---|
| упаковка Debug без сборки | `RpaPack --project …\Net472Robot.csproj --no-build --configuration Debug --version 1.0.0 --out tmp\pack\robot.zip` | код **0**, `Created`, файлов 3 (`lib/Net472Robot.dll`, `lib/Net472Robot.pdb`, `robot/Main.xaml`), хэш `acc804c1…` |
| повторная упаковка того же | та же команда | код **0**, `Reused`, тот же `contentHash` |
| новая версия того же бинарника | `--version 1.0.1 --out tmp\pack\robot-2.zip` | код **0**, `Created` |
| подмена файла в пакете (правка байта в `lib/Net472Robot.dll` и перепаковка архива) | `RpaRunner --package tmp\pack\robot-tampered.zip` | код **5**, «файл «lib/Net472Robot.dll» не совпал с манифестом: ожидался sha256 `3d9656…`, в пакете `93833b…`» |
| пакета нет | `RpaRunner --package tmp\pack\нет-такого.zip` | код **6**, «пакет робота не найден» + подсказка собрать пакет |
| прогон пакета | `RpaRunner --package tmp\pack\robot.zip --arg InputText=max --package-cache tmp\pack\cache --out result.json` | код **0**, `Completed`, `Result=MAX`, `source=package` |
| упаковка **с настоящей сборкой** MSBuild | `RpaPack --project …\Net472Robot.csproj --configuration Release --version 3.0.0 --out tmp\pack3\robot.zip` | код **0**: `MSBuild 18.10.1.42706 (Visual Studio 2026)`, `restore: ok`, `сборка: ok`, взят `bin\Release\Net472Robot.dll`, хэш `e14f12d6…` |
| прогон этого пакета Release-раннером | `RpaRunner\bin\Release\net472\RpaRunner.exe --package tmp\pack3\robot.zip --arg InputText=release` | код **0**, `Result=RELEASE` |

Фрагмент `result.json` приёмки:

```json
{"runId":"acc-or03","status":"success","exitCode":0,"state":"Completed","timedOut":false,
 "source":"package","type":"Net472Robot.Main",
 "package":"…\\tmp\\pack3\\robot.zip","packageId":"Net472Robot","packageVersion":"3.0.0",
 "packageHash":"e14f12d680d9a52dba3ce3416a57e4a614d06d70628e5f524dc055a90c31e9e8",
 "outputs":{"Result":"RELEASE"},"error":""}
```

| Проверка | Результат |
|---|---|
| Полный прогон `AiRpaStudio.Tests` (MSBuild VS + `vstest.console.exe`) | **2105/2105** (2032 на входе + 69 тестов `OR-03` + 4 стража) |
| `MSBuild AiRpa.sln /p:Configuration=Release /restore` из **VS 2022** (`/m:1`, `/nodeReuse:false`) | exit **0**, **0 ошибок** |
| то же из **VS 2026** (MSBuild 18.10.1) | exit **0**, **0 ошибок** |
| Пакет собран с **Release**-сборкой робота | да (`bin\Release\Net472Robot.dll` внутри `lib/`) |

## 8. Открытые хвосты (не закрываются этой карточкой)

| Хвост | Куда |
|---|---|
| Публикация пакета в оркестратор (загрузка, каталог версий, «последняя версия») | `OR-04`, `OR-05` |
| Пункт «Опубликовать» в контекстном меню проекта в студии (собрать пакет + отправить) | `OR-14` |
| Подпись и доверие пакету (в том числе «принимать только пакеты из доверенного каталога») | отдельная карточка беклога — сейчас целостность есть, авторства нет |
| Сравнение версий (`1.10.0 > 1.9.0`) и правила выбора версии — нужны планировщику | `OR-07` (вместе с расписаниями) |
| Удаление старых версий из кэша (GC), лимит размера кэша | `OR-12` (эксплуатация) |
| Файлы данных робота (не XAML и не сборки) в пакет не попадают: нужен явный список ресурсов в проекте | отдельная карточка беклога — решение по формату объявления ресурсов |
| `--package` требует записи в кэш; при кэше «только чтение» нужен режим распаковки в temp | при потребности агента (`OR-08`) |
| Cквозной прогон «пакет → запуск на другом пользователе/машине» (чистый профиль) | `OR-10` (интеграционный тест волны 6) |

## 9. Ссылки

- План: `ROADMAP.md` §8.1–§8.3 (Ц5, `OR-03`), §11 (волна 5).
- Предыдущая волна: `AiRpaStudio\docs\Wave5_Execution_Plan.md` (`OR-02`, headless-раннер).
- Доска: `RPA оркестратор - доска.md`; критерий — `RPA - критерий готовности.md`.
