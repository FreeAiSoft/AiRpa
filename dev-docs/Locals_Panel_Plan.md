> Статус: Этапы 1–5 и этап 8 (полный видимый scope через запоминание в трекинге) завершены; вариант B (ActivityInstance) реализован — B-1/B-2/B-4 закрыты, B-3 (редактирование) остаётся в беклоге
# Панель «Локальные переменные» (Locals) при остановке на точке останова — план

| Поле | Значение |
|---|---|
| Документ | `dev-docs\Locals_Panel_Plan.md` — план реализации окна «Локальные переменные» (`Locals`) студии |
| Цель | При срабатывании точки останова показывать переменные текущего скоупа; раскрывать вьюшки объектов, коллекций и таблиц |
| Связанные документы | `dev-docs\LooseWorkflowRun_Plan.md` (точки останова, маппинг `SourceLocation`), `dev-docs\WorkflowEditTracking_Architecture.md` (трекинг) |
| Reference source | `C:\Users\Maxxx\source\repos\.NET-Framework-4.8-RTM-Reference-Source` |
| Правило приёмки | каждый шаг закрывается тестом (включая негатив), полным зелёным прогоном `AiRpaStudio.Tests` и сборкой `AiRpa.sln` Release из VS 2022 и VS 2026; production-правки — только по TDD (red → green → refactor) |
| Заведён | 2026-11-21 |

## 1. Контекст и причина

Отладка в студии построена на **WF4** (`System.Activities`), а не на `System.Workflow`
(`ndp\cdf\src\WF` — это WF 3.x). Прогон устроен так:

`WorkflowApplication` + `ActivitiesTracker.Core.CustomTrackingParticipant` →
`ActivityStateRecord` → `TrackingEventArgs` → `TrackHandler.HandleTrackingRecord(...)`.

Точка останова обрабатывается в `AiRpaStudio\Helpers\TrackHandler.cs` → `ShowDebug(...)`.
Блокировка фонового потока выполняется через `resumeRuntimeFromHost.WaitOne()`. Именно в
этот момент нужно заполнять панель «Локальные переменные».

Уже есть заготовка данных:
- `ActivitiesTracker\Core\LocalsHelper.cs` — сбор `LocalVar` из `ActivityStateRecord`;
- `ActivitiesTracker\Entities\LocalVar.cs` — модель переменной (`VarName`, `Data`, `TypeName`, `Scope`).

Текущий `LocalsHelper.GetLocalsAndArgs(...)` написан некорректно: цикл `do/while` не меняет
`activity`, а `CreateInstance<ActivityInfo>` собирает `ActivityInfo` через reflection —
это нужно переписать.

UI-панели «Locals» в проекте нет. Панели создаются в `AiRpaStudio\Views\MainView.xaml.cs`
(`InitializeControls`, `PrepareLayout`, `CreateDefaultLayout`), а в XAML-раскладке они
регистрируются как `LayoutAnchorable` с `ContentId`.

В reference source WF4 есть точный механизм получения переменных:
`System.Activities.Debugger.DebugInfo.GetLocals()`
(`ndp\cdf\src\NetFx40\System.Activities\System\Activities\Debugger\DebugInfo.cs`) собирает
`Variables`/`Arguments`/`DelegateArguments` из `ActivityInstance.Environment` с учётом
затенения имён. Но `DebugInfo`/`ActivityInstance` — internal, и штатно `ActivityInstance`
из `WorkflowApplication` наружу не отдаётся.

## 2. Сравнение источников данных

