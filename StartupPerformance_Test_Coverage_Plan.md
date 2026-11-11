# План покрытия тестами оптимизаций запуска AiRpaStudio

> Дополняет `StartupPerformance_Fix_Plan.md`. Цель — не «проверить, что быстро», а **зафиксировать
> тестами каждую сделанную оптимизацию и гарантировать, что она действительно используется**
> (поведением и стражами исходников). План написан по фактическому коду репозитория.

## 0. Резюме

Оптимизации запуска уже внесены в код, но часть из них держится «на доверии»: легко вернуться
к синхронной пересборке палитры, к запросу обновлений при старте NuGet-панели или перестать читать
дисковый кэш — и ни один тест это не заметит. Этот план закрывает пробелы двумя уровнями:

1. **Поведенческие юнит-тесты** — проверяют, что кэши работают, инвалидируются и отдают те же данные.
2. **Стражи исходников** (по образцу `SdkPinGuardTests` / `RunnerSourceGuardTests`) — проверяют,
   что в точках вызова остаётся именно оптимизированный путь (`Task.Run`, `ApplicationIdle`,
   `checkUpdates: false`, `PersistDiskCache`, `NotifyPanelShown`).

Правила TDD из `AGENTS.md` обязательны: сначала падающий тест, затем правка/рефакторинг, в конце —
зелёный прогон `AiRpaStudio.Tests` через MSBuild + vstest (не `dotnet test`).

## 1. Уровни тестов и соглашения

| Уровень | Что проверяет | Пример |
| --- | --- | --- |
| Юнит (поведение) | Логика без UI/сети: кэши, инвалидация, мемоизация, разбор | `PaletteDiskCacheTests`, `LocalPackageIndexCacheTests` |
| Юнит (ViewModel) | `NuGetPanelViewModel` со стабами `NuGetSearchService` | «LoadProject не ходит в источник» |
| Стража исходников | Скан `.cs`/`.xaml`/`.csproj` на наличие/отсутствие паттернов | «нет синхронного `WFToolboxHelper.Build()`» |

- Все новые тесты — в `AiRpaStudio.Tests`.
- Внутренние типы доступны тестам: `InternalsVisibleTo("AiRpaStudio.Tests")` уже есть у
  `AiRpaStudio` и `RPA.NuGet`.
- Минимальные тестовые швы (если нужны) помечаются `internal` и не меняют поведение в проде.

## 2. Карта фич → текущее покрытие → целевое покрытие

| # | Фича (где) | Сейчас есть | Пробел |
| --- | --- | --- | --- |
| 1 | Дисковый кэш локального NuGet-индекса (`RPA.NuGet\Services\LocalPackageIndexCache.cs`) | `NuGetSourcesAndSearchTests` косвенно гоняет `SearchAsync` по папке | Нет прямых тестов кэша: fingerprint, файл, повторное использование, инвалидация |
| 2 | Мемоизация `SearchAsync`/`GetVersionsAsync`/`GetPackageInfoAsync` (`NuGetSearchService.cs`) | нет | Нет проверки, что повторный вызов не перестраивает список |
| 3 | `LoadProject` без проверки обновлений + ленивый поиск (`NuGetPanelViewModel.cs`) | `NuGetPanelViewModelTests` проверяют заполнение Installed | Нет проверки «при старте нет запросов в источник» и «NotifyPanelShown откладывает поиск» |
| 4 | Отложенная инициализация NuGet-панели до `ApplicationIdle` (`MainView.xaml.cs`) | нет | Нет стражи на `DispatcherPriority.ApplicationIdle` |
| 5 | Фоновое сканирование пакетных активностей (`ScanNuGetActivitiesAsync`) | `NuGetPaletteSyncTests`, `NuGetProjectIntegrationTests` проверяют сам loader | Нет стражи на `Task.Run` в `MainView` |
| 6 | Асинхронная загрузка проекта/сессии (`Main.cs`) | нет | Нет стражи на `Task.Run` + `OnUiThreadAsync` |
| 7 | Фоновая пересборка палитры (`RefreshActivityPaletteAsync`) | `ActivityPaletteRefreshTests`, `WFToolboxHelperTests` покрывают результат | Нет стражи на `Task.Run(BuildPaletteModel)` и на отсутствие синхронного `Build()` |
| 8 | Кэши рефлексии (`DescriptorCache`, `LocalizedStringCache`, `ResourceManagerCache`, `AttributeDataCache`) | `ActivityPaletteRegistryTests`, `ActivityCatalogTests` покрывают корректность | Нет теста «повторный Describe не рефлексирует заново» и стражи на использование кэшей |
| 9 | Дисковый кэш палитры `PaletteDiskCache` (п. 3.1 плана — уже реализован) | нет | Нет ни одного теста: ключ, roundtrip, инвалидация, чтение на холодном старте |
| 10 | `WFToolboxHelper.BuildModel`/`BuildControl` (разделение фон/UI) | `WFToolboxHelperTests` | Нет стражи «в `MainView` не осталось вызова `Build()`» и «`BuildControl` собирается из `BuildModel`» |
| 11 | Staging-десериализация раскладки в `PrepareLayout` (п. 3.3 частично) | нет | Стража на staging-`DockingManager` (гарантия, что не вернули синхронный `Deserialize` в реальный `dockManager`) |

