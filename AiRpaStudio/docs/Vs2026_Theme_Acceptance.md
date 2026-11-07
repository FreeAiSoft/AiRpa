# Приёмка темы Visual Studio 2026 (студия AiRpaStudio)

Ручная часть плана `docs\Vs2026_Theme_Plan.md` §9. Пункты 1-8 — обязательная проверка глазами;
всё, что можно проверить без запуска, автоматизировано в `AiRpaStudio.Tests\ThemeAcceptanceTests.cs`
(сквозные стражи) и в тестах этапов 1-8 (`StudioPaletteTests`, `ThemeXamlTests`, `WindowCornerTests`,
`WfThemeTests`, `WfDesignerThemeTests`, `LogAndChatThemeTests`, `ChatThemeBridgeTests`,
`StudioDialogWindowTests`, `DockingThemeBrushesTests`, `AvalonDockThemeBridgeTests`, `StudioTitleBarTests`).

## Как прогнать автоматическую часть

```powershell
$msb   = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'
$vstest = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\TestPlatform\vstest.console.exe'

& $msb AiRpaStudio.Tests\AiRpaStudio.Tests.csproj -t:Build -v:m
& $vstest AiRpaStudio.Tests\bin\Debug\net48\AiRpaStudio.Tests.dll
```

`dotnet test` в этом репозитории не работает (legacy WPF-проекты не собираются SDK-ным MSBuild) —
только MSBuild из Visual Studio + `vstest.console.exe`.

## Что именно смотреть руками

### 1. Тёмная тема по умолчанию: нет светлых прямоугольников

Запустить студию (тема по умолчанию — тёмная: `App.xaml` `BaseTheme="Dark"`, `Properties.Settings.UseDarkTheme`
по умолчанию `true`). Пройти глазами: панели докинга, центр, правая сторона, заголовки, вкладки,
панель «Log», «Chat», popup `SearchableComboBox`, popup настроек лога, диалог «Свойства».
Ни один прямоугольник не должен быть светлее `Studio.Surface` (`#272727`) — кроме hairline 1px
и фона выбранной вкладки `Studio.TabActive` (`#3A3B3E`): активная вкладка «Проект / Активности /
Структура» и вкладка документа обязаны быть светлее неактивных.

Автоматически: `ThemeAcceptanceTests.ThemedXaml_HaveNoHardcodedColors`,
`.ThemedXaml_HaveNoNamedColorAttributes`, `LogAndChatThemeTests.*`.

### 2. Ноль акцента (фиолетовый/лаймовый/синий), кроме красного «закрыть»

Проверить: панель, вкладка, заголовок, автоскрытие, выделение в «Структуре»/«Активностях»/списке
шаблонов, оверлей перетаскивания, Ctrl+Tab, кнопки сохранения на тулбаре.
Красный допустим только у кнопки закрытия и у семантики ошибки/останова.

Автоматически: `ThemeAcceptanceTests.ToolbarPlaybackAndFileIcons_UseSemanticBrushes`,
`.SemanticAliases_EncodeStateAndDoNotDependOnTheme`, `.MetroThemeFallbacks_AreNeutral`,
`StudioBrushesTests.Apply_FillsSystemBrushesUsedByNativeWfControls`.

### 3. Единый фон: окно → панель → документ → поверхность дизайнера

Открыть документ (`Activity1.xaml`), развернуть дизайнер: фон поверхности дизайнера, shell bar,
breadcrumb, MiniMap и панели докинга должны быть одного цвета, различие — только hairline.
Проверить также пустой проект (без открытых документов) и панель «Свойства».

Отдельно — хлебные крошки: провалиться в `Sequence`, затем в `Assign` (крошки пересоздаются) — имена
активностей на поверхности палитры, белых плашек и системного выделения нет, hover — `Hover`, выделение —
`Selected`. То же после переключения темы (п.5).

Автоматически: `WfDesignerThemeTests.Apply_BreadCrumbHasNoLightBackgrounds`,
`.Apply_ThemesBreadCrumbItems`, `.Apply_BreadCrumbItemContainersUseThemedStyle`,
`.Apply_ThemesBreadCrumbItemsCreatedAfterApply`, `.Apply_BreadCrumbFollowsThemeSwitch`,
`.Apply_BreadCrumbHasNoLightBrushAnywhere` (белого пикселя на hairline «крошки/дизайнер» нет),
`.Apply_BreadCrumbClipsArtifactsOverflowingOnTheDivider` (содержимое не рисует вне полосы крошек),
`.Apply_BreadCrumbArtifactsCreatedAfterApplyAreNeutralized` (крошки и шаблон, созданные после темизации),
`.Apply_BreadCrumbJunctionHasNoLightPixel` (рендер полосы: на стыке с дизайнером нет светлого пикселя),
`.Apply_BreadCrumbTemplateCornerDotMergesWithSurface` (угловой `Rectangle` 1×1 шаблона цвета `Surface`),
`.Apply_BreadCrumbBandStaysOpaqueSurfaceUnderWhiteHost` (подложка полосы непрозрачна: белого хоста
`BreadCrumbBarLayout` за крошками не видно), `.Apply_BreadCrumbBackdropBehindNamesIsSurface`.

