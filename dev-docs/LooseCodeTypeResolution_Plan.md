> Статус: canonical
# Резолв CLR-типов code-behind в loose-XAML без сборки проекта — план и журнал

| Поле | Значение |
|---|---|
| Документ | `dev-docs\LooseCodeTypeResolution_Plan.md` — план реализации резолва `TargetType="local:Code"` и других ссылок на C#-типы проекта в loose-дизайнере без сборки и без компиляции XAML |
| Связанные планы | `dev-docs\LooseXamlTypeService_Plan.md` — loose-открытие/сохранение вложенных XAML-процессов; `dev-docs\LooseWorkflowRun_Plan.md` — прогон WF-проекта по собранной сборке |
| Цель | Визуальный конструктор WF не падает при открытии `RPA-1\CodeTest.xaml`, где `<InvokeMethod MethodName="Run" TargetType="local:Code" />` ссылается на CLR-тип из `RPA-1\Code.cs` (`public class Code`), при том что сборка `RPA-1.dll` не загружена, а XAML-файлы компилировать по-прежнему нельзя (тяжёлая компиляция, обход уже найден и реализован фасадами) |
| Дата заведения | 07.02.2026 |
| Правило приёмки | каждый шаг закрывается тестом (включая негатив), полным зелёным прогоном `AiRpaStudio.Tests` и сборкой `AiRpa.sln` Release из VS 2022 и VS 2026; production-правки — только по TDD (red → green → refactor) |

## 1. Контекст и причина

### 1.1. Что ломается

`RPA-1\CodeTest.xaml:29`:

```xml
<InvokeMethod sap2010:WorkflowViewState.IdRef="InvokeMethod_1"
              MethodName="Run"
              TargetType="local:Code" />
```

Корень документа объявляет:

```xml
xmlns:local="clr-namespace:RPA_1"
```

а `local:Code` — это `RPA_1.Code` из `RPA-1\Code.cs:9`:

```csharp
namespace RPA_1
{
    public class Code
    {
        public static void Run()
        {
        }
    }
}
```

При открытии документа в loose-режиме `LooseActivityXamlSchemaContext.GetXamlType("clr-namespace:RPA_1", "Code")`
не находит тип: он умеет резолвить только зарегистрированные XAML-фасады (`SubProcessN` → `DynamicActivity`),
а для `Code` в `WorkflowTypeRegistry` ничего нет. Вызов уходит в `base.GetXamlType(...)`, а `clr-namespace:RPA_1`
**без** `;assembly=` не резолвится штатным `XamlSchemaContext`, потому что сборка `RPA-1.dll` в loose-режиме
не загружена. Итог — исключение при загрузке и падение визуального конструктора.

В Visual Studio 2026 тот же XAML открывается, потому что VS использует механизм полной сборки проекта
(`XamlAppDef` → `XamlBuildTask` → `partial class` → `RPA-1.dll` → CLR-типы доступны). Этот механизм нам не
подходит: он компилирует XAML и все зависимости, что тяжело и медленно. Наше решение должно оставаться
быстрым и не тянуть сборку XAML.

### 1.2. Почему фасад `DynamicActivity` не решает задачу

`InvokeMethod.TargetType` имеет тип `System.Type` и обрабатывается конвертером `TypeTypeConverter`.
При чтении XAML конвертер вызывает `IXamlTypeResolver.Resolve("local:Code")`, тот — `XamlSchemaContext.GetXamlType(...)`.
Фасад `LooseActivityXamlType` базируется на `DynamicActivity`, поэтому его `UnderlyingType` — `DynamicActivity`,
а не `RPA_1.Code`. Для `TargetType` нужен **настоящий `System.Type`**: иначе `Resolve` вернёт `null` и конвертер упадёт.

