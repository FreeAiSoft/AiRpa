# План корректировки области видимости переменных и аргументов панели Locals

> Статус: аналитический этап. Код НЕ изменялся.
> Документ фиксирует правильную модель scope WF4 по reference source, разбирает три бага
> панели «Локальные переменные» и даёт план исправления (TDD, red → green → refactor).

| Поле | Значение |
|---|---|
| Документ | `dev-docs\Locals_Scope_Correctness_Plan.md` |
| Цель | Исправить scope переменных/аргументов в панели Locals при остановке на точке останова |
| Reference source | `C:\Users\Maxxx\source\repos\.NET-Framework-4.8-RTM-Reference-Source` |
| Связанные документы | `dev-docs\Locals_Panel_Plan.md` (базовая панель), `dev-docs\LooseWorkflowRun_Plan.md` (точки останова) |
| Правило приёмки | каждый шаг закрывается тестом; полный зелёный прогон `AiRpaStudio.Tests`; Release `AiRpa.sln` из VS 2022/VS 2026; production-правки — только по TDD |
| Заведён | 2026-11-21 |

---

## 1. Где лежит правильный исходник

Важная поправка к путям: `ndp\cdf\src\WF` — это **WF 3.x / System.Workflow**, а роботы
студии (`Main.xaml`, `SubProcess1.xaml`) работают на **WF4 / System.Activities**.

Актуальные исходники WF4:

```
ndp\cdf\src\NetFx40\System.Activities\System\Activities\
  Activity.cs                       — RuntimeVariables / ImplementationVariables / RuntimeArguments,
                                      PublicEnvironment / ImplementationEnvironment,
                                      RelationshipToParent, GetParentEnvironment()
  Variable.cs                       — Variable (RuntimeVariables) и Variable<T>
  Argument.cs / RuntimeArgument.cs  — аргументы и их привязка
  LocationReference.cs              — базовый LocationReference (Name/Type/Id)
  ActivityLocationReferenceEnvironment.cs — design-time scope (PublicEnvironment/ImplementationEnvironment)
  Runtime\LocationEnvironment.cs    — runtime scope: цепочка LocationEnvironment и TryGetLocation(id, owner)
  Runtime\ActivityExecutor.cs       — порядок ResolveArguments → ResolveVariables → SetInitializedSubstate
  ActivityInstance.cs               — Environment, Parent, Initialize (создание runtime environment)
  Tracking\ActivityStateRecord.cs   — семантика Variables/Arguments при трекинге
  Tracking\RuntimeTrackingProfile.cs — извлечение Variables/Arguments по профилю
  Debugger\DebugInfo.cs             — штатный (но не идеальный) механизм DebugInfo.GetLocals()/GetArguments()
  Debugger\DebugManager.cs          — GenerateLocals(instance) → только "debugInfo"
```

---

## 2. Модель scope WF4 (ключевые нюансы)

### 2.1. Три категории объявлений

| Категория | Тип | Где хранится на Activity | Видимость |
|---|---|---|---|
| Публичные переменные | `Variable` (`Variable<T>`) | `Activity.RuntimeVariables` | видимы публичным потомкам в той же публичной цепочке |
| Приватные переменные | `Variable` | `Activity.ImplementationVariables` | видимы только внутри implementation (тела) активности |
| Аргументы | `RuntimeArgument` (обёртка над `Argument`) | `Activity.RuntimeArguments` | всегда «свои» для активности и видимы её implementation-потомкам |
| Аргументы делегата | `RuntimeDelegateArgument` | `ActivityDelegate.RuntimeDelegateArguments` | видимы внутри обработчика делегата (`HandlerOf`) |

Порядок id в runtime-окружении (см. `LocationEnvironment.Update` и `ActivityUtilities.ProcessActivity`):

```
[ arguments | public variables | private variables | RuntimeDelegateArguments ]
```

`Activity.SymbolCount = arguments.Count + variables.Count + implementationVariables.Count`
(+ делегатные аргументы через отдельный `delegateParameterCount` при создании инстанса).

### 2.2. Design-time scope: PublicEnvironment и ImplementationEnvironment

В `ActivityUtilities.ProcessActivity` (кеширование метаданных):

- аргументы объявляются в `newImplementationEnvironment`;
- `RuntimeVariables` (public) — в `newPublicEnvironment`;
- `ImplementationVariables` — в `newImplementationEnvironment`;
- делегатные аргументы — тоже в implementation-окружении обработчика.

`Activity.GetParentEnvironment()` задаёт, что видит потомок:

