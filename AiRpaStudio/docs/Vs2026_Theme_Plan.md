# План: дизайн окон и панелей AvalonDock + контролов Workflow Foundation в стиле Visual Studio 2026

## Метаданные

| Поле | Значение |
|---|---|
| Задача | Дизайн всех окон и панелей AvalonDock и всех контролов Workflow Foundation в стиле Visual Studio 2026 |
| Область изменений | `AiRpaStudio`, `AvalonDock`, `AvalonDock.Themes.Metro`, `LogControlLibrary`, `RPA.Chat` |
| Вне области | `ProxyChecker`, `AvalonEdit` (в т.ч. `ICSharpCode.AvalonEdit.Sample\Window1.xaml`) |
| Требования | скруглённые окна; один фон на всю площадь окна; тонкая рамка 1px вокруг всех докпанелей (правило 6); акцент не используется; только тёмная и светлая темы |
| Статус | этапы 1-10 (§8) реализованы и покрыты тестами (см. «Сделано по этапам 1-10»); осталась ручная приёмка UI по чеклисту `AiRpaStudio\docs\Vs2026_Theme_Acceptance.md` |

### Уточнения по фактическому состоянию кода

Две поправки к первичному анализу, перепроверено по исходникам:

1. У `LayoutAutoHideWindowControl` стиль в `AvalonDock.Themes.Metro\Theme.xaml` **есть**, но в нём задан только `Background = AvalonDock_ThemeMetro_BaseColor1` — ни рамки, ни foreground (см. W-10).
2. Ключи вкладок `AvalonDock_Base` / `AvalonDock_Hover` / `AvalonDock_Active` объявляет в первую очередь `MetroTheme.GetResourceDictionary()` (`AvalonDock.Themes.Metro\MetroTheme.cs:29-47`) — именно этот словарь попадает в `DockingManager.Resources.MergedDictionaries` (`AvalonDock\Controls\DockingManager.cs:1066`), а не только `Brushes.xaml`.

---

## 1. Правила дизайна (обязательны для всех разделов ниже)

1. **Один фон.** `Surface` — единственный цвет фона окна, панелей, заголовков панелей, вкладок, контента документов и поверхности дизайнера. Различие зон передаётся только hairline-разделителем 1px (`Separator`) — линией-ресайзером или тонкой рамкой панели (правило 6) — а не сменой фона и не толстой рамкой.
2. **Ноль акцента.** Состояние передаётся яркостью (`SurfaceAlt` → `Hover` → `Selected`) и цветом 1px-линии. Единственное исключение — красный hover кнопки закрытия `#E81123` (системная семантика, не акцент).
3. **Скругления.** Окно — `8`, вкладка/кнопка — `4`, меню/подсказка — `6`.
4. **Две темы.** Ровно две палитры (`dark`, `light`), переключение на лету, без «третьей» смешанной.
5. **Кисти только кодом.** Алиасы в XAML через `DynamicResource ResourceKey=` запрещены (`AGENTS.md`: даёт `ResourceReferenceExpression` → `XamlParseException`).
6. **Тонкая рамка каждой докпанели.** Каждая докпанель обведена рамкой 1px: панель главного окна (`LayoutAnchorablePaneControl`), панель документов (`LayoutDocumentPaneControl`), панель автоскрытия (`LayoutAutoHideWindowControl`) и панели внутри откреплённых окон (W-06, W-07). Толщина всегда `1` — никаких 2px и «акцентных полос». Цвет — один шаг яркости от фона в сторону контраста: в тёмной теме рамка светлее фона на ≈`0x10` (`Separator` = `#373737` при `Surface` = `#272727`), в светлой — темнее на ≈`0x1B` (`#E4E4E4` при `#FFFFFF`); у активной панели рамка усилена до `SeparatorStrong` при той же толщине 1px. Рамку рисует ровно один элемент (таблица в разделе 3), поэтому двойных линий нет: внутри плавающего окна панель рамку гасит, а линию даёт `WindowBorder`. Ресайзер панелей в покое линии не рисует. Рамка не заменяет hover/selected и не вводит второй фон (правило 1).

---

## 2. Токены (единая палитра)

| Токен | Dark | Light | Назначение |
|---|---|---|---|
| `Surface` | `#272727` | `#FFFFFF` | единственный фон всего |
| `SurfaceAlt` | `#2E2E2F` | `#F3F3F3` | неактивная вкладка |
| `Hover` | `#333638` | `#EAEAEA` | hover |
| `Selected` | `#404046` | `#E4E4E4` | выбранное состояние |
| `Separator` | `#373737` | `#E4E4E4` | hairline 1px между зонами и рамка неактивной докпанели (+`0x10` к фону в тёмной теме, −`0x1B` в светлой) |
| `SeparatorStrong` | `#48484F` | `#CFCFCF` | рамка активной докпанели, граница активного плавающего окна, hover/drag ресайзера |
| `Foreground` | `#F1F1F1` | `#1F1F1F` | основной текст |
| `ForegroundMuted` | `#9DA0A6` | `#5F6368` | неактивные вкладки, подписи |
| `CloseHoverBg` / `CloseHoverFg` | `#E81123` / `#FFFFFF` | то же | кнопка закрытия |
| `Radius.Window` / `Radius.Tab` / `Radius.Button` | `8` / `4` / `4` | то же | скругления |

### Ключи AvalonDock, записываемые в рантайме

| Новый ключ | Заменяет | Dark / Light |
|---|---|---|
| `AvalonDock_Surface` | `BaseColor1` | `Surface` |
| `AvalonDock_SurfaceAlt` | `BaseColor5` | `SurfaceAlt` |
| `AvalonDock_Foreground` | `BaseColor2` | `Foreground` |
| `AvalonDock_ForegroundMuted` | `BaseColor6` | `ForegroundMuted` |
| `AvalonDock_Separator` | `BaseColor7` | `Separator` |
| `AvalonDock_Selection` | `BaseColor3` | `Selected` |
| `AvalonDock_SelectionHover` | `BaseColor4` | `Hover` |
| `AvalonDock_Base` / `AvalonDock_Hover` / `AvalonDock_Active` | уже существуют | `SurfaceAlt` / `Hover` / `TabActive` |
| `AvalonDock_OverlayFill` / `AvalonDock_OverlayStroke` | `AliceBlue` / `SystemColors.Highlight` | `Surface`@15% / `SeparatorStrong` |
| `AvalonDock_NavigatorHover` / `AvalonDock_NavigatorSelected` | 4 градиента `#FFF1FBFF…#FF9BDDFB` | `Hover` / `Selected` |

`BaseColor1..7` сохраняются как legacy-алиасы на те же нейтральные значения (их проверяет `AiRpaStudio.Tests\DockingThemeBrushesTests.cs`), но ни один XAML-шаблон не должен на них ссылаться — только на новые ключи.

Рамка докпанелей (правило 6) новых ключей не вводит: неактивная панель — `AvalonDock_Separator`, активная — `AvalonDock_SeparatorStrong`. Поэтому `StudioPalette`, `StudioBrushes`, `DockingThemeBrushes`, `AvalonDock\Themes\ThemeBrushBridge.cs` и `MetroTheme` правки не требуют, а `StudioPaletteTests`, `DockingThemeBrushesTests`, `AvalonDockThemeBridgeTests` и страж `ThemeXaml_EveryDynamicResourceKeyIsWrittenByStudio` остаются без изменений.

---

## 3. Сводная таблица окон

| ID | Окно | Файл шаблона | Тема сейчас | Скругление сейчас |
|---|---|---|---|---|
| W-01 | Главное окно студии | `AiRpaStudio\Views\MainView.xaml` | частично (`Studio.*`), свой TitleBar | `CornerRadius="0"` (`:29`) |
| W-02 | Создать активность | `Views\ActivityCreatorView.xaml` | системный chrome, фон не задан | системное |
| W-03 | Создать проект | `Views\ProjectCreatorView.xaml` | системный chrome, фон не задан | системное |
| W-04 | Открыть VS-проект | `Views\VsProjectOpenView.xaml` | системный chrome, фон не задан | системное |
| W-05 | Из VS-шаблона | `Views\VsTemplateProjectCreatorView.xaml` | системный chrome, фон не задан | системное |
| W-06 | Откреплённый документ | `Theme.xaml:1353-1500` | акцент в заголовке | `CornerRadius="0"` |
| W-07 | Откреплённая панель | `Theme.xaml:1505-1710` | акцент в заголовке | `CornerRadius="0"` |
| W-08 | Переключатель документов (Ctrl+Tab) | `Theme.xaml:1711-2025` | `Background="White"`, голубые градиенты, синий фокус | `ResizeMode="NoResize"` |
| W-09 | Оверлей перетаскивания | `Theme.xaml:491-600` | `Fill="AliceBlue"`, stroke = `SystemColors.Highlight` | окна-рамки нет |
| W-10 | Панель автоскрытия | `Theme.xaml:1751-1756` | только `Background = BaseColor1` | — |
| W-11 | Ресайзер панелей / контейнер документа | `Theme.xaml:1120-1143` | `Background` не задан, hover нет | — |
| W-12 | Popup-хосты (не `Window`) | `SearchableComboBox.xaml`, `ChatPanel.xaml`, `LogViewerControl.xaml` | зашитые светлые цвета | свои |

### Кто рисует рамку докпанели (правило 6)

| ID | Зона | Кто рисует рамку | Значение |
|---|---|---|---|
| W-01 | P-01…P-07 (`LayoutAnchorablePaneControl`) | `ContentPanel` (1px по периметру) + `HeaderStrip` вокруг полосы табов (`1,0,1,1`) | `AvalonDock_Separator`; у активной — `SeparatorStrong` |
| W-01 | P-08 (`LayoutDocumentPaneControl`) | шапка `1,1,1,0` + `ContentPanel` `1` | то же |
| W-01 | сам `DockingManager` | — (рамка = кромка окна) | `BorderThickness` не задан (0) |
| W-02…W-05 | диалоги | докпанелей нет | — |
| W-06 | откреплённый документ | `WindowBorder` окна; панель внутри гасит рамку (`Model.IsHostedInFloatingWindow`) | `Separator`; у активного — `SeparatorStrong` |
| W-07 | откреплённая панель | то же | то же |
| W-08 | Ctrl+Tab (`NavigatorWindow`) | не докпанель, `WindowBorder BorderThickness="0"` | без изменений |
| W-09 | оверлей перетаскивания | `PART_PreviewBox` (1px) | `OverlayStroke` |
| W-10 | панель автоскрытия | вложенный `LayoutAnchorableControl` через `AnchorableStyle` | `Separator` |
| W-11 | ресайзер / `LayoutDocumentControl` | рамку не рисует | hover/drag — `SeparatorStrong` |
| W-12 | popup-хосты | hairline popup (этап 9) | `Separator` |

---

## 4. Раздел по каждому окну

### W-01 · Главное окно студии

**Файл:** `AiRpaStudio\Views\MainView.xaml` + `MainView.xaml.cs`

**Сейчас:**
- `Window`: `Background="{DynamicResource Studio.WindowBackground}"`, `WindowState="Maximized"` (`:17-23`).
- `WindowChrome`: `CaptionHeight="0"`, `ResizeBorderThickness="6"`, `GlassFrameThickness="0"`, `CornerRadius="0"` (`:25-31`).
- Свой TitleBar (`:497-540`) на кистях `Studio.TitleBar*`; кнопки minimize/maximize/close — собственные обработчики.
- `DockingManager` (`:738-748`): задан только `Theme = MetroTheme`; `Background`, `Foreground`, `BorderThickness` явно не выставлены → стиль темы берёт `Background = BaseColor1` (= `MaterialDesign.Brush.Background`), а `BorderBrush`/`BorderThickness` остаются `null`/`0`.
- Раскладка панелей строится кодом в строке 3 (`Grid.Row="3"`).

**Целевое:** единый `Surface` от заголовка до нижней кромки окна; hairline 1px под TitleBar и под тулбаром; окно со скруглением 8; `DockingManager` без собственной рамки и без собственного фона, отличного от окна.

**Работы:**
1. `WindowChrome CornerRadius="8"`, `GlassFrameThickness="0"`; фон окна — `Studio.Surface` (алиас `Studio.WindowBackground` перенаправить в палитру).
2. У `DockingManager` явно `BorderThickness="0"`; `SnapsToDevicePixels` оставить.
3. В `SourceInitialized` применить `WindowCorner.Enable(this)` (см. M-03).
4. TitleBar: `Studio.TitleBarBackground` → `Surface` (сейчас `#2D2D30`, то есть «чуть светлее» окна, что нарушает правило одного фона).

