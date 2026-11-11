# План правок: ускорение запуска AiRpaStudio (актуальное состояние кода)

> Переписано по факту репозитория. Лог `AiRpaStudio\bin\Debug\log.txt` снят со **старой** сборки
> (там ещё синхронный `WFToolboxHelper.Build` на UI-потоке и двойной `Describe`). После пересборки
> замер нужно повторить: часть цифр ниже уже не воспроизводится.

## 0. Резюме

Основные источники задержки при старте:

1. **Пересборка палитры «Активности»** — рефлексия Browser/OS-типов и резолв дизайнеров тулбокса.
2. **Первый рендер окна** (`view.Show()` в `MvvmFactory.CreateWindow`) — синхронная инициализация панелей и десериализация раскладки.
3. **Локальный NuGet-источник** — долгий поиск по папке (уже решён кэшем индекса).

Из плана уже сделано: фоновый конвейер палитры, кэш дескрипторов, уход от двойного `Describe`,
кэш локального NuGet-источника, ленивый NuGet-поиск, фоновое сканирование пакетных активностей,
асинхронная загрузка проекта/сессии. Осталось добить: **дисковый кэш палитры (холодный старт)**,
**ленивые иконки/дизайнеры тулбокса** и **ускорение первого рендера окна**.

## 1. Базовые замеры (из старого `log.txt`)

| Метка | Время | Комментарий |
| --- | --- | --- |
| `MvvmFactory.CreateWindow()` | ~8.6–12.8 с | синхронный первый рендер окна |
| `RefreshActivityPalette` / `RefreshActivityCatalog` | ~41.6 с | пересборка палитры |
| ├ `WFToolboxHelper.Build` (тулбокс) | ~11.8 с | Browser ~10.3 с + OS ~1.4 с |
| ├ `ActivityPaletteRegistry` (51 активность) | ~14.9 с | `Describe` Browser 90–673 мс/тип, OS ~180–190 мс/тип |
| └ `StudioActivityCatalog` | ~29.9 с | повторный `Describe` тех же 56 типов |
| NuGet (локальный источник) | ~0.1 с | уже кэшируется |

Эти цифры — до правок. Двойной `Describe` и синхронный тулбокс в текущем коде устранены.

## 2. Что уже сделано (по фактическому коду)

### 2.1. NuGet: кэш локального источника

| Что | Где | Статус |
| --- | --- | --- |
| Дисковый кэш индекса локальной папки | `RPA.NuGet\Services\LocalPackageIndexCache.cs` | ✅ сделано |
| Мемоизация `SearchAsync`/`GetVersionsAsync`/`GetPackageInfoAsync` в памяти | `RPA.NuGet\Services\NuGetSearchService.cs` (`_localSearchMemo`, `_localVersionsMemo`, `_localPackageInfoMemo`) | ✅ сделано |

### 2.2. NuGet: ленивый поиск и лишние запросы при старте

| Что | Где | Статус |
| --- | --- | --- |
| `LoadProject` считает установленные без проверки обновлений | `RPA.NuGet\ViewModels\NuGetPanelViewModel.cs` → `RefreshInstalledItems(checkUpdates: false)` | ✅ сделано |
| Поиск не стартует до показа панели (`_isPanelVisible`) | `OnSourceChangedAsync` → `if (!_isPanelVisible) return;` | ✅ сделано |
| Отложенный поиск при первом открытии панели | `NotifyPanelShown()`, вызов из `NuGetPanel_Loaded` | ✅ сделано |
| README — лениво по выбору карточки | `SelectedItem` → `LoadDetailsAsync` → `LoadReadmeAsync` | ✅ сделано |

### 2.3. Не блокировать появление окна

| Что | Где | Статус |
| --- | --- | --- |
| `SetProjectFolder` + `ScanNuGetActivities` отложены до `ApplicationIdle` | `MainView.OnProjectManagerPropertyChanged` | ✅ сделано |

### 2.4. Сканирование пакетных активностей в фоне

| Что | Где | Статус |
| --- | --- | --- |
| `NuGetActivityPaletteLoader.LoadFromProject` в `Task.Run`, `RefreshActivityPalette` на UI | `MainView.ScanNuGetActivitiesAsync` | ✅ сделано |

### 2.5. Асинхронная загрузка проекта/сессии

