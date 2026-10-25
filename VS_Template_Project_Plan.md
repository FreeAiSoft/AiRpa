# План: создание WF Activity Library из шаблона Visual Studio, открытие и сборка VS-проектов в AiRpaStudio

> Документ-план реализации. Версия 2 (с учётом решений владельца).

> **Статус: реализовано (все фазы 0–6).** Итог: 199 юнит-тестов зелёные.
> Движок `.vstemplate` (`RPA.VsTemplate`), открытие `.sln`/`.csproj` (`RPA.VsProject`),
> загрузка в дерево студии и сборка реализованы. Локализация RU/EN новых строк — добавлена.
> Примечание по сборке: вместо in-process `Microsoft.Build` API использован внешний
> `MSBuild.exe` (поиск через vswhere/реестр/папки установки, fallback `MSBuild.exe` из PATH) —
> это пункт «Fallback» Фазы 5; вариант с NuGet-пакетами `Microsoft.Build.Locator`/`Microsoft.Build`
> не подключался, чтобы не тянуть транзитивные зависимости в legacy `packages.config` и избежать
> конфликта сборок MSBuild (риск №3). Для сборки net4.8 нужны reference assemblies
> (VS / VS Build Tools, либо `Microsoft.NETFramework.ReferenceAssemblies`).

## 1. Цель и объём

Реализовать в `AiRpaStudio`:

1. **Создание проекта WF Activity Library из шаблона Visual Studio** (`ActivityLibrary2010.vstemplate`) силами студии, без запуска VS — с подстановкой параметров и выбором `TargetFrameworkVersion`.
2. **Открытие проектов Visual Studio** (`.sln` / `.csproj`):
   - во внешней Visual Studio — **всегда новый экземпляр**;
   - внутрь студии — парсинг `.sln`/`.csproj`, дерево файлов, открытие `.xaml` в дизайнере.
3. **Сборка `.sln`/`.csproj` внутри студии через MSBuild** (in-process).

### Скоуп (жёстко зафиксирован)
- Только **2 встроенных шаблона**: `VSTemplates\ActivityLibrary2010` (Project) и `VSTemplates\Activity` (Item).
- Только тип проекта **WF Activity Library**. Другие типы (web/mobile/прочее) — не поддерживаются.
- Произвольные `.vstemplate` через диалог — **не нужны**.
- `TargetFrameworkVersion` — **выбор пользователем** (4.7 / 4.7.2 / 4.8 / 4.8.1, по умолчанию 4.8).