| Критерий | A. Tracking (`ActivityStateRecord`) | B. ActivityInstance (`DebugInfo.GetLocals()`) |
|---|---|---|
| Что даёт | `record.Variables` + `record.Arguments` — снимки значений на момент события | Живые `Location` (можно читать и, потенциально, редактировать) |
| Точность scope | Переменные, доступные активности (profile уже настроен `Variables = {"*"}`) | Полный scope через `RuntimeVariables`/`ImplementationVariables`/`RuntimeArguments` + скрытие теней |
| `this`/ActivityInstance | Недоступен (можно показать только `DisplayName`/`Id`) | Доступен как псевдопеременная `this` |
| Редактирование значений | Нет (только просмотр) | Есть (`LocalInfo.Location.Value`) |
| Сложность | Низкая: чиним `LocalsHelper`, встраиваемся в `TrackHandler` | Высокая: нужен доступ к internal `ActivityInstance`/`DebugInfo` через reflection либо встраивание `DebugController`/`StateManager` в прогон |
| Риски | Значения — снимок; при паузе не обновляются сами | Хрупко (reflection к internal runtime .NET 4.8), риск рассинхрона с текущей tracking-отладкой |
| Производительность | Быстро, данные уже извлечены трекером | Нужно обходить `LocationEnvironment` по требованию |
| Соответствие архитектуре | Полностью (текущая отладка на tracking) | Частично (нужен новый канал «runtime → UI») |

**Рекомендация:** идти по **варианту A** сейчас, но сразу спроектировать тонкий контракт
`ILocalsProvider`, чтобы вариант B можно было подключить позже без переделки UI. Это даёт
рабочую панель быстро и не закрывает путь к «настоящим» живым значениям.

## 3. Целевая архитектура (рекомендуемая)

```
TrackHandler.ShowDebug (точка останова)
        │
        ▼
ILocalsProvider.GetSnapshot(record)          ← подменяемый источник
        │   TrackingLocalsProvider (сейчас)
        │   ActivityInstanceLocalsProvider (позже, вариант B)
        ▼
LocalsSnapshot (IReadOnlyList<LocalVar>, ActivityDisplayName)
        │
        ▼
ValueInspector.BuildNodes(...)                ← чистый код без WPF, тестируемый
        │   классификация + ленивый разбор объектов/коллекций/таблиц
        ▼
LocalsModel : BaseModel (ObservableCollection<LocalsNode>)
        │
        ▼
LocalsControl (UserControl, AvalonDock anchorable)
        ├── TreeView: Name / Value / Type
        └── область визуализации выделенного узла:
            ├── Object  → дерево свойств/полей
            ├── Collection/Dictionary → список элементов
            └── Table (DataTable/DataView) → DataGrid
```

## 4. План задач (TDD, red → green → refactor)

### Этап 0. Фиксация плана/беклога
- Дополнить этот документ разделом «Журнал изменений» и беклогом варианта B.
- Зафиксировать дату заведения и решение по месту хранения контракта `ILocalsProvider`.

### Этап 1. Чистая модель данных и инспектор значений (без UI)
1. Переписать `ActivitiesTracker\Core\LocalsHelper.cs`: корректно собирать `List<LocalVar>`
   из `record.Variables` + `record.Arguments`; убрать нерабочий цикл `do/while` и
   `CreateInstance<ActivityInfo>`.
2. Добавить в `ActivitiesTracker` контракт `ILocalsProvider` + `LocalsSnapshot`
   (или разместить контракт в `RPA.Shared`/`AiRpaStudio` — решить по слоям; сам `LocalVar`
   оставить в `ActivitiesTracker`, т.к. это net472-библиотека без WPF).
3. В `AiRpaStudio` создать `Locals\LocalsNode.cs`: `Name`, `DisplayValue`, `TypeName`,
   `Kind` (Scalar/Object/Collection/Dictionary/Table/Null/Error), `Children`,
   `IsExpandable`, `IsExpanded`, ленивая загрузка.
4. Создать `AiRpaStudio\Locals\ValueInspector.cs`:
   - классификация значений (`null`, примитивы, `string`, `enum`, `DateTime`,
     `IEnumerable`, `IDictionary`, `DataTable`/`DataView`, прочие объекты);
   - разбор объектов через reflection (public свойства + поля), с защитой от циклов
     (`HashSet<object>` по reference), ограничениями `MaxDepth`/`MaxItems`;
   - разбор коллекций (по индексам) и словарей (Key/Value);
   - построение табличной модели для `DataTable`/`DataView`.
