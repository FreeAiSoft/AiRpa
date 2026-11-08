# План: менеджер пакетов NuGet в стиле Visual Studio 2026 (дизайн без скруглений)

## Метаданные

| Поле | Значение |
|---|---|
| Задача | Повторить дизайн и функционал менеджера пакетов Visual Studio 2026 в панели `RPA.NuGet`, но с прямыми углами |
| Область изменений | `RPA.NuGet` (модели, сервисы, модели представления, разметка), `AiRpaStudio\Views\MainView.xaml.cs` (размещение панели), `AiRpaStudio.Tests` (стражи) |
| Вне области | `NuGetProjectConfig`, `NuGetVersionResolver`, `NuGetPackageDownloader` (расширяются аддитивно), глобальные токены `StudioPalette` (радиусы окна/меню не меняются) |
| Требования | прямые углы (`CornerRadius="0"`); один фон `Studio.Surface`; ноль акцента; только тёмная и светлая темы; шрифт Roboto; панель открывается вкладкой панели документов |
| Статус | этапы 1–4 реализованы и покрыты тестами; этап 5 (источники из `NuGet.Config`, пакетное обновление, restore) реализован, остаётся ручная приёмка UI |

---

## 1. Решение по размещению

В Visual Studio менеджер пакетов — отдельное широкое окно. В студии панель **открывается вкладкой
панели документов** (`docGroup`): область документа широкая, и двухколоночный макет («список |
детали») читается так же, как на эталонном скриншоте. Прежнее размещение в правой боковой группе
(`rightLayoutAnchorablePaneGroup`, `DockWidth = 600`) удалено: там панель делила высоту с
«Свойствами», «Логом» и «Чатом».

Вкладка остаётся переносимой: если пользователь утащит её в боковой док, панель переходит в
**узкий режим** — детали показываются вместо списка, кнопка «назад» возвращает к списку.

Восстановление раскладки (`MainView.InitializeLayoutRoot`): ветка `contentId == "NuGet"` стоит
**до** отмены `LayoutDocument`, иначе сохранённая вкладка NuGet терялась. Повторное открытие ищет
вкладку по `ContentId` (`MainView.NuGetContentId`) и переиспользует содержимое — второй экземпляр
панели не создаётся.

---

## 2. Дизайн

### 2.1 Каркас

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Browse   Installed   Updates                   NuGet Package Manager: Проект │ 38
├──────────────────────────────────────────────────────────────────────────────┤
│ [🔍 Search (Ctrl+L)         ⌄ ⧉ ☐ Include prerelease]    Package source: [nuget.org ⌄] ⚙ 🗀 │ 40
├──────────────────────────────────────────────────────────────────────────────┤
│ [icon] ★ Microsoft.Entit…Core.SqlServer by Microsoft             10.0.14    ║ [mono] Id / by … │
│        Microsoft SQL Server database provider for EF Core.                  ║ ℹ Package source mapping is off. Configure │
│ [icon]   Newtonsoft.Json by James Newton-King                    13.0.4     ║ Version: [Latest stable 10.0.14 ⌄] [Install] │
│ …                                                        (список, скролл)    ║ ⌄ Options                                   │
│                                                                             ║ README | Package Details                    │
│                                                                             ║ Description / Version / Author(s) / License │
│                                                                             ║ Date published / Downloads / Project URL    │
│                                                                             ║ Report Abuse / Tags                         │
├──────────────────────────────────────────────────────────────────────────────┤
│ Статус: «N updates available…»                                [────] Cancel  │ 24
└──────────────────────────────────────────────────────────────────────────────┘
       список (*)                          1px splitter        детали (0.65*)