## 3. Детальный план тестов

### 3.1. `LocalPackageIndexCache` — дисковый кэш локального фида

**Файл:** `AiRpaStudio.Tests\NuGetLocalPackageIndexCacheTests.cs`

Тестовый стенд: временная папка с одним `.nupkg` (сборка `.nupkg` через `ZipArchive`, как в
`NuGetSourcesAndSearchTests.BuildPackage()`).

| Тест | Проверка |
| --- | --- |
| `Entries_BuildsIndexFromNupkg` | id/version/description читаются из `.nuspec`, пререлиз помечается |
| `Entries_SortsByIdAndVersionDesc` | несколько версий одного пакета → самая свежая первой |
| `Entries_WritesCacheFile_AndSecondInstanceReusesIt` | после первого чтения файл кэша существует; второй экземпляр на том же каталоге возвращает те же данные без перезаписи (сравнивается `LastWriteTimeUtc`) |
| `CorruptedCacheFile_IsRebuilt` | подменить JSON мусором → `Entries` всё равно строится |
| `FingerprintChanges_InvalidatesCache` | добавить/изменить `.nupkg` → кэш пересобирается (файл перезаписан/другой отпечаток) |
| `MissingDirectory_ReturnsEmpty` | несуществующий каталог → пусто, без исключений |

**Тестовый шов:** для надёжной проверки «не пересобирается» сделать `internal int BuildCount { get; }`
(инкремент в `BuildEntries`) или `internal static string ComputeFingerprint`/`GetCachePath`. Без шва
тест `LastWriteTimeUtc` допустим, но шов стабильнее.

### 3.2. `NuGetSearchService` — мемоизация локального источника

**Файл:** `AiRpaStudio.Tests\NuGetSearchServiceMemoizationTests.cs` (или дополнить
`NuGetSourcesAndSearchTests`).

| Тест | Проверка |
| --- | --- |
| `SearchAsync_SecondCall_ReturnsSameListInstance` | `await SearchAsync("Local", ...)` дважды → `Assert.That(second, Is.SameAs(first))` |
| `GetVersionsAsync_SecondCall_ReturnsSameInstance` | тот же принцип |
| `GetPackageInfoAsync_SecondCall_ReturnsSameInstance` | тот же принцип (по найденному пакету) |
| `DifferentQuery_UsesDifferentMemoKey` | `SearchAsync("A")` и `SearchAsync("B")` не отдают один и тот же объект |

Это чистые поведенческие тесты без изменения продакшн-кода (мемо отдаёт один и тот же объект).

### 3.3. `NuGetPanelViewModel` — старт без запросов и ленивый поиск

**Файл:** `AiRpaStudio.Tests\NuGetStartupLazinessTests.cs`

Использовать расширенный стаб `NuGetSearchService`, который переопределяет и **считает** все три
метода: `SearchAsync`, `GetVersionsAsync`, `GetPackageInfoAsync`.

| Тест | Проверка |
| --- | --- |
| `LoadProject_DoesNotContactSource` | загрузить csproj с `PackageReference` → счётчики всех трёх методов = 0 (гарантия `RefreshInstalledItems(checkUpdates:false)`) |
| `SourceChange_BeforePanelShown_DoesNotSearch` | выставить `SelectedSource` до `NotifyPanelShown()` → `SearchAsync` = 0 |
| `NotifyPanelShown_FiresDeferredSearch_Once` | после `NotifyPanelShown()` → ровно один поиск; повторный вызов не добавляет |
| `NotifyPanelShown_RemoteEmptyQuery_DoesNotSearch` | удалённый источник + пустой запрос → поиска нет (контракт `IsLocalSourceSelected`/`SearchQuery`) |