Масштаб в shell bar (`zoomPicker`, editable ComboBox): в свёрнутом виде текст читается на поверхности
палитры (белых плашек и белых букв нет), стрелка раскрытия видна, в раскрытом списке элементы на
поверхности палитры, hover — `Hover`, выделение — `Selected`. Проверить в тёмной и светлой теме и после
переключения темы без перезапуска.

Автоматически: `WfDesignerThemeTests.Apply_ThemesShellBarZoomCombo`,
`.Apply_ShellBarZoomComboIsReadableInBothThemes`, `.Apply_ThemesShellBarZoomComboList`,
`WfThemeTests.Apply_ThemesComboBoxes`, `.Apply_ThemesComboBoxItems`, `.Apply_MakesComboBoxGlyphVisible`,
`.Apply_ComboBoxTextHasContrastInBothThemes`, `.Apply_SwitchesComboBoxPalette`,
`.Apply_ComboBoxThemeIsIdempotent`.

Столбцы `Scope` и `Modifiers` в «Переменных»/«Аргументах» (редактор — `ComboBox`): войти в правку ячейки
двойным кликом — у свёрнутого списка фон `Surface`, буквы `Foreground`, стрелка раскрытия видна, белой
плашки Aero2 нет; раскрыть список — элементы на поверхности палитры, hover `Hover`, выделение `Selected`,
чёрных букв на тёмном фоне нет. То же для enum-свойств панели «Свойства» (`Scope`, `Modifiers`,
`True/False`) и после переключения темы без перезапуска.

Автоматически: `WfDesignerTableThemeTests.Apply_LateCreatedCellComboBoxIsThemed`,
`.Apply_CellComboBoxItemStyleUsesPalette`, `.Sources_TableThemeThemesComboBoxes`,
`WfThemeTests.SetPropertyStyles_ThemesInspectorComboBoxes`,
`.SetPropertyStyles_SwitchesInspectorComboBoxPalette`,
`DesignerVariableContextMenuTests.PreparingCellForEdit_ComboBoxGetsPalette`.

Панель «Свойства активности» (выбрана переменная → строки `Modifiers` и `Scope`): щёлкнуть по комбобоксу
(вход в режим правки) — поле остаётся на `Surface`, белой плашки нет; раскрыть `Modifiers` (flags-enum) —
подписи чекбоксов `ReadOnly`/`Mapped` светлые на тёмной подложке, галочки и рамки видны; всё то же после
переключения темы на лету и после повторного выбора активности (строки WF пересоздаются).

Автоматически: `WfDesignerTableThemeTests.ThemeComboBoxes_PaintsEditableFieldOfCollapsedComboBox`,
`.ThemeComboBoxes_PaintsDropDownCheckBoxTextWithPalette`,
`.SetPropertyStyles_ThemesComboBoxOfRowCreatedAfterSweep`.

Панель «Свойства активности», выбранная переменная (`Modifiers`, `Scope`, `Type`): **до любого клика** по
редактору фон комбобоксов — `Surface` (в тёмной теме `#272727`), поле правки `Modifiers` тоже тёмное, белых
плашек (`#FFFFFF` у `Modifiers`, `#EFEFEF` у `Scope`) нет; после клика цвет не меняется. Раньше
`FlagEditor`/`ScopeComboBox` создавались после прохода темы, и до первого клика они оставались светлыми.

Автоматически: `WfDesignerTableThemeTests.ThemeComboBoxes_ComboBoxCreatedAfterSweep_IsThemedOnLoad`,
`.ThemeComboBoxes_ComboBoxOutsideThemedRootsKeepsSystemChrome`,
`.Sources_ThemerThemesLateCreatedComboBoxesOnLayout`.

Автоматически: `ThemeAcceptanceTests.DialogsAndMainWindow_UseSingleSurfaceBackground`,
`WfDesignerThemeTests` (ключи поверхности, отсутствие акцента в словаре).