5. Тесты: `AiRpaStudio.Tests\Locals\ValueInspectorTests.cs` — классификация, объекты,
   коллекции, словари, `DataTable`, циклы ссылок, `null`, исключения при чтении свойства,
   лимиты.

### Этап 2. Модель панели и WPF-контрол
6. `AiRpaStudio\ViewModels` или `Models`: `LocalsModel : BaseModel` —
   `ObservableCollection<LocalsNode> Roots`, `SelectedNode`, `ActivityDisplayName`,
   `Update(snapshot)`, `Clear()`.
7. `AiRpaStudio\Controls\LocalsControl.xaml(.cs)`:
   - верх — `TreeView` с иерархическим шаблоном (колонки Name/Value/Type), ленивое раскрытие;
   - нижняя область — `DataTemplateSelector` по `Kind`: таблица (`DataGrid`) для
     `DataTable`/`DataView`, дерево для объектов/коллекций;
   - тема строго по правилам студии: только `StudioPalette`, шрифты `Roboto`
     (`StudioFonts`), никаких зашитых цветов/`Consolas`/`Segoe UI`.
8. Тесты: `LocalsModelTests` (Update/Clear/SelectedNode); smoke-стражи XAML контрола по
   аналогии с существующими (`StudioFontTests`, `StudioPaletteTests`, проверка отсутствия
   зашитых цветов).

### Этап 3. Интеграция в docking-раскладку
9. `MainView.xaml.cs`:
   - в `InitializeControls` создать `LocalsControl` + `Border` (`localsBorder`);
   - в `PrepareLayout` добавить ветку `contentId == "Locals"`;
   - в `CreateDefaultLayout` добавить `LayoutAnchorable` `Locals`
     (ContentId `"Locals"`, `Title` из ресурса), разместить в правой группе
     (рядом с Properties/Log/Chat).
10. Локализация: ключ `lang_main_locals` в `Main.resx` и `Main.ru-RU.resx`.
11. Меню: пункт «Локальные переменные» (раздел Tools или отдельный View) →
    `ShowLocalsPanel()` по аналогии с `ShowNuGetPanel()`/`ActivateChatPanel()`.
12. Тесты/стражи: обновить/добавить стражи раскладки и локализации (по образцу
    существующих тестов панелей), проверить, что десериализация сохранённой раскладки
    восстанавливает панель по `ContentId`.

### Этап 4. Привязка к точке останова
13. В `TrackHandler.ShowDebug(...)` при `isBreakpointHit` (после установки
    `CurrentLocation`, до `WaitOne()` или сразу после) через `Dispatcher` вызвать
    `LocalsModel.Update(provider.GetSnapshot(trackingEventArgs.Record))`.
14. Очистка панели в моменты, где уже чистится подсветка: `ActivityRunner`
    (старт/продолжение/завершение/стоп) — по аналогии с
    `ClearCurrentLocationOnAllDesigners()`.
15. Тесты: `TrackHandlerLocalsTests` (при попадании в точку останова провайдер вызван и
    модель обновлена), `ActivityRunnerLocalsClearTests` (панель очищается при
    продолжении/стопе/завершении).

### Этап 5 (опционально, вариант B). Провайдер из `ActivityInstance`
16. Реализовать `ActivityInstanceLocalsProvider` через reflection к
    `System.Activities.Debugger.DebugInfo`/`ActivityInstance` (по reference source
    `DebugInfo.GetLocals()`), если получится штатно/через расширение достать
    `ActivityInstance` из `WorkflowApplication`.
17. Если штатного доступа нет — зафиксировать как беклог-карточку (встраивание
    `DebugController`/`StateManager` в прогон), не блокируя вариант A.
18. Тесты сравнения `TrackingLocalsProvider` vs `ActivityInstanceLocalsProvider` на
    эталонном роботе (`RPA-1`/тестовый XAML с переменными, коллекцией и `DataTable`).

### Этап 6. Сборка и приёмка
19. `MSBuild AiRpa.sln /p:Configuration=Release` из VS 2022 и VS 2026 → 0 ошибок.
20. `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll` →
    все тесты зелёные.