```

| Зона | Содержимое | Файл/элемент |
|---|---|---|
| Заголовок | вкладки Browse / Installed / Updates, `ProjectHeader` (20) | `RadioButton` + `NuGet.TabButtonStyle` |
| Тулбар | поиск с плейсхолдером, очистка, `Include prerelease`, `Package source:`, шестерня (NuGet.Config), выбор проекта | `TextBox` + `NuGet.SearchTextBoxStyle`, `ComboBox` + `NuGet.ComboBoxStyle` |
| Список | виртуализованный `ListBox`: иконка 32×32 (или монограмм), ★ популярного, Id (bold 13), `by Authors`, описание (2 строки), downloads, версия, состояние | `NuGet.PackageRowTemplate` |
| Разделитель | hairline 1px, hover → `SeparatorStrong` | `GridSplitter` + `NuGet.SplitterStyle` |
| Детали | шапка (монограмм 48, Id 20, авторы, источник), плашка сопоставления источников, Version + большая кнопка, `Options`, псевдотабы README / Package Details, сетка метаданных | `Border x:Name="DetailsPanel"` |
| Статус | `StatusText`, `ProgressBar` 2px, `Cancel` | `NuGet.ProgressStyle` |

### 2.2 Правила оформления (обязательны)

1. **Прямые углы.** В панели нет ни одного скругления: во всех `Border`/шаблонах `CornerRadius="0"`
   (как у панели «Свойства», `PropertyWindowStyleHelper`). Токены `StudioPalette` (`8/4/6`) не
   меняются — они про окно, вкладки и меню студии.
2. **Один фон.** `Studio.Surface` у панели, строки, деталей и попапов. Различие зон — hairline
   `Studio.Separator` 1px и яркость.
3. **Ноль акцента.** Hover → `Studio.Hover`, выбранное/нажатое → `Studio.Selected`, текст →
   `Studio.Foreground`, подписи → `Studio.ForegroundMuted`. Кнопка действия серая:
   `SurfaceAlt` + рамка 1px `SeparatorStrong` (в VS 2026 она тоже не синяя).
4. **Шрифт.** Только `FontFamily="{DynamicResource Studio.FontFamily}"` (Roboto); размеры 20/13/12/11.
5. **Только ключи палитры.** Ни одного hex и named-цвета; иконки — векторные `Path`, чтобы не
   тянуть в `RPA.NuGet` новую зависимость.
6. **Семантика.** ★ популярного и плашка сопоставления источников — `ForegroundMuted` /
   `Studio.Semantic.Validation`; красный остаётся только у кнопки закрытия вкладки.

### 2.3 Адаптивность

| Ширина панели | Раскладка |
|---|---|
| ≥ 640 | `[список *] [1px] [детали 0.65*]` — режим вкладки документов |
| < 640 | одна колонка: список, при выборе пакета — детали на всё место + кнопка «назад» |

Порог — `NuGetPanel.CompactWidthThreshold` (640). Переключение делает `ApplyLayout(width)` в
code-behind: он же меняет ширины колонок и видимость блоков (у `ColumnDefinition` нет DataContext,
поэтому биндинг там не работает).

---

## 3. Функционал — паритет с эталоном

| Элемент VS 2026 | Реализация |
|---|---|
| Вкладки Browse / Installed / Updates | `NuGetPanelViewModel.ActiveTab`, `VisibleItems`; Updates — через `NuGetVersionResolver.SelectBestVersion` |
| `Search (Ctrl+L)`, `Esc`, отмена, «Load more» | `SearchCommand`, `LoadMoreCommand`, `CancellationTokenSource`, горячие клавиши в code-behind |
| `Include prerelease` | влияет на поиск и на проверку обновлений (`IncludePrerelease`) |
| `Package source:` + ⚙ | `NuGetSourcesService` (`NuGet.Configuration`: solution/user/global) + `SelectedSource` пересоздаёт `NuGetSearchService` |
| Плашка `Package source mapping is off. Configure` | `SourceMappingNotice` / `HasSourceMappingNotice`, ссылка открывает `NuGet.Config` |
| Иконка, ★, авторы, downloads | `IPackageSearchMetadata` → `NuGetPackageInfo` → `NuGetPackageItemViewModel`; иконки — `NuGetIconService` (память + `%LocalAppData%\AiRpaStudio\NuGetIcons`) |
| `Version: Latest stable X` + `Install` | `NuGetPackageDetailsViewModel.Versions/SelectedVersion/ActionText`; действие считает `NuGetPackageStateResolver` |
| `Options` | prerelease + `Run restore after install` |
| README / Package Details | `NuGetPackageDetailsService` (метаданные + README из `.nupkg` через `PackageArchiveReader`) |
| Установка / обновление / удаление | `NuGetInstallService` (скачивание для `packages.config`, правка проекта, опциональный `msbuild -t:restore`) |
| `Update all` | `UpdateAllCommand` (вкладка Updates) |
| Прогресс и отмена | `IsBusy`, `ProgressValue`, `CancelCommand` |

### 3.1 Состояние кнопки действия (`NuGetPackageStateResolver`)

| Выбранная версия | Подключённая версия | Кнопка |
|---|---|---|
| любая | нет | `Install` |
| выше | есть | `Update` |
| ниже | есть | `Downgrade` |
| равна | есть | `Uninstall` |
| — | — | `None` (кнопка недоступна) |

---

## 4. Файлы

**Добавлено (`RPA.NuGet`)**

```
Models\NuGetPackageVersionInfo.cs, NuGetPackageDetails.cs, NuGetPackageAction.cs,
       NuGetPackageTab.cs, NuGetSourceDefinition.cs
Services\NuGetPackageStateResolver.cs, NuGetPackageDetailsService.cs, NuGetSourcesService.cs,
         NuGetIconService.cs, NuGetInstallService.cs
