# Контекстное меню в таблицах переменных и аргументов дизайнера

Документ фиксирует принципы механизма «правый клик в ячейке таблицы переменных/аргументов открывает
меню Вырезать/Копировать/Вставить, не выходя из режима редактирования». Механизм сделан обходным путём
и держится на порядке событий WPF, поэтому любая правка «по логике» без учёта этих инвариантов
возвращает регресс.

Реализация: `AiRpaStudio\Core\Services\DesignerVariableContextMenuService.cs` (подключение),
`DesignerVariableContextMenuPolicy.cs` (решения, чистые функции),
`DesignerContextMenuState.cs` (автомат флагов), `AiRpaStudio\Helpers\WfDesignerTheme.cs`
(единственная фабрика меню `CreateEditingContextMenu` и перекраска `RecolorEditingContextMenu`),
`AiRpaStudio\Helpers\WfContextMenuTheme.cs` (оформление меню: собственные шаблоны меню и пунктов на
палитре + общий проход по меню дизайнера).
Тесты: `AiRpaStudio.Tests\DesignerVariableContextMenuPolicyTests.cs`,
`AiRpaStudio.Tests\DesignerVariableContextMenuTests.cs`, `AiRpaStudio.Tests\WfContextMenuThemeTests.cs`.
Тема контекстных меню дизайнера — F-06 плана `docs\Vs2026_Theme_Plan.md`.

## Зафиксированные дефекты

| Версия / время | Симптом | Причина | Исправление |
| --- | --- | --- | --- |
| 1.0.0.0, 2026-11-02 03:21:35 | `InvalidOperationException: ResourceDictionary доступен только для чтения и не может быть изменен.` Падает открытие панели «Переменные». | Стиль поля ввода писался в `DataTemplate.Resources` шаблона правки. Шаблоны правки WF-дизайнера **запечатаны** (`FrameworkTemplate.Seal` при первом применении), их словарь доступен только для чтения — `set_Item` бросает исключение. | Записи в ресурсы шаблонов убраны полностью: обработчики живут на `DataGrid`, палитра ставится полю ввода локальными значениями в `PreparingCellForEdit`. |
| 2026-11-02 (F-06) | Меню «Вырезать/Копировать/Вставить» в таблицах переменных и аргументов рисуется **белым, текст белый** — в тему не попадает. | Причин две. (1) Меню — **popup**, а не потомок `DesignerView`: ни `WfDesignerTheme.Apply`, ни `WfVisualTreeThemer` до него не доходят, потому что обходят визуальное дерево дизайнера. (2) Внешний вид задавали **локальные** `Background`/`Foreground`, но рисовал **чужой шаблон** (`MaterialDesignContextMenu`/`MaterialDesignMenuItem` из `App.xaml`, в чистом процессе — системный Aero2): его подсветка строится от `Foreground` (на тёмной теме — светлая накладка, под ней белый текст), углы, рамку и тень рисует шаблон, а не наши кисти. | `Helpers\WfContextMenuTheme.cs`: собственные шаблон меню, панель пунктов и стиль контейнера пункта на палитре (`WfDesignerTheme.CreateEditingContextMenu`/`RecolorEditingContextMenu` идут через него), плюс общий проход по `ContextMenu.OpenedEvent` для меню самого дизайнера. |

Этот случай — главный аргумент инварианта 10: **сервис не обращается к `CellEditingTemplate` вообще**,
даже на чтение, потому что «прочитать и аккуратно дописать» здесь невозможно by design.

## Что именно обслуживается

| Элемент `DesignerView` | Таблица | Панель |
| --- | --- | --- |
| `variableDataGrid` | переменные | `VariableDesigner` |
| `argumentsDataGrid` | аргументы | `ArgumentDesigner` |
| `importedNamespacesDataGrid` | пространства имён | `imports1` (`ImportDesigner`) |