**Тесты:** `StudioPaletteTests`, `WindowCornerTests`.
**Сделано (этап 5):** `CornerRadius="8"` в `WindowChrome` + `MainView.OnSourceInitialized` → `WindowCorner.Enable(this)`; DWM на Windows 11, region на Windows 10 (радиус из палитры).
**Готово когда:** в тёмной теме нет ни одного прямоугольника другого оттенка, кроме hairline; окно со скруглёнными углами на Win11.
**Сделано (этап 10):** панели внутри `DockingManager` (P-01…P-08) обведены рамкой 1px, у активной — `SeparatorStrong`; сам `DockingManager` рамку не рисует (`BorderThickness` не задан, дефолт `0`), поэтому правок в `MainView.xaml` не потребовалось. Контент панелей собственных рамок не рисует (`wfProjectBorder`, `WfPropertyBorder`, `ToolboxContainer` — `BorderThickness` 0), двойных линий нет.

---

### W-02 · Создать активность

**Файл:** `AiRpaStudio\Views\ActivityCreatorView.xaml`; открывается из `AiRpaStudio\Models\Main.cs:170,184` через `MvvmFactory.CreateWindow(..., ViewMode.ShowDialog)`.

**Сейчас:** `Height=165 Width=450 ResizeMode=NoResize`, системный chrome, `Background`/`Foreground` не заданы → в тёмной теме белое окно со светлым caption. В разметке объявлены три `RowDefinition` (40/40/40), а кнопка «Create» стоит в `Grid.Row="3"` (`:29`) — строки с таким индексом нет, WPF клампит в последнюю, из-за чего вёрстка «плывёт».

**Целевое:** фон `Surface`, `Foreground` из палитры, скругление 8, кнопка на `Selected` с радиусом 4, отступы 12, высота пересчитана.

**Работы:**
1. `Background="{DynamicResource Studio.Surface}"`, `Foreground="{DynamicResource Studio.MenuForeground}"`.
2. `WindowChrome` с тем же `CaptionHeight`, что у главного окна, + `WindowCorner` (либо оставить системный chrome и скругление через DWM — вариант выбирается один и применяется ко всем четырём диалогам).
3. Исправить `RowDefinition`: 4 строки (`Auto`×4) вместо `Grid.Row="3"` при трёх строках.
4. `Height` пересчитать либо `SizeToContent="Height"`.
5. Кнопки/`TextBox` — на `Studio.*`.

**Тесты:** регекс-тест «в четырёх диалогах есть `Studio.Surface` и `Foreground`; нет `Grid.Row` больше числа строк».
**Готово когда:** диалог тёмный, кнопка читаемая, ничего не обрезано.

---

### W-03 · Создать проект