```csharp
case RelationshipType.ImplementationChild:
    parentEnvironment = this.Parent.ImplementationEnvironment;   // аргументы + implementation-переменные
case RelationshipType.Child:
case RelationshipType.ImportedChild:
case RelationshipType.VariableDefault:
    parentEnvironment = this.Parent.PublicEnvironment;           // ТОЛЬКО public-переменные
case RelationshipType.DelegateHandler:
    parentEnvironment = this.HandlerOf.Environment;
case RelationshipType.ArgumentExpression:
    parentEnvironment = this.Parent.PublicEnvironment.Parent;
```

**Главный нюанс:** `ImplementationEnvironment` создаётся с parent = `activity.HostEnvironment`
(см. `ActivityUtilities.ProcessActivity`), то есть implementation-цепочка **не продолжается**
в публичный scope родителя. Поэтому тело вложенной XAML-активности (`SubProcess1.xaml`) не видит
публичные переменные `Main.xaml` — только аргументы и implementation-переменные самого
`SubProcess1`.

### 2.3. Runtime scope: цепочка LocationEnvironment

В рантайме у каждого инстанса активности, у которой есть символы, создаётся свой
`LocationEnvironment` с `Definition = activity` и `Parent = среда родительского инстанса`
(`ActivityInstance.Initialize`). Если символов нет (`SymbolCount == 0`), инстанс получает
`noSymbols = true` и переиспользует среду родителя.

Поиск значения переменной/аргумента выполняется не по имени, а по `id` и владельцу:

```csharp
// LocationEnvironment.TryGetLocation(int id, Activity environmentOwner, out Location value)
// поднимается по Parent, пока Definition != environmentOwner, затем читает слот по id.
```

`Variable.InternalGetLocation(environment)` и `RuntimeArgument.InternalGetLocation(environment)`
вызывают именно этот chained-поиск. Поэтому у отладчика есть способ дочитать значение из
родительского scope, даже если оно лежит в среде предка.

### 2.4. Контроль доступа в рантайме

`Variable.GetLocation(context)` / `RuntimeArgument.GetLocation(context)`:

- при `AllowChainedEnvironmentAccess == false` доступ разрешён только к «своим» объявлениям
  (owner == current activity), иначе бросается исключение;
- `AllowChainedEnvironmentAccess = true` включается только внутри вычисления выражений
  (`VariableValue`, `VariableReference`, `EnvironmentLocationValue` и т.п.).

Это значит: implementation-потомок **не может** штатно прочитать публичную переменную предка
через `ActivityContext`. Значения в него попадают только через аргументы.

### 2.5. Семантика трекинга

`ActivityStateRecord.GetVariables()` при `Variables = {"*"}`:

```csharp
// * only extracts variables of the current Activity and not variables in scope.
bool useWildCard = containsWildcard && startActivity == currentElement;
```

То есть wildcard `*` даёт только переменные **текущей** активности, а не весь видимый scope.
Поэтому текущий `LocalsScopeTracker` и вынужден вести стек скоупов.

`ActivityStateRecord.GetArguments()`:

```csharp
// OutArguments will always start with default(T), so there is no need to track them when state == Executing
if (isActivityStateExecuting && argument.Direction == ArgumentDirection.Out) continue;
```

То есть на `Executing` Out-аргументы **не попадают** в `record.Arguments`; на `Closed` попадают
все. Это одна из причин «не всегда ожидаемого» поведения аргументов.

### 2.6. Штатный DebugInfo.GetLocals() и его дефект

`DebugInfo.GetLocals()` собирает scope так:

```csharp
while (activity != null)
{
    allVariables.AddRange(RemoveHiddenVariables(existingNames, activity.RuntimeVariables));
    allVariables.AddRange(RemoveHiddenVariables(existingNames, activity.ImplementationVariables));
    if (activity.HandlerOf != null) { ... delegate arguments ... }
    allArguments.AddRange(RemoveHiddenArguments(existingNames, activity.RuntimeArguments));
    activity = activity.Parent;   // <-- линейный обход дерева activity
}
```

Он:

- скрывает переменные с именем, начинающимся на `_`;
- скрывает одноимённые дальние объявления (затенение);
- добавляет `this` (`ActivityInstance`);
- **не учитывает** `RelationshipToParent` и границу implementation/public.

Поэтому внутри `SubProcess1.xaml` он проходит `SubProcess2 → Sequence(SubProcess1) →
SubProcess1 → Sequence(Main) → Activity(Main)` и ошибочно показывает `output` из `Main.xaml`.
Это корень бага № 2.