Меню обслуживает **все** таблицы дизайнера, а не только две известные: `RefreshTables` обходит поддерево
`DesignerView` и подключается к каждой `DataGrid` (поиск по типу — имена элементов объявляет шаблон WF, и
они отличаются между версиями: панели в `DesignerView.xaml` называются `variables1`/`arguments1`/`imports1`).
Панели и таблицы строит шаблон WF: до его применения таблиц в дереве нет, поэтому:

1. проход выполняется после `Loaded` дизайнера;
2. таблица без столбцов откладывается до своего `Loaded` (столбцы объявляет шаблон панели);
3. на цепочку видимости таблицы (до четырёх предков) ставится `IsVisibleChanged` — вкладка
   «Пространства имён» открывается по кнопке shell bar, и таблица «оживает» именно в этот момент;
4. известные панели (`VariableDesigner`/`ArgumentDesigner`) дополнительно подключаются по имени —
   это быстрый путь, который не ждёт общего прохода.

Тематика тех же таблиц живёт отдельно: `Helpers\WfDataGridTheme.cs` (F-04 плана `Vs2026_Theme_Plan.md`).

## Проблема и её причина

Штатный `ContextMenu` на ячейке `DataGrid` в WF-дизайнере не работает:

1. правый клик по ячейке в режиме правки завершает правку (`CellEditEnding`) и закрывает панель
   переменных — поле ввода исчезает вместе с меню;
2. WF-дизайнер сам перехватывает команды `ApplicationCommands.Cut/Copy/Paste` (у него есть свои
   обработчики), поэтому меню, открытое без установки фокуса, работало «с чужой целью»;
3. контекстное меню на самой ячейке блокируется поверхностью дизайнера (`NoContextMenuGrid`).

Отсюда конструкция: правый клик перехватывается на **таблице**, меню открывается **программно**, правка
удерживается отменой `CellEditEnding`, а цвета берутся из палитры студии (иначе в тёмной теме
появлялся белый выпадающий прямоугольник и «чёрный фон с зелёными буквами» отладочного варианта).

## Схема включения

```
LoadActivityToDesignerHelper.LoadActivityToDesigner
  └─ SetVariablesStyle(...)                          // оформление shell bar
  └─ DesignerVariableContextMenuService.InitializeVariableDesignerTracking()
       └─ View.Loaded                                // дизайнер построен шаблоном
            ├─ RefreshTables(View)                   // все таблицы по типу, включая «Пространства имён»
            │    ├─ Attach(grid)                     // обработчики + цепочка видимости
            │    └─ grid без столбцов → grid.Loaded → RefreshTables(View)
            └─ поиск Control#VariableDesigner / Control#ArgumentDesigner  // быстрый путь по имени
                 └─ IsVisibleChanged → OnVariableDesignerVisible(designer, name) → Attach(grid)

Attach(grid)
  ├─ dataGrid.PreviewMouseRightButtonDown += …       // открыть меню + взвести отмену
  ├─ dataGrid.CellEditEnding             += …        // consume-once отмена
  ├─ dataGrid.PreparingCellForEdit       += …        // палитра поля ввода
  ├─ dataGrid.AddHandler(PreviewLostKeyboardFocusEvent, …, handledEventsToo: true)
  └─ HookTableVisibility(grid)                       // IsVisibleChanged до 4 предков
```

Панели ленивые: `IsVisibleChanged` — единственная точка, где таблица уже существует. Инициализация
ровно одна на панель (`_isSetVariableDataGrid` / `_isSetArgumentsDataGrid`) и ровно одна на таблицу
(`_prepared` — `HashSet<DataGrid>`): без второго барьера повторное подключение дублировало бы
`CellEditEnding`, и второй обработчик снимал бы отмену первого. Общий проход (`RefreshTables`)
идемпотентен по тем же двум барьерам, поэтому его можно вызывать на каждое появление таблицы.

## Инварианты

1. **Все обработчики — на `DataGrid`, а не в шаблоне ячейки.** `PreviewMouseRightButtonDown`
   туннелирует сверху вниз, поэтому таблица успевает и взвести отмену, и пометить событие
   обработанным (`e.Handled = true`) до ячейки. Перенос обработчика в стиль поля ввода или на
   бабблящее `MouseRightButtonDown` ломает отмену — правка завершается и меню закрывается вместе с ней.
   То же правило действует для таблицы «Пространства имён»: она обслуживается тем же `Attach`.