Панели «Переменные», «Аргументы», «Пространства имён»: нажать по очереди три вкладки в shell bar — панель
и её таблица (`DataGrid`) на одном фоне с дизайнером (`Surface`), шапка колонок чуть светлее
(`SurfaceAlt`), сверху hairline. В таблице: hover строки — `Hover`, выделение — `Selected` (не системное
синее), фокус ячейки — hairline `SeparatorStrong`, глиф сортировки виден, белых плашек Aero2 (шапка, угол
«выделить всё», пустая область, «новая строка») нет. Вкладки: активная — `Selected`, неактивная подпись —
`ForegroundMuted`. Двойной клик в ячейку → правый клик: меню открывается во всех трёх таблицах, включая
«Пространства имён» (раньше эта таблица не обслуживалась вовсе), правка не завершается. Само меню — на
палитре: поверхность `Surface`, hairline по периметру, текст `Foreground`, наведение `Hover`, недоступный
пункт приглушён (`ForegroundMuted`); белых плашек и белых букв нет ни в одной теме (раньше меню рисовал
чужой шаблон: popup вне дерева дизайнера). То же после переключения темы (п.5).

Автоматически: `WfDesignerTableThemeTests.Apply_ThemesAllDesignerTables`,
`.Apply_TableHeaderUsesSurfaceAltAndHairline`, `.Apply_TableRowStatesUsePalette`,
`.Apply_TableCellFocusUsesSeparatorStrongHairline`, `.Apply_TablePanelBackdropBehindWhiteHostIsSurface`,
`.Apply_LateCreatedRowsAreThemed`, `.Apply_LiveTableHasNoLightPlates`,
`.Apply_LiveTableHeaderAndCellsUsePalette`, `.Apply_ThemesShellBarItems`,
`.Apply_ShellBarItemsFollowThemeSwitch`, `DesignerVariableContextMenuTests.RefreshTables_*`,
`WfContextMenuThemeTests.CreateEditingContextMenu_*` (свои шаблоны, палитра, контраст в обеих темах),
`.RecolorEditingContextMenu_RebuildsTemplatesForNewPalette`, `.Apply_ThemesForeignDesignerContextMenu`,
`.Apply_ThemesDesignerMenuWithSeparator` (меню WF с разделителем не падает),
`.Hook_LeavesMenusOutsideDesignerAlone`.

Панель «Свойства» — плоские кнопки (правка F-03): у переключателей «По категориям»/«Алфавитный» скруглений
нет (hairline-рамка осталась, размеры 28×28 и глиф 20 px прежние), у кнопки очистки поиска рамки нет вовсе,
а крестик уменьшен примерно в 1,5 раза (11 px вместо 16) при том же размере кнопки 24×24; кнопка «…» в
строках свойств (редактор аргументов) без скруглений и без тени — плоская, без плашки и рамки (ховер-плашки
у неё тоже больше нет). То же после переключения темы и после выбора другой активности: скругления не
возвращаются, «…» не исчезает.

Автоматически: `WfThemeTests.PropertyHelper_StylesButtonsByName`,
`.PropertyHelper_SearchClearButton_IsFramelessWithSmallerGlyph`,
`.EditModeButton_IsFlatWithoutCornerRadiusAndShadow`,
`.EditModeButton_WithoutBorderChrome_GetsFlatTemplateAndKeepsGlyph`,
`.PropertyHelper_FlattensEditModeButtons`.

### 4. Скруглённые углы: главное окно, четыре диалога, откреплённый документ и панель, Ctrl+Tab

Windows 11 — DWM (`DwmSetWindowAttribute`, 8px); Windows 10 — region через
`WindowChrome.CornerRadius` для окон со своим chrome. Диалоги «Создать активность», «Создать проект»,
«Открыть VS-проект», «Из VS-шаблона» и окно ошибки рисуют заголовок сами (`Controls\StudioTitleBar.xaml`),
поэтому скругляются в обеих ОС; Ctrl+Tab (W-08) на Windows 10 остаётся прямоугольным — см. отклонения этапа 5.

Автоматически: `ThemeXamlTests.ThemeXaml_WindowsAreRounded`, `WindowCornerTests.*`.

### 5. Переключение Light/Dark на лету перекрашивает всё

Переключить тему (меню «Настройки»), не перезапуская студию. Обязательно проверить уже открытые
документы дизайнера, откреплённые (floating) панели, «Chat», «Log», «Структуру», «Свойства», тулбар,
меню и системные скроллбары. Ничего не должно остаться в прежней теме; повторное переключение обратно
не должно оставлять «залипших» цветов.