21. Ручной чеклист: открыть робота с переменными (примитив, объект, `List<T>`/`Dictionary`,
    `DataTable`), поставить точку останова, запустить — панель Locals показывает переменные;
    объект/коллекция раскрываются; `DataTable` отображается таблицей; при Continue/Stop
    панель очищается; тема Light/Dark корректна.

## 5. Риски и открытые вопросы
- `record.Variables` — это **снимок** на момент трекинг-события; редактирование значений в
  варианте A не предусмотрено (для редактирования нужен вариант B).
- Решение (зафиксировано 2026-11-21): контракт `ILocalsProvider` и `LocalsSnapshot`
  хранятся в `ActivitiesTracker` (net472, без WPF) рядом с `LocalVar`; инспектор/узлы/UI —
  в `AiRpaStudio`.
- `DataTable` доступен в .NET 4.8 (`System.Data`), проблем с TFM студии нет.
- Для больших коллекций обязательны лимиты (`MaxItems`/`MaxDepth`), иначе раскрытие может
  подвесить UI.

## 6. Журнал изменений

| Дата | Что сделано |
|---|---|
| 2026-11-21 | Заведён план. Код не изменялся. Зафиксирован выбор варианта A (tracking) как базового с контрактом `ILocalsProvider` для будущего перехода на вариант B (ActivityInstance). |
| 2026-11-21 | Этап 0 выполнен: зафиксированы дата заведения (2026-11-21) и решение по месту хранения контракта `ILocalsProvider`; добавлен беклог варианта B (раздел 7). |
| 2026-11-21 | Этап 1 выполнен: переписан `LocalsHelper` (сбор `LocalVar` из `Variables` + `Arguments`), добавлены `ILocalsProvider`/`LocalsSnapshot`/`TrackingLocalsProvider` в `ActivitiesTracker`; в `AiRpaStudio` созданы `LocalsNode`/`LocalsNodeKind`/`ValueInspector`; добавлены тесты `ValueInspectorTests` и `TrackingLocalsProviderTests` (20 тестов зелёные). |
| 2026-11-21 | Этап 2 выполнен: добавлены `LocalsModel : BaseModel` (`Roots`/`SelectedNode`/`ActivityDisplayName`, `Update`/`Clear`), контрол `LocalsControl` (TreeView Name/Value/Type + область деталей через `LocalsNodeTemplateSelector`: дерево для объектов/коллекций/словарей, `DataGrid` для `DataTable`/`DataView`), в `LocalsNode` добавлено поле `Value`; тесты `LocalsModelTests` и `LocalsControlThemeTests` (все Locals-тесты 30 зелёные). |
| 2026-11-21 | Этап 3 выполнен: в `MainView.xaml.cs` созданы `LocalsControl` + `Border` (`localsBorder`), добавлена ветка восстановления `contentId == "Locals"` в `PrepareLayout` и `LayoutAnchorable` `Locals` в правую группу `CreateDefaultLayout`; добавлены метод `ShowLocalsPanel()` и пункт меню Tools → «Локальные переменные»; ключ `lang_main_locals` в `Main.resx`/`Main.ru-RU.resx`. Тесты `LocalsPanelLayoutTests` (6) зелёные; Release `AiRpaStudio.csproj` — 0 ошибок. |
| 2026-11-21 | Этап 4 выполнен: в `TrackHandler.ShowDebug(...)` при `isBreakpointHit` на UI-потоке вызывается `LocalsModel.Update(LocalsProvider.GetSnapshot(record))` (вариант A, `TrackingLocalsProvider`); доступ к модели — через `CustomWfDesigner.LocalsModel`/`MainView.LocalsModel`. В `ActivityRunner` добавлена `ClearLocalsPanel()` и очистка панели при старте/продолжении/завершении/остановке рядом с `ClearCurrentLocationOnAllDesigners()`. Тесты `TrackHandlerLocalsTests` (3) и `ActivityRunnerLocalsClearTests` (6) зелёные; связка Locals-тестов + стражи подсветки (50) зелёная; Release `AiRpa.sln` из VS 2022 и VS 2026 — 0 ошибок. |
| 2026-11-21 | Этап 5 (вариант B) выполнен: добавлен `ActivityInstanceLocalsProvider` в `ActivitiesTracker` — живой `ActivityInstance` извлекается из internal-свойства `ActivityInfo.Instance` трекинг-записи (штатный доступ к `WorkflowApplication` не нужен), значения читаются через reflection-вызов internal `System.Activities.Debugger.DebugInfo.GetLocals()` с учётом затенения имён и псевдопеременной `this`. Тесты `ActivityInstanceLocalsProviderTests` (5) зелёные, в т.ч. сравнение `TrackingLocalsProvider` vs `ActivityInstanceLocalsProvider` (примитив/объект/коллекция/`DataTable`); связка Locals-тестов (50) зелёная; Release `AiRpa.sln` из VS 2022 и VS 2026 — 0 ошибок. |
| 2026-11-21 | Этап 8 (вариант A, полный видимый scope) выполнен: добавлен `LocalsScopeTracker` (стек скоупов по `Executing`/`Closed`/`Canceled`/`Faulted`, обновление значений по `In`/`Out`-аргументам дочерних активностей), `TrackingLocalsProvider` переведён на `GetVisibleLocals`, `CustomTrackingParticipant` вызывает `Observe(record)` до формирования `TrackingEventArgs`, `ActivityRunner` вызывает `Reset` на границах прогона (кроме resume). Тесты `LocalsScopeTrackerTests` (10) и вся связка Locals-тестов (76) зелёные; Release `AiRpaStudio.csproj` — 0 ошибок. |