2. **`e.Handled = true` в `Preview`-обработчике таблицы.** Парное (бабблящее) `MouseRightButtonDown`,
   на котором висит `ContextMenuService`, не поднимается: меню не открывается «само» и не конфликтует с
   программным открытием.
3. **Отмена одноразовая.** `DesignerContextMenuState.NotifyCellEditEnding()` возвращает решение и
   **всегда** сбрасывает взвод. Взвод ставится только при клике в поле ввода
   (`DesignerVariableContextMenuPolicy.FindEditingField` / `ShouldArmCancel`), иначе флаг залипал и
   глушил следующее, уже пользовательское, завершение правки.
4. **`Opened` не снимает взвод.** Меню открывается отложенно (`DispatcherPriority.Input`), поэтому
   `Opened` приходит раньше `CellEditEnding`; сброс взвода в `Opened` снова завершал бы правку.
   Снимают взвод только `Closed` и `NotifyCellEditEnding`.
5. **Фокус ставится до открытия меню.** Иначе `ApplicationCommands.Cut/Copy/Paste` уходят дизайнеру.
   На время показа меню `PreviewLostKeyboardFocus` помечается `Handled`, чтобы меню не забирало
   фокус у поля.
6. **Позиционирование явное.** `PlacementTarget` = поле ввода, `Placement = PlacementMode.MousePoint`
   (`ConfigureMenuPlacement`): программно открытое меню иначе рисуется в углу экрана.
7. **Меню — только из фабрики и только на собственных шаблонах.** `WfDesignerTheme.CreateEditingContextMenu(palette)`;
   `new ContextMenu`, зашитые `#RRGGBB`/`Colors.*`, `MessageBox` и `Debug.WriteLine` запрещены.
   `StaysOpen = true`, `HasDropShadow = false` (закрытием управляем сами; тень даёт светлый прямоугольник
   поверх тёмной темы). Одних локальных `Background`/`Foreground` **недостаточно**: внешний вид меню
   задаёт шаблон, поэтому `WfContextMenuTheme.Apply` ставит `Template` меню, `ItemsPanel` и
   `ItemContainerStyle` пунктов (свой `ControlTemplate`: рамка палитры, заголовок, жест, `PART_Popup`
   подменю). Триггеры стиля: наведение/подсветка — `Hover`, `IsEnabled = false` — `ForegroundMuted`.
8. **Цвета применяются на каждое открытие.** Меню создаётся один раз на поле ввода, тема может
   измениться → `WfDesignerTheme.RecolorEditingContextMenu(menu, palette)` в `Opened`. Перекраска не
   пересоздаёт пункты и не меняет команды.
9. **Глобальные хуки живут только на время показа.** Подключение — в `Opened`, снятие — в `Closed`
   (оба обработчика, ровно один раз). Снятие внутри самого обработчика оставляло хуки на окне.
   Корневое окно — null-safe: `TryGetInteractiveRoot()` = `Application.Current?.MainWindow`; при
   отсутствии окна меню открывается без глобальных хуков, а не падает.
10. **Никаких записей в ресурсы шаблонов.** `CellEditingTemplate`/`DataTemplate.Resources` сервис не
    трогает: шаблоны запечатаны, словарь только для чтения (дефект 1.0.0.0). Палитра поля ввода
    ставится **локальными значениями** в `PreparingCellForEdit`
    (`DesignerVariableContextMenuService.ApplyCellPalette`): это работает и после смены темы, и для
    `DataGridTextColumn`, где своего `CellEditingTemplate` нет вовсе, и не зависит от того, перекрывает
    ли стиль шаблона WF неявный ресурс выше по дереву.
11. **Подключение идемпотентно.** Панель — один раз (`_isSet…`), таблица — один раз (`_prepared`);
    таблица без столбцов не подключается (`CanAttach`) и ждёт своего `Loaded`, чужая таблица
    (другое имя) в быстром пути по имени — тоже.