`DebugInfo.GetArguments()` в отличие от `GetLocals()` возвращает **только** аргументы текущей
активности (`activityInstance.Activity.RuntimeArguments`), без предков, но все направления.

---

## 3. Ожидаемое поведение панели Locals

1. **Точка останова на активности `A`** показывает:
   - аргументы `A` (с направлением: `In` / `InOut` / `Out`);
   - публичные переменные `A` (`RuntimeVariables`);
   - implementation-переменные `A` (`ImplementationVariables`) — как в штатном `DebugInfo.GetLocals()`;
   - видимые предки **по правилам design-time scope**, а не по плоскому `Parent`;
   - `this` (для режима ActivityInstance).

2. **Внутри вложенной XAML-активности** (на активности в теле `SubProcess1`) показывает:
   - аргументы и implementation-переменные `SubProcess1`;
   - собственные объявления текущей активности;
   - **НЕ** показывает публичные переменные `Main.xaml` (`output`).

3. **Аргументы** должны отображаться всегда с учётом направления и состояния:
   - `Executing`: `In`/`InOut` имеют значения; `Out` ещё не присвоен (показывать как «не присвоен»
     либо default с пометкой направления);
   - `Closed`/`Faulted`/`Canceled`: `Out`/`InOut` показывают итоговое значение;
   - аргументы предков не должны «протекать» через implementation-границу.

---

## 4. Разбор трёх багов

### Баг 1. Точка останова на `SubProcess1` — `output` не отображается

**Причина (по текущему коду):** точка останова срабатывает не на настоящем
`ActivityStateRecord` активности, а на **синтетическом событии «Prestart»** из
`CustomTrackingParticipant.Track`:

```csharp
// CustomTrackingParticipant.Track — после реального ActivityStateRecord
if (nextActivity != null)
{
    TrackingEventArgsRecieved.Invoke(this, new TrackingEventArgs()
    {
        Message = $"Запуск {nextActivity.DisplayName}",
        Activity = nextActivity,
        ActivityState = ActivityState.Prestart
    });
}
```

У этого события `Record` не заполнен (`null`). В `TrackHandler.ShowDebug(...)` точка останова
проверяется по `SourceLocation`, а панель обновляется так:

```csharp
localsModel.Update(ResolveLocalsProvider().GetSnapshot(record)); // record == null
```

`GetSnapshot(null)` возвращает пустой снапшот. То есть панель пуста, хотя по scope `output`
должен быть виден (SubProcess1 — публичный потомок `Sequence`, объявившего `output`).

Дополнительный фактор: после срабатывания по Prestart реальный `Executing`-запись
`SubProcess1` уже не вызывает повторную остановку из-за защиты
`if (targetDesigner.LastBreakpoint != src)`. Поэтому пустой снапшот «залипает».

**Ожидаемый фикс:** точки останова и заполнение Locals привязывать только к реальному
`ActivityStateRecord` (Executing/Closed/Faulted), а синтетический Prestart использовать только
для подсветки/лога, без `ShowDebug`-остановки. Если Prestart остаётся для навигации — Locals
заполнять из текущего стека скоупов, а не из `record == null`.

### Баг 2. При входе в `SubProcess1.xaml` виден `output` из `Main.xaml`

**Причина:** `ActivityInstanceLocalsProvider` использует штатный `DebugInfo.GetLocals()`,
который идёт по `activity.Parent` без учёта `RelationshipToParent`/границы implementation
(см. 2.6). Поэтому из тела `SubProcess1` он доходит до `Sequence(Main)` и добавляет `output`.

**Ожидаемый фикс:** заменить линейный обход `Parent` на обход с учётом границы scope:

- если текущий потомок достиг родителя как `ImplementationChild` — добавить у родителя только
  `RuntimeArguments` + `ImplementationVariables` (+ делегатные аргументы, если есть `HandlerOf`)
  и **остановиться** (не подниматься к публичным предкам);
- если как публичный потомок (`Child`/`ImportedChild`/`VariableDefault`/`ArgumentExpression`) —
  добавить у родителя только `RuntimeVariables` и продолжить подъём;
- `DelegateHandler` — обрабатывать отдельно (аргументы делегата + окружение `HandlerOf`).

### Баг 3. Аргументы отображаются не всегда ожидаемо

**Причины (несколько источников):**

1. Трекинг-провайдер берёт аргументы из `record.Arguments`, а WF на `Executing` **пропускает
   Out-аргументы** (`ActivityStateRecord.GetArguments`, см. 2.5). Поэтому на точке останова
   в начале активности Out-аргументы отсутствуют.