**Файл:** `Views\ProjectCreatorView.xaml`; `Main.cs:306,318`.
**Сейчас:** `Height=290 Width=450`, строки 50/50/*/50, системный chrome, фон не задан. Содержит `TextBox`, кнопку `...` и список типов проекта.
**Целевое / работы:** как W-02 (фон, foreground, chrome, скругление); кнопка `...` шириной 40 → 28 с радиусом 4; `Margin="5"` → `12`.
**Готово когда:** тёмный фон; `ListBox`/`ComboBox` внутри на палитре, выделение нейтральное.

---

### W-04 · Открыть VS-проект

**Файл:** `Views\VsProjectOpenView.xaml`; `Main.cs:438`. `Height=180 Width=520`, две строки по 50, заголовок из `lang_open_vs_project_title`.
**Сейчас:** фон не задан, системный chrome.
**Работы:** те же, что W-02; проверить, что длинный путь не растягивает окно (фиксированная ширина 520).
**Готово когда:** тёмный фон; `TextBox` с путём на палитре.

---

### W-05 · Создать из VS-шаблона

**Файл:** `Views\VsTemplateProjectCreatorView.xaml`; `Main.cs:402`. `Height=245 Width=450`, строки 50/50/50/*.
**Сейчас:** фон не задан, системный chrome; последняя строка `*` — список шаблонов.
**Работы:** те же, что W-02; `ListView` шаблонов на палитре (`Studio.Surface` фон, `Selected` вместо системного `Highlight`).
**Готово когда:** тёмный фон, выделение строки нейтральное (не синее и не фиолетовое).

---

**Сделано (этап 8) для W-02…W-05:** все четыре диалога задают `Background="{DynamicResource Studio.Surface}"` и `Foreground="{DynamicResource Studio.Foreground}"`, внешний отступ 12 (внутренние — 0,0,0,8), кнопка обзора «...» сжата до 28px, в `ActivityCreatorView` исправлены строки (`Grid.Row="3"` при трёх объявленных → кнопка в строке 2) и удалены лишние отступы; `VsProjectOpenView` получил `HorizontalContentAlignment` вместо недопустимого у `TextBox` `TextTrimming`; `VsTemplateProjectCreatorView` — выравнивание кнопки по верху последней строки. Системный chrome на этом этапе оставался; снят на этапе 11 — заголовок рисует `Controls\StudioTitleBar.xaml`. Тесты: `AiRpaStudio.Tests\StudioDialogWindowTests.cs`.

### W-06 · Откреплённый документ (`LayoutDocumentFloatingWindowControl`)

**Файл:** `AvalonDock.Themes.Metro\Theme.xaml:1353-1500`; логика — `AvalonDock\Controls\LayoutDocumentFloatingWindowControl.cs` (`UpdateThemeResources` `:292-330`).

**Сейчас:**
- `WindowChrome`: `CaptionHeight=24`, `CornerRadius="0"`, `ResizeBorderThickness=10`.
- `DataTrigger IsActive=True` → `bd` и `Header` получают `BaseColor3` (в студии это `MaterialDesign.Primary` = DeepPurple), текст заголовка — `BaseColor1`. То есть активное плавающее окно фиолетовое.
- Кнопки maximize/restore/close — PNG (`PinMaximize.png`, `PinClose.png`), при hover подменяются на `*_Black.png` (в тёмной теме чёрная иконка не читается).
- `BorderThickness=1` вокруг всего окна (`BaseColor6`).
- Тема попадает в собственные `Resources.MergedDictionaries` окна (`:328`), ресурсы приложения сюда не проникают.

**Целевое:** фон `Surface`; заголовок `Surface` без заливки + hairline; активность передаётся рамкой окна (`Separator` → `SeparatorStrong`) и яркостью текста заголовка (`ForegroundMuted` → `Foreground`); иконки векторные; скругление 8.

**Работы:**
1. В триггерах `BaseColor3` → `AvalonDock_SeparatorStrong`, `BaseColor1` → `AvalonDock_Surface`.
2. PNG → `Path` (геометрия MaterialIcons) с `Fill = ForegroundMuted`/`Foreground` в триггерах; убрать `*_Black.png`.
3. `CornerRadius="8"` в `WindowChrome` (уже сделано в этапе 4) + `WindowCornerBridge.Apply` из `OnSourceInitialized` (сделано в этапе 5).
4. `BorderThickness` 1 → `AvalonDock_Separator`.
5. Для окон, созданных после смены темы, повторно вызвать проброс кистей из `MainView.DockManager_LayoutChanged` (см. M-02/M-05).

**Тесты:** регекс-тесты по `Theme.xaml` («нет `BaseColor3` в сеттерах», «нет `_Black.png`»), `WindowCornerTests`, STA-интеграция «после `ReapplyDockManagerResources()` новые ключи есть и в `floatingWindow.Resources`».
**Готово когда:** откреплённый документ тёмный, скруглённый, без фиолетового, иконки читаемы при hover.
**Сделано (этап 10):** заливка заголовка `SeparatorStrong` убрана; `IsActive` переключает `BorderBrush` у `WindowBorder` на `SeparatorStrong` и цвет текста заголовка на `Foreground` (по умолчанию — `ForegroundMuted`). Окну явно задан `Foreground = AvalonDock_Foreground`: раньше заголовок читался только за счёт заливки, без неё он наследовал системный чёрный. Панель внутри окна рамку гасит (`Model.IsHostedInFloatingWindow`), поэтому линия одна. Страж — `ThemeXamlTests.ThemeXaml_PaneFramesAreOnePixel`.

---

### W-07 · Откреплённая панель (`LayoutAnchorableFloatingWindowControl`)

**Файл:** `Theme.xaml:1505-1710`.

**Сейчас:** `Background = BaseColor1`, `BorderBrush = BaseColor6`, `BorderThickness=1`, `WindowChrome CaptionHeight=24` с `ReservationBorder`, `CornerRadius="0"`; `Header Background = BaseColor3`; `DataTrigger Model.SinglePane.SelectedContent.IsActive` снова даёт `BaseColor3`, иконки `*_Dark.png`.
**Целевое:** идентично W-06 — единый `Surface` без заливки заголовка, hairline, векторные иконки, радиус 8, активность — рамкой окна. Заголовок панели визуально не отличается от заголовка документа.
**Работы:** пункты 1-4 из W-06 + контекстное меню `SinglePaneContextMenu` (сейчас PNG).
**Готово когда:** откреплённая панель стилистически неотличима от откреплённого документа.
**Сделано (этап 10):** постоянная заливка `Header Background = SeparatorStrong` убрана (в светлой теме она давала белый текст на светло-сером), заголовок — `Surface` + `ForegroundMuted`; `Model.SinglePane.SelectedContent.IsActive` переключает `BorderBrush` у `WindowBorder` и цвет текста, иконки — на `Foreground`.

---

### W-08 · Переключатель документов (Ctrl+Tab, `NavigatorWindow`)

**Файл:** `Theme.xaml:1711-2025` + `AvalonDock\Controls\NavigatorWindow.cs` (тема подмешивается в `:421-441`).

**Сейчас:**
- Стиль `NavigatorWindow`: `Background = SystemColors.ControlBrushKey`, `SizeToContent`, `ResizeMode="NoResize"`.
- Внутренний `Border Background="White"` (`:1875`) с `BorderBrush = SystemColors.ControlDarkBrushKey` — всегда белое окно в тёмной теме.
- Внутренняя область (`:1918`) — `BaseColor7`.
- Списки: `ItemContainerStyle = AvalonDock_ThemeMetro_NavigatorWindowListBoxItemStyle` с градиентами `#FFF1FBFF/#FFD5F1FE` (hover) и `#FFD9F4FF/#FF9BDDFB` (selected), `BorderBrush="#FF98DDFB"`, `UpperHighlight Fill="#40FFFFFF"`; `FocusVisual` — `Rectangle Stroke="#8E6EA6F5"` (сине-фиолетовый).
- Четыре градиента объявлены как `LinearGradientBrush` (`:1729-1755`) — переопределить кистью нельзя, только заменить значение.

**Целевое:** фон `Surface`; заголовочная зона тоже `Surface`; hairline 1px; hover = `Hover`; selected = `Selected` (сплошная кисть); focus rect = `SeparatorStrong`; скругление окна 8.

**Работы:**
1. `:1875` `Background="White"` → `{DynamicResource AvalonDock_Surface}`, `BorderBrush` → `AvalonDock_Separator`.
2. `:1918` `BaseColor7` → `AvalonDock_Surface`.
3. Четыре `LinearGradientBrush` → `SolidColorBrush`-ключи `AvalonDock_NavigatorHover` / `_NavigatorSelected` (+ `_Inactive`, `_SelectedHover` при необходимости).
4. `FocusVisual Stroke` → `AvalonDock_SeparatorStrong`.
5. `UpperHighlight` убрать либо сделать `#14FFFFFF`/`#14000000` по теме.
6. `WindowCorner` в `NavigatorWindow` (M-03).

**Тесты:** регекс «нет `#FF98DDFB`, `#8E6EA6F5`, `Background="White"`», `WindowCornerTests`.
**Готово когда:** Ctrl+Tab показывает тёмное окно без голубых градиентов; в светлой теме — светлое.

---

### W-09 · Оверлей перетаскивания (`OverlayWindow`)

**Файл:** `Theme.xaml:491-600` + `AvalonDock\Controls\OverlayWindow.cs` (тема в `:184-209`).

**Сейчас:** `Canvas Opacity="0.9"`; `Path PART_PreviewBox Fill="AliceBlue" Stroke="{SystemColors.HighlightBrushKey}" StrokeThickness="2"` (в студии `Highlight` = Primary → фиолетовая обводка). Дополнительно ~18 PNG-подсказок dock-таргетов (`DockAnchorable*.PNG`, `DockDocument*.png`, `DockPaneEmpty.png`) — растровые и светлые.
**Целевое:** `Fill = AvalonDock_OverlayFill` (Surface @15%), `Stroke = AvalonDock_OverlayStroke` 1px; dock-таргеты векторные на палитре; `Opacity` 0.9 оставить.
**Работы:** замена `Fill`/`Stroke`, `StrokeThickness` 2 → 1, замена иконок-таргетов на `Path` либо (компромисс) тонирование подложки `Border` фоном `Surface`.
**Тесты:** регекс «нет `AliceBlue`», «нет ссылок на `.PNG`/`.png` в секции Overlay».
**Готово когда:** при перетаскивании вкладки подсказка нейтральная и различима в обеих темах.

---

### W-10 · Панель автоскрытия (`LayoutAutoHideWindowControl`)

**Файл:** `Theme.xaml:1751-1756`.
**Сейчас:** один сеттер `Background = AvalonDock_Surface`; `BorderBrush` и `Foreground` не заданы.
**Целевое:** `Surface` + рамка 1px по линии примыкания, заголовок как у W-07.
**Работы:** `HwndHost` собственных кистей не имеет, поэтому рамку рисует вложенный `LayoutAnchorableControl`: для него объявлен keyed-стиль `AvalonDock_ThemeMetro_AutoHideAnchorableStyle` (`BorderBrush = AvalonDock_Separator`, `BorderThickness = 1`) и передан через штатное свойство `AnchorableStyle` (`LayoutAutoHideWindowControl.cs:76-84`, применяется в `:286`).
**Сделано (этап 10):** стиль добавлен и подключён сеттером `AnchorableStyle` — рамка 1px есть, `MC4005` не возникает, отклонение «W-10 закрыт частично» снято.
**Готово когда:** выдвинутая полоска автоскрытия не светлее окна и обведена рамкой 1px.

---

### W-11 · Ресайзер панелей и контейнер контента документа

**Файл:** `Theme.xaml:1120-1131` (`LayoutGridResizerControl`), `:1133-1143` (`LayoutDocumentControl`).
**Сейчас:** ресайзер — `Border` с `TemplateBinding Background`, `Background` не выставлен, hover отсутствует; `LayoutDocumentControl` — `Border` с `TemplateBinding`, кисти не заданы (`GridSplitterWidth/Height = 5` в стиле `DockingManager`).
**Целевое:** в покое ресайзер линии не рисует (`Background = Transparent`), при `IsMouseOver`/`IsDragging` — 1px `SeparatorStrong`; контент документа рамку не рисует (рамку документа даёт `LayoutDocumentPaneControl`), фон наследует `Surface`.
**Работы:** `Background` ресайзера → `Transparent` (именно `Transparent`: `null` отключает hit-testing и сплиттер нельзя схватить); триггеры hover/drag оставлены; `Bd Margin="2"` при `GridSplitterWidth/Height = 5` даёт ровно 1px линии по центру зоны захвата.
**Сделано (этап 10):** постоянная линия убрана, ширина полосы захвата 5px сохранена (при 4px и `Margin="2"` линия стала бы нулевой); между двумя панелями теперь ровно две линии — по одной на рамку.
**Готово когда:** между панелями нет постоянной линии ресайзера, при захвате появляется подсветка, двойных рамок вокруг вкладки документа нет.

---

### W-12 · Popup-хосты

Не `Window`, но в тёмной теме выглядят светлыми островами:
- `AiRpaStudio\Controls\SearchableComboBox.xaml:105` — popup с собственным фоном;
- `RPA.Chat\Views\ChatPanel.xaml:273` — border-popup;
- `LogControlLibrary\Controls\LogViewerControl.xaml:320` — `Border Background="White"`.

**Работы:** перевести на `DynamicResource Studio.Surface` и `Studio.ToolbarBorder` (эти ключи заполняются в `Application.Resources`, popup — дочерняя визуальная ветка, наследование работает).
**Готово когда:** ни один popup не светлее панели.

---

## 5. Панели AvalonDock в главном окне

Раскладка строится кодом: `MainView.xaml.cs:310-450` (`CreateDefaultLayout`); восстановление — `Properties.Settings.ADLayout` (`:194-260`); пере-темизация — `:115-122` (`ReapplyWorkflowControlThemes`) и `:568` (`DockManager_LayoutChanged`).

| ID | ContentId | Контент | Примечание |
|---|---|---|---|
| P-01 | `ProjectManager` | `ProjectManagerControl` (`wfProjectBorder`) | заголовок вкладки/панели на `AvalonDock_*`; рамка панели 1px (правило 6), у самого контрола рамки нет |
| P-02 | `Activities` | `ToolboxPanel` → `ToolboxControl` (WF) | см. F-01 |
| P-03 | `Outline` | `Border` → `OutlineView` (WF) | см. F-02 |
| P-04 | `Properties` | `Border` → `PropertyInspectorView` (WF) | см. F-03 |
| P-05 | `Log` | `LogViewerControl` | см. W-12 / F-07 |
| P-06 | `Chat` | `ChatPanel` | см. F-08 |
| P-07 | `NuGet` | `NuGetPanel` | ✅ переведена на дизайн VS 2026 **без скруглений**: цвета только ключами палитры, `CornerRadius="0"`, шрифт `Studio.FontFamily`; стражи — `NuGetPanelThemeTests` (см. `NuGet_Panel_Redesign_Plan.md`) |
| P-08 | документы | `WorkflowDesigner.View` / `CodeEditorView` | см. F-04 |

Общие части темы, правящиеся один раз (действуют на все панели):

- `AnchorablePaneControlStyle` (`Theme.xaml:124-…`) — рамка панели: `BorderThickness=1`, `BorderBrush=AvalonDock_Separator`; `ContentPanel` `BorderThickness="1"`; полоса табов обёрнута в `HeaderStrip` (`1,0,1,1`), `HeaderPanel Margin` → `0`; активность — `BorderBrush=SeparatorStrong`; внутри плавающего окна (`Model.IsHostedInFloatingWindow`) рамка гасится в `0`.
- Вкладка панели (TabItem в том же стиле) — `BorderThickness=1` → `0`; выбранная = `AvalonDock_Active`, hover = `AvalonDock_Hover`.
- `AnchorablePaneTitle` (`:294-400`) — `DataTrigger Model.IsActive` → `BaseColor3` убрать; фон заголовка `Surface`, hairline под заголовком (`BorderThickness="0,0,0,1"`), потому что верхнюю линию теперь рисует рамка панели.
- `AnchorableTitleTemplate` (`:373-386`) — жёсткий `Foreground="White"` и `FontFamily="Consolas"` → `AvalonDock_Foreground`, `Segoe UI`.
- `AnchorableHeaderTemplate` — `Consolas` → `Segoe UI`.
- `AnchorTemplate` и auto-hide полоски (`:437-487`) — `BorderBrush = BaseColor3` толщиной 2-5px (акцентная полоса) → `AvalonDock_Separator` 1px, активное состояние передаётся `Foreground`.
- `DocumentPaneControlStyle` (`Theme.xaml:804-…`) — шапка `BorderThickness="1,1,1,0"`, контент `"1"` (линию «табы ↔ контент» рисует верхняя рамка контента), `BorderBrush` — `TemplateBinding`; активность и гашение в плавающем окне — теми же триггерами.
- Стиль `DockingManager` (`:1032-1120`) — `Background` = `AvalonDock_Surface`, `BorderThickness="0"`.
- `ToolButtonStyle` (`:39-57`) и `ButtonStyle` (`:60-82`) — добавить `IsMouseOver` (`Hover`), `IsPressed`/`IsChecked` (`Selected`), `CornerRadius=4`.
- `LayoutAnchorableTabItem` (`:1259-1283`), `LayoutAnchorableControl` (`:1284-1350`) — убрать `BorderThickness` из шаблонов.
- `ContextMenuEx` (`:2027-2029`) — проверить, что меню берёт палитру MaterialDesign без акцента в подсветке.

**Тесты:** регекс-тесты по `Theme.xaml` (этап 4) + ручной чеклист раздела 9.
**Сделано (этап 10):** рамку рисует ровно один элемент на зону — матрица в разделе 3 («Кто рисует рамку докпанели»); все пункты списка выше реализованы в `AvalonDock.Themes.Metro\Theme.xaml`, страж — `ThemeXamlTests.ThemeXaml_PaneFramesAreOnePixel`, ручная проверка — пункт 8 чеклиста.

---

## 6. Контролы Workflow Foundation

| ID | Контрол | Где появляется | Сейчас | Работы |
|---|---|---|---|---|
| F-01 | `ToolboxControl` (+`TreeView PART_Tools`, `ToolboxCategory`, `ToolboxItem`) | панель «Активности» | `WFToolboxHelper.Build()`, `ToolboxPanel.SetToolboxControl`, `WfVisualTreeThemer.Apply` | hover/selected через триггеры стиля, а не разовое присваивание `Foreground`; `Consolas` → `Segoe UI`; скроллбар на палитре |
| F-02 | `OutlineView` (TreeView + поиск + collapse/expand) | панель «Структура» | `WfVisualTreeThemer.Apply` (`MainView.xaml.cs:118`) — только `Foreground` у `TreeViewItem` | добавить `TreeViewItem` `Style` с `IsSelected`/`IsMouseOver` (сейчас выделение = `SystemColors.HighlightBrushKey`, а он в `StudioBrushes.ApplySystemBrushes` = Primary/DeepPurple → акцент) |
| F-03 | `PropertyInspectorView` | панель «Свойства» | `PropertyWindowStyleHelper`: обход «первый `Border` → первый `Grid` → первый `StackPanel`», `Debug.WriteLine` на каждом шаге, кнопки 28×28, `Colors.Transparent` статикой | переписать на поиск по именам (`_propertyToolBar`, `PART_ByCategoryViewButton`, `PART_AlphaViewButton`, `PART_SearchClearButton`), убрать `Debug.WriteLine`, добавить реакцию на смену темы без пересоздания контрола, разделители — `Separator` |
| F-04 | `DesignerView` (поверхность дизайнера, shell bar `Imports`/`MiniMap`/`Variables`/`Arguments`/`Zoom`/`PanMode`, breadcrumb, `ZoomControl`, `MiniMap`, rubber band, сетка/watermark) | вкладки документов | не темизируется вообще | новый `Helpers\WfDesignerTheme.cs` + `Helpers\WfDataGridTheme.cs` (таблицы панелей «Переменные/Аргументы/Пространства имён»); вызов из `DocGroupHelper.AddDocument` (`WorkflowDesignerView_Loaded`, `DocGroupHelper.cs:82-85`) и из `MainView.ReapplyWorkflowControlThemes` |
| F-05 | `ExpressionTextBox` + `RoslynExpressionEditor` | внутри дизайнеров активностей | `RoslynExpressionEditor.xaml:335` ссылается на несуществующий ключ `DesignerViewBackground` → фон остаётся пустым | объявить `DesignerViewBackground` / `DesignerViewForeground` в палитре |
| F-06 | Адорнеры `ValidationError`, `ErrorView`, контекстные меню дизайнера во всех трёх таблицах (`DataGrid` панелей «Переменные», «Аргументы», «Пространства имён»; `DesignerVariableContextMenuService.cs`; принципы, инварианты и стражи — `Designer_Variable_ContextMenu_Principles.md`) | поверхность дизайнера | не темизированы | кисти из палитры; красный только для ошибок (семантика, не акцент) |
| F-07 | `LogViewerControl` | панель «Log» | `#FAFAFA` (`:190,227`), `White` (`:321`), `#F0F0F0` (`:493`), `#CCCCCC` | → `Studio.Surface` / `Studio.ToolbarBorder` |
| F-08 | `ChatPanel`, `PinnedChatMessageControl` | панель «Chat» | своя тема `Dark.*`: `Dark.Accent #264F78`, `Dark.Primary #64B5F6` (dark) / `#1976D2` (light); подмена `Resources.MergedDictionaries[0]` в `ChatPanel.xaml.cs:66-69` | `Dark.Accent`/`Dark.Primary` → нейтрали (`Selected`/`Foreground`) либо прокинуть `Studio.*` в `ChatTheme.*.xaml` |

**Сделано (этап 6):** F-01, F-02, F-03 закрыты — цвета только из `StudioPalette` (`MaterialThemePalette` в WF-контролах не используется), шрифт Segoe UI, состояния через триггеры стиля, `PropertyWindowStyleHelper` ищет по именам и перекрашивается повторным применением. Скроллбар и виртуализованные элементы — частично (см. отклонения этапа 6).
**Сделано (этап 7):** F-04, F-05, F-06 закрыты — `Helpers\WfDesignerTheme.cs` темизирует поверхность дизайнера (один `Surface`, hairline вместо рамок, shell bar/breadcrumb/MiniMap на палитре), объявляет `DesignerViewBackground`/`DesignerViewForeground` и ключи шаблонов WF, ставит свой адаптер ошибки (`CloseHoverBackground` — единственный красный) и отдаёт контекстное меню дизайнера на палитре. Ограничения (см. отклонения): валидационная иконка WF не перекрашивается, `PropertyInspectorFontAndColorData` не используется.
**Сделано (контекстные меню дизайнера — второй заход по живому дефекту).** В таблицах «Переменные» и
«Аргументы» меню «Вырезать/Копировать/Вставить» рисовалось белым с белыми буквами. Причин две: меню
живёт в **popup** и в проходы темы по дереву дизайнера не попадает ни разу, а внешний вид задавал чужой
шаблон (`MaterialDesignContextMenu`/`MaterialDesignMenuItem` из `App.xaml`, в чистом процессе Aero2) —
его подсветка строится от `Foreground`, поэтому в тёмной теме наведение давало светлую накладку под
белым текстом. Локальные `Background`/`Foreground` этот симптом не лечат. Добавлен
`AiRpaStudio\Helpers\WfContextMenuTheme.cs`: собственные `Template` меню, `ItemsPanel` и
`ItemContainerStyle` пунктов на палитре (`Surface`, hairline `SeparatorStrong`, `Hover`,
`ForegroundMuted` для недоступного пункта, радиус `MenuCornerRadius`, `HasDropShadow = false`,
`PART_Popup` подменю) и общий проход `Hook()` по `ContextMenu.OpenedEvent` для меню самого дизайнера,
ограниченный цепочкой родителей/`PlacementTarget` до `DesignerView`/`System.Activities.Presentation`;
палитру, заданную фабрикой, проход не перебивает. Меню правого клика темизируется **до показа**
(`ContextMenuService.ContextMenuOpeningEvent` → `PrepareOpen`): стиль пунктов раздаётся
`ItemContainerStyleSelector`, а `Separator` получает свой hairline-стиль — иначе чужой
`ItemContainerStyle` с `TargetType=MenuItem` роняет открытие меню на разделителе. `WfDesignerTheme.CreateEditingContextMenu` и
`RecolorEditingContextMenu` идут через одну точку; шаблоны кэшируются по палитре. Стражи —
`WfContextMenuThemeTests` — 11.
**Сделано (этап 8):** F-07, F-08 закрыты — `LogControlLibrary\Controls\LogViewerControl.xaml` переведён на `{DynamicResource Studio.*}` (фон панели, разделители, hover/выделение, статусная строка, popup настроек, заголовки колонок), а `LogLevelToColorConverter`/`LogLevelToHoverConverter`/`LogLevelToBackgroundConverter` отдают средние тона и полупрозрачные накладки (замороженные кисти). В `RPA.Chat` все цвета собраны в `ChatTheme.Dark.xaml`/`ChatTheme.Light.xaml`, акцент заменён нейтральными токенами, локальные `<SolidColorBrush>` из контролов убраны, а в студии словарь на лету перекрывает `RPA.Chat\Themes\ChatStudioBridge.cs` (ключи `Studio.*` читаются по строкам — сборка не ссылается на `RPA.Helpers`).

**Сделано (комбобоксы панели «Свойства активности» — второй заход по живому дефекту).** На запущенной
студии симптом остался: в строках `Modifiers`/`Scope` типа `DesignTimeVariable` свёрнутый комбобокс
показывал белое поле, а в раскрытом списке `Modifiers` (flags-enum → чекбоксы `ReadOnly`/`Mapped`) буквы
были чёрными на тёмной подложке. Замеры по UI Automation и по пикселям запущенного процесса показали:
каркас панели темизируется (кнопки 28×28, поиск 24, фон `Surface`), но до комбобоксов строк проход не
доезжает — строки свойств WF создаёт позже (выбор активности, разворот категории), а `ThemeComboBox`
работал до реализации шаблона, когда внутреннего `PART_EditableTextBox` ещё нет, поэтому поле правки
оставалось белым. Что добавлено: `comboBox.ApplyTemplate()` до покраски; `PaintItemForeground` —
локальная покраска текста popup (строки и чекбоксы flags-enum, включая привязки, которые
`NeutralizeLightBrushes` пропускает); подписки `ItemContainerGenerator.StatusChanged` (контейнеры
создаются при раскрытии) и `IsKeyboardFocusWithinChanged` (переход в режим правки); в
`PropertyWindowStyleHelper.Attach` — доводка комбобоксов по `PreviewMouseDown`/`GotKeyboardFocus` панели
(клик внутри инспектора — гарантированная точка, где строка уже существует). Стражи —
`WfDesignerTableThemeTests.ThemeComboBoxes_PaintsEditableFieldOfCollapsedComboBox`,
`.ThemeComboBoxes_PaintsDropDownCheckBoxTextWithPalette`,
`.SetPropertyStyles_ThemesComboBoxOfRowCreatedAfterSweep`.

**Сделано (дополнение к F-03/F-04 — комбобоксы WF-поверхностей).** Свёрнутые `ComboBox` таблиц (столбцы
`Scope`/`Modifiers` в «Переменных» и «Аргументах») и панели «Свойства» (enum-свойства) стояли на опаковой
белой плашке шаблона Aero2, а элементы их раскрытых списков оставались системными — чёрные буквы на
тёмном фоне. Причины: 1) `WfDataGridTheme.Refresh` комбобоксы не темизировал вовсе; 2)
`PropertyWindowStyleHelper.ApplyValueEditors` красил `ComboBox`/`ComboBoxItem` внутри визуального дерева
инспектора, но список живёт в popup — вне этого дерева, поэтому обход `ComboBoxItem` был мёртвым кодом;
3) `WfVisualTreeThemer.ThemeComboBoxes` ставил подписку на `Loaded` только когда комбобоксов ещё нет: в
дизайнере к этому моменту `zoomPicker` уже есть, поэтому позже созданные комбобоксы не темизировались
никогда. Решение: единственный вход `WfVisualTreeThemer.ThemeComboBoxes` (безусловная подписка на
`Loaded` с актуальной палитрой), вызов последним проходом `WfDataGridTheme.Refresh`,
`WfDataGridTheme.HookGrid` ставит `PreparingCellForEdit` (палитра до первой отрисовки, а не следующим
циклом раскладки), общий вход `WfVisualTreeThemer.ApplyEditorPalette` (поля ввода + комбобоксы) для
таблиц и для `DesignerVariableContextMenuService`, `PropertyWindowStyleHelper.ApplyValueEditors` отдаёт
комбобоксы строк тому же механизму. Стражи —
`WfDesignerTableThemeTests.Apply_LateCreatedCellComboBoxIsThemed`,
`.Apply_CellComboBoxItemStyleUsesPalette`, `.Sources_TableThemeThemesComboBoxes`,
`WfThemeTests.SetPropertyStyles_ThemesInspectorComboBoxes`,
`.SetPropertyStyles_SwitchesInspectorComboBoxPalette`,
`DesignerVariableContextMenuTests.PreparingCellForEdit_ComboBoxGetsPalette`.

**Сделано (третье дополнение — WF создаёт редактор значения уже после прохода темы).** Замеры на
запущенной студии (UI Automation + пиксели панели «Свойства активности») после второго захода: при выборе
переменной, когда строка `Modifiers`/`Scope` видна, но по ней ещё не кликали, левый край редактора
`Modifiers` (`FlagEditor`, `autoId=ComboBox_1`) — `#FFFFFF`, `Scope` (`autoId=scopeEditoComboBox`) —
`#EFEFEF`, поле правки `PART_EditableTextBox` — `#F1F1F3`; после первого клика те же пиксели становятся
`#272727`. Причина: WF материализует редактор значения (и пересоздаёт его при смене выделения) **после**
прохода темы, поэтому `ThemeComboBoxes` его не видел, а отпечаток дерева инспектора (`ComputeSignature` —
число контролов и `TextBlock`) к этому моменту уже не менялся, и повторного прохода не было. Решение:
`WfVisualTreeThemer.HookLayoutSweep` — корень, за которым наблюдает темизатор, на каждом `LayoutUpdated`
синхронно проверяет, есть ли комбобоксы без отметки активной палитры (`ComboBoxMarks`), и темизирует только
их отложенно (приоритет `Background`, флаг `Pending` — одна проверка на цикл раскладки); плюс
`ScheduleComboBoxRetry` — один повторный проход для комбобокса, у которого шаблон ещё не материализован
(`Template == null` или нет `PART_EditableTextBox` при `IsEditable`), с пометкой на палитру от бесконечной
цепочки. Контрольный замер после правки: доминирующий цвет всех редакторов строк (`Modifiers`, `Scope`,
`Type`, поля ввода) — `#272727`. Стражи —
`WfDesignerTableThemeTests.ThemeComboBoxes_ComboBoxCreatedAfterSweep_IsThemedOnLoad` (красный без правки),
`.ThemeComboBoxes_ComboBoxOutsideThemedRootsKeepsSystemChrome`,
`.Sources_ThemerThemesLateCreatedComboBoxesOnLayout`.

**Регресс F-04 (shell bar, комбобокс масштаба):** свёрнутый `zoomPicker` показывал белые буквы на белом
фоне. WF-стиль `statusBarComboBoxStyle` задаёт только `FontFamily`/`Foreground`/`Background = #00FFFFFF`,
поэтому видимую площадь рисует шаблон Aero2: `Border#templateRoot` кнопки раскрытия и `Border#Border`
редактируемой части — опаковый белый. `Foreground` комбобокс наследует от shell bar, то есть в тёмной
теме он светлый. Решение: `WfVisualTreeThemer.ThemeComboBoxes` (вызов из `WfVisualTreeThemer.Apply` и из
`WfDesignerTheme.ThemeShellBar`, потому что путь `TryApplyShellBarStyle` полного обхода не делает) —
кисти и шрифт палитры, `ItemContainerStyle` элементов списка с собственным шаблоном (состояния Aero2
`Border#Bd` светлые), гашение белых плашек `PaintTemplatePlates` (общий проход их не трогает: чистый
белый — токен палитры `CloseHoverForeground`), `ForegroundMuted` для стрелки раскрытия и темизация
popup-списка по `DropDownOpened`. Страж — `WfThemeTests.Apply_ThemesComboBoxes`,
`WfDesignerThemeTests.Apply_ThemesShellBarZoomCombo`, `.Apply_ShellBarZoomComboIsReadableInBothThemes`.


**Панели «Переменные», «Аргументы», «Пространства имён» (F-04, F-06): вкладки, таблицы, меню.** К вкладкам
shell bar, их таблицам (`DataGrid`) и контекстному меню тема не применялась. Причины три: (1) WF объявляет
таблицы стилем `defaultDataGridStyle` с `{StaticResource}` на дефолтные цвета WF
(`GridViewRowUnderlineColor`, `GridViewRowSelectedColor`, `GridViewRowHoverColor`), а `StaticResource` не
пересчитывается — шапка, сетка и выделение оставались светлыми; (2) плашки шапки, угла «выделить всё» и
строк рисует шаблон системной темы Aero2, а триггер шаблона сильнее сеттера и триггера стиля; (3) сервис
контекстного меню подключался ровно к двум панелям по именам, поэтому таблица пространств имён
(`importedNamespacesDataGrid`) не обслуживалась вовсе. Решение: `Helpers\WfDataGridTheme.cs` (стили шапки,
строки и ячейки — обёртки стилей WF через `WfThemeStyles.Wrap`, локальные кисти у материализованных
контейнеров через `WfThemeStyles.WatchState`, гашение светлых плашек общим `NeutralizeLightBrushes`,
повтор прохода по `ItemContainerGenerator.StatusChanged` и отпечатку раскладки, фон панели-контейнера
`Surface`), вызов из `WfDesignerTheme.Apply` после общего темизатора WF; вкладки shell bar —
`WfDesignerTheme.ThemeShellBarItems` (активная — `Selected`, hover — `Hover`, неактивная подпись —
`ForegroundMuted`); `DesignerVariableContextMenuService.RefreshTables` подключает **все** таблицы дизайнера
по типу, а не по именам. Стражи: `AiRpaStudio.Tests\WfDesignerTableThemeTests.cs` (18 тестов, включая живую
таблицу в окне и проверку отсутствия светлых плашек), `DesignerVariableContextMenuTests.RefreshTables_*`;
приёмка — `Vs2026_Theme_Acceptance.md`, п.3.


**Регресс после этапа 6 (панель «Активности», тёмная тема):** под названиями категорий оставалась светлая
плашка. Причина не в палитре: WF-стиль категории (`TreeViewContainerStyleSelector`) задаёт контейнеру
`Background = #E9ECEE`, шаблон элемента повторяет его через `TemplateBinding` (`Border#Bd`), а состояние
(hover/выделение) рисует системная тема Aero2 плашками `#FFE8A6`/`#F0F0F0` с оранжевой рамкой `#E5C365`.
Подмена `SystemColors.*BrushKey` на зашитое в стиль значение не влияет, а подменять сам стиль запрещено
(в нём `EventSetter` перетаскивания, см. `ToolboxDragContractTests`). Решение: единый проход
`WfThemeStyles.NeutralizeLightBrushes` по эффективным значениям всех `Brush`-свойств (площади → `Surface`,
линии → `Transparent`, тёмный текст → `Foreground`, стрелка экспандера → `ForegroundMuted`, кисти
предыдущей темы пересчитываются по токену палитры), состояния элементов с WF-стилем ведёт
`WfVisualTreeThemer.HookItemStates` (Hover/Selected вместо системных цветов, плашка шаблона перепривязана
к `Background` элемента), а контейнеры, созданные после темизации (разворачивание категории, «Развернуть
всё», виртуализация), достраивает `ToolboxPanel.HookContainers` по `ItemContainerGenerator.StatusChanged`
и `TreeViewItem.ExpandedEvent`. Дополнительно в `StudioBrushes.SystemBrushKeys` закрыты градации
`ControlLight*/ControlDark*/ScrollBar/AppWorkspace/WindowFrame/Info`. Стражи:
`AiRpaStudio.Tests\ToolboxThemeTests.cs`, `StudioBrushesTests.Apply_SystemBrushGradationsFollowPalette`.

---

## 7. Общие механизмы

**M-01 · Единая палитра.** Новый `AiRpaStudio\Helpers\StudioPalette.cs`: `StudioThemeColors` (все токены раздела 2), `StudioPalette.For(bool dark)`, `DetectDark(ResourceDictionary)` по luminanсe. `StudioBrushes` и `DockingThemeBrushes` берут значения только оттуда, а не из `MaterialDesign.Primary`/`Secondary`.

**M-02 · Мост кистей для окон AvalonDock.** `DockingManager` отдаёт тему в `MergedDictionaries` каждого `LayoutFloatingWindowControl` (`:328`), `NavigatorWindow` (`:441`), `OverlayWindow` (`:209`) — их `Resources` изолированы от `Application.Resources`. Нужен статический `AvalonDock\Themes\ThemeBrushBridge.cs` и по одному вызову в трёх точках; единственная правка ядра AvalonDock, публичный API не меняется.

**M-03 · Скругления окон.** `AiRpaStudio\Helpers\WindowCorner.cs`: `DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)`; вызов в `SourceInitialized` для W-01…W-08; fallback (Win10 или ошибка DWM) → `WindowChrome.CornerRadius=8` + `GlassFrameThickness`. `AllowsTransparency` не включаем (потеря ClearType и просадка производительности).
**Сделано (этап 8):** `Primary`/`Secondary` темы MaterialDesign создаются из палитры (`SeparatorStrong` и `ForegroundMuted`), `App.xaml` объявляет `BaseTheme="Dark"` — рассинхрон со стартовой тёмной темой устранён.

**M-04 · Материализация без акцента.** `AppSettings.ApplyMaterialDesignTheme` (`:161-172`) создаёт тему с `DeepPurple` + `Lime` — заменить на нейтральные `Primary`/`Secondary` (серый), чтобы акцент не пробивался в фокус и скроллбары стандартных контролов. Дефолт темы — dark (`ChatViewModel.IsDarkTheme` → `_settings.UseDarkTheme ?? true`), при этом `App.xaml` объявляет `BaseTheme="Light"` → рассинхрон убрать, стартовать сразу в dark.

**M-05 · Единственная точка применения.** `AppSettings.ReapplyDockManagerResources()` (`:85-110`) остаётся единственным местом записи кистей AvalonDock (уже вызывается из `ApplyTheme` и `DockManager_LayoutChanged`).

---

## 8. Этапы (TDD: red → green → refactor)
### Сделано по этапам 1-10

| Этап | Артефакты | Тесты |
|---|---|---|
| 1. M-01 | `AiRpaStudio\Helpers\StudioPalette.cs`: 12 токенов, две палитры, `DetectDark` по яркости, `Luminance`/`Chroma`, скругления 8/4/4/6, `OverlayFill = Surface@15%` | `StudioPaletteTests` — 13 |
| 2. `StudioBrushes` | `Helpers\StudioBrushes.cs` переписан на палитру: 24 алиаса (`Studio.*`, включая `Studio.Surface`/`Studio.Separator`/`Studio.OverlayFill`), `SystemColors.Highlight*` нейтральны, `ToolbarPressed/Checked → Selected` | `StudioBrushesTests` — 13 |
| 3. M-02 | `AvalonDock\Themes\ThemeBrushBridge.cs` (16 ключей + нейтральные fallback), вызовы в `LayoutFloatingWindowControl.UpdateThemeResources`, `NavigatorWindow.UpdateThemeResources`, `OverlayWindow.UpdateThemeResources`; `Helpers\DockingThemeBrushes.cs` пишет все `AvalonDock_*` и legacy `BaseColor1..7` из палитры; `AppSettings.ReapplyDockManagerResources` — единственная точка записи (в т.ч. в `Application.Resources` как источник для моста) | `AvalonDockThemeBridgeTests` — 8, `DockingThemeBrushesTests` — 15 |
| 4. `Theme.xaml` | `BaseColor1..7` → нейтральные ключи (`Surface`/`Foreground`/`Separator`/`SeparatorStrong`/`SelectionHover`/`SurfaceAlt`), ноль зашитых hex в файле, скругления 8/4, hairline-ресайзер панелей и шапка документа, оверлей `Surface@15%` 1px, Ctrl+Tab без градиентов и синего фокуса, `Pin*.png` → векторные `PathGeometry`-глифы, `Consolas` → `Segoe UI` | `ThemeXamlTests` — 9 |
| 5. M-03 | `AiRpaStudio\Helpers\WindowCorner.cs` (DWM `33`/`2`, fallback на region через `WindowChrome.CornerRadius`, идемпотентный `Enable`), `AvalonDock\Themes\WindowCornerBridge.cs` + вызовы из `OnSourceInitialized` в `LayoutFloatingWindowControl` и `NavigatorWindow`, `MainView.xaml` `CornerRadius="8"`, `WindowCorner.Enable(this)` в пяти окнах студии, `WindowCorner.EnableAvalonDockWindows()` в `App.Application_Startup` | `WindowCornerTests` — 23 |
| 6. F-01…F-03 | `AiRpaStudio\Helpers\WfThemeStyles.cs` (шрифт Segoe UI, замороженные кисти, маркер «стиль уже наш» + `Wrap()` с сохранением шаблона через `BasedOn`); `Helpers\WfVisualTreeThemer.cs` переписан на `StudioPalette`, ставит `ItemContainerStyle` с триггерами `IsMouseOver` → `Hover` и `IsSelected` → `Selected`, добавлен `ApplyWhenLoaded()`; `Helpers\PropertyWindowStyleHelper.cs` переписан на поиск по именам (`_propertyToolBar`, `PART_ByCategoryViewButton`, `PART_AlphaViewButton`, `PART_SearchClearButton`), состояния кнопок — триггеры стиля, ноль `Debug.WriteLine`, разделители — `Separator`, повторное применение без пересоздания контрола; `ToolboxPanel` — `Consolas` → Segoe UI, `ApplyWhenLoaded`; `MainView` — единый `_propertyStyleHelper` + `ApplyWhenLoaded` для `OutlineView` и `PropertyInspectorView`; `DocGroupHelper` — переиспользуемый хелпер | `WfThemeTests` — 24 |
| 7. F-04…F-06 | `AiRpaStudio\Helpers\WfDesignerTheme.cs`: ключи `DesignerViewBackground`/`DesignerViewForeground` (F-05) + ключи шаблонов WF (поверхность, элементы активности, shell bar, breadcrumb, MiniMap, контекстные меню, PropertyInspector — берутся из публичных `WorkflowDesignerColors.*Key`), все цвета только из `StudioPalette`; словарь палитры кладётся и в `Application.Resources`, и в `DesignerView.Resources` (иначе локальные `ColorResources.xaml` перекрывают приложение) и мутируется на месте при смене темы (без роста `MergedDictionaries`); дефолтные цвета WF, пришедшие через `StaticResource`, заменяются по значению, пользовательские кисти не трогаются; `Validation.ErrorTemplate` — hairline `CloseHoverBackground` (красный только для ошибок); `WfDesignerTheme.CreateEditingContextMenu` для `DesignerVariableContextMenuService` (ноль `Debug.WriteLine`, ноль зашитых `Colors.Black`/`Colors.Green`); обход дерева идёт по логическим и визуальным детям (дизайнер темизируется до применения шаблона); вызовы из `DocGroupHelper.DesignerView_Loaded`, `MainView.ReapplyWorkflowControlThemes`, `App.Application_Startup`, `AppSettings.ApplyTheme` | `WfDesignerThemeTests` — 24 |
| 8. W-02…W-05, F-07, F-08, M-04 | Диалоги: `Background`/`Foreground` из палитры (`Studio.Surface`/`Studio.Foreground`), внешний отступ 12, кнопка обзора 28px, исправлен несуществующий `Grid.Row="3"` в `ActivityCreatorView`. Панель «Log»: все цвета — `{DynamicResource Studio.*}` (ноль зашитых hex), уровни логирования — средние тона, фон/hover строк — полупрозрачные накладки в конвертерах. Чат: кисти только в `ChatTheme.Dark.xaml`/`ChatTheme.Light.xaml` (ни одного `<SolidColorBrush>` в контролах), нейтральные `Dark.Accent`/`Dark.Primary`/`Dark.ControlPressed`, режимы Plan/Act/Help различаются яркостью серого, мост `RPA.Chat\Themes\ChatStudioBridge.cs` (таблица «ключ чата → `Studio.*`») перекрывает словарь из студии в `ChatPanel.ApplyTheme`. M-04: `AppSettings.ApplyMaterialDesignTheme` берёт нейтральные `Primary`/`Secondary` из палитры, `App.xaml` стартует в `BaseTheme="Dark"` | `StudioDialogWindowTests` — 9, `LogAndChatThemeTests` — 11, `ChatThemeBridgeTests` — 6 |
| 9. Финал | `AiRpaStudio.Tests\ThemeAcceptanceTests.cs` — сквозные стражи приёмки §9: ноль зашитых цветов и named-цветов во всех темизируемых XAML (кроме двух значений кнопки закрытия в `Themes\StudioTheme.xaml`), ноль `MaterialDesign.Brush.*` в студии, семантика `Studio.Semantic.*` (Run/Pause/Stop/FolderIcon/XamlFileIcon/JsonFileIcon) вместо синего акцента тулбара и иконок проекта, W-12 (`SearchableComboBox` на палитре, радиус 6, без тени), Roslyn-редактор на палитре с hairline-бейджем ошибки, `AvalonDock\Themes\generic.xaml` читает `AvalonDock_OverlayFill/_OverlayStroke`, `MetroTheme` отдаёт нейтральные fallback-кисти без акцента, удалены `Themes\StudioTheme.Dark.xaml`, `Themes\StudioTheme.Light.xaml` и `AvalonDock.Themes.Metro\Brushes.xaml`; чеклист `AiRpaStudio\docs\Vs2026_Theme_Acceptance.md` | `ThemeAcceptanceTests` — 21 |
| 10. Рамка докпанелей (правило 6) | `AvalonDock.Themes.Metro\Theme.xaml`: `AvalonDock_ThemeMetro_AnchorablePaneControlStyle` (`BorderThickness=1`, `BorderBrush=AvalonDock_Separator`, `HeaderStrip` вокруг полосы табов, триггеры `Model.SelectedContent.IsActive` → `SeparatorStrong` и `Model.IsHostedInFloatingWindow` → `0`), `DocumentPaneControlStyle` (шапка `1,1,1,0` + контент `1`), `AnchorablePaneTitle` (hairline под заголовком), ресайзер (`Transparent` в покое), W-06/W-07 (активность — рамка окна и яркость текста вместо заливки заголовка, явный `Foreground` окна), W-10 (`AvalonDock_ThemeMetro_AutoHideAnchorableStyle` + `AnchorableStyle`) | `ThemeXamlTests` — 10 (`ThemeXaml_PaneFramesAreOnePixel`; `ThemeXaml_PanesAndDocumentsUseHairlines` переписан под «ресайзер в покое без линии») |
| 11. Заголовок окон (W-02…W-05 + окно ошибки) | `AiRpaStudio\Controls\StudioTitleBar.xaml(.cs)` — единая полоса заголовка 32px как у главного окна: фон `Studio.TitleBarBackground`, текст из `Title` окна-владельца (`RelativeSource FindAncestor`), кнопки «свернуть»/«закрыть» 46×32 на `MaterialIcon` 16×16, hover закрытия `Studio.CloseHoverBackground`/`CloseHoverForeground`, перетаскивание `DragMove()`, DP `ShowMinimize`/`ShowClose`; во всех пяти окнах `WindowChrome CaptionHeight="0"` / `GlassFrameThickness="0"` / `CornerRadius="8"`, системный caption убран; `ErrorView` — `ResizeMode="CanResize"` + `ResizeBorderThickness="6"` и `ShowMinimize="False"` (окно не в панели задач) | `StudioTitleBarTests` — 7 |

Прогон `vstest.console.exe AiRpaStudio.Tests\bin\Release\net48\AiRpaStudio.Tests.dll` — **731 тест, 0 падений** (683 после этапа 10 + 7 стражей `StudioTitleBarTests` + сквозной страж по `Controls\StudioTitleBar.xaml` в `ThemeAcceptanceTests`);
Release-сборка `AiRpaStudio.csproj` проходит.

**Сделано (этап 11):** системный caption убран у всех окон студии — полосу заголовка в диалогах W-02…W-05 и в окне ошибки рисует тот же контрол, что у главного окна: `Controls\StudioTitleBar.xaml` (высота 32, `Studio.TitleBarBackground`/`TitleBarForeground`, кнопки 46×32 на `MaterialIcon` 16×16, hover закрытия — `Studio.CloseHoverBackground`/`CloseHoverForeground`, перетаскивание `DragMove()`, DP `ShowMinimize`/`ShowClose`). Заголовок берётся из `Title` окна-владельца (`RelativeSource AncestorType=Window`), поэтому контрол работает и в `ErrorView`, который создаётся из обработчика падения без DataContext; в оффлайн-пути `ErrorDialog` палитра подкладывается в `Resources` окна, и `DynamicResource` находит её по дереву. Все пять окон получили `WindowChrome` (`CaptionHeight="0"`, `GlassFrameThickness="0"`, `CornerRadius="8"`), поэтому region-fallback `WindowCorner.ApplyCornerRadiusFallback` скругляет их и на Windows 10. У `ErrorView` `ResizeMode="CanResizeWithGrip"` заменён на `CanResize` + `ResizeBorderThickness="6"` (системный grip исчез вместе с caption), кнопка «свернуть» скрыта — окно не в панели задач. Высоты диалогов пересчитаны (+32 на полосу): 197/322/212/277/412. Тесты: `AiRpaStudio.Tests\StudioTitleBarTests.cs`.

После этапа 9 (зачистка `App.xaml`): восстановлен UTF-8 BOM (был потерян при правке этапа 9, из-за чего в диффе менялась первая строка) и удалён неиспользуемый ресурс `Bulb` — `DrawingImage` с `Brush="{DynamicResource Studio.ForegroundMuted}"`, то есть `ResourceReferenceExpression` внутри `Freezable`, который нельзя заморозить (ровно анти-паттерн из комментария `Themes\StudioTheme.xaml`). Новый страж: `ThemeAcceptanceTests.App_HasNoDynamicResourceLookupsInResourceValues` (`ThemeAcceptanceTests` — 22).

Отклонения от плана по факту кода:

- `LayoutAutoHideWindowControl` — это `HwndHost`, собственных кистей у него нет; рамку 1px рисует вложенный `LayoutAnchorableControl` через штатное свойство `AnchorableStyle` (этап 10: ключ `AvalonDock_ThemeMetro_AutoHideAnchorableStyle`). `MC4005` не возникает, отклонение по W-10 снято.
- PNG dock-таргетов оверлея (`Dock*.png`) не тронуты — это отдельный подшаг «иконки» (§10); заменены только кнопки (`Pin*.png`), из темы убраны `_Black.png`/`_Dark.png`.
- `Brush` ресайзера панелей в покое прозрачен (`Transparent`), линия 1px `AvalonDock_SeparatorStrong` появляется на `IsMouseOver`/`IsDragging` (этап 10: постоянную линию убрали, иначе между панелями было бы три линии). Ширина полосы захвата осталась 5px — при `GridSplitterWidth=4` и `Bd Margin="2"` линия стала бы нулевой.
- Этап 5: `WindowCorner.ApplyCornerRadiusFallback` работает только там, где `WindowChrome` уже есть. Поэтому W-01, W-06, W-07 скругляются в обеих ОС (DWM на Win11, region на Win10), а `NavigatorWindow` (W-08, `WindowChrome` в теме закомментирован) — только через DWM, то есть на Windows 10 остаётся прямоугольным. Диалоги W-02…W-05 и окно ошибки с этапа 11 рисуют заголовок сами (`WindowChrome` у них есть), поэтому на Windows 10 скругляются через region. Навязывать `WindowChrome` окну с системным заголовком нельзя: пропадает caption и ломается перетаскивание.
- Этап 5: `NavigatorWindow` и `LayoutFloatingWindowControl` зовут `OnSourceInitialized` (а не `SourceInitialized`-подписку) — HWND доступен ровно в этот момент, повторных подписок нет.
- Этап 6: скроллбар WF-контролов не ретемплейтится — замены шаблона `ScrollBar` нет, кисти (`Background`/`Foreground`/`BorderBrush`) выставляются на найденные `ScrollBar`, поэтому встроенный шаблон Aero2 рисует бегунок своими системными цветами. Полный ретемплейт — отдельный подшаг (вместе с PNG-иконками оверлея, §10), чтобы не ломать нативный шаблон `TreeView`.
- Этап 6: стиль элемента дерева ставится через `ItemContainerStyle` + `BasedOn` на текущий стиль, поэтому собственный шаблон элемента Toolbox (иконка активити) сохраняется. Уже материализованные `TreeViewItem` с чужим (не нашим) `Style` не перезаписываются — иначе терялся бы шаблон WF.
- Этап 6: `WfVisualTreeThemer.Apply` работает по визуальному дереву, поэтому виртуализованные (ещё не созданные) элементы Toolbox темизируются только через `ItemContainerStyle` — при первом появлении они получают стиль контейнера.
- Этап 6: вместо событий (`Checked`, `MouseEnter`, `GotFocus`) в `PropertyWindowStyleHelper` используются `DataTrigger`-триггеры стиля; исходный стиль поля поиска хранится в `ConditionalWeakTable`, поэтому повторное применение меняет палитру и не накапливает подписки.
- Этап 7: `WorkflowDesigner.PropertyInspectorFontAndColorData` (официальный API VS-интеграции) не используется — он пишет во внутренний `WorkflowDesignerColors.FontAndColorResources` и требует строку XAML с `Hashtable`, то есть работает только до разбора шаблонов WF; вместо него палитра кладётся в ресурсы ключами (мутируются на месте) + дефолтные цвета WF заменяются по значению.
- Этап 7: цвета, пришедшие в элементы через `{StaticResource}` (`designerPresenterBorder`, `ErrorView` ScrollViewer, breadcrumbs), заменяются по точному значению дефолта WF. Если WF сменит дефолты, замена не сработает (значение читается через `WorkflowDesignerColors.*Color` при каждом прогоне, запасной вариант — константы из Reference Source).
- Этап 7: иконка валидации (`WorkflowDesignerIcons.DesignerItems.TextBoxErrorValidation`) — зашитый `DrawingImage` в сборке WF, ресурсом не перекрашивается; поэтому свой адаптер — hairline-рамка цвета `CloseHoverBackground` (`Validation.ErrorTemplate` ставится на контролы дизайнера).
- Этап 7: shell bar WF (`StatusBar`) получает `BorderThickness="0,1,0,0"` — единственная hairline над панелью инструментов дизайнера: акцентная полоса `BaseColor3` из темы WF в палитре отсутствует.
- Этап 7: хлебные крошки (`breadCrumbListBox`, имена активностей `Main › Sequence › Assign`)
  темизируются отдельным проходом `WfDesignerTheme.ThemeBreadCrumb`: `ItemContainerStyle` палитры на
  контейнеры (закрывает и элементы, созданные позже), перекраска содержимого уже материализованных
  крошек, достройка новых по `ItemContainerGenerator.StatusChanged` (крошки создаются в момент провала
  в активность, то есть после применения темы) и гашение светлых кистей по яркости относительно
  `Surface` — цвет детей `DataTemplate` (chrome кнопки Aero2, рамка, `TextBlock`) приходит из шаблона,
  а не локально, поэтому проход по локальным значениям его не видел (белые плашки в тёмной теме).
  Шаблон кнопки крошки не подменяется: в нём навигация по крошкам и раскрытие списка вложенных
  активностей — теряются обработчики WF. Страж — `WfDesignerThemeTests.Apply_BreadCrumbHasNoLightBackgrounds`.
- Причина «одного белого пикселя» (найдена рендером, а не рассуждением): это угловой `Rectangle` **1×1**
  из шаблона `ScrollViewer` Aero2 — при скрытых полосах прокрутки он вырождается в точку в правом нижнем
  углу `ListBox` крошек; ширина списка равна ширине крошек, поэтому точка смещалась вместе с последней
  крошкой и лежала ровно на hairline «крошки/дизайнер». Лечение: `WfDesignerTheme.IsTemplateCorner`
  сливает его с `Surface` (в `PaintBreadCrumbBand` и в проходе по содержимому). Диагностика велась
  копией полосы из Reference Source (`View\DesignerView.xaml`): `RenderTargetBitmap` + обход пикселей +
  hit-test по светлым точкам, поэтому причина установлена по наблюдаемому результату. Стражи —
  `WfDesignerThemeTests.Apply_BreadCrumbJunctionHasNoLightPixel` (рендер: в нижних строках полосы нет
  пикселей светлее поверхности) и `.Apply_BreadCrumbTemplateCornerDotMergesWithSurface`. Пункты ниже
  остаются в силе как дополнительные меры (все свойства `Brush`, обрезка, достройка после темизации).
- Регресс «один белый пиксель» (после этапа 7): от полосы крошек оставался светлый пиксель внизу справа от
  последнего имени — он лежал на hairline «крошки/дизайнер» и сдвигался к новой последней крошке. Причин
  три, и все закрыты:
  1) проход гасил три фиксированных свойства фона и только `SolidColorBrush`, поэтому кисть на
     `TextBlock.Background`, градиент и элемент 1×1 оставались белыми → обходятся **все** свойства типа
     `Brush` (рефлексия по типу элемента с кэшем на тип, `WfDesignerTheme.BrushPropertiesOf`); кисти
     палитры не трогаются, `Foreground` правится отдельно (тёмный текст на тёмной поверхности);
  2) содержимое рисовало вне полосы крошек: hairline под крошками рисует `Path` высотой 1px со
     `Stretch="Fill"`, а `ItemsPanel` WF объявлен с `{StaticResource}` на белый `DesignerViewBackground` и
     не пересчитывается при смене темы → `ClipToBounds` + `UseLayoutRounding` на крошке;
  3) шаблон содержимого и `ItemsPanel` создаются после применения темы → повторный проход по
     `LayoutUpdated`, но только при изменении отпечатка поддерева (число крошек + визуальных детей), иначе
     событие работало бы на каждую раскладку глубокого дизайнера.
  Стражи: `WfDesignerThemeTests.Apply_BreadCrumbHasNoLightBrushAnywhere`,
  `.Apply_BreadCrumbClipsArtifactsOverflowingOnTheDivider`,
  `.Apply_BreadCrumbArtifactsCreatedAfterApplyAreNeutralized`, `Sources_BreadCrumbSweepsAllBrushProperties`.
- Регресс «полоса крошек стала белой целиком» (после правки пикселя): светлая площадь внутри крошки
  гасилась в `Transparent`, и полоса показывала белого предка — хост `BreadCrumbBarLayout`, шаблон
  `ListBox`, `ScrollViewer` и `ItemsPanel`, пришедшие со `{StaticResource}` на белый
  `DesignerViewBackground` (не пересчитывается при смене темы). Решение: 1) подложка полосы красится
  **непрозрачным** `Surface` — сама полоса, её шаблонные части и обёртки до панели дизайнера
  (`WfDesignerTheme.PaintBreadCrumbBand`), а фон элементов самой крошки не трогается, потому что означает
  состояние; 2) светлые **площади** гасятся в `Surface`, а в `Transparent` — только линии (рамки, обводки:
  под ними уже нарисована подложка). Стражи —
  `WfDesignerThemeTests.Apply_BreadCrumbBandStaysOpaqueSurfaceUnderWhiteHost` (белый Border шаблона полосы,
  белый `ItemsPanel`, белый хост — полоса обязана остаться `Surface`) и
  `.Apply_BreadCrumbBackdropBehindNamesIsSurface` (первая непрозрачная кисть вверх от имени активности
  обязана быть поверхностью палитры).
- Этап 8: семантические цвета уровней логирования (Trace/Debug/Info/Warning/Error/Fatal) сохранены — это кодировка данных, а не акцент интерфейса. Они приведены к средним тонам, а фон/hover строк — к полупрозрачным накладкам, чтобы читаться и в тёмной, и в светлой теме.
- Этап 8: иконки фильтров «Log» получили семантические цвета уровней `Studio.Semantic.Log.*` (классика синий/зелёный/оранжевый/красный/тёмно-красный), непрозрачные — alpha 255: глиф обязан читаться на панели и в тёмной, и в светлой теме. Trace в семантику не входит — у него нет цвета-сигнала: иконка и буквы уровня красятся основным цветом текста панели `Studio.Foreground` (белый `#F1F1F1` в тёмной теме, чёрный `#1F1F1F` в светлой), а `LogLevelToColorConverter` читает этот ключ из ресурсов приложения по строке (библиотека лога не ссылается на `RPA.Helpers`), с запасным средним тоном вне студии. Строки Trace фона не получают вовсе (ни фон, ни hover). Цвет ставится на глиф, а не на `ToggleButton`: фон/hover/checked кнопки остаются палитровыми, а hex в `LogViewerControl.xaml` отсутствует.
- Этап 8: `Commit.SuccessBackground`/`Commit.ErrorBackground`, `Dark.ErrorText` и цвета `NotificationKind` в `RPA.Chat` — семантика git-статуса, ошибки и важности уведомления (приходят кодом, не из темы). Красный остаётся единственным не-нейтральным токеном; полный перевод семантики уведомлений — подшаг «иконки/семантика» (§10).
- Этап 8: значения словаря `ChatTheme.Dark/Light.xaml` продублированы из палитры, потому что `RPA.Chat` не может ссылаться на `RPA.Helpers`. Согласованность словаря и палитры стережёт тест `ChatThemeBridgeTests.ThemeDictionaries_AlreadyMatchPalette`: правки таблицы моста и словарей делаются вместе.
- Этап 8: диалоги студии остаются с системным chrome (решение этапа 5), поэтому на Windows 10 их углы прямоугольные — DWM работает только на Windows 11.
- Этап 8: `LogViewerControl` сохраняет моноширинный `Consolas` для строк лога (это чтение данных, а не подпись интерфейса); кегль и шрифт панели не менялись.
**Сделано (этап 9, финал).** Сквозные стражи приёмки — `AiRpaStudio.Tests\ThemeAcceptanceTests.cs` (21 тест):
* ноль зашитых hex и named-цветов во всех темизируемых XAML (комментарии исключены; разрешены только
  `#E81123`/`#FFFFFF` кнопки закрытия в `Themes\StudioTheme.xaml`), ноль `MaterialDesign.Brush.*` в студии;