| Что | Где | Статус |
| --- | --- | --- |
| `Main.Loaded` → `LoadProjectIntoStudioAsync`; дерево в `Task.Run`, core через `OnUiThreadAsync` | `AiRpaStudio\Models\Main.cs` | ✅ сделано |

### 2.6. Фоновая пересборка палитры + кэш рефлексии

| Что | Где | Статус |
| --- | --- | --- |
| Async-конвейер `RefreshActivityPaletteAsync` → `Task.Run(BuildPaletteModel)` | `MainView.xaml.cs` | ✅ сделано |
| Индикатор `ShowLoading()`/`HideLoading()` + `ProgressBar` | `ToolboxPanel.xaml` / `.xaml.cs` | ✅ сделано |
| `WFToolboxHelper` разделён на `BuildModel` (фон) / `BuildControl` (UI) | `WFToolboxHelper.cs` | ✅ сделано |
| Кэш дескрипторов `DescriptorCache` (в памяти) | `ActivityPaletteRegistry.cs` | ✅ сделано |
| Устранён двойной `Describe` — каталог переиспользует реестр | `StudioActivityCatalog.cs` | ✅ сделано |
| Чтение атрибутов без инстанцирования (`CustomAttributeData`) | `ActivityPaletteRegistry.GetAttributeSafe` / `ReadStringAttribute` | ✅ сделано |
| Кэш локализованных строк + `ResourceManager` | `LocalizedStringCache` / `ResourceManagerCache` | ✅ сделано |
| Кэш `CustomAttributeData` по члену | `ConditionalWeakTable<MemberInfo, ...>` | ✅ сделано |
| Подстановка готовых реестра/каталога без повторной сборки | `ActivityPaletteRegistry.Install` / `StudioActivityCatalog.Install` | ✅ сделано |

## 3. Что НЕ сделано (backlog, по приоритету)

### 3.1. Дисковый кэш палитры для холодного старта — **главный остаток**

**Проблема.** `DescriptorCache` живёт только в памяти процесса. Первый запуск по-прежнему платит
~15 с рефлексии Browser/OS (теперь один раз и в фоне, но всё равно до готовности палитры).

**Что делать.**

- Сериализовать `PaletteActivityDescriptor` (ключ, displayName, категория, тип, описание, аргументы,
  контейнеры) в `%LOCALAPPDATA%\AiRpaStudio\PaletteCache\<hash>.json`.
- Ключ инвалидации — хэш (MVID + имя + версия) задействованных DLL пакетов + версия схемы кэша.
  Для встроенных WF-типов хэш фиксирован по версии сборки `System.Activities`.
- На холодном старте читать дескрипторы из кэша, рефлексию делать только для промахов.
- Сбрасывать/обновлять кэш в `RegisterUserLibrary`/`ReplacePackageLibraries`/`InvalidateTypes`.

**Эффект.** Холодный старт палитры с ~15 с до десятков–сотен мс после первого прогрева.

### 3.2. Ленивые иконки/дизайнеры тулбокса

**Проблема.** `ToolboxItemWrapper` теперь создаётся в фоне (`BuildModel`), но финальный
`wfToolbox.Categories.Add(category)` в `BuildControl` выполняется на UI-потоке и всё ещё резолвит
`ToolboxBitmapAttribute`/`DesignerAttribute` (~6 с для Browser + ~1 с для OS).

**Что делать.**

- Не резолвить bitmap/дизайнеры при добавлении категории; грузить их лениво при первом раскрытии
  категории или показе элемента (кастомный `ToolboxItemWrapper`/перехват realization).
- Проверить, можно ли перенести `Categories.Add` целиком в фон (если WF это допускает) — иначе
  оставить на UI, но без дорогой realization до востребования.

**Эффект.** `BuildControl` на UI-потоке с ~6–7 с до миллисекунд; окно не замирает.

### 3.3. Ускорение первого рендера окна (`view.Show()`)

**Проблема.** `MvvmFactory.CreateWindow` синхронно вызывает `view.Show()`, а `MainView` в
конструкторе синхронно делает `InitializeControls` (ChatPanel, NuGetPanel, темы) и
`InitializeLayoutRoot` (десериализация AvalonDock `ADLayout`). Это ~8.6–12.8 с до появления окна.

**Что делать.**

- Разнести создание тяжёлых панелей (Chat/Log/Outline/Properties/NuGet) на ленивую инициализацию
  по первому открытию вкладки.