Автоматически: `ThemeAcceptanceTests.ApplyTheme_ReappliesEverySubsystem`,
`.MainView_ReappliesOpenDocumentsOnThemeChange`, `WfThemeTests`, `ChatThemeBridgeTests`.

### 6. Открепление панели даёт стиль главного окна; NuGet и Chat без светлых артефактов

Открепить панель (двойной клик по заголовку) — заголовок и кнопки должны выглядеть как в главном окне,
PNG-иконки не должны «чернеть» при hover. Открыть/закрыть «NuGet» и «Chat» — без белых панелей,
popup и полос прокрутки.

Автоматически: `AvalonDockThemeBridgeTests`, `ThemeXamlTests`, `LogAndChatThemeTests`,
`ChatThemeBridgeTests`.

### 7. Перезапуск с сохранённым ADLayout и сброс раскладки

Закрыть и открыть студию с сохранённой раскладкой (`Properties.Settings.ADLayout`) — вид не ломается,
панели на месте, тема та же. Затем «сбросить раскладку» — возвращается дефолт, стили остаются новыми.

Автоматически: `DockingThemeBrushesTests` (единственная точка записи кистей `ReapplyDockManagerResources`),
`ChatThemeBridgeTests`.

### 8. Тонкая рамка вокруг каждой докпанели

Все докпанели обведены рамкой 1px: панели главного окна («Проект», «Активности», «Структура», «Свойства»,
«Log», «Chat», «NuGet»), панель документов, выдвинутая панель автоскрытия и панели внутри откреплённых окон.
Цвет — `Separator` (`#373737` в тёмной теме, `#E4E4E4` в светлой), у активной панели — `SeparatorStrong`.
Толщина всегда 1px (не 2 и не 3), фон панели при этом не меняется.

- между двумя соседними панелями ровно две линии (по одной на рамку), постоянной линии ресайзера нет;
  при наведении на границу и при перетаскивании появляется подсветка `SeparatorStrong` (1px);
- при активации панели (клик по её вкладке) ярче становится только рамка — заливки заголовка нет;
- открепить панель (двойной клик по заголовку): активное окно обведено `SeparatorStrong`, внутри второй
  рамки нет; вернуть панель в док — рамка панели снова 1px, не «залипла» погашенной;
- выдвинуть панель автоскрытия — она обведена рамкой 1px;
- в панели «Свойства» нет второй рамки вокруг контрола.

Автоматически: `ThemeXamlTests.ThemeXaml_PaneFramesAreOnePixel`,
`.ThemeXaml_PanesAndDocumentsUseHairlines`.

### 9. Заголовок окон: единый стиль во всех окнах студии

Открыть по очереди «Создать активность», «Создать проект», «Открыть VS-проект», «Из VS-шаблона»
и окно ошибки (например, через ошибку модели). Полоса заголовка у всех — как у главного окна:
высота 32, фон `Studio.Surface`, текст — `Title` окна, кнопки «свернуть»/«закрыть» 46×32 на
`MaterialIcon`, hover закрытия красный (`Studio.CloseHoverBackground`), окно тянется за полосу,
системного белого caption нет; в тёмной теме полоса тёмная, в светлой — светлая. У окна ошибки
кнопки «свернуть» нет (окно не в панели задач), размер меняется за кромку окна.

Автоматически: `StudioTitleBarTests.*`.

## Отметка о результате

| Пункт | Windows 11 | Windows 10 | Дата | Кто |
|---|---|---|---|---|
| 1 |  |  |  |  |
| 2 |  |  |  |  |
| 3 |  |  |  |  |
| 4 |  |  |  |  |
| 5 |  |  |  |  |
| 6 |  |  |  |  |
| 7 |  |  |  |  |
| 8 |  |  |  |  |
| 9 |  |  |  |  |

### 10. Шрифт Roboto, вкладки документов, дерево проекта, цвета редакторов

Доработка по замечаниям приёмки (этап 12 плана, §12). Проверять в тёмной и светлой теме.

1. **Шрифт.** Ни в одном окне, панели, меню, диалоге, логе и чате нет Consolas/Segoe UI:
   весь интерфейс — Roboto (шрифт встроен в сборку, установленный Roboto не требуется).
   Проверить: меню, заголовок окна, «Проект», «Свойства», «Структура», «Log», чат, окно ошибки,
   диалоги создания проекта/активности.
2. **Вкладки документов.** Заголовок активной и неактивной вкладки читается в обеих темах;
   при наведении текст не темнеет; стрелка-переключатель открывает меню документов с фоном
   `Studio.Surface` и волосяной рамкой; `Ctrl+Tab` показывает список документов с читаемым текстом.