Вывод: вложенные процессы (`<local:SubProcessN/>` — это объекты Activity) остаются фасадами, а ссылки на
**code-behind типы** (`TargetType="local:Code"`, типы аргументов, дженерик-параметры) требуют реального
`System.Type`. Эти два случая не конфликтуют и обрабатываются раздельно.

## 2. Выбранное решение

Скомпилировать **только C#-файлы проекта** (code-behind) Roslyn’ом в in-memory сборку, загрузить её и дать
loose-схеме резолвить `local:Code` в реальный `System.Type`. XAML по-прежнему не компилируется — остаётся
текущий быстрый фасадный путь. Сборка in-memory имеет синтетическое имя, чтобы не конфликтовать с реальной
`RPA-1.dll`, если проект позже будет собран штатно.

Ключевые решения:

1. **`ProjectCodeTypeCatalog`** — новый компонент: компилирует `.cs` из `<Compile Include>` проекта в
   `MemoryStream`, грузит через `Assembly.Load(bytes)`, строит `Dictionary<string, Type>` по `Type.FullName`.
2. **Присоединение к `WorkflowTypeRegistry`** — каталог типов живёт рядом с фасадами, потому что реестр уже
   проброшен во все точки loose-загрузки и кэшируется на проект. Новых сигнатур почти нет.
3. **Резолв в `LooseActivityXamlSchemaContext`** — после проверки фасадов, до `base.GetXamlType`, резолвим
   code-behind тип в `new XamlType(codeType, this)`.
4. **Round-trip rewrite** — `LooseXamlRoundTripSerializer` обязан переписать сериализованный
   `TargetType="...;assembly=RpaLooseCode_..."` обратно в `TargetType="local:Code"`, иначе открытие→сохранение
   испортит файл.

## 3. Архитектура

```
RPA-1.csproj
  ├── <Compile Include="Code.cs" />            ← только C#, без XamlAppDef
  └── <XamlAppDef Include="*.xaml" />         ← не компилируется (существующий фасадный путь)

        │
        ▼
ProjectCodeTypeCatalog.Build(project)
  ├── перечислить <Compile Include="*.cs">
  ├── исключить AssemblyInfo.cs, bin/, obj/
  ├── Roslyn: собрать ссылки из <Reference>/HintPath + RoslynAssemblyCatalog.FindLocation
  ├── Emit → MemoryStream → Assembly.Load(bytes)
  └── словарь Type.FullName → Type   (например "RPA_1.Code" → typeof(Code))

        │
        ▼
WorkflowTypeRegistry
  ├── _facades:  x:Class → XamlTypeFacade          (существующее)
  └── CodeTypes: ProjectCodeTypeCatalog            (новое, может быть null)

        │
        ▼
LooseActivityXamlSchemaContext.GetXamlType(ns, name, typeArgs)
  1) фасад (x:Class в реестре)  → LooseActivityXamlType (DynamicActivity)   ← приоритет
  2) CodeTypes.TryGetType       → new XamlType(codeType, this)              ← новый резолв
  3) base.GetXamlType(...)      → штатный путь

        │
        ▼
LooseXamlRoundTripSerializer.RewriteFacades(...)
  ├── DynamicActivity → <local:SubProcessN/>        (существующее)
  └── "…;assembly=RpaLooseCode_*" → "local:Code"    (новое, отдельный проход)
```

## 4. План по шагам

