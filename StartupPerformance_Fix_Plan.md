# План правок: ускорение запуска AiRpaStudio

## 0. Приоритет (новый): фоновая пересборка палитры с индикатором прогресса

> Требование: пересборка палитры «Активности» должна идти в фоне, не блокируя UI,
> а пока она идёт — в области тулбокса показывается indeterminate-прогресс.

### 0.1 Цель

1. Убрать заморозку окна на время пересборки палитры (`RefreshActivityPalette` + `RefreshActivityCatalog`).
2. Пока идёт пересборка, в панели «Активности» виден спиннер:

```xml
<ProgressBar
  IsIndeterminate="True"
  Style="{StaticResource MaterialDesignCircularProgressBar}"
  Value="0" />
```

3. Старая палитра остаётся видимой (или слегка затемняется), новая подменяется атомарно — без «прыжка» и без пустой панели.

### 0.2 Текущая картина (замер из второго запуска, `3:35:03`)

`RefreshActivityPalette` на UI-потоке = **41 654 ms** (`3:35:16:995 → 3:35:58:650`).

| Фаза | Время | Где |
| --- | --- | --- |
| `WFToolboxHelper.Build` (тулбокс, 56 типов) | 11 785 ms | `MainView.RefreshActivityPalette` |
| `ActivityPaletteRegistry построен` (51 активность) | 14 921 ms | `StudioActivityCatalog` |
| `StudioActivityCatalog построен` (всего, 56 типов) | 29 855 ms | `RefreshActivityCatalog` |
| Итог | 41 654 ms | — |

Ключевые наблюдения:

- `Describe` выполняется **дважды** на одних и тех же типах: один раз внутри `new ActivityPaletteRegistry(...)`, второй раз в цикле `StudioActivityCatalog` через `registry.Describe(...)`. Это ~15 с дублирующей рефлексии.
- Browser-типы дороги в `Describe` (90–673 ms каждый) из-за `[LocalizedDescription]`/`[LocalizedDisplayName]`/`[LocalizedCategory]`: их геттеры создают `new ResourceManager(...)` и делают lookup ресурса на каждое чтение атрибута.
- В тулбоксе каждый `ToolboxItemWrapper` для Browser стоит ~181–213 ms (итого ~4.1 с), ещё ~6 с уходит в финальный `wfToolbox.Categories.Add(категория)` (realization иконок/дизайнеров).

### 0.3 Затрагиваемые файлы

| Файл | Что делаем |
| --- | --- |
| `AiRpaStudio\Controls\ToolboxPanel.xaml` / `.xaml.cs` | индикатор загрузки: `ShowLoading()`/`HideLoading()` |
| `AiRpaStudio\Views\MainView.xaml.cs` | `RefreshActivityPalette` → async-конвейер «прогресс → фон → UI-подмена» |
| `AiRpaStudio\Helpers\WFToolboxHelper.cs` | разделить `Build()` на фоновую модель и UI-контрол |
| `AiRpaStudio\Core\Generation\ActivityPaletteRegistry.cs` | кэш дескрипторов, чтение атрибутов без инстанцирования |
| `AiRpaStudio\Core\Workflow\StudioActivityCatalog.cs` | переиспользовать реестр (убрать двойной `Describe`) |
| `AiRpaStudio\Core\Workflow\NuGetActivityPaletteLoader.cs` | проверить/исправить чтение атрибутов и типов в фоне |

### 0.4 Архитектура решения

Разделяем пересборку на две фазы:

- **Фоновая фаза** (`Task.Run`): всё, что не зависит от WPF — `StudioActivityPalette.Categories()` уже загруженных сборок, `ActivityPaletteRegistry`, `StudioActivityCatalog`, кэш дескрипторов, список `(категория, типы, displayName)`.
- **UI-фаза**: построение `ToolboxControl`/`ToolboxItemWrapper` из подготовленных данных, `ToolboxPanel.SetToolboxControl`, `ApplyTheme`, атомарная подмена `WfToolboxBorder.Child`.

Правило потоков:

