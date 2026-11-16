# Правила проекта RPA.NuGet

> Фрагмент иерархии правил решения `AiRpa.sln`. Глобальные правила и общий индекс —
> в корневом `AGENTS.md`. Этот файл поднимается в контекст только при работе в `RPA.NuGet`.

## Панель NuGet (`RPA.NuGet`)

- Дизайн — как менеджер пакетов Visual Studio 2026, но **без скруглений**: во всех `Border` и
  шаблонах `CornerRadius="0"` (панели студии плоские, ср. `Helpers\PropertyWindowStyleHelper.cs`).
  Токены `StudioPalette` (`8/4/6`) не меняются — они про окно, вкладки и меню.
- Цвета — только ключами `Studio.*` из `StudioBrushes` (`Surface/SurfaceAlt/Hover/Selected/
  Separator/SeparatorStrong/Foreground/ForegroundMuted` + `Studio.Semantic.Validation`), шрифт —
  только `{DynamicResource Studio.FontFamily}`, иконки — векторные `Path` (новых зависимостей в
  `RPA.NuGet` нет). Стражи: `AiRpaStudio.Tests\NuGetPanelThemeTests.cs`.
- Оверлей «Package sources» (`SourcesOverlay`) накрывает строки 0–2 корневого `Grid` (`Grid.RowSpan="3"`), а не строку состояния: её непрозрачная полоса с 1px-разделителем рисовалась поверх оверлея и «резала» подпись «Project sources are stored in AiRpaStudio…». Страж — `AiRpaStudio.Tests\NuGetPanelLayoutTests.cs` (`SourcesOverlay_FootnoteIsNotClippedByStatusBar`).

- Выбор источника в комбобоксе персистится **на воркспейс** в настройке `NuGetSelectedSources`
  (JSON-словарь `workspaceFolder → sourceUrl`); парсинг/запись — `Core\NuGetSourceSelectionStore.cs`,
  панель читает/пишет через хуки `NuGetPanelViewModel.LoadSelectedSourceUrl`/`SaveSelectedSourceUrl`
  (сам `RPA.NuGet` на студию не ссылается). Стражи — `NuGetSourceSelectionStoreTests`,
  `NuGetSourcesAndSearchTests.SelectedSource_*`.
- Панель открывается **вкладкой панели документов** (`MainView.ShowNuGetPanel`, `ContentId = "NuGet"`),
  а не боковым доком: там помещается двухколоночный макет «список | детали». Узкая ширина
  (< 640) включает режим, где детали показываются вместо списка. Ветка восстановления раскладки
  для `"NuGet"` обязана стоять до отмены `LayoutDocument`. Стражи:
  `AiRpaStudio.Tests\NuGetPanelDocumentTabTests.cs`.
- Правила состояния пакета (какое действие предлагает кнопка, подписи версий и downloads) живут в
  `Services\NuGetPackageStateResolver.cs` без WPF и сети — там же их тесты
  (`NuGetPackageStateTests.cs`). План и таблица этапов — `dev-docs\NuGet_Panel_Redesign_Plan.md`.
- Шеврон блока «Options» в панели деталей — **кнопка без фона** с иконкой Material
  (`materialIcons:MaterialIcon Kind="ChevronRight"` → `ChevronDown` по `IsChecked`). В шаблоне шапки
  запрещены триггеры `IsMouseOver`/`IsPressed`: наведение не меняет ни фон, ни цвет иконки.
  Стражи — `AiRpaStudio.Tests\NuGetOptionsChevronTests.cs`.
- README пакета читается из `.nupkg`, скачанного **из выбранного источника**
  (`NuGetSearchService.DownloadNupkgAsync`), с запасной версией и кешем `id|version`; причина неудачи
  остаётся в `NuGetPackageDetailsService.LastReadmeError` и показывается в строке состояния.
  Молчаливый `catch` вокруг README запрещён: из-за него вкладка README «пропадала у всех пакетов».
  Стражи — `AiRpaStudio.Tests\NuGetReadmeLoadTests.cs`.