* семантические кисти `Studio.Semantic.*` (`Run`/`Pause`/`Stop`/`FolderIcon`/`XamlFileIcon`/`JsonFileIcon`/`Validation`)
  объявляются в `StudioBrushes` кодом, одинаковы в обеих темах (`chroma > 6`), ими заменены синий `#4169E1`
  кнопок сохранения (стали `Studio.ToolbarForeground`), `Orange`, `#228B22`, `#DC143C` тулбара и
  `OrangeRed`/`#4CAF50`/`#2196F3` иконок `ProjectManagerControl`;
* W-12 закрыт: `SearchableComboBox` переведён на `Studio.Surface`/`Studio.Selected`/`Studio.ForegroundMuted`,
  радиус popup 6 (§1 п.3), `DropShadowEffect` удалён (границу даёт hairline); тень убрана и в popup
  настроек `LogViewerControl`;
* Roslyn-редактор (`CSharpExpressionEditor\RoslynExpressionEditor.xaml`): многоцветные `DrawingBrush`
  `TextBoxErrorIcon`/`ResizeGripIcon` удалены, рамка — `Studio.Separator`, бейдж ошибки — hairline-рамка
  `Studio.CloseHoverBackground` (красный только для ошибки), `MaterialDesign.Brush.Card.Border` заменён;
  **Уточнение (баг «ошибка выражения — красная рамка без иконки»):** hairline-бейдж неотличим от
  «иконки нет» — в `RoslynExpressionEditor.xaml` он заменён глифом `materialIcons:MaterialIcon`
  (`Kind="Error"`) в `Studio.Semantic.Validation` (голубой, ключ семантики `StudioBrushes`), а красный `Studio.CloseHoverBackground` остаётся у иконки валидации активности и у кружка адаптера ошибки; проверка выражения идёт по изменению текста с
  паузой-дребезгом (`Core\ExpressionValidationTiming.cs`, 350 мс; резервный цикл 3000 мс вместо
  10 000 мс). Иконка на активности рисуется родным рисунком WF
  (`ValidationIconState.ErrorIconBrush()` = `DesignerItems.ActivityErrorValidation`, сплошная кисть
  палитры только как резерв), у адаптера ошибки рядом с рамкой появился значок «!».
  Стражи: `AiRpaStudio.Tests\ErrorBadgeTests.cs`, `ExpressionValidationTimingTests.cs`,
  `ValidationIconGlyphTests.cs`.