- `ToolboxControl`, `ToolboxPanel`, `ToolboxCategory`, `ToolboxItemWrapper` — WPF/WF-объекты: создавать и мутировать только на UI-потоке, пока не доказано обратное.
- Рефлексия (`GetTypes`, чтение атрибутов, `Describe`), сборка реестра/каталога — чистый CPU/IO, безопасно в `Task.Run`.

### 0.5 Шаги (по порядку)

#### Шаг 1. Индикатор загрузки в `ToolboxPanel`

**`ToolboxPanel.xaml`**: добавить поверх контейнера тулбокса (в `Grid`, перекрывая `ToolboxContainer` в `Grid.Row=1`) спиннер со скрытым состоянием:

```xml
<ProgressBar x:Name="LoadingProgress"
             Grid.Row="1"
             IsIndeterminate="True"
             Style="{StaticResource MaterialDesignCircularProgressBar}"
             Value="0"
             Width="48"
             Height="48"
             HorizontalAlignment="Center"
             VerticalAlignment="Center"
             Visibility="Collapsed" />
```

Примечание: стиль `MaterialDesignCircularProgressBar` приходит из `MaterialDesignThemes.Wpf` (сборка уже подключена). Если в контексте панели стиль не резолвится — добавить `pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Defaults.xaml` в `App.xaml`/ресурсы панели либо временно использовать обычный `ProgressBar IsIndeterminate="True"`.

**`ToolboxPanel.xaml.cs`**:

```csharp
public void ShowLoading()
{
    LoadingProgress.Visibility = Visibility.Visible;
    ToolboxContainer.Opacity = 0.35;      // старая палитра видна, но приглушена
    ToolboxContainer.IsHitTestVisible = false;
}

public void HideLoading()
{
    LoadingProgress.Visibility = Visibility.Collapsed;
    ToolboxContainer.Opacity = 1.0;
    ToolboxContainer.IsHitTestVisible = true;
}
```

#### Шаг 2. Async-конвейер в `MainView`

`RefreshActivityPalette()` превращается в `async void RefreshActivityPalette()` (или `async Task` с `_ =` у вызывающих):

```
[UI] если нет доступа к Dispatcher → BeginInvoke и return
[UI] _wfToolbox?.ShowLoading()  (если панель уже существует; иначе показать прогресс в WfToolboxBorder)
[фон]  Task.Run:
         var model = PaletteModelBuilder.Build()   // реестр + каталог + (категория, типы, имя)
[UI]  _wfToolbox = BuildToolboxControl(model)      // WFToolboxHelper.BuildControl
[UI]  WfToolboxBorder.Child = _wfToolbox
[UI]  _wfToolbox.ApplyTheme()
[UI]  RefreshActivityCatalog(model)                // подставить уже готовый реестр/каталог
[UI]  _wfToolbox.HideLoading()  (в finally)
```

Все три вызова `_wfToolbox = new WFToolboxHelper().Build(); WfToolboxBorder.Child = _wfToolbox;` (в `InitializeLayoutRoot`, `CreateDefaultLayout`, `RefreshActivityPalette`) сводятся к этому одному конвейеру.

#### Шаг 3. Кэш дескрипторов + устранение двойного `Describe`

- В `ActivityPaletteRegistry` завести статический `ConcurrentDictionary<Type, PaletteActivityDescriptor>` (или кэш по имени сборки), чтобы при повторном рефреше OS/Browser не рефлексировались заново.
- В `StudioActivityCatalog` использовать уже построенный реестр (`registry.All`/`registry.Find`), а не вызывать `registry.Describe(...)` второй раз. Это убирает ~15 с.

#### Шаг 4. Чтение атрибутов без инстанцирования

- Заменить `member.GetCustomAttribute<T>(inherit)` + try/catch на `CustomAttributeData.GetCustomAttributes(member)` / `member.CustomAttributes`: они не резолвят типы атрибутов и не бросают `FileNotFound`/`TypeLoad`.
- Кэшировать локализованные строки: `LocalizedDescriptionAttribute`/`LocalizedDisplayNameAttribute`/`LocalizedCategoryAttribute` хранить статический кэш `(resourceType, key) → строка`, чтобы не создавать `ResourceManager` при каждом чтении. Либо кэшировать готовый `PaletteActivityDescriptor` (Шаг 3) — тогда атрибуты читаются один раз.

#### Шаг 5. Удешевить тулбокс для пакетных активностей

