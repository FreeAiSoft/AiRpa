# Папка проекта Visual Studio и контекстное меню файла .csproj

Исправление двух дефектов:

1. при добавлении активности в проект Visual Studio студия сообщала
   «Папка `C:\...\ActivityLibrary5.csproj` не существует. Укажи существующую папку проекта»;
2. у основного файла проекта Visual Studio (`.csproj`) в дереве проекта не было контекстного меню,
   которое есть у нативного проекта студии (`project.json`).

## 1. Папка проекта: единый резолвер

Причина ошибки: в `Properties.Settings.Default.LastLoadedProjectPath` хранится **файл** проекта
(`.csproj`/`.sln`), а диалог создания активности (`Models\ActivityCreator.cs`) использовал это значение
как **папку**. Проверка принадлежности проекту делалась через `string.Contains`, что дополнительно
пропускало чужие каталоги с общим префиксом (`C:\proj2` при корне `C:\proj`).

Решение — `Helpers\ProjectPathResolver.cs`, единственная точка решений о папке проекта:

| Метод | Назначение |
|---|---|
| `GetProjectFolder(pathOrFile)` | папка по пути к папке **или** файлу проекта; пустой/непригодный путь → `null` |
| `IsInsideProject(path, rootOrFile)` | принадлежность проекту по границе каталога, регистронезависимо |
| `GetNodeFolder(node)` | папка узла дерева: для `Dir`/`Project` — сам путь, для `File`/`VsProject`/`Solution` — каталог |
| `IsContainerNode(node)` | узел принимает файлы и папки |
| `AreSamePath(a, b)` | сравнение путей файловой системы |

Точки применения:

- `Models\ActivityCreator.cs` — нормализация в конструкторе и в `CreateActivity`, проверка
  принадлежности проекту через `IsInsideProject`;
- `Models\Main.cs` — `CmdAddNewActivityToProject` берёт папку из настроек через резолвер,
  обе команды создания активности идут в общий `OpenCreatedActivity`;
- `Models\ProjectManager.cs` — `AddFile` ищет родителя по каталогу (а не разбором относительного пути
  по именам узлов), достраивает отсутствующие папки (`AddFolderChain`). Это работает и для дерева
  из `.csproj`, и для дерева из `.sln` (там дети решения — узлы `VsProject`);
- `Controls\ProjectManagerControl.xaml.cs` — создание папки (`AddFolder_Click`,
  `AddFolderToFolderMenuItem_Click`, `CommitRename`) считает путь от папки узла.

## 2. Регистрация активности в `.csproj`

Раньше созданный `.xaml` не попадал в файл проекта, и сборка библиотеки его не видела.
`VsProject\VsProjectWriter.cs`:

- `AddXamlItem(projectPath, relativePath)` — идемпотентно добавляет
  `<XamlAppDef Include="..."><SubType>Designer</SubType><Generator>MSBuild:Compile</Generator></XamlAppDef>`
  в существующую группу `XamlAppDef` (иначе в группу `Compile`, иначе создаёт новую до `<Import>`);
  форматирование и кодировка файла сохраняются;
- `GetRelativeIncludePath(fromDir, path)` — относительный путь с `\`;
- `RenameProjectFile(projectPath, newName)` — переименование **только файла** проекта;
- `UpdateSolutionReferences(old, new)` — правка ссылки на переименованный файл в ближайших `.sln`.

Вызывается из `ProjectManager.RegisterFileInVsProject` (после создания активности в
`Main.OpenCreatedActivity`); для проекта `project.json` — no-op.

## 3. Контекстное меню файла проекта Visual Studio

`Controls\ProjectManagerControl.xaml`: новое меню `VsProjectRootContext` (узел `FSNodeType.VsProject`),
по составу повторяющее меню нативного проекта:

- Добавить активность → `AddActivityToVsProjectMenuItem_Click` → `AddActivityToNode`
  (папка узла через `GetNodeFolder`, поэтому работает и для `.csproj`);
- Добавить папку проекта → `AddFolder_Click` (inline-редактирование имени);
- Переименовать проект → `Rename_Click` → `CommitRename` → `ProjectManager.RenameVsProject`:
  переименовывается **только файл `.csproj`** (имя узла следует имени файла, `AssemblyName` и
  содержимое проекта не меняются), правится ссылка в `.sln`, обновляется
  `LastLoadedProjectPath`;
- Добавить зависимость → `AddDependencyToVsProjectMenuItem_Click` →
  `Main.CmdOpenNuGetPanel` → `MainView.ShowNuGetPanelFor` (панель NuGet для папки проекта);
- separator, «Открыть в VS» и «Build» (узел `FSNodeType.Solution` по-прежнему использует
  `VsProjectContext`).

Имена пунктов меню отличаются от файлового меню намеренно: `ContextMenu_Opened` решает по именам,
что скрывать, и не должен прятать «Открыть в VS» у корня проекта.

## 4. Тесты

- `ProjectPathResolverTests` — резолвер путей, включая регресс `C:\proj2` внутри `C:\proj`;
- `ActivityCreatorPathTests` — диалог получает папку, даже если в настройке лежит `.csproj`;
- `VsProjectTreeFileTests` — `AddFile` для `.csproj`- и `.sln`-дерева, достройка папок,
  `RegisterFileInVsProject`, `RenameVsProject` (файл, узел, сохранённый путь);
- `VsProjectWriterTests` — `XamlAppDef` (legacy и SDK-style проект, идемпотентность),
  переименование файла проекта и ссылки в `.sln`;
- `VsProjectContextMenuTests` — стражи XAML (меню и пункты на месте, привязка `VsProject` →
  `VsProjectRootContext`, уникальность `x:Name`) и по исходникам (обработчики, `ShowNuGetPanelFor`,
  `CmdOpenNuGetPanel`, отсутствие `Path.Combine(parentNode.RuntimeFullPath, ...)`).

Запуск (см. `AGENTS.md`): `MSBuild.exe AiRpaStudio.Tests\AiRpaStudio.Tests.csproj -t:Build`,
затем `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll`.