| Шаг | Что делаем | Результат / тест |
|---|---|---|
| 1 | `ProjectCodeTypeCatalog` — enumeration `.cs` из `Project.Items` (`ItemType == "Compile"`, `Include.EndsWith(".cs")`), исключение `Properties\AssemblyInfo.cs`, `bin/`, `obj/`; компиляция Roslyn в memory, загрузка сборки с синтетическим именем `RpaLooseCode_<project>_<hash>`, словарь `Type.FullName → Type`. Публичный API: `Build(Project)`, `TryGetType(string fullName, out Type)`, `IReadOnlyList<string> Diagnostics`, `IReadOnlyList<Type> Types`. | юнит-тесты `ProjectCodeTypeCatalogTests`: простой `class Code` резолвится по `RPA_1.Code`; без `.cs` → пустой; `AssemblyInfo.cs` пропущен; ошибка компиляции → пустой + диагностика, исключения нет |
| 2 | Ссылки для компиляции: всегда `mscorlib`, `System`, `System.Core`; затем `<Reference Include>`/`HintPath` проекта, fallback `RoslynAssemblyCatalog.FindLocation(name)`, дедупликация по простому имени. | тест: ссылка по `HintPath` попадает в compilation; отсутствующая сборка не роняет компиляцию |
| 3 | Присоединить каталог к `WorkflowTypeRegistry`: поле `internal ProjectCodeTypeCatalog CodeTypes { get; set; }`, метод `internal bool TryGetCodeType(string fullName, out Type type)`. `LooseXamlProjectService.BuildRegistry(project)` после `Scan(...)` строит каталог и прикрепляет. | тест `WorkflowTypeRegistryTests`: реестр с каталогом резолвит `RPA_1.Code`; реестр без каталога возвращает `false` |
| 4 | Резолв в `LooseActivityXamlSchemaContext.GetXamlType`: после блока фасадов, если `TryGetLocalFullName(...)` и `_registry.TryGetCodeType(fullName, out Type t)` → `return new XamlType(t, this)`. Фасады сохраняют приоритет. | тест: `local:Code` → `XamlType` с `UnderlyingType == typeof(Code)`; `local:SubProcessN` по-прежнему фасад; неизвестный тип → `base` |
| 5 | Сквозная загрузка `InvokeMethod`: открыть XAML с `TargetType="local:Code"` через `LooseXamlDocumentPersistenceService.Load` → `ActivityBuilder` содержит `InvokeMethod` с `TargetType == typeof(Code)` (тип из скомпилированной сборки). | тест в `LooseXamlDocumentPersistenceServiceTests` + live-тест `LooseXamlDesignerIntegrationTests` для `RPA-1\CodeTest.xaml` (`LoadSucceeded == true`, `Error` пуст) |
| 6 | Round-trip rewrite: сериализовать объект с `InvokeMethod { TargetType = typeof(Code) }`, зафиксировать точный формат писателя (`;assembly=...`), затем в `LooseXamlRoundTripSerializer` переписать токен обратно в `local:Code`, не трогая `DynamicActivity`-фасады. Гарантировать `xmlns:local="clr-namespace:RPA_1"` через существующий `EnsurePrefix`. | тесты: `Flush` даёт `TargetType="local:Code"`, нет синтетической сборки; повторная загрузка после round-trip успешна |
| 7 | Guard-тесты защиты от регресса: `LooseActivityXamlSchemaContext` проверяет code-каталог после фасадов; `LooseXamlProjectService` прикрепляет `CodeTypes`; `LoadActivityToDesignerHelper`/`DebuggerMappingsHelper` используют схему с каталогом без новых веток. | guard-тесты в `LooseXamlDesignerIntegrationTests` и/или новых фикстурах |
| 8 | Приёмка: `RPA-1\CodeTest.xaml` открывается в реальном конструкторе без `bin\*.dll`; `Main.xaml` и `SubProcess*.xaml` не регрессируют; полный прогон `AiRpaStudio.Tests`; Release сборка `AiRpa.sln` VS 2022 + VS 2026; обновить `ROADMAP.md` | все критерии подтверждены |

## 5. Конкретные изменения в коде

### 5.1. Новый файл `AiRpaStudio\Core\Workflow\ProjectCodeTypeCatalog.cs`

```csharp
namespace RPA.Core.Workflow
{
    internal sealed class ProjectCodeTypeCatalog
    {
        internal static ProjectCodeTypeCatalog Build(RPA.Entities.Project project);

        internal bool TryGetType(string fullName, out Type type);

        internal IReadOnlyList<string> Diagnostics { get; }
        internal IReadOnlyList<Type> Types { get; }
        internal int Count { get; }
    }
}
```