Commands\RelayCommand.cs, AsyncRelayCommand.cs
Converters\NuGetConverters.cs
ViewModels\ObservableObject.cs, NuGetPackageItemViewModel.cs, NuGetPackageDetailsViewModel.cs
Views\NuGetPanel.Styles.xaml
```

**Переписано:** `Views\NuGetPanel.xaml`, `Views\NuGetPanel.xaml.cs`, `ViewModels\NuGetPanelViewModel.cs`,
`Services\NuGetSearchService.cs` (страницы поиска, карточки, версии с признаками), `Models\NuGetPackageInfo.cs`.

**Студия:** `AiRpaStudio\Views\MainView.xaml.cs` — вкладка документов вместо боковой панели.

**Не изменилось:** публичный API панели `SetProject` / `SetProjectFolder`, `NuGetProjectConfig`,
`NuGetVersionResolver`, `NuGetPackageDownloader`.

---

## 5. Этапы и статус

| Этап | Объём | Статус |
|---|---|---|
| 1 | Каркас: вкладка документов, три зоны, стили без скруглений, узкий режим | ✅ |
| 2 | Список: иконки, авторы, downloads, описание, версия, плейсхолдер пустого списка | ✅ |
| 3 | Детали: версии, кнопка действия, Options, вкладки README / Package Details | ✅ |
| 4 | Installed / Updates, поиск с отменой, «Load more», прогресс и отмена | ✅ |
| 5 | Источники из `NuGet.Config`, `Update all`, опциональный `msbuild -t:restore` | ✅ |
| 6 | Локализация строк панели (сейчас английские подписи, как в VS) | ⏳ |

---

## 6. Тесты и стражи

| Тест | Что держит |
|---|---|
| `NuGetPanelThemeTests.PanelXaml_UsesSquareCornersOnly` | только `CornerRadius="0"` |
| `NuGetPanelThemeTests.PanelXaml_HasNoHardcodedColors` / `...HasNoNamedColors` | нет hex и named-цветов |
| `NuGetPanelThemeTests.PanelXaml_UsesOnlyStudioResources` | каждый `{DynamicResource}` — ключ `StudioBrushes`/`StudioFonts`; нет `MaterialDesign.Brush.*`, `Segoe UI`, `Consolas` |
| `NuGetPanelThemeTests.PanelXaml_HasVisualStudioLayout` | состав разметки: вкладки, `Search (Ctrl+L)`, `Package source:`, `Options`, README, Package Details, Project URL, Report Abuse, Tags, `GridSplitter` |
| `NuGetPanelThemeTests.PanelCodeBehind_HasShortcutsAndStableApi` | `SetProject`/`SetProjectFolder`, `Ctrl+L`, `Esc`, порог узкого режима |
| `NuGetPanelDocumentTabTests` | открытие вкладкой документов, отсутствие боковой панели, порядок восстановления раскладки, живая загрузка стилей и шаблонов (Measure/Arrange) |
| `NuGetPackageStateTests` | правила кнопки, формат downloads и подписи версии, тексты строки и панели деталей |
| `ThemeAcceptanceTests` | оба файла панели включены в сквозной чеклист темы |
| `StudioFontTests` | шрифт панели — только ресурс студии |
| `TfmMatrixGuardTests` | `RPA.NuGet` остаётся `net48` |

---

## 7. Риски и что осталось

1. **README.** Читается из `.nupkg` (запись `readme` в nuspec или `README.md` в корне). Для больших
   пакетов это лишняя загрузка при выборе пакета — можно перейти на ленивое чтение по кнопке.
2. **Restore.** Опция `Run restore after install` включена по умолчанию: без restore проект не
   собирается. Если MSBuild не найден, панель пишет это в строке состояния, но правка проекта уже
   выполнена (как и в NuGet).
3. **Транзитивные зависимости.** Показываются только версии и метаданные; полный resolver графа
   (nearest-wins) не реализован — отдельная задача.
4. **Иконки.** Сеть + двойной кеш (память/диск); при ошибке остаётся монограмм.
5. **Локализация.** Подписи панели английские (как в VS 2026). Переход на `lang_*` из
   `Resources\Main.resx` — этап 6.

---

## 8. Правки по замечаниям приёмки (07.11.2026)

### 8.1 Строка компоновки (тулбар)

| Замечание | Как исправлено |
|---|---|
| «Package source:» с отступом 10px слева | `Margin="10,0,10,0"` (10px слева и 10px до комбобокса) |
| Комбобокс источника «сжался», содержимого не видно | `MinWidth="50"` / `MaxWidth="150"`; шаблон `NuGet.ComboBoxStyle` переписан: текст выбранного элемента рисует сам `ComboBox` (`ContentPresenter` с `TemplateBinding SelectionBoxItem`, `TextElement.Foreground` из палитры), а `ToggleButton` стал прозрачным слоем клика. Раньше текст жил внутри шаблона `ToggleButton` и схлопывался/терял цвет |
| Иконки шестерни и папки крупнее в 1.5 раза | `Path` шестерни `12×12` → `18×18`, папки `12×10` → `18×15` |

### 8.2 Правая колонка (обзор пакета)

| Замечание | Как исправлено |
|---|---|
| При отсутствии выбора правая колонка не должна показываться | `ApplyLayout`: без `HasSelection` ширины колонок деталей и разделителя = 0, панель деталей `Collapsed`; заглушка «Select a package to see its details.» удалена |
| Комбобокс версии сжался, кнопка `Install` неактивна | добавлен `NuGetPackageDetailsService.FromPackageInfo`: предварительные сведения из карточки списка показываются сразу (версия = установленная либо версия источника), затем уточняются ответом источника. `MinWidth="80"` у комбобокса; действие (`Install`/`Update`/`Downgrade`/`Uninstall`) доступно без ожидания сети |
| Если README нет — вкладки README быть не должно | `ShowReadmeTab => HasReadme`; `IsReadmeVisible` / `IsPackageDetailsVisible`; при пустом README вкладка скрывается, показывается «Package Details». Плейсхолдер «This package doesn't provide a README file.» удалён |
| Логотип пакета не отображается | в шапке деталей `Image Source="{Binding Item.Icon}"`, монограмм — только фолбэк; в `NuGetPackageItemViewModel` снимается отметка `_iconRequested` при появлении `IconUrl` (метаданные приходят позже), поэтому иконка догружается |

### 8.3 Левая колонка (список пакетов)

| Замечание | Как исправлено |
|---|---|
| Кнопки установки/удаления справа на плашке, по наведению мыши | в `NuGet.PackageRowTemplate` добавлена колонка с кнопками `Install`/`Update` и `Uninstall`; их показывает `MultiDataTrigger` (`IsMouseOver` у `ListBoxItem` + `CanInstallFromRow` / `IsInstalled`). Умолчание `Visibility=Collapsed` задано **в стиле** `NuGet.RowButtonStyle`, а не локальным значением: локальное значение перебивает триггер шаблона |
| Иконки пакетов должны отображаться | исправлена причина: в вкладке Installed иконка запрашивалась до прихода метаданных, `_iconRequested` выставлялся навсегда, и повторной попытки не было. Теперь `LoadIconAsync` ставит отметку только при непустом `IconUrl`, а `IconUrl` сбрасывает её при изменении |

### 8.4 Новые команды панели

`InstallItemCommand` и `UninstallItemCommand` (кнопки плашки): установка берёт `LatestVersion` (или версию карточки), удаление вызывает `NuGetInstallService.Uninstall`; после операции проект перечитывается, вкладки Installed/Updates обновляются, restore выполняется по опции `Run restore after install`.

### 8.5 Тесты

| Тест | Что держит |
|---|---|
| `NuGetPanelDocumentTabTests.Panel_WithoutSelection_HasNoDetailsColumn` | правая колонка и разделитель имеют нулевую ширину, панель деталей `Collapsed` |
| `NuGetPanelDocumentTabTests.Panel_RendersSelectedPackageAndDetails` | после выбора пакета колонка появляется; **измеренные** ширины: источник в 50..150, версия ≥ 80; версия и действие доступны без источника; рендер в `RenderTargetBitmap` не падает |
| `NuGetPanelDocumentTabTests.Panel_ShowsPackageIconFromLoader` | иконка проходит путь карточка → `LoadIconAsync` → `HasIcon` (загрузчик подменён заглушкой) |
| `NuGetPackageStateTests.Details_WithoutReadme_HidesReadmeTab` | вкладка README скрыта, пока README нет, и появляется вместе с ним |
| `NuGetPackageStateTests.PreliminaryDetails_*` | предварительные сведения дают выбранную версию и активное действие без сети |
| `NuGetPackageStateTests.PackageItem_RowButtonsFollowState` | подписи и видимость кнопок плашки для нового/устаревшего/актуального пакета |
| `NuGetPackageStateTests.PackageItem_LoadsIconAfterMetadataArrives` | повторная попытка загрузки иконки после прихода `IconUrl` |
| `NuGetPanelThemeTests.PanelXaml_HasVisualStudioLayout` | границы ширины источника, наличие команд плашки, отсутствие заглушки и локального `Visibility` у кнопок |

---

## 9. Правки по замечаниям второй приёмки (07.11.2026)

### 9.1 Поиск без Enter (дебаунс) и иконка повтора

| Замечание | Как сделано |
|---|---|
| Не ждать ввода: искать по мере набора | `NuGetPanelViewModel.SearchQuery` запускает отложенный поиск (`CancellationTokenSource` + `Task.Delay`, по умолчанию 300 мс, настраивается `SearchDebounceMilliseconds`). Предыдущая попытка отменяется, в источник уходит только последний запрос. `PendingSearch` позволяет дождаться результата в тестах |
| Убрать Enter, добавить иконку обновления после поля | обработчик `KeyDown="SearchTextBox_KeyDown"` удалён и из разметки, и из code-behind; сразу после поля поиска стоит кнопка-иконка «Refresh search results» (`SearchCommand`). `Ctrl+L` — фокус, `Esc` — очистка |
| Пустой запрос | отменяет таймер, очищает список и статус, в сеть не ходит |

Для тестируемости методы `NuGetSearchService` (`SearchAsync`, `GetPackageInfoAsync`, `GetVersionsAsync`, `GetVersionInfosAsync`) сделаны `virtual` — стаб считает вызовы без сети.

### 9.2 Оверлей «Package sources» (шестерня)

| Элемент | Реализация |
|---|---|
| Открытие | шестерня в тулбаре (`OpenSourcesCommand`) и ссылка «Configure» в плашке открывают `Border x:Name="SourcesOverlay"` — он накрывает строки 0–2 корневого `Grid` (`Grid.RowSpan="3"`: вкладки, тулбар, список/детали), а строка состояния остаётся видимой ниже, фон `Studio.Surface` |
| Таблица | шапка `Enabled | Name | Source` + `ListBox` (`NuGet.OverlayRowStyle`): галочка `IsEnabled`, имя, адрес (обрезка по ширине), выделение = `Studio.Selected` |
| Кнопки | `+ Add`, « Edit, « Remove» с векторными иконками; активность — по выделению и режиму редактора |
| Редактор | встроенная панель `Add source` / `Edit source` (поля Name/Source, `Save`/`Cancel`), проверка: имя обязательно, адрес обязателен, дубликат имени не допускается (при `Add` выделенная строка тоже проверяется) |
| Сохранение | `NuGetSourcesService.Save` пишет источники в ближайший `NuGet.Config` через `PackageSourceProvider.SavePackageSources` (включая состояние `Enabled`). После записи список источников и выпадающий выбор перечитываются |
| Выключенные и локальные источники | оверлей показывает всё, что есть в конфигурации (включая offline-папки), а в `Package source` попадают все включённые источники; если не осталось ни одного — панель сообщает об этом |
| Закрытие | кнопка «×», `Esc` (сначала закрывает оверлей, потом уже очищает поиск), ссылка «Open NuGet.Config» открывает файл конфигурации |

### 9.3 Комбобоксы не раскрывались — причина и лечение

Регрессия прошлой правки: визуал комбобокса был вынесен **из** `ToggleButton`, а поверх лежал прозрачный слой клика. Щелчок уходил в слой, но ComboBox забирал мышь себе (capture для попапа), слой терял захват, `IsChecked` откатывался в `false`, попап закрывался сразу — список «не выдвигался».

Исправлено возвратом проверенной схемы: `ToggleButton` содержит всё оформление (`Border` → `Grid` → `ContentPresenter` + шеврон), `Popup x:Name="PART_Popup"` стоит рядом. Исходная проблема «текст не виден» решена точечно: у `ContentPresenter` заданы `TextElement.Foreground` и `TextElement.FontSize` (внутри шаблона `ToggleButton` текст наследовал чёрный цвет и пропадал на тёмном фоне).

Страж: `NuGetPanelDocumentTabTests.Panel_ComboBoxes_OpenDropDown` — панель хостится в реальном окне (Popup требует `PresentationSource`), у обоих комбобоксов выставляется `IsDropDownOpen` и проверяется `PART_Popup.IsOpen`.

### 9.4 README: Markdown вместо сырого текста

| Часть | Реализация |
|---|---|
| Конвертер | `Converters\MarkdownRenderer.cs` + `MarkdownToFlowDocumentConverter` (новых зависимостей нет) |
| Блоки | заголовки `#`…`######` (20/17/15/13), абзацы, списки `-`/`*`/`1.`, цитаты `>`, горизонтальные линии, ограждённые блоки ```` ``` ```` (моноширинный шрифт, фон `SurfaceAlt`, рамка 1px) |
| Инлайн | `**жирный**`, `*курсив*`, `` `код` ``, ссылки `[текст](url)` → `Hyperlink` (текст ссылки разбирается рекурсивно), картинки `![alt](url)` и бейджи `[![alt](img)](url)` полностью скрываются |
| Отложено | загрузка настоящих картинок по абсолютным URL (бейджи shields.io / nuget.org) с кешем по образцу `NuGetIconService`; относительные пути — извлечение картинок из `.nupkg` в `NuGetPackageDetailsService` и показ через `BlockUIContainer`/`InlineUIContainer` |
| Тема | цвета и шрифты — через `Converters\NuGetThemeResources.cs`: ключи `Studio.*` резолвятся из `Application.Current`, есть нейтральный фолбэк (жёстких цветов в коде нет) |
| Разметка | README отображается `FlowDocumentScrollViewer` (`x:Name="ReadmeViewer"`) |

