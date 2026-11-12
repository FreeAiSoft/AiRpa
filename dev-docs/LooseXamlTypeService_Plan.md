# Лёгкий сервис типов вложенных XAML-активностей — план и журнал

| Поле | Значение |
|---|---|
| Документ | `dev-docs\LooseXamlTypeService_Plan.md` — план реализации отображения вложенных XAML-процессов в AirPaStudio **без сборки и без DLL** |
| Цель | **Ц1-а** (см. `dev-docs\ROADMAP.md`): стабильный и быстрый XAML-дизайнер |
| Дата заведения | 12.11.2026 |
| Правило приёмки | каждый шаг закрывается тестом (включая негатив), полным зелёным прогоном `AiRpaStudio.Tests` и сборкой `AiRpa.sln` Release из VS 2022 и VS 2026; production-правки — только по TDD (red → green → refactor) |

## 1. Контекст и договорённости

Проект `RPA-1` — валидная WF4 Activity Library, где один XAML ссылается на другой по типу:

```xml
<Activity x:Class="RPA_1.Main" ... xmlns:local="clr-namespace:RPA_1">
  ...
  <local:SubProcess1 />
</Activity>
```

`<local:SubProcess1/>` — это CLR-тип `RPA_1.SubProcess1`, который появляется только **после сборки**
проекта (`XamlAppDef` → `XamlBuildTask` → partial class → `RPA-1.dll`).

**Решение задачи:** не собирать и не грузить DLL. Лёгкий сервис отдаёт дизайнеру только то, что нужно
для отображения блока: **тип (полное имя), namespace и аргументы**. Тело вложенного процесса при
открытии родителя не разбирается.

**Метод быстрого разбора** уже проверен стендом `AiRpaStudio.Tests\Workflow\XamlTypeFacadeBenchmarkTests.cs`:
читается только корневой `x:Class` и секция `x:Members` через `XmlReader`, дальше возвращаемся, не касаясь
`<Sequence>` с сотнями блоков и переменными. Замер: **100 активностей (20 переменных / 300 блоков /
10 аргументов) — медиана 0.8 мс** (цель «< 1 сек»). Полная WF-загрузка тела для контраста ≈ 130 мс/100.

## 2. Архитектура

```
XAML-файлы проекта (XamlAppDef)
        │
        ▼
XamlTypeFacadeBuilder.Build(xaml)      — быстрый разбор: x:Class + x:Members, тело не читается
        │
        ▼
XamlTypeFacade { FullName, Namespace, Name, Arguments[] }
        │
        ▼
WorkflowTypeRegistry                   — реестр x:Class → (facade, sourceFile), инвалидация по файлу
        │
        ▼
LooseXamlDocumentPersistenceService    — реализация IDocumentPersistenceService
        │   Load(main.xaml): строит ActivityBuilder, где <local:SubProcessN/> → DynamicActivity-фасад
        │   Flush(root): сериализует обратно <local:SubProcessN/>, не трогая вложенный файл
        ▼
WorkflowDesigner.Load(file)            — стандартный дизайнер, без сборки и без MSBuild
```

Ключевые факты из reference source .NET 4.8, на которые опирается решение:

- `IDocumentPersistenceService` (`System.Activities.Presentation.Hosting`) — публичная точка перехвата
  загрузки/сохранения: если сервис опубликован в `Context.Services`, `WorkflowDesigner.Load(file)` зовёт
  `service.Load(file)`, а `Flush` — `service.Flush(root)` вместо встроенной сериализации.
- `DynamicActivity`/`ActivityBuilder` (`System.Activities`) — готовый «тип без сборки»:
  `Name` (полное имя = namespace + имя) и `Properties` (аргументы) — это ровно то, что нужно дизайнеру.
- `DynamicActivityXamlReader` переписывает `<Activity x:Class=...>` → `<DynamicActivity Name=...>`,
  а `ActivityBuilderXamlWriter` — обратно. Это готовый и быстрый конвейер, его используем для фасадов.

## 3. План по шагам