12. **Обслуживаются все таблицы дизайнера.** `RefreshTables` подключает каждую `DataGrid` поддерева
    (поиск по типу: `WfDataGridTheme.FindTables`), поэтому «Пространства имён» получают то же меню, что
    переменные и аргументы. Имена панелей и таблиц шаблон WF меняет между версиями, а панель
    «Пространства имён» называется `imports1` — на имена полагаться нельзя. Отдельного меню «только для
    пространств имён» нет: пункты «Вырезать/Копировать/Вставить» одинаковы, а доступность команды
    решает сам `ApplicationCommands` (в таблице пространств имён ячейки только для чтения).
13. **Popup темизируется отдельно от дерева дизайнера.** Меню не является потомком `DesignerView`,
    поэтому общий проход по дереву до него не доходит — ни разу. Отсюда отдельная точка правды:
    `WfContextMenuTheme.Hook()` ставит обработчик класса `ContextMenu` на `ContextMenu.OpenedEvent`
    (не на `Loaded`: у popup-ребёнка `Loaded` обработчикам класса не доставляется — проверено
    `WfContextMenuThemeTests.Apply_ThemesForeignDesignerContextMenu`). Проход применяется только к меню,
    чья цепочка родителей (или `PlacementTarget`) доходит до `DesignerView`/контролов
    `System.Activities.Presentation`: меню AvalonDock, дерева проекта и чата остаются на своих стилях
    (`Hook_LeavesMenusOutsideDesignerAlone`). Палитру, заданную владельцем меню (фабрика/перекраска),
    общий проход не перебивает — только доводит материализованные части до неё же.
    Меню дизайнера, открываемое правым кликом, темизируется **до показа** — на
    `ContextMenuService.ContextMenuOpeningEvent` (`WfContextMenuTheme.PrepareOpen`): если стиль пунктов
    ещё чужой, генератор контейнеров падает на разделителе
    (`InvalidOperationException: Стиль, заданный для типа "MenuItem", не может применяться к типу
    "Separator"`), поэтому стиль пунктов раздаётся селектором (`ItemContainerStyleSelector`), а
    `ItemContainerStyle` снимается. Разделители получают собственный hairline-стиль палитры
    (`WfContextMenuThemeTests.Apply_ThemesDesignerMenuWithSeparator`).

## Правила закрытия меню

| Действие | Обработчик | Результат |
| --- | --- | --- |
| выбор пункта | `MenuItem.Click` | `IsOpen = false`, сброс автомата, возврат фокуса в поле |
| клик вне меню и вне поля | `Mouse.AddPreviewMouseDownHandler` на корне | то же самое |
| `Escape` | `Keyboard.AddPreviewKeyDownHandler` на корне | то же самое, `e.Handled = true` |
| любой другой ввод | — | меню остаётся (иначе не работают `Ctrl+C`/`Ctrl+V`) |

Все пути идут через `CloseMenu`, снимают хуки в `ContextMenu.Closed` и вызывают
`DesignerContextMenuState.NotifyMenuClosed()`.

## Известные ограничения

* Механизм не восстанавливает правку, если ячейка уже вышла из режима редактирования другим путём
  (`Enter`, `Tab`, программный `CommitEdit`): отмена действует только на один `CellEditEnding`.
* Меню создаётся на поле ввода (`TextBox.ContextMenu`) и живёт до пересоздания ячейки; следующий вход
  в правку создаёт новое поле и новое меню.
* Глобальные хуки закрытия вешаются на `MainWindow`: меню, открытое в модальном диалоге поверх
  главного окна, закроется кликом по главному окну.
* Палитра применяется в начале правки: если тема сменилась **во время** правки ячейки, поле
  перекрасится при следующем входе в правку.

## Ручной чеклист приёмки

1. Открыть воркфлоу, панель «Переменные» (именно этот путь падал в 1.0.0.0): панель открывается без
   исключения в журнале.