- Десериализацию раскладки в `InitializeLayoutRoot` оставить, но не ждать её до первого кадра:
  показать пустой каркас, затем поднять доки через `Dispatcher`.
- Замерить вклад каждой фазы отдельно (`InitializeComponent`, `InitializeControls`,
  `InitializeLayoutRoot`) и точечно чинить самую дорогую.

**Эффект.** Главное окно появляется раньше, «висящего» старта нет.

### 3.4. Параллельный `Describe`

**Проблема.** 56 типов рефлексируются последовательно (`foreach` в `ActivityPaletteRegistry`).

**Что делать.**

- `Parallel.ForEach` по типам при сборке реестра/каталога (сборка метаданных и
  `ResourceManager.GetString` потокобезопасны; `ConcurrentDictionary` уже есть).
- Обязательно сохранять детерминированный порядок категорий/активностей после распараллеливания.

**Эффект.** Холодная рефлексия Browser/OS с ~15 с до нескольких секунд (пока нет дискового кэша).

### 3.5. Кэш `StudioActivityPalette.Categories()`

**Проблема.** Метод каждый раз заново делает `Assembly.Load` и пересобирает список категорий;
он вызывается из `Default`-лази, `BuildPaletteModel` и тестов.

**Что делать.**

- Кэшировать снапшот `Categories()` и сбрасывать в `RegisterUserLibrary`/
  `ReplacePackageLibraries`/`ResetUserLibraries`/`ResetPackageLibraries`.

### 3.6. Иконки Browse загружаются автоматически

**Проблема.** При формировании карточек Browse `CreateItem` → `item.LoadIconAsync()` грузит иконки
сразу, хотя README уже ленивый. Иконки можно отложить до отображения карточки (виртуализация).

### 3.7. Чистка dead-кода

**Проблема.** Старый синхронный `WFToolboxHelper.Build()` и `AddToolboxCategory()` остались в классе,
но из `MainView` больше не вызываются. Их следует удалить, чтобы не было двух путей сборки палитры.

### 3.8. Операционная гигиена локального фида

**Проблема.** В `nuget_local` 56 версий `Activities.Browser` и 44 `ActivityLibraryBase`. После кэша
индекса это не критично для поиска, но замедляет первый прогрев индекса и раздувает список версий.

## 4. Порядок внедрения остатка

1. **3.1 Дисковый кэш палитры** — самый большой выигрыш для холодного старта.
2. **3.2 Ленивые иконки/дизайнеры тулбокса** — убирает последний крупный UI-блок в пересборке.
3. **3.3 Ускорение `view.Show()`** — максимальный UX-эффект «окно сразу».
4. **3.4 Параллельный `Describe`** — страховка, пока дисковый кэш не прогрелся.
5. **3.5–3.8** — гигиена и мелкие оптимизации.

## 5. Критерии приёмки (обновлённые)

- Главное окно появляется **< 2 с** после запуска.
- Полная готовность (проект + палитра) на тёплом кэше — **< 3–5 с**.
- Холодный старт (первый после изменения `nuget_local`/пакетов) — прогрев кэша палитры и NuGet
  индекса **< 2–3 с**, без заморозки UI.
- Пересборка палитры (install/uninstall/новая активность) на тёплом кэше — **< 1–2 с**,
  UI отзывчив, в панели «Активности» виден спиннер, подмена атомарная.
- Повторный рефреш не перечитывает уже известные типы.
- В логе при старте нет `Search`/`GetVersions`/`Icon download`/`README load`, пока панель NuGet
  не открыта.
- Регрессия: тесты палитры (`WFToolboxHelperTests`, `ToolboxDragContractTests`, `ToolboxThemeTests`,
  `ActivityCatalogTests`, `ActivityPaletteRefreshTests`, `NuGetPaletteSyncTests`,
  `NuGetProjectIntegrationTests`) и NuGet (`NuGetSourcesAndSearchTests`, `NuGetPanelViewModelTests`,
  `NuGetReadmeLoadTests`, `NuGetProjectIntegrationTests`) — зелёные.

## 6. Как проверять

- Пересобрать `AiRpa.sln` (Release), запустить студию, измерить по `StartupTimer`-меткам в `log.txt`.
- Повторить запуск (тёплый кэш) и сравнить с базовыми 59.2 с / 41.6 с.
- Вручную открыть панель NuGet: поиск/версии/README работают как раньше.
- Прогнать `AiRpaStudio.Tests` (особенно перечисленные в разделе 5).