### 3.4. Стражи точек вызова в студии (`MainView` / `Main`)

**Файл:** `AiRpaStudio.Tests\StartupPerformanceGuardTests.cs` (по образцу `RunnerSourceGuardTests`).

Скан только `AiRpaStudio\Views\MainView.xaml.cs` и `AiRpaStudio\Models\Main.cs` с исключением
`bin/obj`.

| Тест | Правило |
| --- | --- |
| `MainView_DefersNuGetInitToApplicationIdle` | в `OnProjectManagerPropertyChanged` есть `DispatcherPriority.ApplicationIdle` |
| `MainView_ScansPackageActivitiesInBackground` | в `ScanNuGetActivitiesAsync` есть `Task.Run(...LoadFromProject...)` |
| `MainView_RebuildsPaletteInBackground` | в `RefreshActivityPaletteAsync` есть `Task.Run(BuildPaletteModel)` |
| `MainView_HasNoSynchronousToolboxBuild` | в `MainView.xaml.cs` нет `new WFToolboxHelper().Build()` / `.Build()` вызова палитры |
| `Main_LoadsProjectAsync` | в `Main.Loaded` вызывается `LoadProjectIntoStudioAsync`, а в `LoadProjectAsync`/`LoadVsProjectIntoStudioAsync` есть `Task.Run(BuildFileTree/BuildVsProjectTree)` и `OnUiThreadAsync` |
| `BuildPaletteModel_PersistsDiskCache` | в `BuildPaletteModel` вызывается `registry.PersistDiskCache()` |
| `NuGetPanel_ShownHookIsWired` | `RPA.NuGet\Views\NuGetPanel.xaml.cs` вызывает `_viewModel.NotifyPanelShown()` из `Loaded` |

### 3.5. Кэши рефлексии `ActivityPaletteRegistry`

**Файл:** `AiRpaStudio.Tests\Generation\ActivityPaletteRegistryCacheTests.cs` (дополнить к
существующим `ActivityPaletteRegistryTests`).

| Тест | Проверка |
| --- | --- |
| `Describe_SameTypeAcrossRegistries_ReturnsSameDescriptor` | два реестра по одному небольшому набору категорий (без дискового кэша) → `Describe(typeof(Sequence))` возвращает тот же экземпляр (работает `DescriptorCache`) |
| `GetAttributeSafe_DoesNotThrowOnNonResolvableAttributes` | тип с «чужим» атрибутом → метод возвращает атрибут/`null` без падения (уже косвенно покрыто, зафиксировать явно) |
| `Describe_NonActivity_ReturnsNull` | уже есть, не дублировать |

**Стража** (в `StartupPerformanceGuardTests`): в `ActivityPaletteRegistry.cs` присутствуют
`DescriptorCache.GetOrAdd`/`TryGetValue`, `LocalizedStringCache.GetOrAdd`, `ResourceManagerCache.GetOrAdd`,
`AttributeDataCache.GetValue`; отсутствует прямой `GetCustomAttribute<T>` в горячем пути
`DescribeArgument`/`DescribeContainer` (гарантия сырого `CustomAttributeData`).

### 3.6. Дисковый кэш палитры `PaletteDiskCache`

**Файл:** `AiRpaStudio.Tests\Generation\PaletteDiskCacheTests.cs`

| Тест | Проверка |
| --- | --- |
| `ComputeKey_IsDeterministic` | два вызова на одинаковом наборе категорий → одинаковый ключ |
| `ComputeKey_ChangesWhenTypeSetChanges` | добавить тип → ключ меняется |
| `SaveLoad_RoundtripPreservesDescriptor` | `Save` → `Load` возвращает эквивалентный дескриптор (Key, DisplayName, Category, Type, Arguments, Containers) |
| `Load_MissingKey_ReturnsNull` | нет файла → `null` |
| `Load_CorruptedJson_ReturnsNull` | битый файл → `null`, без исключения |
| `Invalidate_RemovesCacheFiles` | после `Save` вызвать `Invalidate` → файлов `*.json` нет |
| `Registry_ColdStart_ReadsDiskCache` | прогреть кэш через реестр №1 + `PersistDiskCache`; реестр №2 на том же составе должен взять дескрипторы из диска (нужен `internal` шов — см. ниже) |