* `AvalonDock\Themes\generic.xaml`: `Background="White"` → `Transparent`, `Fill="AliceBlue"`/`Stroke`
  → `AvalonDock_OverlayFill`/`AvalonDock_OverlayStroke` (ключи моста этапа 3);
* `AvalonDock.Themes.Metro\MetroTheme.cs`: акцент `#DC143C`/`#FF8C00` и светлые `#FFFFFF`/`#FAFAFA`
  заменены нейтральными fallback-кистями (метод `Neutral`, замороженные `SolidColorBrush`);
* удалены «третьи палитры»: `Themes\StudioTheme.Dark.xaml`, `Themes\StudioTheme.Light.xaml` (и их `<Page>`
  в csproj) и `AvalonDock.Themes.Metro\Brushes.xaml` (никем не подключался, содержал `#FFFFFF`/`#41B1E1`);
* `MainView.xaml` берёт фон окна напрямую из `Studio.Surface`; `App.xaml` подключает ровно один словарь темы;
* документация: новый чеклист `AiRpaStudio\docs\Vs2026_Theme_Acceptance.md` (пункты §9 с указанием, что
  проверяется автоматически), обновлены `AGENTS.md` и `docs\Theming_MaterialDesign_Plan.md` (помечен
  заменённым).

Отклонения этапа 9:
- Семантические цвета остаются не-нейтральными (запуск/пауза/стоп, типы файлов, уровни лога, git-статус):
  это кодировка данных, а не акцент. Палитра `StudioPalette` при этом остаётся строго нейтральной
  (`chroma ≤ 6`), поэтому семантика живёт отдельным списком `StudioBrushes.SemanticKeys`.
