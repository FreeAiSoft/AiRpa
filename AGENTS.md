# Правила разработки (AiRpaStudio)

## Язык общения в чате - русский

## Навигация по правилам (иерархия, правило П2)

Глобальные правила — ниже. Специфика проектов вынесена в проектные файлы, чтобы агент
поднимал в контекст только нужное:

- `AiRpaStudio\AGENTS.md` — студия (WPF): платформа и UI-стек, версии пакетов и привязка сборок, MVVM, ошибки и падения, темы, вкладки и валидация WF, дерево проекта.
- `RPA.NuGet\AGENTS.md` — панель NuGet.
- `Rpa.Workflow.Runtime\AGENTS.md` — робот: рантайм, раннер `RpaRunner.exe`, пакет `robot.zip`.
- `RPA.Shared\AGENTS.md` — общий код студии: AI-агент (`spec_*`, WF-инструменты), тема, читатели документов ТЗ, локатор MSBuild.

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

## Целевые платформы (правило `CP-10`)
- **студия — .NET Framework 4.8**, а **всё, что попадает к роботу, — 4.7.2** (`net472`).
  Новый проект, на который может ссылаться робот, обязан целиться в `net472`/`netstandard2.0`;
  исключение — только с записью в белом списке `AiRpaStudio.Tests\TfmMatrixGuardTests.cs` (ID задачи + причина).
  Генерируемые студией роботы и библиотеки активностей по умолчанию создаются на **4.7.2**
  (`Consts.defaultTargetFrameworkVersion`), шаблон VS подставляет TFM через `$targetframeworkversion$`.

## Сборка решения
- `MSBuild` из **VS 2026** (MSBuild 18) **и** из **VS 2022** (MSBuild 17.13) собирает решение полностью:
  `MSBuild AiRpa.sln /p:Configuration=Release` → 0 ошибок (`TD-14`; проверено `-t:Rebuild` для `AvalonEdit`
  обоими тулчейнами). Пин .NET SDK — `AvalonEdit\global.json` = `9.0.100` + `rollForward: minor`
  (SDK 10.x требует MSBuild 18 или новее и ломал сборку из VS 2022); страж — `AiRpaStudio.Tests\SdkPinGuardTests.cs`.
  После смены TFM у `AvalonDock` (`TD-13`) сборку решения запускайте с `/restore` (иначе `NETSDK1005`);
  параллельную сборку из VS 2022 не запускать — `/m:1`.

## Сборка и тесты
- Решение: `AiRpa.sln` (корень репозитория).
- Проект приложения: `AiRpaStudio\AiRpaStudio.csproj`.
- Тесты: `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` (SDK-style, net48 + NUnit).
  Запуск — через MSBuild из Visual Studio + `vstest.console.exe` (см. выше), т.к.
  `dotnet test` не собирает legacy WPF-проекты решения.
- Полная сборка решения: MSBuild из Visual Studio.

