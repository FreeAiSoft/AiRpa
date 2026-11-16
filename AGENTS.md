# Правила разработки (AiRpaStudio)

## Язык общения в чате - русский

## Навигация по правилам (иерархия, правило П2)

Глобальные правила — ниже. Специфика проектов вынесена в проектные файлы, чтобы агент
поднимал в контекст только нужное:

- `AiRpaStudio\AGENTS.md` — студия (WPF): MVVM, ошибки и падения, темы, вкладки и валидация WF, дерево проекта.
- `RPA.NuGet\AGENTS.md` — панель NuGet.
- `Rpa.Workflow.Runtime\AGENTS.md` — робот: рантайм, раннер `RpaRunner.exe`, пакет `robot.zip`.
- `Agent.Core\AGENTS.md` — AI-агент: `spec_*`, WF-инструменты, правка XAML, создание активности, промпты.

Правила организации решения и бюджета контекста агента — `SolutionOrganizationRules.md`
(применяются при проверке соответствия).

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