3. **Откреплённое окно документа/панели** — заголовок тоже читаемый (та же тема Metro, 
   ключи `AvalonDock_*` прокинуты мостом).
4. **Дерево проекта.** Клик по активности: файл открывается, название остаётся светлым (в светлой
   теме — тёмным) и на выбранном, и на элементе под курсором; переименование (F2/двойной клик)
   показывает поле с фоном `Studio.Surface`.
5. **Окно WF «Свойства».** После выбора другой активности (и после разворота категории, прокрутки)
   фон, заголовки категорий, строки, редакторы значений и кнопки тулбара — в теме; светлых
   прямоугольников и чёрного текста нет; шрифт — Roboto (WF берёт его из `FontFamilyKey`).
6. **Редакторы кода и XAML.** Открыть `.cs` и `.xaml` из проекта: фон `#1E1E1E`, текст `#D4D4D4`,
   номера строк и текущая строка из палитры, подсветка как в Visual Studio (ключевые слова синие,
   строки оранжевые, комментарии зелёные, XML-теги/атрибуты в схеме VS). Переключить тему —
   редактор и подсветка меняются на лету, без переоткрытия документа.

Автоматически: `StudioFontTests.*`, `ProjectManagerThemeTests.*`, `StudioEditorThemeTests.*`,
`WfThemeTests.NewInspectorContent_IsThemedAfterRecreation`,
`WfDesignerThemeTests.Resources_DeclareWorkflowFont`, `ThemeXamlTests.*`.

### 11. Панель «Активности» (WF `ToolboxControl`) в тёмной теме

Доработка по замечанию приёмки: под названиями категорий была светлая плашка. Проверять в обеих темах.

1. **Заголовки категорий.** Фон заголовка — `Studio.Surface` (никаких белых/серых полос), название
   читается (`Studio.Foreground`), стрелка раскрытия видна (`Studio.ForegroundMuted`).
2. **Разворачивание категории** (и кнопки «Развернуть всё»/«Свернуть всё»): строки активностей и
   вновь созданные категории тоже в теме; светлых прямоугольников нет.
3. **Наведение и выделение.** Hover — `Studio.Hover`, выделение — `Studio.Selected`, без системной
   светло-жёлтой плашки (`#FFE8A6`) и оранжевой рамки (`#E5C365`).
4. **Перетаскивание активности в дизайнер** работает как раньше (стиль WF не подменяется), двойной
   клик вставляет активность.
5. **Смена темы Light↔Dark на лету** (в том числе с развёрнутыми категориями) не оставляет плашек
   предыдущей темы.

Автоматически: `ToolboxThemeTests.*`, `ToolboxDragContractTests.*`,
`StudioBrushesTests.Apply_SystemBrushGradationsFollowPalette`.

### 12. Ошибки валидации: иконки и бейдж выражения

Доработка по замечанию приёмки: «ошибки валидации не показываются сразу, а когда показываются — только
красная рамка без иконки». Проверять в обеих темах.

1. **Ошибка выражения появляется на лету.** В строке выражения активности напечатать заведомо
   неверное выражение (`1 +`): значок ошибки появляется без ухода фокуса и без Enter, не позже
   ~0,5 с после последнего нажатия; подсказка при наведении показывает текст ошибки.
2. **Значок — это иконка, а не рамка.** Бейдж справа от поля выражения: залитый голубой круг с «!»
   (`materialIcons:MaterialIcon Kind="Error"`, цвет `Studio.Semantic.Validation`); пустых красных
   рамок нет ни у бейджа, ни у адаптера ошибки вокруг контрола.
3. **Исправление снимает ошибку.** Дописать выражение до корректного (`1 + 1`) — значок исчезает так
   же быстро; очистка поля тоже убирает значок.
4. **Иконка на активности.** Оставить у активности обязательное свойство пустым (или ошибку в
   аргументе) — на активности виден красный круг с «!», как в проектах Visual Basic, а не сплошной
   красный прямоугольник; подсказка содержит текст ошибки WF.
5. **Ничего не мигает.** Значок не исчезает сам через секунду-две и не появляется заново без правок
   модели (полная валидация выполняется по изменениям, а не каждый тик).

Автоматически: `ErrorBadgeTests.*`, `ValidationBadgeColorTests.*`, `ExpressionValidationTimingTests.*`, `ValidationIconTests.*`,
`ValidationIconGlyphTests.*`, `ThemeAcceptanceTests.RoslynExpressionEditor_UsesPaletteAndErrorIconBadge`.