| Шаг | Что делаем | Состояние |
|---|---|---|
| 1 | Вынести `XamlTypeFacade` + `XamlTypeFacadeBuilder` из теста в production (`AiRpaStudio\Core\Workflow\`). Бенчмарк продолжает ссылаться на production-класс; добавляются юнит-тесты корректности + страж «тело не разбирается». | ✅ сделано 12.11.2026 |
| 2 | `WorkflowTypeRegistry`: сканирует XAML-файлы проекта, строит словарь `x:Class → (facade, file)`, быстрый lookup, пропуск битых/без-`x:Class` файлов, первый дубликат побеждает. Тесты: пустой набор, дубликаты, файл без `x:Class`, битый XML. | ✅ сделано 12.11.2026 |
| 3 | `LooseXamlDocumentPersistenceService`: по реестру превращает `<local:SubProcessN/>` в `DynamicActivity`-фасад, грузит `Main.xaml` через `ActivityXamlServices.CreateBuilderReader` + `XamlServices.Load`. Тесты: `Main.xaml` с N вложенными ссылками → `ActivityBuilder` с N фасадами; аргументы доезжают. | ✅ сделано 12.11.2026 |
| 4 | Интеграция в путь открытия: `CustomWfDesigner.LoadProject` публикует сервис в `Context.Services` до `designer.Load`; вручную выставить `WorkflowFileItem` (нюанс: при `IDocumentPersistenceService` сам дизайнер его не кладёт). Тест на живом стенде: `RPA-1\Main.xaml` открывается, блоки `SubProcess*` видны. | ✅ сделано 12.11.2026 |
| 5 | Round-trip сохранение: `Flush(root)` сериализует фасады обратно в `<local:SubProcessN/>`; ссылки остаются ссылками, содержимое вложенных файлов не меняется. Тест: открыть → сохранить → байтово сравнить ссылки. | ✅ сделано 12.11.2026 |
| 6 | Приёмка: `RPA-1` открывается/сохраняется без MSBuild и без `bin\*.dll`; полный прогон тестов; Release сборка VS 2022 + VS 2026; обновить `ROADMAP.md` §3. | ✅ сделано 12.11.2026 |

## 4. Определения «готово» на шаг

- Шаг 1: production-классы в `RPA.Core.Workflow`, тесты зелёные, бенчмарк проходит по production-классу.
- Шаг 2: реестр строит карту для `RPA-1` (файлы с `x:Class`) без чтения тела.
- Шаг 3: сервис превращает `Main.xaml` в `ActivityBuilder`, где вложенные ссылки — `DynamicActivity` с корректными `Name`/аргументами.
- Шаг 4: в дизайнере `RPA-1` отображаются вложенные активности; сборка проекта не вызывается.
- Шаг 5: сохранение не «распаковывает» ссылки и не меняет вложенные файлы.
- Шаг 6: все критерии подтверждены тестами и сборкой; `ROADMAP.md` обновлён.

## 5. Журнал изменений

| Дата | Что сделано |
|---|---|
| 12.11.2026 | Заведён план; замеры производительности (100 активностей — 0.8 мс медиана); цель Ц1-а в `ROADMAP.md` |
| 12.11.2026 | Шаг 1: `XamlTypeFacade` + `XamlTypeFacadeBuilder` вынесены в `RPA.Core.Workflow`; 8 тестов зелёные |
| 12.11.2026 | Шаг 2: `WorkflowTypeRegistry` (сканирование шапок, lookup, пропуск битых); 14 тестов зелёные |
| 12.11.2026 | Шаг 3: `LooseActivityXamlSchemaContext` + `LooseXamlDocumentPersistenceService` (виртуальный XamlType, аргументы в `DynamicActivity.Properties`); 3 теста зелёные |
| 12.11.2026 | Шаг 4: интеграция в путь открытия — `LooseXamlDesignerLoader.Open` публикует `IDocumentPersistenceService` до `Load` и вручную выставляет `WorkflowFileItem`; `CustomWfDesigner.LoadProject` строит реестр из `XamlAppDef` проекта (`LooseXamlProjectService`) и открывает `Main.xaml` loose-путём; live-тест `Rpa1_MainOpensLoose_WithSubProcessFacade` зелёный |
| 12.11.2026 | Шаг 5: round-trip сохранение — `LooseXamlRoundTripSerializer` (штатный `ActivityBuilderXamlWriter` + обратная замена `DynamicActivity`→`<local:SubProcessN/>`), `Flush` отдаёт XAML в `WorkflowDesigner.Text`; тесты `Flush_*` и `Rpa1_MainRoundTrips_ReferencesStayReferences` зелёные |
| 12.11.2026 | Шаг 6: приёмка — `RPA-1` открывается/сохраняется без `bin\*.dll`; полный прогон `AiRpaStudio.Tests` (2476/2478, 2 падения — предсуществующий environmental-страж `ReferenceWorkflow_*` стандартного пути без `RPA-1.dll`); Release сборка `AiRpa.sln` VS 2022 и VS 2026 — 0 ошибок; `ROADMAP.md` §3 обновлён |