2. `LocalsHelper.GetArgsFromRecord` дополнительно вырезает «стандартные» имена
   (`IsSafe`, `IsNeedTrace`, `IsEnabled`, `IsNeedLogging`) — это бизнес-фильтр, который может
   скрывать настоящие аргументы, если у активности есть такие же имена.
3. `ActivityInstanceLocalsProvider` (через `DebugInfo.GetLocals`) показывает аргументы **всех
   предков** и все направления без пометки направления/состояния; на `Executing` у Out-аргумента
   виден default, что выглядит как «не то значение».
4. `DebugInfo.GetArguments()` (штатный метод) возвращает только аргументы текущей активности,
   но текущий провайдер его не использует — используется `GetLocals()`, что смешивает аргументы
   и переменные и не различает направления.

**Ожидаемый фикс:** единая модель аргумента с направлением и состоянием; отображать аргументы
текущего scope (по правилам из раздела 2), с корректными значениями в зависимости от
`Executing`/`Closed`/`Faulted`/`Canceled`; не применять произвольный фильтр имён к аргументам.

---

## 5. Целевая архитектура

Один источник истины для scope — **`LocalsScopeResolver`** в `ActivitiesTracker` (net472, без
WPF), который по живому `ActivityInstance` (из internal `ActivityInfo.Instance`) и дереву
`Activity` строит корректный список видимых переменных/аргументов.

```
TrackHandler.ShowDebug(реальный ActivityStateRecord)
        │
        ▼
ILocalsProvider.GetSnapshot(record)
        │   MixedLocalsProvider (по умолчанию)
        ▼
ActivityInstanceLocalsProvider
        │   ActivityInfo.Instance → ActivityInstance
        ▼
LocalsScopeResolver.Resolve(instance, record.State)
        │   правильный обход с учётом RelationshipToParent / границы implementation
        ▼
LocalsSnapshot (LocalVar + метаданные: Kind = Variable|Argument|This, Direction, State)
        │
        ▼
LocalsModel / ValueInspector (без изменений в UI, только расширение модели)
```

### 5.1. Алгоритм `LocalsScopeResolver.Resolve`

Псевдокод (без реализации сейчас):

```
Resolve(ActivityInstance instance, string state):
    result = []
    seen = HashSet<string>()

    // 1. Собственные объявления текущей активности
    AddArguments(current.Activity, direction-aware, state)
    AddPublicVariables(current.Activity)
    AddImplementationVariables(current.Activity)   // как в DebugInfo.GetLocals()
    if current.Activity.HandlerOf != null: AddDelegateArguments(current.Activity.HandlerOf)

    // 2. Подъём по дереву с учётом границ
    child = current.Activity
    parent = child.Parent
    while parent != null:
        rel = child.RelationshipToParent   // internal, доступ через reflection
        if rel == ImplementationChild:
            AddArguments(parent, direction-aware, state)
            AddImplementationVariables(parent)
            if parent.HandlerOf != null: AddDelegateArguments(parent.HandlerOf)
            break                          // implementation-цепочка запечатана от публичных предков
        else if rel == DelegateHandler:
            AddDelegateArguments(child.HandlerOf)
            // продолжить по окружению делегата (отдельный кейс с тестом)
        else: // Child, ImportedChild, VariableDefault, ArgumentExpression
            AddPublicVariables(parent)
        child = parent
        parent = parent.Parent

    // 3. this (для режима ActivityInstance)
    AddThis(instance)

    // 4. затенение имён: ближайший scope побеждает; "_" — скрыть
    return ShadowAndSort(result)
```

Чтение значений — через internal `Variable.InternalGetLocation(instance.Environment)` и
`RuntimeArgument.InternalGetLocation(instance.Environment)` (chained-поиск по id + owner).

### 5.2. Модель аргументов

Добавить к `LocalVar` (или отдельной сущности) поля:

- `Kind`: `Variable | Argument | DelegateArgument | This`;
- `Direction`: `In | InOut | Out` (для аргументов);
- `ActivityState` (для семантики Out-на-Executing).

Правила отображения:

| State | In | InOut | Out |
|---|---|---|---|
| Executing | значение | значение | «не присвоен» / default + пометка |
| Closed/Faulted/Canceled | значение | итоговое значение | итоговое значение |

---

## 6. План задач (TDD, red → green → refactor)

> Порядок: сначала тесты, затем минимальная реализация. Код сейчас не трогаем.