### 9.5 Тесты второй приёмки

| Тест | Что держит |
|---|---|
| `NuGetSourcesAndSearchTests.SearchQuery_SearchesOnceAfterTypingStops` | три быстрых изменения запроса → один поиск с последним текстом |
| `...SearchQuery_EmptyClearsListWithoutRequest` | очистка поля не ходит в источник |
| `...SourcesOverlay_OpensAndCloses` | шестерня открывает/закрывает оверлей |
| `...SourcesOverlay_AddEditRemove_WritesNuGetConfig` | Add/Edit/Remove действительно меняют `NuGet.Config` |
| `...SourcesOverlay_ValidatesNameAndDuplicates` | проверки имени и дубликатов |
| `...SourcesOverlay_EnabledLocalSourceIsOfferedAndDisabledIsNot` | включённые локальные (offline) источники видны и в оверлее, и в списке для поиска; снятая галочка убирает источник из списка и пишется в конфиг |
| `NuGetPanelLayoutTests.SourcesOverlay_FootnoteIsNotClippedByStatusBar` | подпись «Sources are stored in NuGet.Config…» целиком выше строки состояния, а не перекрыта её непрозрачной полосой (оверлей `Grid.RowSpan="3"`) |
| `...MarkdownRenderer_RendersHeadingsBoldListsAndCode` / `...HandlesLinksImagesAndEmptyInput` / `...HidesImagesIncludingLinkWrappedBadges` | разметка README разбирается, исходных `**` и `![` в тексте нет, картинки и бейджи скрыты |
| `...Panel_ReadmeIsShownAsFlowDocument` | README рисуется документом |
| `NuGetPanelDocumentTabTests.Panel_ComboBoxes_OpenDropDown` | оба комбобокса раскрываются |
| `NuGetPanelThemeTests.PanelXaml_HasVisualStudioLayout` | нет `KeyDown` у поиска, есть иконка повтора, оверлей с Add/Save/Remove, `FlowDocumentScrollViewer` |