- Пункт 4 чеклиста на Windows 10 выполняется не для всех окон: диалоги W-02…W-05 и Ctrl+Tab остаются
  прямоугольными (системный chrome), это зафиксировано ещё в отклонениях этапа 5.
- `LogViewerControl` по-прежнему использует моноширинный `Consolas` для строк лога (чтение данных,
  а не подпись интерфейса).
- Ручная часть §9 (реальный запуск студии, переключение темы, открепление панелей, сохранённый `ADLayout`)
  автоматизирована быть не может: для неё создан чеклист с таблицей отметок.

1. **M-01.** `AiRpaStudio.Tests\StudioPaletteTests.cs` (падает) → `Helpers\StudioPalette.cs`. Тесты: все токены заданы; dark luma < 0.5, light > 0.5; монохромность `max(R,G,B) - min(R,G,B) ≤ 6`; порядок яркости dark `Surface < SurfaceAlt < Hover < Selected`; `DetectDark` работает по luminanсe.
2. **`StudioBrushes` на палитре.** Обновить `StudioBrushesTests` (сейчас ждут `#2D2D2D` и `Highlight = Primary`), добавить тест «`Highlight` нейтрален (R == G == B)». Реализация: значения из `StudioPalette`, `SystemColors.Highlight*` → нейтраль, `ToolbarPressed/Checked` → `Selected`.
3. **M-02.** STA-тест: после `ReapplyDockManagerResources()` все новые ключи есть и в `manager.Resources`, и в `Resources` созданных `NavigatorWindow` / `OverlayWindow` / `LayoutFloatingWindowControl`; реализация `ThemeBrushBridge` + 3 вызова.
4. **`Theme.xaml`** (самый объёмный этап). Тесты-регексы по тексту: нет `BaseColor3`/`BaseColor4` в сеттерах; нет `Background="White"`, `Fill="AliceBlue"`, `#FF98DDFB`, `#FFF1FBFF`, `Stroke="#8E6EA6F5"`; есть `IsMouseOver` у `ToolButtonStyle` и `ButtonStyle`; есть стиль с `AvalonDock_Separator` для панелей и документов; нет `_Black.png`. Затем правки шаблонов и замена PNG на вектор.
5. **M-03.** `WindowCornerTests` (константы `33`/`2`, `Apply(IntPtr.Zero)` не бросает, идемпотентность) → реализация + вызовы в W-01…W-08, `CornerRadius` в теме → 8.
6. **F-01…F-03.** `WfThemeTests` (STA): `WfVisualTreeThemer.Apply` ставит замороженные кисти палитры и меняет их после смены темы; `PropertyWindowStyleHelper` находит элементы по именам на синтетическом дереве.
7. **F-04…F-06.** `WfDesignerTheme` + тест «`DesignerViewBackground` объявлен»; проверка, что `DocGroupHelper` вызывает темизацию поверхности дизайнера.
8. **W-02…W-05, F-07, F-08, M-04.** Правки четырёх диалогов, `LogControlLibrary`, `RPA.Chat`, нейтральные `Primary`/`Secondary`; регекс-тесты «нет зашитых hex».
9. **Финал.** Release-сборка `AiRpaStudio.csproj` и `AiRpaStudio.Tests.csproj` + `vstest.console.exe`, затем ручной чеклист раздела 9; обновить `AGENTS.md` (раздел «Темы») и `docs\Theming_MaterialDesign_Plan.md`.
10. **Рамка докпанелей (правило 6).** Сначала стражи (`ThemeXaml_PaneFramesAreOnePixel` + переписанный `ThemeXaml_PanesAndDocumentsUseHairlines`) и правки документа; затем панели главного окна, плавающие окна, автоскрытие и ресайзер; в конце `vstest.console.exe` и пункт 8 чеклиста (открепить → вернуть панель: рамка не должна «залипнуть»).