**Тестовый шов** (минимальный): добавить в `ActivityPaletteRegistry` read-only
`internal bool DiskCacheLoaded { get; }` и `internal int DiskCacheMisses { get; }` (поля уже есть).
Тогда тест холодного старта: реестр №2 → `DiskCacheLoaded == true && DiskCacheMisses == 0`.

> Внимание: тест обязан использовать свой уникальный набор категорий/типов или чистить
> `%LOCALAPPDATA%\AiRpaStudio\PaletteCache` в `[TearDown]`, чтобы не зависеть от реального кэша машины.

### 3.7. `WFToolboxHelper` — разделение фоновой и UI-фазы

`WFToolboxHelperTests` уже проверяет сортировку и `BuildModel`→`BuildControl` на пробном наборе.
Добавить:

| Тест | Проверка |
| --- | --- |
| `BuildModel_PreloadsToolboxItem_SoBuildControlIsFastPath` | после `BuildModel` у каждой `ToolboxItemWrapper` `IsValid == true` ДО `BuildControl` (гарантия, что дорогая резолвция ToolboxItem ушла в фон) |
| `BuildControl_CompletesFromPreload` | `BuildControl` даёт рабочий `ToolboxControl` с `IsValid` и `Type != null` (уже есть, оставить как регрессию) |

**Стража** — см. 3.4 (`MainView_HasNoSynchronousToolboxBuild`).

### 3.8. `PrepareLayout` — staging-десериализация (частично сделанная оптимизация)

Пока фича не доведена до конца, фиксируем **стражей** (в `StartupPerformanceGuardTests`):

| Тест | Правило |
| --- | --- |
| `MainView_PrepareLayout_UsesStagingDockingManager` | в `PrepareLayout` создаётся отдельный `new DockingManager()` (staging), а реальный `dockManager` получает раскладку позже; нет прямого `new XmlLayoutSerializer(dockManager).Deserialize(...)` в конструкторе/`Show` |

После завершения п. 3.3 плана — добавить поведенческий тест на подъём доков через `Dispatcher`.

## 4. Сводный список новых файлов тестов

| Файл | Покрывает пункты |
| --- | --- |
| `NuGetLocalPackageIndexCacheTests.cs` | 3.1 |
| `NuGetSearchServiceMemoizationTests.cs` | 3.2 |
| `NuGetStartupLazinessTests.cs` | 3.3 |
| `StartupPerformanceGuardTests.cs` | 3.4, 3.5 (стража), 3.7 (стража), 3.8 |
| `Generation\ActivityPaletteRegistryCacheTests.cs` | 3.5 |
| `Generation\PaletteDiskCacheTests.cs` | 3.6 |

## 5. Порядок внедрения

1. **Стражи** (3.4, 3.7, 3.8) — самые дешёвые и сразу ловят регресс в точках вызова.
2. **`PaletteDiskCacheTests`** (3.6) — закрывает главную не протестированную оптимизацию холодного старта.
3. **`LocalPackageIndexCacheTests` + мемоизация** (3.1, 3.2).
4. **`NuGetStartupLazinessTests`** (3.3).
5. **`ActivityPaletteRegistryCacheTests`** (3.5).
6. Прогнать `AiRpaStudio.Tests` целиком, добиться зелёного, затем по необходимости рефакторить.

## 6. Критерии приёмки плана

- Каждая фича из раздела 2 плана правок и реализованный п. 3.1 покрыты минимум одним тестом или стражей.
- Стражи падают, если убрать оптимизацию (проверить «негативом»: временно заменить `Task.Run` на
  синхронный вызов / `checkUpdates:false` на `true` — соответствующий тест обязан упасть).
- `MSBuild.exe AiRpaStudio.Tests\AiRpaStudio.Tests.csproj -t:Build` — 0 ошибок.
- `vstest.console.exe AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll` — все тесты зелёные.
- Регрессионные тесты из раздела 5 `StartupPerformance_Fix_Plan.md` остаются зелёными.