---

## 10. Правки по замечаниям третьей приёмки (07.11.2026)

### 10.1 Ширина комбобоксов — по содержимому

`NuGet.ComboBoxStyle` получил `HorizontalAlignment="Left"`: ширина считается по тексту выбранного
элемента, а не по колонке сетки. Из стиля убран `MinWidth`, границы задаются разметкой:
источник — `MinWidth="50"` / `MaxWidth="150"`, версия — `MinWidth="80"`. Подпись
«Latest stable 13.0.4» больше не обрезается: комбобокс вырастает под неё.

### 10.2 Шеврон — иконка MaterialDesign слева

В `RPA.NuGet.csproj` добавлен `Material.Icons.WPF 2.4.1` (та же версия, что в студии).
В шаблоне комбобокса сетка теперь `Auto | *`: в первой колонке
`<materialIcons:MaterialIcon Kind="ChevronDown" Width="12" Height="12" Margin="6,0,4,0" />`,
во второй — текст выбранного элемента. Шеврон стоит слева и центрируется по вертикали.

### 10.3 Кнопки плашки — иконки с зарезервированным местом

- Новый стиль `NuGet.RowIconButtonStyle` (24×24, прозрачный, `Visibility=Collapsed` по умолчанию).
- В `NuGet.PackageRowTemplate` четвёртая колонка имеет **фиксированную ширину 56px**, поэтому
  появление кнопок по наведению не сдвигает ни имя, ни версию, ни описание.