Детали реализации:

- Источник файлов: `project.Items` (`VsProjectItem.ItemType == "Compile"` и `Include` с `.cs`),
  путь резолвится от `project.ProjectDirPath` (как уже делает `LooseXamlProjectService.EnumerateXamlFiles`).
- Исключаются: `Properties\AssemblyInfo.cs` (в нём `assembly:`-атрибуты, тип не нужен и может мешать),
  пути под `bin/`, `obj/`, `packages/`, `.vs/`.
- Парсинг: `CSharpSyntaxTree.ParseText(code, CSharpParseOptions(LanguageVersion.CSharp7_3), path)`.
- Компиляция: `CSharpCompilation.Create(syntheticName, trees, references, options)` с
  `OutputKind.DynamicallyLinkedLibrary`, `OptimizationLevel.Debug`.
- References: mscorlib/System/System.Core всегда; остальные из `<Reference>` (HintPath, затем
  `RoslynAssemblyCatalog.FindLocation(simpleName)`), дедупликация по простому имени.
- Emit: `compilation.Emit(stream)`; ошибки не бросаются — каталог остаётся пустым, диагностики сохраняются.
- Загрузка: `Assembly.Load(stream.ToArray())`; имя уникально (`RpaLooseCode_<RootNamespace>_<hash>`).
- Индекс: `assembly.GetTypes()` → `dictionary[type.FullName] = type`, защита от дублей
  (`First wins`), пропуск `null`/пустых `FullName`.

### 5.2. `WorkflowTypeRegistry.cs`

Добавить:

```csharp
internal ProjectCodeTypeCatalog CodeTypes { get; set; }

internal bool TryGetCodeType(string fullName, out Type type)
{
    type = null;
    return CodeTypes != null && CodeTypes.TryGetType(fullName ?? string.Empty, out type);
}
```

### 5.3. `LooseXamlProjectService.BuildRegistry`

После построения реестра:

```csharp
WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(EnumerateXamlFiles(project));
registry.CodeTypes = ProjectCodeTypeCatalog.Build(project);
return registry.Count > 0 ? registry : null;
```

Кэш инвалидируется уже существующим `CustomWfDesigner.ClearProject`/`LoadProject` через
`_looseTypeRegistry = null` — новых механизмов кэширования не нужно.

### 5.4. `LooseActivityXamlSchemaContext.GetXamlType`

Вставить после текущего блока фасадов, перед `return base.GetXamlType(...)`:

```csharp
if (TryGetLocalFullName(xamlNamespace, name, out string fullName)
    && _registry.TryGetCodeType(fullName, out Type codeType))
{
    return new XamlType(codeType, this);
}
```

Приоритет остаётся у фасадов: если имя есть и в XAML-реестре, и в code-каталоге, побеждает XAML-фасад
(совместимость с текущим поведением `<local:SubProcessN/>`).

### 5.5. `LooseXamlRoundTripSerializer`

Добавить отдельный проход (после `RewriteFacades` или внутри `Serialize`):

- Найти атрибуты со значением-ссылкой на тип, где `;assembly=RpaLooseCode_` (или точный префикс,
  зафиксированный тестом шага 6).
- По `Type.FullName` через `registry.CodeTypes` восстановить `namespace + name`.
- Заменить значение на `local:<Name>`; убедиться, что `xmlns:local="clr-namespace:<ns>"` объявлен
  (переиспользовать `EnsurePrefix`).
- Не трогать `DynamicActivity`-фасады и не менять вложенные файлы.

Точный формат сериализованного токена фиксируется unit-тестом на этапе реализации: сериализуем
`InvokeMethod { TargetType = typeof(Code) }` штатным писателем и наблюдаем вывод, затем кодируем
его в rewrite (а не наоборот — иначе rewrite будет гадать о формате писателя).