---

## 9. Ручная приёмка

Автоматическая часть каждого пункта — `AiRpaStudio.Tests\ThemeAcceptanceTests.cs`; пошаговый порядок
ручной проверки и таблица отметок — `AiRpaStudio\docs\Vs2026_Theme_Acceptance.md`.

1. Тёмная тема по умолчанию → нет ни одного светлого прямоугольника (панели, центр, правая сторона, заголовки, вкладки, логи, чат, popup).
2. Нигде нет фиолетового, лаймового или синего акцента (кроме красного «закрыть»): панель, вкладка, заголовок, автоскрытие, выделение в `Outline`/`Toolbox`/`ListView` шаблонов, оверлей, Ctrl+Tab.
3. Единый фон: окно → панель → документ → поверхность дизайнера без «ступенек», кроме hairline.
4. Скруглены углы: главное окно, четыре диалога, откреплённый документ и панель, Ctrl+Tab.
5. Переключение Light/Dark на лету перекрашивает всё, включая уже открытые floating-окна и дизайнер.
6. Открепить панель → стиль идентичен главному окну; открыть/закрыть `NuGet` и `Chat` → без светлых артефактов.
7. Перезапуск с сохранённым `ADLayout` → вид не ломается; «сбросить раскладку» возвращает дефолт.
8. Каждая докпанель обведена рамкой 1px (панели главного окна, панель документов, автоскрытие, панели в откреплённых окнах): у активной рамка ярче (`SeparatorStrong`), между двумя панелями ровно две линии (по одной на рамку), постоянной линии ресайзера нет. Открепить панель и вернуть в док — рамка не остаётся «погашенной».

---

## 10. Риски и осознанные ограничения

| Риск | Мера |
|---|---|
| `Theme.xaml` ~2030 строк, риск регресса шаблонов | регекс-тесты-стражи + правки по секциям + приёмка после каждого этапа |
| PNG-иконки (`Pin*.png`, `Dock*.PNG` — около 24 файлов) зашиты в шаблоны | отдельный подшаг «иконки», можно отложить, временно тонируя подложку |
| `RPA.Chat` и `LogControlLibrary` — сторонние сборки со своей темой | менять только их XAML-кисти, публичный API не трогать |
| Смена `DockingManager.Theme` в рантайме | не делаем: `OnThemeChanged` бросает `NotImplementedException` для не-`DictionaryTheme` (`DockingManager.cs:1043-1047`) |
| `AllowsTransparency` ради скруглений | не включаем (ClearType, производительность) |
| `Themes\StudioTheme.Dark.xaml` / `.Light.xaml` | в `App.xaml` подключён только `StudioTheme.xaml`; либо не используем, либо подключаем осознанно на этапе M-04 |
| Сохранённая раскладка `Properties.Settings.ADLayout` | `ContentId` не меняются, старые сохранения не ломаются; при визуальных артефактах использовать «сбросить раскладку» (`MainView.xaml.cs:670`) |
| Двойная линия внутри плавающего окна (рамка окна + рамка панели) | панель гасит рамку по `Model.IsHostedInFloatingWindow`; свойство вычисляемое и без `INotifyPropertyChanged`, поэтому «открепить → вернуть» проверяется вручную (пункт 8 чеклиста); при «залипании» — пересчёт в `Loaded`/`OnApplyTemplate` панели без изменения публичного API |
| +1px к габаритам контента у каждой панели | проверка глазами: `WorkflowDesigner` и `LogViewerControl` не должны получить скроллбар «на пиксель»; при необходимости рамка гасится только у вложенного контрола |
| Рамка `#373737` на фоне `#272727` на грани различимости (требование «чуть светлее фона») | единственная ручка — поднять до `SeparatorStrong` у всех панелей, не меняя толщину 1px |