- Шапка правой колонки берёт картинку пакета **только** через `PackageIcon`/`HasPackageIcon`/`PackageMonogram`
  (`NuGetPackageDetailsViewModel`): прямая привязка `Item.Icon` не обновляется, потому что `Details` — один
  экземпляр на всю панель, `Item` присваивался без уведомления, а иконка приходит позже (`LoadIconAsync`).
  Фолбэк — монограмма (первая буква идентификатора). Стражи: `NuGetPackageStateTests.Details_HeaderShowsIconOrMonogramFallback`,
  `NuGetPanelDocumentTabTests.Panel_DetailsHeaderShowsPackageIcon`.
- Кнопки плашки списка: колонка **37px** (прежние 56px, суженные в 1,5 раза), кнопки 16×16, глифы 10×10;
  удаление — `MaterialIcon Kind="Close"` (крестик), установка/обновление — `Kind="Download"`; 16 + 16 + 5 = 37.
  Стражи: `NuGetPanelLayoutTests.RowIconButtons_AreSizedForNarrowColumn`, `...Panel_RowButtonsFitNarrowColumn`.

- Фокус в поле поиска ставится **отложенно** (`FocusSearchBoxDeferred` → `DispatcherPriority.Input`) и только
  при живом `PresentationSource`: синхронный `Focus()` в `Loaded` падал `NullReferenceException` в
  `HwndKeyboardInputProvider.AcquireFocus`, потому что `Loaded` приходит из `HwndTarget.OnResize` (WM_SIZE —
  разворачивание окна, восстановление раскладки доков). Автофокус однократный (переезд вкладки в боковой док
  фокус пользователя не перебивает), Ctrl+L идёт через ту же защищённую точку.
  Страж — `AiRpaStudio.Tests\NuGetPanelFocusTests.cs`.

- Список источников пакетов проекта — **собственное per-project хранилище AiRpaStudio**, а не
  `NuGet.Config` проекта: JSON-словарь `workspaceFolder → [sources]` в `%AppData%\AiRpaStudio\NuGet\nuget_sources.json`
  (`Core\NuGetProjectSourcesStore.cs` + `Core\NuGetPaths.cs`), панель читает/пишет хуками
  `LoadProjectSources`/`SaveProjectSources`. Студия **никогда** не создаёт и не правит `NuGet.Config`
  в каталоге проекта автоматически.
- Внешние источники из `NuGet.Config` (машина/пользователь/решение) читаются **только на чтение**
  (`NuGetSourcesService.LoadMachineAndUserSources`/`LoadSolutionSources`) и показываются в оверлее
  отдельной секцией; их можно импортировать копией в источники проекта (`ImportSourceCommand`).
- Канонические места конфигов NuGet: машинные — `%ProgramFiles(x86)%\NuGet\Config\*.config` и
  `%ProgramFiles%\NuGet\Config\*.config`; пользовательский — `%AppData%\NuGet\NuGet.Config` (или
  `NUGET_CONFIG_FILE`, если задан); уровня решения — `NuGet.Config`/`nuget.config` в папке решения
  и всех родительских папках до корня диска. Источник помечается `Origin` (Machine/User/Solution),
  `SourceConfigPath` хранит путь к файлу, в оверлее импорта источники сгруппированы по `OriginLabel`.
- Создание/редактирование/экспорт `NuGet.Config` — только по явным кнопкам оверлея
  (`CreateNuGetConfigCommand`, `ExportProjectSourcesToConfigCommand`, `OpenConfigFileCommand`), через
  `NuGetSourcesService.WriteConfig`/`Save`. Жёстких URL nuget.org в рабочем коде нет: поиск/версии/
  скачивание идут только через выбранный источник, всё логируется через `NuGetLog`.
  Стражи — `NuGetProjectSourcesStoreTests`, `NuGetSourcesConfigTests`,
  `NuGetPanelViewModelTests.Sources_WithoutConfig_DoesNotCreateNuGetConfig`,
  `NuGetPanelLayoutTests.IconUrl_NoFallbackWhenSourceHasNoIconUrl`.