2. Дважды щёлкнуть ячейку «Имя» → правый клик → «Копировать». Правка не завершается, текст в буфере.
3. Повторить для «Тип», «Область» и для панели «Аргументы» — меню появляется в каждом столбце,
   включая столбцы без собственного шаблона правки. То же для панели «Пространства имён»
   (`importedNamespacesDataGrid`): раньше она не обслуживалась вовсе, и правый клик закрывал панель.
4. Правый клик по заголовку столбца/скроллбару → меню не открывается, а следующая правка ячейки
   завершается обычным способом (флаг отмены не залип).
5. Открыть меню, переключить тему (`Настройки → Тема`) и открыть меню снова — цвета светлой темы,
   без белых/чёрных артефактов.
6. Меню в обеих темах: поверхность `Surface`, hairline по периметру, текст `Foreground`, наведение —
   `Hover`, недоступный пункт — приглушённый текст. Белых плашек и белых букв быть не должно ни в одном
   столбце и ни в одной таблице (переменные, аргументы, пространства имён).
7. Закрыть меню клавишей `Escape` и кликом вне — поле сохраняет фокус, повторный показ меню работает.
8. Нажать `Ctrl+C` при открытом меню — меню не закрывается, копирование работает.

Автоматизированная часть чеклиста: `vstest.console` по `AiRpaStudio.Tests` (все тесты, включая стражи),
плюс отдельный запуск `RealRightClickInEditingCell_ReachesGridHandler`
(`vstest.console … /Tests:RealRightClickInEditingCell_ReachesGridHandler`) — он делает настоящий правый
клик через `SendInput`/`mouse_event` и потому помечен `[Explicit]`.

## Тесты

| Проверка | Тест |
| --- | --- |
| палитра поля ввода: кисти, каретка, шрифт, null-поле | `DesignerVariableContextMenuTests.ApplyCellPalette_*` (5 тестов) |
| сквозной сценарий: правка → правый клик → меню открылось, правка не завершилась | `DesignerVariableContextMenuTests.RightClickInEditingCell_OpensMenuAndKeepsEdit` |
| клик мимо поля правки: меню не открывается, отмена не взводится | `DesignerVariableContextMenuTests.RightClickOutsideEditingField_DoesNotOpenMenuNorCancelEdit` |
| **реальный** правый клик мышью доходит до обработчика таблицы | `DesignerVariableContextMenuTests.RealRightClickInEditingCell_ReachesGridHandler` (`[Explicit]`: двигает курсор, нужен интерактивный рабочий стол) |
| инициализация поверх **запечатанных** шаблонов не падает | `DesignerVariableContextMenuTests.OnVariableDesignerVisible_SealedTemplateResources_DoNotThrow` |
| в шаблоны правки ничего не пишется | `DesignerVariableContextMenuTests.OnVariableDesignerVisible_DoesNotWriteTemplateResources` |
| обработчики подключены к таблице; повтор — идемпотентно | `..._AttachesHandlersToGrid` / `..._AttachTwice_IsIdempotent` |
| все таблицы дизайнера (включая «Пространства имён») подключаются общим проходом | `RefreshTables_AttachesVariablesArgumentsAndNamespaceTables` / `_IsIdempotent` / `_WithoutColumns_DoesNotThrow` / `_NullRoot_DoesNotThrow` |
| тематизация таблиц: кисти, шапка, строки, ячейки, панель-контейнер, живые контейнеры | `WfDesignerTableThemeTests.Apply_*` (13 тестов) |
| тематизация вкладок shell bar (активная/hover/подпись) | `WfDesignerTableThemeTests.Apply_ThemesShellBarItems` / `Apply_ShellBarItemsFollowThemeSwitch` |
| таблица без столбцов / только текстовые / чужое имя / null | `..._WithoutColumns_DoesNotThrow` / `..._TextColumnsOnly_Attaches` / `..._UnknownDataGridName_DoesNothing` / `..._NullDesigner_DoesNotThrow` |
| перекраска меню при смене темы без пересоздания | `RecolorEditingContextMenu_SwitchesPalette` / `_KeepsStructureAndCommands` / `_NullArguments_DoNotThrow` |
| поле правки: сам `TextBox`, его потомок, не-поле, null | `DesignerVariableContextMenuPolicyTests.FindEditingField_*` (3 теста) |
| взвод отмены только для поля ввода | `DesignerVariableContextMenuPolicyTests.ShouldArmCancel_*` (3 теста) |
| отмена одноразовая и не снимается в `Opened` | `DesignerVariableContextMenuPolicyTests.State_*` (6 тестов) |
| правила закрытия (клик вне, `Escape`) | `ShouldCloseOnMouseDown_*` / `ShouldCloseOnKey_OnlyEscape` |
| позиционирование меню | `ConfigureMenuPlacement_*` |
| ровно одна инициализация после `SetVariablesStyle` | `DesignerVariableContextMenuTests.Source_InitializesTrackingOnceAfterVariablesStyle` |
| стражи по исходникам (см. раздел «Стражи») | `DesignerVariableContextMenuTests.Source_*`, `ProjectFile_*`, `Document_*`, `Agents_*` |
| меню на палитре: свои шаблоны, поверхность, текст, контраст в обеих темах | `WfContextMenuThemeTests.CreateEditingContextMenu_*` (4 теста) |
| перекраска меню при смене темы (шаблоны пересобираются) | `WfContextMenuThemeTests.RecolorEditingContextMenu_RebuildsTemplatesForNewPalette`, `DesignerVariableContextMenuTests.RecolorEditingContextMenu_SwitchesPalette` |
| меню самого дизайнера (popup вне дерева) и границы общего прохода | `WfContextMenuThemeTests.Apply_ThemesForeignDesignerContextMenu`, `Hook_LeavesMenusOutsideDesignerAlone` |
| меню с разделителями (меню самого WF) не падает и темизируется | `WfContextMenuThemeTests.Apply_ThemesDesignerMenuWithSeparator` |