---

## 11. Объём работ

| Этап | Оценка |
|---|---|
| 1-3 (палитра + `StudioBrushes` + мост) | ~1 итерация, полностью покрывается юнит-тестами |
| 4 (`Theme.xaml`) | самый большой: ~2000 строк темы, 13 шаблонов + иконки |
| 5-7 (скругления, WF-контролы, сторонние сборки) | по 1 итерации на этап |
| Тесты | ~25-30 новых к существующим 12 (`DockingThemeBrushesTests`) + 8 (`StudioBrushesTests`) |

## 12. Шрифт Roboto, вкладки документов, дерево проекта и цвета редакторов

Доработка по замечаниям приёмки (5 пунктов). Все правки — в тех же правилах: цвета и шрифты только
кодом (`StudioBrushes`/`StudioFonts`/`StudioEditorTheme`), в XAML — только `{DynamicResource Studio.*}`,
никаких зашитых `FontFamily` и hex-цветов.

### 12.1 Шрифт Roboto для всего проекта (п.1 и п.3)

* `Helpers\StudioFonts.cs` — единственный источник шрифтов: `FamilyName = "Roboto"`,
  `UiFont`/`CodeFont`, ключи `Studio.FontFamily`, `Studio.CodeFontFamily`, `Studio.FontFamilyName`.
* Начертания Roboto лежат в сборке (`Resources\Roboto\*.ttf`), `UiFont` —
  `new FontFamily(pack://application:,,,/RPA;component/Resources/Roboto/, "./#Roboto")`;
  fallback — установленный Roboto, затем системный шрифт (исключений хелпер не бросает).
* Ключи пишутся в `Application.Resources` на старте (`App.StartupCore`) и после каждой смены темы
  (`AppSettings.ApplyTheme`); сторонние сборки (`LogControlLibrary`, `RPA.Chat`) читают их по строковому
  ключу: XAML — `{DynamicResource Studio.FontFamily}`, чат — `Themes\ChatFonts.cs`.
* `WfThemeStyles.FontFamilyName = StudioFonts.FamilyName` (Roboto), размер WF-шрифта —
  `WfThemeStyles.FontSize = 12`. WF-контролы берут шрифт из ключей `FontFamilyKey`/`FontSizeKey`/
  `FontWeightKey`, их объявляет `WfDesignerTheme.CreateResourceValues`.
* Заменены все Consolas/Segoe UI: меню (`MainView`), дерево проекта, редактор кода, редактор
  выражения (`Roslyn...`/`Vb...`), shell bar дизайнера, лог, чат, тема AvalonDock; корни окон и панелей
  получили `FontFamily="{DynamicResource Studio.FontFamily}"` (наследование покрывает вложенные элементы).

### 12.2 Окно WF «Свойства» (п.1 списка доработки)

Причина: инспектор рисует шаблоны из **своих** ресурсов (`TextBrushKey`/`BackgroundBrushKey`/
`FontFamilyKey`), которые перекрывали словарь приложения, а строки свойств WF пересоздаёт
при выборе активности/развороте категории — разовое применение темы их не покрывало.

* `PropertyWindowStyleHelper.SetPropertyStyles` сливает словарь палитры **в ресурсы самого инспектора**
  (`WfDesignerTheme.ApplyToApplication(inspector.Resources, palette)`) — ключи WF получают кисти и шрифт
  студии;
* `Attach(inspector)` + `LayoutUpdated` с дебаунсом по отпечатку дерева (`ComputeSignature`) — новые
  строки и редакторы значений перекрашиваются автоматически; повторные вызовы идемпотентны
  (`IsAttached`, `ConditionalWeakTable`), подписки снимаются на `Unloaded`;
* новый шаг `ApplyValueEditors` — `TextBox`/`ComboBox`/`ComboBoxItem`/`ListBox`/`CheckBox`/`Expander`
  внутри инспектора (фон, текст, рамка, каретка, шрифт).

### 12.3 Вкладки документов и переключатели (п.2 списка доработки)

`AvalonDock.Themes.Metro\Theme.xaml`: `DocumentHeaderTemplate`, `DocumentTitleTemplate`,
`AnchorableHeaderTemplate`, `AnchorableTitleTemplate`, стили `LayoutDocumentTabItem`/
`LayoutAnchorableTabItem`, `DocumentPaneControl`/`AnchorablePaneControl`, меню-переключатель
(`ContextMenuEx`) и `Ctrl+Tab` (`NavigatorWindowListBoxItemStyle`) получили `Foreground` и
`FontFamily` из ресурсов студии — текст берётся из палитры, а не из системных кистей.

### 12.4 Дерево проекта (п.4 списка доработки)

`Controls\ProjectManagerControl.xaml`: у названия файла (TextBlock) явный `Foreground` палитры
(раньше цвет приходил из системного стиля и на выбранном элементе становился чёрным), у
`TreeView.ItemContainerStyle` — сеттеры `Foreground`/`FontFamily` и триггеры `IsMouseOver` →
`Studio.Hover`, `IsSelected` → `Studio.Selected`; сам `TreeView`, поля поиска и переименования —
кисти палитры, шрифт Roboto. Кисти заданы `{DynamicResource}`, поэтому смена темы перекрашивает
дерево без перезагрузки проекта.

### 12.5 Цвета редакторов кода и XAML как в Visual Studio (п.5 списка доработки)

* `Helpers\StudioEditorPalette.cs` — таблица токенов редактора (схема VS 2022, dark/light):
  фон/текст/номера строк/текущая строка/выделение + подсветка (`Comment`, `String`, `Keyword`,
  `ControlKeyword`, `Type`, `Number`, `Method`, `Field`, `Preprocessor`, `XmlTag`, `AttributeName`,
  `AttributeValue`, `MarkupExtension`, `Entity`, `CData`, `XmlDeclaration`).
* `Helpers\StudioEditorTheme.cs` — применяет оформление к `TextEditor` и **перекрашивает определения
  подсветки по именам цветов**: XSHD читается в `XmlDocument`, у `<Color name="…">` подменяется
  `foreground`, затем `HighlightingLoader.Load`. Поэтому один XSHD (включая встроенный в AvalonEdit
  `CSharp-Mode.xshd` с Green/Blue/Magenta) работает в обеих темах; определения кэшируются по паре
  «язык + тема» и не мутируют общее определение `HighlightingManager`.
* `Resources\Highlighting\Xaml.xshd` — цвета удалены (задаются палитрой); `CodeLanguageProvider`
  отдаёт определения только через `StudioEditorTheme`.
* `Controls\CodeEditorView` — `ApplyTheme()`, вызов из `MainView.ReapplyWorkflowControlThemes`;
  редактор выражения WF (`RoslynExpressionEditorInstance2`, `VbExpressionEditorInstance`) — шрифт
  `StudioFonts.CodeFont` и темизированное определение `C#`/`VB`.

### 12.7 Плоские кнопки окна «Свойства» (правка F-03)

* `Helpers\PropertyWindowStyleHelper.cs`, `CreateIconHost` получил `iconSize`/`framed`:
  у переключателей «По категориям»/«Алфавитный» `CornerRadius = 0` — скругления убраны, hairline-рамка
  `Separator`, размер кнопки 28×28 и глиф 20 px (`ToolButtonIconSize`) не менялись; у кнопки очистки
  поиска рамки нет вовсе (`framed: false`), а крестик уменьшен в 1,5 раза — `ClearIconSize = 11` вместо
  16 (было `ClearButtonSize − IconPadding`) при том же размере кнопки 24×24. Токен
  `StudioPalette.ButtonCornerRadius` остался общим правилом студии и здесь просто не применяется.
* Кнопка «…» строк свойств (WF `EditModeSwitchButton`) плоская: `ApplyEditModeButtons` →
  `MakeFlatButtonChrome` ставит прозрачные `Background`/`BorderBrush`, `BorderThickness = 0`,
  `Effect = null`, а в шаблоне обнуляет `CornerRadius`, рамку, фон и тень у всех детей (скругление и
  «объём» приходят из чужого шаблона кнопки, своих сеттеров WF-стиль не содержит). Если хромировку
  рисует системный `ButtonChrome` (Border-потомка нет) — ставится плоский шаблон на `ContentPresenter`;
  глиф не теряется, потому что WF отдаёт его через `Content` (триггер `TargetEditMode = Dialog` →
  `Content = "..."` в `stylescore.editmodeswitchbutton.baml`). Ховер-плашки у «…» больше нет — прямое
  следствие «плоскости»; размеры (`Width`/`Height` = 20 из WF-стиля) не менялись.
* Тесты: `WfThemeTests.PropertyHelper_StylesButtonsByName` (радиус 0 у обоих переключателей),
  `.PropertyHelper_SearchClearButton_IsFramelessWithSmallerGlyph`,
  `.EditModeButton_IsFlatWithoutCornerRadiusAndShadow`,
  `.EditModeButton_WithoutBorderChrome_GetsFlatTemplateAndKeepsGlyph`,
  `.PropertyHelper_FlattensEditModeButtons`.

## F-07: цвета WF до первого кадра (кэш словаря цветов)

**Симптом.** Тема применялась «вторым проходом»: первый кадр активности рисовался дефолтами WF
(`#FFFFFF` плита, `#E9ECEE` плашка заголовка, `#D0D7E2` рамка), и только потом элемент перекрашивался —
видимое мерцание при старте программы и при перетаскивании активности из панели.

**Причина.** Шаблоны WF подмешивают `ColorResources.xaml` в собственные `ControlTemplate.Resources`
через `{wf:CachedResourceDictionaryExtension}`, который раздаёт один и тот же экземпляр словаря из
статического кэша `CachedResourceDictionaryExtension.Cache`. Любая покраска после построения элемента —
это второй проход, и он попадает в отдельный кадр (локальные значения в `LayoutUpdated` ставятся на
`DispatcherPriority.Background`, то есть после Render).

**Решение.** `Helpers\WfColorResourcesOverride.cs` кладёт в кэш WF свой словарь **до** первого
`WorkflowDesigner` / `DesignerMetadata().Register()`:
* снимок WF-словаря уходит в `MergedDictionaries` — непереопределённые ключи остаются как есть;
* переопределяются только ключи дизайнера (`OverrideTokens`): плита, рамка, подпись, выделение,
  `DesignerViewBackground`, `DesignerViewStatusBarBackground`, `ParallelSpacerColor`,
  `FlowchartConnectorColor`, shell bar / статус-бар, контекстные меню — цвета `StudioPalette`;
* кисти **связанные**: `Color` привязан к источнику токена, такую кисть нельзя заморозить, WPF делает
  клон на элемент и копирует выражение привязки — смена темы = новое значение источника, без обхода дерева;
* пишутся оба ключа кэша: относительный `/System.Activities.Presentation;component/...` и абсолютный pack-URI.

**Режимы взаимоисключающие.** Успешная установка включает режим кэша: `WfActivityTheme` плиту и рамку не
красит, `ReplaceWorkflowDefaultColors` не выполняется. Остаётся текст: подпись наследует `Foreground` от
`WorkflowViewElement`, а тот приходит из темы WF (чёрный), поэтому `ThemeStaleText` ставит цвет
**синхронно** в `LayoutUpdated` (до Render) и помечает активность (`Marks`), иначе раскладка зацикливается.
Любой сбой установки (нет поля кэша, не загрузился словарь WF) возвращает прежнюю пост-покраску;
аварийный тумблер — switch `Studio.WfLegacyTheming` в `AiRpaStudio.exe.config`.

**Границы.** Ключи `PropertyInspector*` живут в `FontAndColorResources` (инспектор остаётся на
`WfDesignerTheme` / `PropertyWindowStyleHelper`), Toolbox `ColorResources` не использует,
`DesignerViewForeground` в словаре WF отсутствует (остаётся в `Application.Resources`).

**Проверка.** `AiRpaStudio.Tests\WfColorResourcesOverrideTests.cs` (глобальный кэш WF не трогается:
установка проверяется на локальном словаре). Сквозная проверка на живом дизайнере — пробник
`tmp\cacheprobe\CacheOverrideProbe5.cs` (`Run-Probe.ps1 -Probe probe5`, режимы `cache` / `legacy`): в тёмной
теме первый кадр даёт `bg=#FF272727 border=#FF48484F` со связанными кистями и ноль остатков дефолтов WF,
а тот же сценарий в режиме `legacy` — `bg=#FFFFFFFF border=#FFD0D7E2` (это и есть мерцание).