- Кнопки: `MaterialIcon Kind="Download"` (Install/Update, подпись — в `ToolTip`) и
  `MaterialIcon Kind="Delete"` (Uninstall).
- Видимость по-прежнему задаёт `MultiDataTrigger` (`IsMouseOver` у `ListBoxItem` + условие состояния),
  а умолчание — сеттер стиля (локальное значение перебило бы триггер).

### 10.4 Почему не отображались логотипы — найдено три причины

1. **Главная.** `BitmapImage` создавался с `BitmapCreateOptions.IgnoreImageCache` и `StreamSource`.
   Этот режим работает только с `UriSource`; со `StreamSource` он бросает
   `ArgumentNullException` («Ключ не может быть неопределенным»), исключение глушилось — картинка
   не создавалась **ни для одного пакета**. Теперь `CacheOption=OnLoad` без `IgnoreImageCache`
   (поток читается сразу, владельца не остаётся).
2. **Адрес иконки.** Если источник не отдал `IconUrl`, панель подставляет стандартный путь
   `https://api.nuget.org/v3-flatcontainer/{id}/{version}/icon` (`NuGetIconService.BuildDefaultIconUrl`).
   Важно: путь **без** расширения — вариант с `.png` отдаёт 404. Вычисляемое свойство —
   `NuGetPackageItemViewModel.IconSourceUrl`.
3. **Сеть и поток.** В статическом конструкторе `NuGetIconService` явно включается TLS 1.2
   (у .NET Framework 4.8 по умолчанию `SystemDefault`, соединение с nuget.org обрывалось на записи),
   добавлены 3 попытки с паузами, `HttpClient` с `AutomaticDecompression`, а `BitmapImage`
   создаётся на потоке диспетчера (картинка с потока пула молча не привязывалась к `Image.Source`).
   Причина неудачи теперь видна в `NuGetIconService.LastError`.

### 10.5 Тесты третьей приёмки

| Тест | Что держит |
|---|---|
| `NuGetPanelLayoutTests.ComboBoxStyle_SizesToContentWithMaterialChevron` | `HorizontalAlignment=Left`, отсутствие `MinWidth` в стиле, `MaterialIcon Kind="ChevronDown"` левее текста |
| `...Panel_ComboBoxWidthFollowsContent` | короткая подпись помещается, длинная «Latest stable 13.0.4» расширяет комбобокс, но он не растягивается на всю панель |
| `...Panel_RowButtonsDoNotShiftLayoutOnHover` | кнопка скрыта по умолчанию; при показе ширина строки не меняется (колонка 56px) |
| `...IconUrl_FallsBackToNuGetFlatContainerPath` | запасной адрес иконки без `.png`, приоритет адреса источника |
| `...IconService_LoadsIconFromDownloadedBytes` | PNG из байтов действительно превращается в картинку (страховка от `IgnoreImageCache`) |
| `...IconService_ReportsNetworkFailureInsteadOfSilence` | недоступный хост: иконки нет, но причина записана в `LastError` |
| `NuGetIconNetworkDiagnostics` (Explicit) | живая проверка: реальный поиск + реальная загрузка иконки |

### 10.6 Зависимость

`RPA.NuGet` теперь ссылается на `Material.Icons.WPF 2.4.1` (`PackageReference`). Версия совпадает
с той, что использует студия, поэтому конфликта сборок нет.

### 10.7 Уточнение: ширина комбобоксов по содержимому (07.11.2026)

Замер показал две отдельные причины «поля маленькие»:

| Причина | Что было | Исправление |
|---|---|---|
| Поле источника выглядело пустым и сжатым | текст рисовал `ContentPresenter` с `Content="{Binding SelectionBoxItem}"`; при заданном `DisplayMemberPath` `SelectionBoxItem` приходит пустым, поэтому ширина считалась по одной иконке (50px) и текста не было видно | текст рисует `TextBlock` с `Text="{Binding SelectedItem}"`: берётся `ToString()` выбранного элемента (у `NuGetSourceDefinition` — имя, у `NuGetPackageVersionInfo` — подпись версии), текст честно измеряется — ширина растёт по содержимому |
| Поле версии обрезалось | комбобокс стоял в `*`-колонке grid, и колонка сжимала его ниже содержимого | колонка версии стала `Auto`, кнопка действия прижата вправо в звёздной колонке |

Замеры после правки (панель 1100×700, детали 427px): источник — ширина 82px при тексте «nuget.org» 52px; версия — 130px при тексте «Latest stable 13.0.4» 100px. Подпись версии не обрезается, а при длинном тексте поле растёт (проверено тестом на смену `Label`).

Стражи: `NuGetPanelLayoutTests.ComboBoxStyle_SizesToContentWithMaterialChevron` (в стиле нет `ContentPresenter` с `SelectionBoxItem`, текст берётся из `SelectedItem`) и `...Panel_ComboBoxWidthFollowsContent` (измеренные ширины обоих полей больше ширины их текста; длинная подпись расширяет поле).

---

## 11. Четвёртая приёмка (07.11.2026)

### 11.1 Комбобоксы не растягивались по содержимому — настоящая причина