- `WFToolboxHelper.Build()` разделить: `BuildModel` (фон) и `BuildControl` (UI).
- Проверить, можно ли создавать `ToolboxItemWrapper` вне UI-потока (это model-объекты `System.Activities.Presentation.Toolbox`, а не визуальные); если да — перенести создание wrappers в фон, а на UI оставить только `wfToolbox.Categories.Add` + `SetToolboxControl`.
- Отложить загрузку иконок/дизайнеров: не резолвить `ToolboxBitmapAttribute`/`DesignerAttribute` в момент сборки, а лениво при первом раскрытии категории/показе элемента.
- Разобраться с ~6 с в `wfToolbox.Categories.Add` для Browser (realization) — вероятно, это следствие резолва дизайнеров; ленивая загрузка должна его убрать.

#### Шаг 6. Первичная сборка при старте — через тот же механизм

`InitializeLayoutRoot`/`CreateDefaultLayout` сейчас строят палитру синхронно. Направить их в тот же `RefreshActivityPalette`-конвейер: окно показывается сразу, палитра появляется с прогрессом, затем подменяется.

### 0.6 Риски и ограничения

- `WorkflowDesigner` и `ToolboxControl` тред-аффинны к UI: в фон выносим только рефлексию/реестр/каталог и (по возможности) model-обёртки.
- Параллельный вызов `RefreshActivityPalette` (например, install пакета во время старта) — защититься флагом «пересборка уже идёт» или последним победителем, чтобы не гонять два ребилда одновременно.
- Статический кэш дескрипторов обязан сбрасываться в `InvalidateDefault()`/`ResetUserLibraries()`/`ResetPackageLibraries()`, иначе agent-каталог и панель разойдутся с новыми активностями.

### 0.7 Приёмка (для этого блока)

- Во время пересборки UI отзывчив, в панели «Активности» виден спиннер.
- Новая палитра подменяется атомарно, старая не «мигает» пустотой.
- После прогрева кэша полная пересборка (реестр + каталог + тулбокс) < 1–2 с.
- Повторный рефреш (install/uninstall/новая активность) не перечитывает уже известные типы.
- Регрессия: `AiRpaStudio.Tests\WFToolboxHelperTests`, `ToolboxDragContractTests`, `ToolboxThemeTests`, `ActivityCatalogTests`, `ActivityPaletteRefreshTests`, `NuGetPaletteSyncTests`, `NuGetProjectIntegrationTests` — зелёные.

## 1. Контекст и симптом

Приложение стартует ~59 секунд. Замер из `AiRpaStudio\bin\Debug\log.txt`:

| Метка | Время (total) | Комментарий |
| --- | --- | --- |
| `MainView: InitializeControls()` | 2067 ms | создание контролов, в т.ч. NuGet-панели |
| `LoadVsProjectIntoStudio: RestoreSessionState()` | **+42866 ms** | основная задержка |
| `MvvmFactory.CreateWindow()` | **+12827 ms** | вторая задержка |
| `StartupCore: EntryPoint.Run()` завершён | **59251 ms** | итог |

Ключевые факты из лога:

- `Search: query="Activities.Browser"` отправлен в `22:12:52.052`, результат пришёл только в `22:13:48.249` — **один локальный поиск занял ~56 секунд**.
- `GetVersions` для `Activities.Browser` возвращает **56 версий**, для `ActivityLibraryBase` — **44 версии**; запросы повторяются несколько раз.
- README и иконки пакетов скачиваются сразу при старте, до того как панель NuGet реально открыта пользователем.
- Палитра «Активности» пересобирается в процессе восстановления сессии (`Палитра «Активности» пересобрана` в `22:13:34.707`).

## 2. Диагноз (первопричина)

Долго грузится не WPF/инициализация окна, а две операции, которые выполняются в момент загрузки проекта **до/во время показа окна**:

1. **Медленный поиск по локальному NuGet-источнику** `nuget_local`.
   `NuGetSearchService.SearchAsync` для локальной папки ходит через `PackageSearchResource.SearchAsync` без какого-либо кэша. Каждый запуск и каждый повторный запрос заново перебирает `.nupkg`.