## Стражи

Стражи сделаны на двух уровнях: поведение (юнит-тесты выше) и текст исходников (регресс «кто-то
переписал на «логичный» вариант»).

| Страж | Что запрещает |
| --- | --- |
| `Source_InitializesTrackingOnceAfterVariablesStyle` | вторую точку инициализации; инициализацию до `SetVariablesStyle` |
| `Source_HandlersLiveOnGrid` | перенос обработчиков в ячейку/шаблон; обход `DesignerContextMenuState`; потерю `e.Handled = true` |
| `Source_NoWritesIntoCellTemplates` | любое обращение к `CellEditingTemplate`, запись в `*.Resources[…]`, `??= new ResourceDictionary()` — то есть возврат дефекта 1.0.0.0 |
| `Source_RemovesGlobalHandlersOnClosed` | подключение хуков при создании меню; отсутствие снятия в `Closed` |
| `Source_InteractiveRootIsNullSafe` | `Application.Current.MainWindow` без `?` |
| `Source_UsesSinglePaletteMenuFactory` | `new ContextMenu`, `MessageBox`, `Debug.WriteLine`, `#RRGGBB`, `Colors.*`; отсутствие `RecolorEditingContextMenu` |
| `Source_UsesElementNameConstants` | дублирующиеся литералы имён WF-элементов (`variableDataGrid`, `VariableDesigner`, …) |
| `Source_ThemeRecolorIsUsedByFactory` | раскраску пунктов мимо `RecolorEditingContextMenu` |
| `Source_AttachesEveryDesignerTable` | возврат к двум зашитым именам панелей; потерю общего прохода `RefreshTables` и поиска таблиц по типу |
| `Sources_TableThemeIsWiredIntoDesignerTheme` / `Sources_TableThemeUsesPaletteOnly` | выпадение темизации таблиц из `WfDesignerTheme.Apply`; возврат `#RRGGBB`, `Colors.*` и записи в шаблоны правки |
| `WfContextMenuThemeTests.Sources_ContextMenuThemeUsesPaletteOnly` | возврат меню на чужой шаблон (нет `new ControlTemplate(typeof(ContextMenu))`/`ItemContainerStyle`), `#RRGGBB`, `Colors.*`, `SystemColors`, `MaterialDesign.Brush`, `DropShadowEffect` |
| `WfContextMenuThemeTests.Sources_FactoryAndRecolorGoThroughMenuTheme` | раскраску меню мимо `WfContextMenuTheme`; потерю общего прохода `WfContextMenuTheme.Hook()` |
| `WfContextMenuThemeTests.ProjectFile_CompilesContextMenuTheme` | забытый `<Compile Include="Helpers\WfContextMenuTheme.cs" />` (legacy-csproj) |
| `WfContextMenuThemeTests.Document_RecordsMenuTemplateInvariant` | удаление из документа причины (popup), симптома «белые буквы» и упоминания `WfContextMenuTheme` |