| Замечание | Как исправлено |
|---|---|
| Комбобоксы не растягиваются по контенту (правили несколько раз); по ширине ограничены 150px | В `NuGetPanel.Styles.xaml` внутренний `ToggleButton` комбобокса (`DropDownToggle`) и шапка `Expander` (`HeaderToggle`) получили **явный** `Style="{StaticResource NuGet.StretchedToggleButtonStyle}"` (`Width=Auto`, `Height=Auto`, `MinWidth=0`, `MinHeight=0`, `HorizontalAlignment/VerticalAlignment=Stretch`). Потолок ширины 150px перенесён в `NuGet.ComboBoxStyle` (`MaxWidth`) — он общий для источника и версии; минимумы (50 / 80) остались в разметке |

**Причина.** `App.xaml` подключает `MaterialDesign2.Defaults.xaml`, а там есть **неявный** стиль
`ToggleButton` — переключатель с `Width = 34`. `DropDownToggle` задавал только локальный `Template`,
поэтому ширину 34 ему ставил чужой стиль: коробка комбобокса физически была 34px, и поле упиралось в
`MinWidth` (50px у источника, 80px у версии) — подпись «nuget.org» (≈72px) не влезала. Правки
`HorizontalAlignment`, `TextTrimming` и колонок `Grid` на это не влияли: измерение заканчивалось на
ширине 34. Правило то же, что у `OutlinedComboBoxStyle` в чате (`AGENTS.md`, «Внутренние `ToggleButton`
в шаблонах обязаны задавать `Style` явно»).

**Почему прежние проверки были зелёными.** Тестовая панель хостится в `Window` без `App.xaml`, то есть
без неявного стиля MaterialDesign, — там коробка честно растягивалась.

Стражи: `NuGetPanelLayoutTests.ComboBoxInnerToggleButtons_HaveExplicitStretchedStyle` (явный `Style` у
обоих внутренних `ToggleButton` + геометрия базового стиля) и
`...Panel_ComboBoxesSurviveImplicitMaterialDesignToggleStyle` (неявный стиль `ToggleButton` с `Width=34`
ставится в `window.Resources`; проверяется, что коробка равна ширине комбобокса, поле вмещает подпись и
не превышает 150px). Негативный контроль: без правки тест красный — коробка 34px, поле 50px при тексте
71.9px. Потолок ширины держит `NuGetPanelThemeTests.PanelXaml_HasVisualStudioLayout` (150px в стиле +
минимумы в разметке).

---

## 12. Пятая приёмка (07.11.2026)

### 12.1 Шеврон «Options» — кнопка без фона

| Замечание | Как исправлено |
|---|---|
| Шеврон, раскрывающий блок полей, не должен менять цвет при наведении: это кнопка без фона с иконкой Material | Из шаблона шапки `NuGet.OptionsExpanderStyle` удалён триггер `IsMouseOver → Studio.Hover`; два самодельных `Path` (`ChevronRight`/`ChevronDown`) заменены одной `materialIcons:MaterialIcon Kind="ChevronRight"` с переключением `Kind="ChevronDown"` по `IsChecked`; фон шапки — `Transparent`, углы прямые |

Правила, которые теперь держат поведение:

- шапка `Expander` — `ToggleButton` **без фона**: в её шаблоне нет триггеров `IsMouseOver`/`IsPressed`, поэтому ни фон, ни цвет подписи, ни цвет иконки при наведении не меняются;
- состояние «раскрыто» передаёт только глиф: `ChevronRight` → `ChevronDown` (алиасы Material.Icons: `NavigateNext` → `ExpandMore`), `Foreground` иконки постоянный — `Studio.ForegroundMuted`;
- явный `Style="{StaticResource NuGet.StretchedToggleButtonStyle}"` у внутреннего `ToggleButton` сохранён: неявный стиль MaterialDesign ставит `Width=34` и ломает геометрию (см. §11.1).

### 12.2 Вкладка README пропала у всех пакетов

Вкладка рисуется по `NuGetPackageDetailsViewModel.ShowReadmeTab => HasReadme`, то есть её «пропажа» означала одно: **текст README не приходил**. Сама связка VM ↔ разметка была в порядке — дефектов было четыре, и все в цепочке получения текста.

| Причина | Проявление | Исправление |
|---|---|---|
| Пустые `catch` в `TryReadReadmeAsync` и `FindReadmeEntry` | любая ошибка (HTTP, TLS, битый zip) глушилась — вкладка молча исчезала | `NuGetPackageDetailsService.LastReadmeError`; панель пишет причину в строку состояния («README unavailable: …») |
| Скачивание жёстко с `api.nuget.org` (`NuGetPackageDownloader`) | для приватного, корпоративного и offline-источника README не появлялся никогда | новый `NuGetSearchService.DownloadNupkgAsync` — `FindPackageByIdResource.CopyNupkgToStreamAsync` через **выбранный источник** (прокси и авторизация из `NuGet.Config`, TLS и повторы NuGet.Protocol); прямая ссылка осталась только страховкой |
| `HttpClient` без TLS 1.2, повторов и таймаута | у .NET Framework 4.8 по умолчанию `SystemDefault` — соединение с nuget.org обрывалось (та же причина, что у иконок, §10.4) | статический конструктор с `SecurityProtocol |= Tls12`, 3 попытки, таймаут 60 с, `AutomaticDecompression` |
| README искался только в корне пакета | пакеты с `docs\README.md` или `README.markdown` выглядели как «README нет» | порядок поиска: `<readme>` из nuspec → README в корне → README в любой папке (ближайший к корню); расширения `.md/.markdown/.txt` и файл без расширения |