## 7. Беклог варианта B (ActivityInstance / живые значения)

Не блокирует вариант A; берётся после закрытия Этапов 1–4.

| ID | Карточка | Критерий готовности |
|---|---|---|
| B-1 | ✅ Исследован доступ к `ActivityInstance` (2026-11-21): штатного public-API у `WorkflowApplication` нет, но internal-свойство `ActivityInfo.Instance` в `ActivityStateRecord` уже хранит живой `ActivityInstance`; internal `System.Activities.Debugger.DebugInfo.GetLocals()` вызывается через reflection. Отдельный доступ к `WorkflowApplication`/`DebugController` не нужен. | Прототип `ActivityInstanceLocalsProviderTests` достаёт `ActivityInstance` из трекинг-записи и читает живые `Location`. |
| B-2 | ✅ Реализован `ActivityInstanceLocalsProvider : ILocalsProvider` (`ActivitiesTracker`): `GetSnapshot(record)` через reflection к `DebugInfo.GetLocals()` — живые `Location` (`RuntimeVariables`/`ImplementationVariables`/`RuntimeArguments`/`DelegateArguments`), затенение имён и псевдопеременная `this`. | `ActivityInstanceLocalsProviderTests` (5) зелёные: живые значения, `this`, сравнение с `TrackingLocalsProvider`. |
| B-3 | Редактирование значений в панели Locals через `LocalInfo.Location.Value` (переход от просмотра к правке). | Есть UI-операция правки + тест, значение в рантайме меняется. |
| B-4 | ✅ Сравнительный тест `GetSnapshot_MatchesTrackingScope_AndAddsThis` на кодовом эталонном workflow (примитив, объект, коллекция, `DataTable`); расхождение задокументировано: `TrackingLocalsProvider` с `Variables = {"*"}` берёт только переменные текущей активности, а `ActivityInstanceLocalsProvider` — полный scope предков + `this`. | Тест зелёный. |
| B-5 | ✅ Не потребовался: штатный путь найден — `ActivityInfo.Instance` + reflection к `DebugInfo.GetLocals()`. Fallback (`DebugController`/`StateManager`) остаётся только запасным вариантом и не реализуется. | Решение зафиксировано в этом документе; вариант A не блокировался. |

## 8. План (вариант A): полный видимый scope через запоминание в трекинге

> Статус: реализовано и покрыто тестами. Вариант B (`ActivityInstanceLocalsProvider` / `DebugInfo`) не трогается.

### 8.1 Причина