2. **Синхронное восстановление сессии + пересборка палитры активностей пакетов.**
   `Main.Loaded` → `LoadVsProjectIntoStudio` → `ProjectManager.InitVsProjectTree` → `PropertyChanged(ProjectFolder)` → `MainView.OnProjectManagerPropertyChanged` → `nuGetPanel.SetProjectFolder(...)` (это запускает `LoadSources` → `SelectedSource` → `OnSourceChangedAsync` → `SearchAsync` для локального источника) **и** `ScanNuGetActivities(...)` (`NuGetActivityPaletteLoader.LoadFromProject` — `Assembly.LoadFrom` всех DLL пакетов + `GetTypes` + `RefreshActivityPalette`).

Обе ветки выполняются на UI-потоке и блокируют появление окна. `RestoreSessionState` дополнительно открывает сохранённые документы.

## 3. Затрагиваемые файлы

| Файл | Что делаем |
| --- | --- |
| `RPA.NuGet\Services\NuGetSearchService.cs` | кэш индекса локального источника; мемоизация `SearchAsync`/`GetVersionsAsync`/`GetPackageInfoAsync` |
| `RPA.NuGet\ViewModels\NuGetPanelViewModel.cs` | ленивый поиск; не запускать `SearchAsync`/`RefreshInstalledAsync(checkUpdates:true)` при старте |
| `AiRpaStudio\Views\MainView.xaml.cs` | отложить `SetProjectFolder`/`ScanNuGetActivities`; сканирование палитры в фоне |
| `AiRpaStudio\Core\Workflow\NuGetActivityPaletteLoader.cs` | вынести тяжёлое сканирование DLL из UI-потока |
| `AiRpaStudio\Models\Main.cs` | (опционально, этап 4) асинхронная загрузка проекта/сессии после показа окна |
| `nuget_local\` (данные) | операционная чистка: сократить число версий пакетов |

## 4. Этапы правок

### Этап 0. Быстрая мера: не блокировать появление окна (низкий риск, заметный UX-выигрыш)

**Файл:** `AiRpaStudio\Views\MainView.xaml.cs`

- `OnProjectManagerPropertyChanged` (строки ~171–177) сейчас сразу делает:
  - `_ = nuGetPanel.SetProjectFolder(...)` — при этом `LoadSources` → `SelectedSource` → `OnSourceChangedAsync` → `SearchAsync`;
  - `ScanNuGetActivities(...)`.
- Изменение: выполнять эти действия после показа окна, через `Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ...)`, а не в момент смены `ProjectFolder`. Окно успевает отрисоваться до старта тяжёлой работы.

Результат: главное окно появляется за ~2 с, «висящего» старта нет.

### Этап 1. Кэш индекса локального NuGet-источника (главный выигрыш по времени)

**Файл:** `RPA.NuGet\Services\NuGetSearchService.cs` (+ новый тип в `RPA.NuGet\Services`)

- Добавить `LocalPackageIndexCache`:
  - для `IsLocal == true` один раз читать каталог источника и строить индекс `packageId → версии + базовые метаданные` (из `.nuspec` внутри `.nupkg` через `System.IO.Compression`);
  - сохранять индекс на диск (`%LOCALAPPDATA%\AiRpaStudio\NuGetIndex\<hash>.json`);
  - инвалидировать по хэшу файлов/`LastWriteTimeUtc`/набору `.nupkg`.
- `SearchAsync`, `GetVersionsAsync`, `GetPackageInfoAsync` для локального источника должны идти через кэш, а не через `PackageSearchResource` / `FindPackageByIdResource`.
- Добавить мемоизацию в памяти на время жизни `NuGetSearchService` (повторные `GetVersions`/одинаковые `Search` не ходят в источник).

Результат: локальный поиск из ~56 с становится миллисекундами после первого прогрева кэша; повторный запуск быстрый.

### Этап 2. Убрать лишние запросы при старте (низкий риск)

**Файл:** `RPA.NuGet\ViewModels\NuGetPanelViewModel.cs`

- `LoadProject` (строки ~564–591): убрать `_ = RefreshInstalledAsync(checkUpdates: true)` при первичной загрузке. Обновления вычислять лениво — при переходе на вкладки Installed/Updates.
- `SelectedSource` / `OnSourceChangedAsync` (строки ~401–431, ~1458–1464): при старте не вызывать `SearchAsync`, пока панель NuGet не показана пользователем. Поиск запускать при первом открытии панели (и уже после прогрева кэша из этапа 1).
- Скачивание README/иконок отложить до фактического выбора элемента пользователем (а не «первого результата» при автопоиске).

Результат: при старте нет длинного `Search` и серии `GetVersions`/`Download nupkg`/`Icon download`.

### Этап 3. Сканирование активностей пакетов — в фоне (средний риск)

**Файлы:** `AiRpaStudio\Views\MainView.xaml.cs`, `AiRpaStudio\Core\Workflow\NuGetActivityPaletteLoader.cs`

- `NuGetActivityPaletteLoader.LoadFromProject` выполняет `Assembly.LoadFrom` всех DLL пакетов и `BuildCategory` (`GetTypes`) — это CPU/IO, не для UI-потока.
- Изменение: `MainView.ScanNuGetActivities` запускать `LoadFromProject` в `Task.Run`, результат (`NuGetPaletteScanResult.Changed`) возвращать на UI-поток и только там вызывать `RefreshActivityPalette()`.

Результат: пересборка палитры не блокирует отрисовку окна и не растягивает `RestoreSessionState`.

### Этап 4. Асинхронная загрузка проекта/сессии (высокий риск, максимальный UX)

**Файл:** `AiRpaStudio\Models\Main.cs`

- `Loaded()` (строки ~105–130) сейчас синхронно вызывает `LoadVsProjectIntoStudio`/`LoadProject`.
- Цель: показывать окно, затем грузить проект/сессию в фоне; открытие вкладок и обновление дерева — через `Dispatcher`.
- Внимание: `WorkflowDesigner` тред-аффинен к UI; в фон можно выносить только IO/парсинг/сканирование пакетов, а создание/привязку дизайнеров оставлять на UI-потоке.

Результат: окно доступно сразу; прогресс-индикатор вместо «замороженного» старта.

### Этап 5. Операционная гигиена локального фида

**Файл:** `nuget_local\` (данные)

- В `nuget_local` много версий одного пакета (`Activities.Browser` — 56, `ActivityLibraryBase` — 44). `GetVersions` перебирает их все.
- Рекомендация: оставить только актуальные версии либо положиться на кэш из этапа 1 (после кэширования число версий перестаёт быть проблемой).

## 5. Порядок внедрения

1. **Этап 0** — окно появляется сразу (быстрый UX-фикс).
2. **Этап 1** — кэш индекса локального источника (убирает ~56 с поиска).
3. **Этап 2** — ленивые NuGet-запросы (убирает дубли и лишнюю работу).
4. **Этап 3** — фоновое сканирование активностей пакетов.
5. **Этап 4** — асинхронная загрузка проекта (по необходимости, после 0–3).
6. **Этап 5** — чистка фида (опционально).

## 6. Критерии приёмки / метрики

Базовые значения (из `log.txt`): полный старт **59251 ms**, `RestoreSessionState` **42866 ms**, `MvvmFactory.CreateWindow` **12827 ms**.

Целевые значения после правок:

- Главное окно отображается **< 2 с** после запуска.
- Полная готовность (проект + палитра) на тёплом кэше — **< 5 с**.
- Первый запуск после изменения `nuget_local` — прогрев индекса **< 2 с**.
- В логе при старте нет `Search`/`GetVersions`/`Icon download`/`README load`, пока панель NuGet не открыта.
- Регрессия: существующие тесты панели NuGet (`AiRpaStudio.Tests\NuGet*Tests.cs`) и стражи палитры остаются зелёными.

## 7. Как проверять

- Перезапустить студию, измерить по `StartupTimer`-меткам в `log.txt`.
- Повторить запуск второй раз (тёплый кэш) и сравнить с базовыми 59251 ms.
- Открыть панель NuGet вручную и убедиться, что поиск/версии/README работают как раньше.
- Прогнать `AiRpaStudio.Tests` (особенно `NuGetSourcesAndSearchTests`, `NuGetPanelViewModelTests`, `NuGetReadmeLoadTests`, `NuGetProjectIntegrationTests`).