Дополнительно:

- **запасная версия.** Если у источника нет версии, показанной в панели (в проекте зафиксирована удалённая версия), сервис пробует последнюю из списка — `TryReadReadmeAsync(search, id, version, fallbackVersion)`;
- **кеш.** Прочитанные README кешируются в памяти по `id|version` (включая отрицательный ответ «README нет»), поэтому повторный выбор пакета показывает вкладку сразу и не скачивает `.nupkg` заново;
- **тесты без сети.** `.nupkg` собирается в памяти (`ZipArchive` + nuspec), поэтому цепочка проверяется без обращения к nuget.org.

### 12.3 Тесты пятой приёмки

| Тест | Что держит |
|---|---|
| `NuGetOptionsChevronTests.OptionsExpander_UsesMaterialChevronWithoutHoverBackground` | шапка «Options» — кнопка без фона, шеврон — `MaterialIcon`, триггеров наведения и нажатия нет |
| `...Panel_OptionsChevronFlipsGlyphAndKeepsTransparentBackground` | на живой панели: фон шапки прозрачный, глиф меняется на `ChevronDown` при раскрытии |
| `NuGetReadmeLoadTests.ReadReadme_ReadsRootReadme` / `..._ReadsReadmeFromSubfolder` / `..._UsesNuspecDeclaredEntry` | поиск README внутри `.nupkg`: корень, подпапка, запись из nuspec |
| `...ReadReadme_ReturnsNullWhenPackageHasNoReadme` | пакет без README и пустые входные данные не падают |
| `...TryReadReadme_DownloadsFromSelectedSourceAndCaches` | README скачивается из выбранного источника один раз, повтор — из кеша |
| `...TryReadReadme_FallsBackToAnotherVersion` | удалённая версия → README берётся у запасной |
| `...TryReadReadme_ReportsFailureReasonInsteadOfSilence` | причина неудачи видна в `LastReadmeError` |
| `...Panel_ReadmeTabAppearsWhenReadmeLoaded` | после загрузки README вкладка «README» в разметке становится видимой |

---

## 11. Правки по замечаниям четвёртой приёмки (07.11.2026)

### 11.1 Картинка пакета (или фолбэк) в правой колонке

Причина «в правой колонке нет картинки» — не вёрстка. Разметка уже содержала `Image` и
фолбэк-монограмм, но привязка шла напрямую к `Item.Icon`: панель деталей — один экземпляр
`NuGetPackageDetailsViewModel` на всю панель (`Details` не пересоздаётся, уведомления о его смене
нет), а `Item` присваивался без `RaisePropertyChanged`. Привязка запоминала `null` и больше не
пересчитывалась; вдобавок иконка загружается асинхронно уже после выбора пакета.

Исправление:

- `Item` в `NuGetPackageDetailsViewModel` стало свойством с уведомлением; при смене строки подписка
  на `PropertyChanged` карточки снимается и ставится заново (в `Clear` — снимается).
- Добавлены `PackageIcon`, `HasPackageIcon`, `PackageMonogram` (фолбэк — первая буква
  идентификатора). Они поднимаются и при смене карточки, и по событиям `Icon`/`HasIcon`/`Monogram` её
  самой, поэтому картинка «доезжает» до шапки без повторного выбора пакета.
- В шапке деталей привязки переведены на эти свойства; элементы получили имена `DetailsIcon` и
  `DetailsMonogram` — для стражей.

### 11.2 Кнопки плашки: узкая колонка и крестик

| Что | Было | Стало |
|---|---|---|
| Колонка кнопок в `NuGet.PackageRowTemplate` | `56` | `37` (56 / 1,5) |
| `NuGet.RowIconButtonStyle` | 24×24 | 16×16 |
| Глифы кнопок | 14×14 | 10×10 |
| Отступ панели кнопок | `8,0,0,0` | `5,0,0,0` |
| Иконка удаления | `MaterialIcon Kind="Delete"` | `MaterialIcon Kind="Close"` (крестик) |

Арифметика колонки: 16 + 16 + 5 = 37, поэтому обе кнопки укладываются ровно в колонку, и появление
по наведению по-прежнему не сдвигает ни имя, ни версию, ни описание.

### 11.3 Стражи четвёртой приёмки

| Тест | Что держит |
|---|---|
| `NuGetPackageStateTests.Details_HeaderShowsIconOrMonogramFallback` | монограмм до прихода иконки; после `LoadIconAsync` приходит `PackageIcon` **и** уведомление |
| `NuGetPanelDocumentTabTests.Panel_DetailsHeaderShowsPackageIcon` | в шапке деталей `Image` показывает иконку выбранного пакета, а без иконки виден монограмм |
| `NuGetPanelLayoutTests.RowIconButtons_AreSizedForNarrowColumn` | кнопка плашки 16×16 (прежние 24×24 не влезают в колонку) |
| `NuGetPanelLayoutTests.Panel_RowButtonsFitNarrowColumn` | обе кнопки по 16px укладываются в колонку 37px, крестик не выходит за край плашки |
| `NuGetPanelLayoutTests.ComboBoxStyle_SizesToContentWithMaterialChevron` | колонка 37px, `Kind="Close"`, отсутствие `Kind="Delete"` и прежней ширины 56px |