`CustomTrackingParticipant` использует `Variables = {"*"}`. В WF4 `ActivityStateRecord.GetVariables()`
при wildcard `*` берёт **только переменные текущей активности**, а не родительских скоупов:

```csharp
// reference source: ActivityStateRecord.GetVariables()
// * only extracts variables of the current Activity and not variables in scope.
bool useWildCard = containsWildcard && startActivity == currentElement;
```

Поэтому в точке останова на вложенной активности `TrackingLocalsProvider` видит только её scope,
а переменные родителя (Sequence/корень) не отображаются. Требование: панель должна показывать и
текущий scope, и все видимые scope иерархией выше.

### 8.2 Алгоритм запоминания

Вести **стек активных scope** прямо в трекинге:

- `Executing` активности → положить на стек frame с её снимком `Variables` + `Arguments`;
- `Closed` / `Canceled` / `Faulted` → снять соответствующий frame со стека;
- на каждом `ActivityStateRecord` дочерней активности дополнительно обновлять значение
  переменной скоупа по входным (`In`) и выходным (`Out`) значениям её аргументов, привязанных
  к этой переменной (см. 8.3) — снимок родителя не устаревает после `Assign`/вызова вложенной
  активности;
- в точке останова → собрать видимый scope, идя от вершины стека (текущая активность) к корню:
  первое встреченное имя фиксируется (ближайший scope побеждает), дальние одноимённые пропускаются.

Frame идентифицируется по `record.Activity.InstanceId` (у `ActivityInfo` это `instanceId`),
ключ стека — пара `(InstanceId, State)`, чтобы симметрично снимать по тому же инстансу.

### 8.3 Обновление значений по входным/выходным аргументам дочерних активностей

Трекер обязан отслеживать значение переменной в scope не только как разовый снимок на `Executing`
родителя, но и по входным/выходным значениям во всех дочерних активностях:

- `Executing` дочерней активности → её `In`-аргументы несут входные значения переменных;
- `Closed` (и `Faulted`/`Canceled`, если есть выход) → `Out`/`InOut`-аргументы несут выходные
  значения переменных;
- сопоставление «аргумент → переменная» выполняется по привязке аргумента к переменной скоупа
  (в `CustomTrackingParticipant` активность уже достаётся существующей reflection-обёрткой
  `activityStateRecord.Activity.GetProperty<Activity>("Activity")`, без `DebugInfo`);
- обновляется frame именно того скоупа, которому принадлежит переменная; при одноимённых
  переменных обновляется ближайший видимый (тот, который побеждает при затенении).

Это устраняет устаревание снимка: значение родительской переменной перезаписывается свежим
значением из аргументов ребёнка на каждом шаге. Реализуемо и проверено на практике.

Остаточное ограничение (принято): если переменная меняется внутри активности без привязки через
аргумент (например, прямой записью в `Location`), трекинг не увидит изменение до тех пор, пока
оно не проявится в аргументе. Для просмотра в точках останова это приемлемо; `this` и живое
редактирование остаются за вариантом B и не входят в этот план.

### 8.4 Изменения

1. **Новый файл `ActivitiesTracker/Core/LocalsScopeTracker.cs`** (`public static`):
   - `Observe(ActivityStateRecord record)` — push (`Executing`) / pop (`Closed`/`Canceled`/`Faulted`),
     плюс обновление значения переменной скоупа из `In`/`Out`-аргументов записи по привязке
     «аргумент → переменная» (см. 8.3);
   - `Reset()` — очистка стека (вызывается ActivityRunner на границах прогона);
   - `GetVisibleLocals(ActivityStateRecord record)` — сбор видимого scope от вершины стека с
     затенением имён; fallback на `LocalsHelper.GetLocalsAndArgs(record)`, если текущая запись
     не была в стеке (сохраняет обратную совместимость);
   - потокобезопасность через `lock`; frame хранит `InstanceId`, `Name`, снимок `Variables`
     и отфильтрованные `Arguments` (`LocalsHelper.GetArgsFromRecord`).