| `ProjectFile_CompilesSourcesAndShipsDocument` | забытый `<Compile Include>` / документ вне проекта |
| `Document_RecordsPrinciplesInvariantsAndGuards` | удаление разделов «Инварианты»/«Стражи»/«Тесты»; пропажу симптома «только для чтения» и записи о 1.0.0.0 |
| `Agents_ReferencesDocument` | выпадение механизма из карты проекта `AGENTS.md` |

## Куда смотреть при поломке

* **`InvalidOperationException: ResourceDictionary доступен только для чтения`** → кто-то снова пишет
  в `DataTemplate.Resources` шаблона правки. Это тот самый дефект 1.0.0.0: шаблоны WF запечатаны,
  писать в них нельзя — обработчики и палитра должны оставаться на `DataGrid` (инвариант 10).
* **Правка завершается при правом клике** → сломано туннелирование: обработчик обязан быть на
  `DataGrid.PreviewMouseRightButtonDown` и ставить `e.Handled = true`; `CellEditEnding` не должен быть
  перехвачен раньше.
* **Ячейка «залипает» в режиме правки** → флаг отмены взведён, но `CellEditEnding` не пришёл (взвод без
  consume). Проверьте `DesignerContextMenuState.NotifyMenuClosed` в `Closed` и в `CloseMenu`.
* **Меню не появляется только в части столбцов** → обработчики попали в шаблон правки вместо таблицы
  (у `DataGridTextColumn` своего шаблона нет).
* **Меню белое с белыми буквами / не следует теме** → меню рисует чужой шаблон: проверьте, что
  `RecolorEditingContextMenu` идёт через `WfContextMenuTheme.Apply` (собственный `Template` меню и
  `ItemContainerStyle` пунктов), а `WfDesignerTheme.ApplyToApplication` вызывает
  `WfContextMenuTheme.Hook()` для меню самого дизайнера. Локальные `Background`/`Foreground` этот
  симптом не лечат — они шаблон не перекрывают.
* **Меню старых цветов после смены темы** → `Opened` не вызывает `RecolorEditingContextMenu`, либо
  палитра поля ставится не в `PreparingCellForEdit`.
* **Утечка обработчиков / падение после закрытия документа** → `Closed` не снимает хуки с корня.
* **Меню в углу экрана** → не задан `PlacementTarget` (`ConfigureMenuPlacement`).

## История

| Дата | Изменение |
| --- | --- |
| 2026-11-02 | Документ создан: механизм зафиксирован, покрыт тестами и стражами. |
| 2026-11-02 | Дефект 1.0.0.0 (запись в запечатанный `DataTemplate.Resources`) — исправлен, добавлены `SealedTemplateResources_DoNotThrow` и `Source_NoWritesIntoCellTemplates`; палитра переведена на локальные значения (`ApplyCellPalette`), обработчики — на `DataGrid`. |
| 2026-11-02 (F-06) | Дефект «белое меню с белыми буквами» — исправлен: меню и пункты получили собственные шаблоны (`WfContextMenuTheme`), добавлен общий проход по `ContextMenu.OpenedEvent` для меню дизайнера (popup вне дерева), инвариант 7 переписан, появился инвариант 13 и `WfContextMenuThemeTests`. |