### Этап 1. Зафиксировать правильную модель scope тестами
- `LocalsScopeResolverTests` на эталонных workflow (code-first), которые воспроизводят:
  - публичная переменная видна публичному потомку;
  - публичная переменная **не видна** implementation-потомку вложенной активности;
  - аргументы вложенной активности видны её implementation-потомку;
  - `this` присутствует;
  - затенение имён и скрытие `_`-переменных.

### Этап 2. Реализовать `LocalsScopeResolver`
- reflection-доступ к internal: `Activity.RelationshipToParent`, `RuntimeVariables`,
  `ImplementationVariables`, `RuntimeArguments`, `HandlerOf`, `Variable.InternalGetLocation`,
  `RuntimeArgument.InternalGetLocation`.
- обход по псевдокоду 5.1.
- юнит-тесты на каждое правило границы (public vs implementation vs delegate).

### Этап 3. Перевести `ActivityInstanceLocalsProvider` на resolver
- убрать вызов `DebugInfo.GetLocals()` (или оставить только как fallback);
- `GetSnapshot(record)` = `LocalsScopeResolver.Resolve(instance, record.State)`.
- тест: внутри вложенной XAML-активности `output` из `Main.xaml` **отсутствует**, аргументы
  вложенной активности присутствуют.

### Этап 4. Исправить точку останова (баг 1)
- `TrackHandler.ShowDebug`/`CustomTrackingParticipant.Track`: не останавливаться и не заполнять
  Locals на синтетическом `Prestart`; использовать реальный `ActivityStateRecord`.
- тест `TrackHandlerLocalsTests`: на Prestart-событии `GetSnapshot` не вызывается/не пустой;
  на реальном `Executing` вызывается с непустым record.
- тест-страж: в `CustomTrackingParticipant` Prestart-ветка не передаёт `Record` в
  `ShowDebug`-путь останова.

### Этап 5. Унифицировать аргументы (баг 3)
- расширить `LocalVar`/снапшот метаданными `Kind`/`Direction`/`State`;
- в resolver формировать аргументы по правилам 5.2;
- убрать/изолировать фильтр `StandardArgumentNames` (перенести в явную настройку, не применять
  к настоящим аргументам по умолчанию);
- тесты: `Out` на Executing помечен «не присвоен», `Out` на Closed показывает значение,
  `In` не пропадает на Closed, аргументы предков не протекают через границу.

### Этап 6. Привести `LocalsScopeTracker` к правилам границ (только трекинг)
- fallback `TrackingLocalsProvider` на `record.Variables`/`record.Arguments` пока не вводим;
- `LocalsScopeTracker` привести к тем же правилам границ, что и resolver:
  implementation-граница не пропускает `output` из `Main.xaml` внутрь вложенной
  XAML-активности;
- тест: `LocalsScopeTracker` не протекает `output` внутрь вложенной XAML-активности.

### Этап 7. Приёмка
- `MSBuild AiRpa.sln /p:Configuration=Release` из VS 2022 и VS 2026 → 0 ошибок;
- `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll` → все Locals-тесты
  зелёные;
- ручной чек-лист на `RPA-1\Main.xaml` + `SubProcess1.xaml`:
  1) точка останова на `SubProcess1` → `output` виден;
  2) вход в `SubProcess1.xaml` → `output` из Main не виден, видны только аргументы
     `SubProcess1` (если есть) и локальные объявления тела;
  3) аргументы отображаются с направлением и корректным значением по состоянию.

---

## 7. Риски и открытые вопросы

1. **Reflection к internal `RelationshipToParent`/`InternalGetLocation`** — стабильно в .NET
   4.8, но нужен негативный тест на недоступность (fallback без падения отладки).
2. **Implementation-переменные на точке останова в самой активности** — штатный
   `DebugInfo.GetLocals()` их показывает; оставляем поведение, но фиксируем тестом, чтобы не
   сломать.
3. **DelegateHandler** — редкий кейс; нужен отдельный эталонный тест, иначе возможна регрессия
   в обработчиках `ActivityAction`/`ActivityFunc`.
4. **Синтетический Prestart** используется для подсветки «следующей активности»; при переносе
   останова на реальный record нужно сохранить навигацию/подсветку без потери UX.
5. **Снимок vs живые значения** — основной режим остаётся Mixed (ActivityInstance → Tracking);
   после перехода resolver на живые значения нужно проверить, что значения не «уезжают» при
   паузе (живые `Location` читаются в момент заполнения панели).