## 6. Тестовый план

### 6.1. Юнит-тесты (`AiRpaStudio.Tests\Workflow\ProjectCodeTypeCatalogTests.cs`)

- `Build_CompilesCodeBehindType`: `.cs` с `namespace RPA_1 { public class Code }` → `TryGetType("RPA_1.Code") == true`, `Type.FullName == "RPA_1.Code"`.
- `Build_NoCsFiles_ReturnsEmptyCatalog`: нет `.cs` → `Count == 0`.
- `Build_SkipsAssemblyInfo`: `Properties\AssemblyInfo.cs` в списке `Compile` → не попадает в `Types`.
- `Build_IgnoresBinAndObj`: `.cs` под `bin/`/`obj/` не компилируется.
- `Build_CompileError_ReturnsEmptyWithDiagnostics`: битый `.cs` → `Count == 0`, `Diagnostics` не пуст, исключения нет.
- `Build_ReferencesHintPath`: `HintPath` на существующую сборку попадает в compilation.
- `Build_MissingReference_DoesNotThrow`: отсутствующая сборка не роняет компиляцию.

### 6.2. Реестр и схема (`WorkflowTypeRegistryTests`, `LooseXamlDocumentPersistenceServiceTests`)

- Реестр с `CodeTypes` резолвит `RPA_1.Code`; без каталога — `false`.
- `local:Code` → `XamlType` с `UnderlyingType == typeof(Code)`; `local:SubProcessN` остаётся фасадом.
- `Load` XAML с `TargetType="local:Code"` → `InvokeMethod.TargetType` — тип из in-memory сборки.
- `Flush` → `TargetType="local:Code"`, нет `DynamicActivity`, нет `RpaLooseCode_` в выводе.
- Round-trip: `Flush`-вывод снова грузится без ошибок.

### 6.3. Интеграция (`LooseXamlDesignerIntegrationTests`)

- `Rpa1_CodeTestOpensLoose_WithCodeType`: `DesignerBenchProxy.LooseOpen(CodeTest.xaml)` → `LoadSucceeded == true`, `Error` пуст.
- `Rpa1_CodeTestRoundTrips_TargetTypeStaysLocalCode`: после round-trip `TargetType="local:Code"`, файл заново открывается.
- Регресс: `Main.xaml`/`SubProcess1.xaml` по-прежнему открываются и round-trip-ят фасады без ошибок.

### 6.4. Guard-тесты

- `LooseActivityXamlSchemaContext` проверяет code-каталог **после** фасадов (негатив снятием ветки — падение).
- `LooseXamlProjectService` прикрепляет `CodeTypes` к реестру.
- `DeserializeFreshDocument` (DebuggerMappingsHelper) работает с code-типами без новой сигнатуры — уже использует `LooseTypeRegistry`.

## 7. Определения «готово» на шаг

- Шаг 1–2: `ProjectCodeTypeCatalog` компилирует `Code.cs`, резолвит `RPA_1.Code`, ошибки не роняют дизайнер.
- Шаг 3–4: `WorkflowTypeRegistry`+`LooseActivityXamlSchemaContext` резолвят `local:Code` в реальный `Type` при сохранении приоритета фасадов.
- Шаг 5: `RPA-1\CodeTest.xaml` открывается в реальном конструкторе без `bin\*.dll` и без падения.
- Шаг 6: round-trip оставляет `TargetType="local:Code"` без синтетической сборки.
- Шаг 7–8: guard-тесты зелёные, полный прогон `AiRpaStudio.Tests` зелёный, Release `AiRpa.sln` VS 2022 + VS 2026 — 0 ошибок, `ROADMAP.md` обновлён.

## 8. Риски и границы

- **Утечка сборок**: `Assembly.Load(bytes)` в .NET Framework (net48) не выгружается. Это уже принятая в проекте
  модель (`RoslynSandbox` грузит сборки агента так же). Один кэш на проект + синтетическое имя минимизируют утечку.