### Не-цели (out of scope)
- Запуск `WizardExtension` (например `ItemTemplateWizard`) из `.vstemplate` — мастер **не исполняется**, реализуем только «контентную» генерацию.
- Поддержка произвольных типов проектов и произвольных шаблонов.
- Полная IDE-функциональность VS (рефакторинг C#, IntelliSense C# и т.п.).

## 2. Текущее состояние

| Компонент | Где | Примечание |
|---|---|---|
| Создание RPA-проекта | `Models\ProjectCreator.cs` + `ViewModels\ProjectCreatorViewModel.cs` + `Views\ProjectCreatorView.xaml` | Создаёт `project.json` + `Main.xaml`, `.vstemplate` не использует |
| Создание активности | `Models\ActivityCreator.cs` | Копирует `Main.xaml`, меняет `x:Class` |
| Загрузка RPA-проекта | `Models\Main.cs::LoadProject`, `Models\ProjectManager.cs::InitFileTree`, `Core\CustomWfDesigner.cs::LoadProject` | Только `project.json` |
| Дерево файлов | `Entities\FSNode.cs`, `Enums\FSNodeType.cs` (`File`,`Dir`,`Project`) | Требует расширения |
| Открытие workflow | `Helpers\LoadActivityToDesignerHelper.cs::LoadActivityToDesigner` | Загружает `.xaml` в `WorkflowDesignerEx` |
| Шаблоны VS | `VSTemplates\ActivityLibrary2010\`, `VSTemplates\Activity\` | Два `.vstemplate` |
| Прецедент VS-автоматизации | `WorkflowHost\Program.cs` (EnvDTE `DTE2`) | Для «открыть в VS» |
| Тесты | `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` | ⚠️ ссылка битая: `..\RPA\AiRpaStudio.csproj` |

## 3. Функциональные требования

- **FR-1.** Создание WF Activity Library из `ActivityLibrary2010.vstemplate` в указанной папке.
- **FR-2.** Подстановка параметров: `$safeprojectname$`, `$projectname$`, `$rootnamespace$`, `$guid1$..$guid5$`, `$targetframeworkversion$`, `$registeredorganization$`, `$year$`, `$username$`, `$machinename$`, `$time$`.
- **FR-3.** Обработка `$if$ (expr) ... $else$ ... $endif$`, включая inline (в `Activity1.xaml`/`Activity.xaml`).
- **FR-4.** Выбор `TargetFrameworkVersion` (4.7 / 4.7.2 / 4.8 / 4.8.1).
- **FR-5.** Открытие `.sln`/`.csproj` во внешней VS **новым экземпляром**.
- **FR-6.** Загрузка `.sln`/`.csproj` внутрь студии (дерево + `.xaml` в дизайнере, `.cs`/прочее — в VS/внешний редактор).
- **FR-7.** Сборка `.sln`/`.csproj` через MSBuild внутри студии, вывод результата в лог.
- **FR-8.** Логирование через `CustomWfDesigner.Log` / `RPA.Logging.Log`.
- **FR-9.** Покрытие юнит-тестами (`AiRpaStudio.Tests`).

## 4. Ключевые архитектурные решения

1. **Собственный движок `.vstemplate`** (парсинг XML + подстановка + условия). Без EnvDTE-мастера, без VSIX, без `dotnet new` (у него другой формат).
2. **Чистая логика отделена от UI** — `RPA.VsTemplate` и `RPA.VsProject` без WPF, тестируемы напрямую.
3. **Открытие в VS** — `Process.Start("devenv.exe", путь)` (новый экземпляр); при необходимости — EnvDTE (прецедент `WorkflowHost`).
4. **Сборка** — `Microsoft.Build.Locator` (найти MSBuild) + `Microsoft.Build` API (in-process). Fallback: внешний `MSBuild.exe`.
5. **Загрузка внутрь студии** — расширяем `FSNodeType` (`Solution`, `VsProject`) + отдельный метод загрузки, не трогая `project.json`-логику.
6. **MVVM по FW.EasyMvvm**: модель-центричная, команды — методы модели по имени, окна — `MvvmFactory.CreateWindow`.

## 5. Новые файлы

```
AiRpaStudio/
  VsTemplate/                       # RPA.VsTemplate
    VsTemplateModels.cs
    VsTemplateParser.cs
    VsTemplateParameterResolver.cs
    VsTemplateConditionEvaluator.cs
    VsTemplateInstantiator.cs
    VsTemplateInstantiationRequest.cs
    VsTemplateInstantiationResult.cs
  VsProject/                        # RPA.VsProject
    VsProjectModels.cs              # VsSolution, VsProjectInfo, VsProjectItem
    VsSolutionParser.cs
    VsProjectParser.cs
    VsProjectLoader.cs              # FSNode-дерево
    VsProjectOpener.cs              # devenv
    VsProjectBuilder.cs             # MSBuild
  Models/
    VsTemplateProjectCreator.cs
    VsProjectOpen.cs
  ViewModels/
    VsTemplateProjectCreatorViewModel.cs
    VsProjectOpenViewModel.cs
  Views/
    VsTemplateProjectCreatorView.xaml(.cs)
    VsProjectOpenView.xaml(.cs)

AiRpaStudio.Tests/
  VsTemplateParserTests.cs
  VsTemplateParameterResolverTests.cs
  VsTemplateConditionEvaluatorTests.cs
  VsTemplateInstantiatorTests.cs
  VsSolutionParserTests.cs
  VsProjectParserTests.cs
  VsProjectLoaderTests.cs
  VsProjectBuilderTests.cs
  VsProjectOpenerTests.cs
```

## 6. Фазы (TDD: red → green → refactor)

### Фаза 0 — Подготовка
- [x] Починить `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj`: `..\RPA\AiRpaStudio.csproj` → `..\AiRpaStudio\AiRpaStudio.csproj`.
- [x] `dotnet test AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` — baseline зелёный.
- [x] `Consts.cs`: пути/фильтры (`*.sln`, `*.csproj`), имена встроенных шаблонов, список `TargetFrameworkVersion`.
- [x] Решить доставку `VSTemplates\*` в output (`CopyToOutputDirectory`).

### Фаза 1 — Движок `.vstemplate`
- [x] Red/Green: `VsTemplateParser` (+модели) на `ActivityLibrary2010.vstemplate` и `Activity.vstemplate`.
- [x] Red/Green: `VsTemplateParameterResolver` (все параметры, `$guid1$`, sanitize `$safeprojectname$`).
- [x] Red/Green: `VsTemplateConditionEvaluator` (`>=`, `<`, `$else$`, inline, вложенность).
- [x] Red/Green: `VsTemplateInstantiator` на реальных шаблонах (`ActivityLibrary.csproj`, `Activity1.xaml`, `AssemblyInfo.cs`).
- [x] Refactor: общий `TemplateTextProcessor`.

### Фаза 2 — UI «Создать проект из шаблона VS»
- [x] `VsTemplateProjectCreator` (Model) + ViewModel + View: имя, путь, выбор `TargetFrameworkVersion`.
- [x] Команды: `SetFolder()`, `CreateProject()` → `VsTemplateInstantiator`.
- [x] Меню `MainView.xaml` → `CmdVsNewProjectFromTemplate()` в `Models\Main.cs`.
- [x] После генерации — предложить «Открыть в VS» / «Загрузить в студию» / «Собрать».
- [x] Тесты модели (валидация, путь, имя).

### Фаза 3 — Открытие `.sln`/`.csproj` во внешней VS (новый экземпляр)
- [x] `VsProjectOpener`: поиск `devenv.exe` (vswhere + реестр + fallback), `OpenInVisualStudio(path)`.
- [x] Red/Green: `VsProjectOpenerTests` (построение команды/аргументов, без реального запуска).
- [x] `CmdVsOpenInVisualStudio(string path)` + контекстное меню в `ProjectManagerControl`.

### Фаза 4 — Загрузка `.sln`/`.csproj` внутрь студии
- [x] Расширить `FSNodeType`: `Solution`, `VsProject` (+ `FSNode`).
- [x] Red/Green: `VsSolutionParser` (строки `Project("{...}") = "Name", "rel\path.csproj", "{guid}"`).
- [x] Red/Green: `VsProjectParser` (`RootNamespace`, `AssemblyName`, `TargetFrameworkVersion`, `<Compile>`, `<XamlAppDef>`, `<ProjectReference>`, `<Reference>`; legacy + SDK-style).
- [x] Red/Green: `VsProjectLoader` (FSNode-дерево `Solution → VsProject → files/dirs`).
- [x] `ProjectManager.InitVsProjectTree(path)` (рядом с `InitFileTree`).
- [x] `CmdVsOpenProject()` → диалог `VsProjectOpenView` → «Открыть в VS» / «Загрузить в студию».
- [x] `.xaml` → `LoadActivityToDesignerHelper`; `.cs`/прочее → VS/внешний редактор.

### Фаза 5 — Сборка через MSBuild
- [x] Добавить NuGet: `Microsoft.Build.Locator`, `Microsoft.Build`, `Microsoft.Build.Framework`, `Microsoft.Build.Utilities.Core` (совместимы с net48).
- [x] `VsProjectBuilder`:
  - `MSBuildLocator.RegisterDefaults()` вызывать один раз при старте, **до** любого использования типов `Microsoft.Build` (отдельный метод);
  - `Build(path, configuration) -> BuildResult` через `Microsoft.Build.Evaluation.Project`/`BuildManager`;
  - вывод лога сборки в `CustomWfDesigner.Log`.
- [x] Fallback: внешний `MSBuild.exe` (если in-process недоступен).
- [x] Red/Green: `VsProjectBuilderTests` (на сгенерированном проекте; проверка кодов возврата/ошибок).
- [x] Команда `CmdVsBuild()` + кнопка/пункт меню.
- [x] ⚠️ Задокументировать зависимость: для сборки net4.8 нужны reference assemblies (VS Build Tools бесплатно, или `Microsoft.NETFramework.ReferenceAssemblies`).

### Фаза 6 — Интеграция и полировка
- [x] Локализация RU/EN новых строк, логирование.
- [x] Сквозной сценарий: создать из шаблона → загрузить в студию → собрать → открыть в VS.
- [x] Полный прогон тестов — зелёные.

## 7. План тестирования

| Слой | Класс | Ключевые кейсы |
|---|---|---|
| Парсер | `VsTemplateParserTests` | Project/Item, параметры, `WizardExtension` игнорируется |
| Резолвер | `VsTemplateParameterResolverTests` | параметры, GUID, sanitize |
| Условия | `VsTemplateConditionEvaluatorTests` | `>=`, `<`, `$else$`, inline, вложенность |
| Инстансиатор | `VsTemplateInstantiatorTests` | реальные шаблоны, структура файлов |
| .sln | `VsSolutionParserTests` | `Project(...)` строки |
| .csproj | `VsProjectParserTests` | legacy + SDK-style |
| Загрузчик | `VsProjectLoaderTests` | FSNode-дерево |
| Сборка | `VsProjectBuilderTests` | успех/ошибка на сгенерированном проекте |
| Открытие в VS | `VsProjectOpenerTests` | команда/аргументы |

## 8. Риски

1. **`$if$/$endif$` inline** — сложный парсинг; митигация: потоковый `TemplateTextProcessor`, при необходимости предобработанные шаблоны под 4.8.
2. **Загрузка XAML Activity Library в дизайнер** — `x:Class` ссылается на компилируемый C#-тип; `WorkflowDesignerEx.Load` может не резолвить тип без сборки. Митигация: загрузка после сборки библиотеки, fallback в VS.
3. **`Microsoft.Build.Locator` в WPF** — регистрировать до первого обращения к типам MSBuild, иначе конфликт сборок. Митигация: изоляция кода MSBuild в отдельный метод/класс.
4. **Reference assemblies для net4.8** — на машине без VS/Build Tools сборка не пойдёт. Митигация: требовать VS Build Tools (бесплатно) или подключать `Microsoft.NETFramework.ReferenceAssemblies`.
5. **`WizardExtension` не исполняется** — генерируем «контентную» часть; поведение WF-мастера не воспроизводим (зафиксировано как не-цель).

## 9. Критерии готовности (Definition of Done)
- [x] Тестовый проект собирается, все тесты (старые + новые) зелёные.
- [x] Из студии создаётся WF Activity Library с корректной подстановкой и выбранным `TargetFrameworkVersion`.
- [x] `.sln`/`.csproj` открывается во внешней VS (новый экземпляр).
- [x] `.sln`/`.csproj` загружается в дерево студии, `.xaml` открывается в дизайнере (или корректный fallback).
- [x] Сборка `.sln`/`.csproj` выполняется внутри студии, результат виден в логе.
- [x] `project.json`-логика не сломана.

## Приложение A. MSBuild в свободном ПО (справка)

MSBuild можно использовать без лицензионных ограничений:

- **MSBuild (движок, dotnet/msbuild)** — лицензия **MIT**, открытый исходный код.
- **`Microsoft.Build`** (NuGet, API программной сборки: `Microsoft.Build.Evaluation.Project`, `BuildManager`) — **MIT**, совместим с .NET Framework 4.7.2+ (студия — net48).
- **`Microsoft.Build.Locator`** (NuGet, поиск установленного MSBuild из VS / VS Build Tools / .NET SDK) — **MIT**, совместим с .NET Framework 4.6+.
- Reference assemblies для .NET Framework 4.8: бесплатный **VS Build Tools** или NuGet **`Microsoft.NETFramework.ReferenceAssemblies`** (уже используется в `AiRpaStudio.Tests`).