2. **`ActivitiesTracker/Core/TrackingLocalsProvider.cs`**:
   `GetSnapshot(record)` берёт `LocalsScopeTracker.GetVisibleLocals(record)` вместо прямого
   `LocalsHelper.GetLocalsAndArgs(record)`.

3. **`ActivitiesTracker/Core/CustomTrackingParticipant.cs`**:
   в ветке `ActivityStateRecord` (после отсечки `Logger`) вызвать `LocalsScopeTracker.Observe(record)`
   до формирования `TrackingEventArgs` — к моменту `ShowDebug` стек уже актуален. Запись уже
   содержит `Arguments` и активность (`ActivityInfo.Activity` через существующий
   `GetProperty<Activity>("Activity")`), поэтому `Observe` обновляет значения по входным/выходным
   аргументам без обращения к варианту B.

4. **`AiRpaStudio/Core/ActivityRunner.cs`**:
   вызвать `LocalsScopeTracker.Reset()` в `Start(...)`, `OnCompleted`, `OnAborted`, `WorkflowStop`
   и catch-ветках — рядом с `ClearLocalsPanel()`. В ветке `resume` (`_resumeRuntimeFromHost?.Set()`)
   Reset **не** вызываем: стек сохраняется, чтобы родительский scope не потерялся после продолжения.

5. **`ActivitiesTracker/ActivitiesTracker.csproj`**:
   добавить `<Compile Include="Core\LocalsScopeTracker.cs" />`.

Вариант B (`ActivityInstanceLocalsProvider`) и `MixedLocalsProvider` не меняются. Фолбэк
`MixedLocalsProvider` на `TrackingLocalsProvider` автоматически получает тот же стек.

### 8.5 Тесты (TDD, red → green)

Новый файл `AiRpaStudio.Tests/Locals/LocalsScopeTrackerTests.cs` (SDK-проект, подхватывается
автоматически):

1. `TrackingProvider_WithParentAndChild_ReturnsBothScopes` — `Observe(parent Executing)` →
   `Observe(child Executing)` → `GetSnapshot(child)`; видны переменная родителя (`Scope == "Parent"`)
   и переменная ребёнка (`Scope == "Child"`).
2. `TrackingProvider_Shadowing_NearestScopeWins` — одноимённая переменная у родителя и ребёнка →
   видно значение ребёнка (родительская затенена).
3. `TrackingProvider_ClosedChild_KeepsParentOnly` — после `Closed` ребёнка снимок родителя
   не содержит переменные ребёнка.
4. `TrackingProvider_Faulted_RemovesScope` — `Faulted` тоже снимает frame.
5. `TrackingProvider_WithoutObserve_FallsBackToRecord` — изоляция/обратная совместимость.
6. `Reset_ClearsObservedScopes` — после `Reset()` старый стек не виден.
7. `TrackingProvider_ChildArgument_RefreshesParentVariable` — после `Observe(child Closed)` с
   `Out`-аргументом, привязанным к родительской переменной, `GetSnapshot(parent)` отдаёт
   обновлённое (выходное) значение.

Source-стражи (по образцу `ActivityRunnerLocalsClearTests`):
- `CustomTrackingParticipant` вызывает `LocalsScopeTracker.Observe`;
- `ActivityRunner` вызывает `LocalsScopeTracker.Reset` в `Start`/`OnCompleted`/`OnAborted`/`WorkflowStop`
  и **не** вызывает в ветке `resume`.

### 8.6 Приёмка

- `MSBuild AiRpa.sln /p:Configuration=Release` → 0 ошибок (VS 2022 и VS 2026);
- `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll` → все Locals-тесты
  и полный прогон зелёные;
- ручной чек: переменная объявлена на `Sequence`, точка останова на вложенной активности → в панели
  видны и родительская, и текущая переменные во всех режимах (Tracking / ActivityInstance / Mixed).

### 8.7 Дальнейшее развитие

Тот же стек scope будет использован для будущего логирования каждого шага прогона со значениями
переменных: сообщение шага (`Executing`/`Closed`) дополняется снимком видимых переменных из
`LocalsScopeTracker`. Это отдельная карточка, не входит в текущий этап.