- **Первая итерация — только top-level не-дженерик типы** (как `Code`). Закрытые дженерики и вложенные типы —
  расширение: `GetXamlType` уже получает `typeArguments`, поэтому `MakeGenericType` можно поддержать отдельным шагом.
- **Ссылки code-behind на внешние библиотеки**: если библиотека недоступна, компиляция code-behind даст
  диагностику и `local:Code` не резолвится (корректно), но остальной документ открывается.
- **`x:Class`-процессы не компилируются**: границы loose-режима сохраняются — тяжёлая компиляция XAML не возвращается.
- **Порядок приоритета**: XAML-фасад всегда побеждает code-тип при совпадении имени, чтобы не сломать существующее
  поведение вложенных процессов.

## 9. Журнал изменений

| Дата | Что сделано |
|---|---|
| 07.02.2026 | Заведён план; диагноз: `InvokeMethod.TargetType="local:Code"` требует настоящий `System.Type`, фасад `DynamicActivity` не подходит; выбрано решение — Roslyn-компиляция только C#-файлов проекта + каталог типов в `WorkflowTypeRegistry` + резолв в `LooseActivityXamlSchemaContext` + round-trip rewrite |
| 21.11.2026 | Шаг 4: резолв code-behind в `LooseActivityXamlSchemaContext.GetXamlType` после фасадов — `new XamlType(codeType, this)`; тесты `LooseActivityXamlSchemaContextTests` (code-тип, приоритет фасада, делегирование в `base`) зелёные |
| 21.11.2026 | Шаг 5: сквозная загрузка `InvokeMethod` с `TargetType="local:Code"` через `LooseXamlDocumentPersistenceService.Load` → `ActivityBuilder` содержит `InvokeMethod` с `TargetType` из in-memory сборки (`RPA_1.Code`); live-тест `Rpa1_CodeTestOpensLoose_WithCodeType` для `RPA-1\CodeTest.xaml` (`LoadSucceeded == true`, `Error` пуст). `DesignerBenchProxy.LooseOpen` прикрепляет `CodeTypes` к реестру из `.csproj`. Связанные фикстуры — 38/38 зелёные |
| 21.11.2026 | Шаг 6: round-trip rewrite в `LooseXamlRoundTripSerializer.RewriteCodeTypes` — синтетические `clr-namespace:...;assembly=RpaLooseCode_*` объявления и значения `TargetType="r:Code"` переписываются в `TargetType="local:Code"` с `xmlns:local="clr-namespace:RPA_1"` через `EnsurePrefix`; `DynamicActivity`-фасады не затрагиваются. Тесты `Flush_RewritesCodeBehindTargetTypeToLocalCode`, `Flush_RoundTripsCodeBehindTargetType`, `Rpa1_CodeTestRoundTrips_TargetTypeStaysLocalCode` зелёные (23/23 в связанных фикстурах) |
| 21.11.2026 | Шаг 7: guard-тесты защиты от регресса в `LooseXamlDesignerIntegrationTests` — `LooseActivityXamlSchemaContext_ChecksCodeCatalogAfterFacades` (code-каталог проверяется ПОСЛЕ фасадов), `LooseXamlProjectService_AttachesCodeTypesToRegistry`, `LoadActivityToDesignerHelper_UsesCatalogCarryingRegistryWithoutNewBranches`, `DebuggerMappingsHelper_UsesCatalogCarryingSchemaWithoutNewBranches`. Прогон: 4/4 новых guard-тестов, `LooseXamlDesignerIntegrationTests` 14/14, связанные фикстуры (`LooseActivityXamlSchemaContextTests`, `WorkflowTypeRegistryTests`, `ProjectCodeTypeCatalogTests`, `LooseXamlDocumentPersistenceServiceTests`) 31/31 зелёные |
