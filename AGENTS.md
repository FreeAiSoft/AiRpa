# Правила разработки (AiRpaStudio)

## Язык общения в чате - русский

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

## Соглашения проекта
- Целевая платформа: .NET Framework 4.8, WPF-приложение (`OutputType=WinExe`),
  `RootNamespace`/`AssemblyName` = `RPA`.
- UI-стек: WPF + MahApps.Metro (`MetroWindow`), AvalonDock (докинг-панели),
  AvalonEdit (редактор кода), Material.Icons.WPF, LogControlLibrary (просмотр логов).
- Дизайнер рабочих процессов: `System.Activities.Presentation.WorkflowDesigner`
  (Windows Workflow Foundation).
- MVVM: модель-центричный шаблон на `FW.EasyMvvm` (см. раздел ниже).
- Команды привязываются по имени метода модели через reflection (см. раздел ниже).
- Локализация: RU/EN через `ResourceManager` + `Properties.Settings.Language` (см. `App.xaml.cs`).
- Настройки сохраняются через `Properties.Settings` (метод `Save()`).
- Логирование: `RPA.Logging.Log` (запись в `log.txt`) + просмотр через `LogControlLibrary`.
- AI-интеграция: MCP-сервер (`RPA.Mcp`, stdio/http).

## Ошибки и падения приложения
- Любая ошибка в студии показывается окном `AiRpaStudio\Views\ErrorView.xaml` через
  `AiRpaStudio\Helpers\ErrorDialog.cs` (`Show` / `ShowException` / `ShowCrash`). `MessageBox` для ошибок
  и сообщений модели (`IView.OnSendMessage`) запрещён: он не следует теме и не даёт скопировать текст.
  `MessageBox` остаётся только для вопросов-подтверждений (например, «удалить папку?»).
- Окно ошибки: readonly `TextBox` (`IsReadOnly=True` + `IsReadOnlyCaretVisible=True`) — текст выделяется
  и копируется по Ctrl+C; кнопка «Копировать» кладёт текст в буфер (`Clipboard.SetText`), при недоступном
  буфере текст остаётся выделенным; цвета только из палитры (`Studio.Surface`/`SurfaceAlt`/`SeparatorStrong`,
  красный — только семантика `Studio.Semantic.Stop`); `WindowCorner.Enable(this)` из `OnSourceInitialized`.
- Падения: подписки живут в конструкторе `App` (`AppDomain.UnhandledException` +
  `DispatcherUnhandledException`) — они работают ещё до разбора `App.xaml`. Обработчик
  `Log.ReportExceptionAndShowMessage` пишет `log.txt` и открывает то же окно с заголовком «Критическая
  ошибка» (версия + время + стек). WinForms-событие `Application.ThreadException` для WPF не используется.
- `ErrorDialog` не бросает исключений и сам выбирает путь показа: UI-поток → модальное окно поверх владельца;
  фоновый поток → `Dispatcher.Invoke` с таймаутом; диспетчера нет (падение на старте/после `Shutdown`) →
  отдельный STA-поток со своей копией палитры (`StudioBrushes.ApplyPalette`), последняя страховка —
  системный `MessageBox`. Окно не создаётся через `MvvmFactory` намеренно: во время падения ни модели,
  ни `DataContext` уже нет.
- Локализация подписей — ключи `lang_error_title`, `lang_error_crash_title`, `lang_error_hint`,
  `lang_error_copy`, `lang_error_copied` (+ существующий `lang_close`) в `Main.resx` и `Main.ru-RU.resx`;
  в XAML — `{DynamicResource lang_*}`, в code-behind — fallback через `LocalizeHelper` (старт приложения,
  когда словарь ресурсов ещё не создан).
- Тесты: `AiRpaStudio.Tests\ErrorWindowTests.cs` — поведение окна (readonly, выделение, копирование, тема)
  и стражи по точкам показа ошибок (ни один файл студии не должен вернуться к `MessageBox.Show` для ошибок).
- В тексте ошибки обязателен стек вызовов. Формат один на всю студию — `AiRpaStudio\Helpers\ErrorText.cs`
  (`Of(ex)` — полный `Exception.ToString()`; `Describe(context, ex)` — «контекст: сообщение» + стек);
  им же пользуется `ErrorDialog.BuildExceptionMessage`. В местах показа/логирования (`OnSendMessage`,
  `ErrorDialog`, лог дизайнера, `Debug`/`Console`, ответы MCP, `result.Errors`) пишется полное исключение,
  а не `ex.Message`: интерполяция — `$"...: {ex}"`, аргумент — `ErrorText.Of(ex)`. Причина — инцидент
  «Не удалось загрузить в дизайнер: «…RowDefinition» не является Visual или Visual3D»: по тексту без стека
  невозможно найти место падения. Страж — `AiRpaStudio.Tests\ErrorStackTraceTests.cs`.
- Обход визуального дерева — только через `Helpers\XamlHelper.cs` (`IsVisual`, `FindVisualChildren<T>`,
  `FindVisualParent<T>`). `LogicalTreeHelper.GetChildren` отдаёт для `Grid`/`Panel` не только элементы,
  но и `RowDefinition`/`ColumnDefinition`, а `VisualTreeHelper` на них бросает `ArgumentException`
  («…не является Visual или Visual3D»). Страж — `AiRpaStudio.Tests\VisualTreeWalkTests.cs`.


## Версии пакетов и привязка сборок
- В решении должна быть **одна** версия каждой транзитивной зависимости. `System.Threading.Tasks.Extensions`
  закреплена на 4.5.4 (сборка 4.2.0.1): в legacy-проектах через `packages.config`, в SDK-проектах — явным
  `PackageReference`. Пакет 4.5.2 даёт сборку 4.2.0.0 (её тянули транзитивно `RPA.Chat` и `VibeCoder.Core`).
- Симптом нарушения: при загрузке проекта WF-панель «Пространства имён» (`ImportDesigner`) перечисляет типы
  сборок (`Assembly.GetExportedTypes`), привязка зависимости падает с `FileLoadException` / HRESULT 0x80131040,
  и дизайнер показывает модальное окно «Конструктор рабочих процессов», блокирующее студию.
- Страховка: `AiRpaStudio\Helpers\AssemblyBindingUnifier.cs` (`Install(AppDomain.CurrentDomain)` в конструкторе
  `App`) — на `AppDomain.AssemblyResolve` возвращает уже загруженную сборку того же имени и открытого ключа с
  версией **не меньше** запрошенной. Сателлиты `*.resources`, запросы с культурой, без версии и без открытого
  ключа, а также понижение версии не подменяются. Тесты: `AiRpaStudio.Tests\AssemblyBindingUnifierTests.cs`
  (включая стражи: единая версия пакета, redirect в `App.config`, версии сборок в папках вывода).
- Запускать студию можно только из `AiRpaStudio\bin\Debug|Release`: в `AiRpaStudio.Tests\bin\Debug\net48` копия
  `AiRpaStudio.exe` лежит без ~20 зависимостей студии (WF-сборки, `System.Reflection.Metadata`), и запуск оттуда
  даёт ложные ошибки Roslyn/дизайнера.

## Сборка и тесты
- Решение: `AiRpa.sln` (корень репозитория).
- Проект приложения: `AiRpaStudio\AiRpaStudio.csproj`.
- Тесты: `AiRpaStudio.Tests\AiRpaStudio.Tests.csproj` (SDK-style, net48 + NUnit).
  Запуск — через MSBuild из Visual Studio + `vstest.console.exe` (см. выше), т.к.
  `dotnet test` не собирает legacy WPF-проекты решения.
- Полная сборка решения: MSBuild из Visual Studio.

## Темы (MaterialDesign.Themes.Wpf) и хранилище исходников
- Темы студии строятся на `MaterialDesignThemes.Wpf` (`BundledTheme` + `MaterialDesign2.Defaults.xaml`
  + алиасы `Studio.*`, создаваемые в рантайме `Helpers\StudioBrushes.cs`); переключение Light/Dark — через
  `MaterialDesignThemes.Wpf.PaletteHelper`.
- ⚠️ Кисти `Studio.*` и `SystemColors.*BrushKey` НЕЛЬЗЯ объявлять в XAML как
  `<DynamicResource x:Key=... ResourceKey=... />`: в словарь попадает `ResourceReferenceExpression`,
  и WPF падает с `XamlParseException` на Style-сеттерах (проверяется тестом
  `AiRpaStudio.Tests\StudioBrushesTests.cs`). Только кодом — `Helpers\StudioBrushes.cs`
  (`ApplyToApplication()` при старте приложения и после каждой смены темы).
- План темизации всех визуальных контролов: `AiRpaStudio\docs\Theming_MaterialDesign_Plan.md`.
- Единая палитра студии (в стиле Visual Studio 2026): `AiRpaStudio\Helpers\StudioPalette.cs`
  (`Surface`/`SurfaceAlt`/`Hover`/`Selected`/`Separator`/`SeparatorStrong`/`Foreground`/`ForegroundMuted`,
  ровно две темы, ноль акцента, скругления 8/4/6). `StudioBrushes` и `DockingThemeBrushes` берут значения
  только оттуда: `MaterialDesign.Brush.Primary`/`Secondary` больше не источник цветов.
  Тесты: `AiRpaStudio.Tests\StudioPaletteTests.cs`.
- Шрифты: **Roboto для всего проекта**. Единственный источник — `AiRpaStudio\Helpers\StudioFonts.cs`
  (`FamilyName = "Roboto"`, `UiFont`, `CodeFont`, ключи `Studio.FontFamily`, `Studio.CodeFontFamily`,
  `Studio.FontFamilyName`). Начертания встроены в сборку (`Resources\Roboto\*.ttf`), поэтому установленный
  Roboto не требуется. В XAML — только `FontFamily="{DynamicResource Studio.FontFamily}"`
  (`Studio.CodeFontFamily` для редакторов кода), в C# — только `StudioFonts.UiFont`/`CodeFont`.
  Строки `Consolas`/`Segoe UI` в XAML и `new FontFamily("Consolas")` в коде запрещены;
  WF-контролы получают шрифт ключами `FontFamilyKey`/`FontSizeKey`/`FontWeightKey`
  (`WfDesignerTheme.CreateResourceValues`). Стражи: `AiRpaStudio.Tests\StudioFontTests.cs`.
- Редакторы кода и XAML: цвета — только из `AiRpaStudio\Helpers\StudioEditorPalette.cs` (схема
  Visual Studio 2022, dark/light), оформление и подсветка — через `AiRpaStudio\Helpers\StudioEditorTheme.cs`
  (XSHD перекрашивается по именам цветов `<Color name="…">`, поэтому один файл подсветки работает в обеих
  темах). В `Resources\Highlighting\Xaml.xshd` зашитых цветов быть не должно.
  Стражи: `AiRpaStudio.Tests\StudioEditorThemeTests.cs`.
- Инспектор свойств WF: `PropertyWindowStyleHelper.SetPropertyStyles` сливает словарь палитры в ресурсы
  самого инспектора (его шаблоны читают собственные `TextBrushKey`/`BackgroundBrushKey`/`FontFamilyKey`)
  и подписывается на `LayoutUpdated` (`Attach` + отпечаток дерева): WF пересоздаёт строки свойств, и новые
  элементы обязаны перекрашиваться повторно. Стражи: `AiRpaStudio.Tests\WfThemeTests.cs`.
- Кисти AvalonDock: студия пишет нейтральные ключи `AvalonDock_*` кодом
  (`Helpers\DockingThemeBrushes.cs`, единственная точка — `AppSettings.ReapplyDockManagerResources`),
  а в изолированные ресурсы плавающих окон / Ctrl+Tab / оверлея их прокидывает
  `AvalonDock\Themes\ThemeBrushBridge.cs` (вызывается из их `UpdateThemeResources`).
  `AvalonDock_ThemeMetro_BaseColor1..7` — только legacy-алиасы, в шаблонах темы не используются.
  Тесты: `AiRpaStudio.Tests\DockingThemeBrushesTests.cs`, `AvalonDockThemeBridgeTests.cs`,
  `ThemeXamlTests.cs` (регекс-стражи по `AvalonDock.Themes.Metro\Theme.xaml` + полный разбор XAML).
  Полоса вкладок документов (P-08): состояния рисует `Border x:Name="TabBorder"` в шаблоне контейнера
  вкладки (`AvalonDock_Base` → hover `AvalonDock_Hover` → выбранная `AvalonDock_Active` — светлее неактивной, + hairline
  `AvalonDock_Separator`). Сеттеры `Background`/`BorderBrush` в триггерах обязаны быть адресованы
  `TargetName` элемента шаблона: `BorderBrush` у самого `TabItem` ничем не отрисовывается — из-за
  этого вкладки выглядели «без темы». Кнопка закрытия вкладки —
  `AvalonDock_ThemeMetro_DocumentCloseButtonStyle`: hover — нейтральная подсветка фона
  (`AvalonDock_SelectionHover`, нажатие — `AvalonDock_Selection`), глиф остаётся `AvalonDock_Foreground`;
  красные `AvalonDock_CloseHover*` в шапке вкладок не используются (токены остаются для кнопки закрытия
  окна и валидации WF). Стражи: `ThemeXamlTests.ThemeXaml_DocumentTabTemplateDrawsStates`,
  `ThemeXaml_DocumentTabCloseButtonHoversNeutral`, `DocumentTabStyle_RendersPaletteBrushes`.
- Скругления окон: `AiRpaStudio\Helpers\WindowCorner.cs` — DWM
  (`DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)`, Windows 11) с
  fallback на region через `WindowChrome.CornerRadius` (радиус — `StudioPalette.WindowCornerRadius`,
  Windows 10). Окна студии зовут `WindowCorner.Enable(this)` из `OnSourceInitialized`; окна AvalonDock
  (откреплённые панели/документы, Ctrl+Tab) — `AvalonDock\Themes\WindowCornerBridge.cs`, провайдер в него
  ставит `WindowCorner.EnableAvalonDockWindows()` в `App.Application_Startup`. Хелпер никогда не бросает
  исключений: без DWM окно просто остаётся прямоугольным.
  Тесты: `AiRpaStudio.Tests\WindowCornerTests.cs` (контракт DWM, идемпотентность, мост, стражи по точкам вызова).


- WF-контролы (F-01…F-03): `Helpers\WfThemeStyles.cs` — общие примитивы (шрифт Segoe UI вместо Consolas,
  замороженные кисти палитры, маркер «стиль уже наш» + `Wrap()` для сохранения шаблонов Toolbox через
  `BasedOn`). `Helpers\WfVisualTreeThemer.cs` берёт цвета только из `StudioPalette` (акцент
  `MaterialThemePalette` в WF больше не используется), ставит `ItemContainerStyle` с триггерами
  `IsMouseOver` → `Hover` и `IsSelected` → `Selected` — **только если стили элементов не выбирает
  `ItemContainerStyleSelector`**, — и умеет `ApplyWhenLoaded()` (дерево `OutlineView` строится
  шаблоном при загрузке). Выставлять `ItemContainerStyle` поверх селектора запрещено: в стиле
  активности `ToolboxControl` лежит `EventSetter MouseMove → OnToolMouseMove` (вызов
  `DragDrop.DoDragDrop`), и подмена стиля молча отключала перетаскивание активностей в дизайнер
  (категория оставалась видимой, поэтому баг выглядел как «перестало перетаскиваться»).
  Страж — `AiRpaStudio.Tests\ToolboxDragContractTests.cs` (стили WF не подменяются, `EventSetter`
  `MouseMove`/`MouseDoubleClick` на месте, `SelectedTool` появляется при выделении).
  Светлые плашки шаблонов WF (регресс «белый фон под названием категории в панели Активности») гасятся
  не кистями системных цветов, а проходом по эффективным значениям — `WfThemeStyles.NeutralizeLightBrushes`
  (площади → `Surface`, линии → `Transparent`, тёмный текст → `Foreground`, стрелка экспандера →
  `ForegroundMuted`; кисти предыдущей темы пересчитываются по токену палитры: цвет зашит в WF-стиль,
  и `SystemColors.*BrushKey` на него не влияют). Состояния элементов с WF-стилем ведёт
  `WfVisualTreeThemer.HookItemStates` (Hover/Selected палитры вместо системных `#FFE8A6`/`#E5C365`),
  плашка шаблона перепривязывается к `Background` элемента, а контейнеры, созданные после темизации
  (разворачивание категории, «Развернуть всё»), достраивает `ToolboxPanel.HookContainers` по
  `ItemContainerGenerator.StatusChanged` и `TreeViewItem.ExpandedEvent`. Стражи —
  `AiRpaStudio.Tests\ToolboxThemeTests.cs`. Обход дерева — только через `XamlHelper`
  (`Children`/`FindVisualChildren`): в `WfVisualTreeThemer` `VisualTreeHelper.GetChildrenCount` запрещён.
  `Helpers\PropertyWindowStyleHelper.cs` ищет элементы по именам
  (`_propertyToolBar`, `PART_ByCategoryViewButton`, `PART_AlphaViewButton`, `PART_SearchClearButton`),
  состояния кнопок — триггеры стиля (не обработчики), повторное применение перекрашивает контрол без
  пересоздания. Тесты: `AiRpaStudio.Tests\WfThemeTests.cs`.
- Плоские кнопки окна «Свойства» (правка F-03). `PropertyWindowStyleHelper.CreateIconHost` принимает
  `iconSize`/`framed`: у переключателей «По категориям»/«Алфавитный» `CornerRadius = 0` (hairline-рамка
  `Separator` осталась, кнопка 28×28 и глиф 20 px не менялись), у кнопки очистки поиска рамки нет вовсе
  (`framed: false`), а крестик уменьшен в 1,5 раза — `ClearIconSize = 11` вместо 16 при том же размере
  кнопки `ClearButtonSize` 24×24. Кнопка «…» строк свойств (WF `EditModeSwitchButton`) полностью плоская:
  `ApplyEditModeButtons` → `MakeFlatButtonChrome` ставит прозрачные `Background`/`BorderBrush`,
  `BorderThickness = 0`, `Effect = null` и обнуляет `CornerRadius`/рамку/фон у `Border` шаблона; если
  хромировку рисует системный `ButtonChrome` (Border-потомка нет) — ставится плоский шаблон на
  `ContentPresenter`. Глиф «…» не теряется: WF отдаёт его через `Content` (триггер
  `TargetEditMode = Dialog` → `Content = "..."`, проверено по `stylescore.editmodeswitchbutton.baml`).
  Ховер-плашки у «…» больше нет — это следствие «плоскости». Проход идемпотентен, строки, созданные
  после выбора другой активности, подхватывает тот же `Attach`/`LayoutUpdated`. Тесты:
  `WfThemeTests.PropertyHelper_StylesButtonsByName`, `.PropertyHelper_SearchClearButton_IsFramelessWithSmallerGlyph`,
  `.EditModeButton_IsFlatWithoutCornerRadiusAndShadow`,
  `.EditModeButton_WithoutBorderChrome_GetsFlatTemplateAndKeepsGlyph`, `.PropertyHelper_FlattensEditModeButtons`.
- Поверхность дизайнера WF (F-04…F-06): `Helpers\WfDesignerTheme.cs` — объявляет ключи
  `DesignerViewBackground`/`DesignerViewForeground` (на первый ссылается
  `CSharpExpressionEditor\RoslynExpressionEditor.xaml`) и ключи шаблонов WF (поверхность, элементы
  активности, shell bar, breadcrumb, MiniMap, контекстные меню, `PropertyInspector` — имена берутся из
  публичных `WorkflowDesignerColors.*Key`). Словарь палитры кладётся и в `Application.Resources`, и в
  `DesignerView.Resources` (локальные `ColorResources.xaml` дизайнера перекрывают приложение) и
  мутируется на месте при смене темы. Кисти, пришедшие через `{StaticResource}`, заменяются по точному
  значению дефолта WF; пользовательские кисти не трогаются. Адаптер ошибки — `Validation.ErrorTemplate`
  с hairline `CloseHoverBackground` (красный только для ошибок). Контекстное меню дизайнера —
  `WfDesignerTheme.CreateEditingContextMenu` (им пользуется `DesignerVariableContextMenuService`).
  Оформление меню вынесено в `Helpers\WfContextMenuTheme.cs`: меню — **popup**, а не потомок
  `DesignerView`, поэтому проход темы по дереву дизайнера до него не доходит ни разу, а локальных
  `Background`/`Foreground` недостаточно — рисует чужой шаблон (`MaterialDesignContextMenu`/
  `MaterialDesignMenuItem` из `App.xaml`, в чистом процессе Aero2), и его подсветка строится от
  `Foreground` (белые буквы на светлой накладке). `WfContextMenuTheme.Apply` ставит собственные
  `Template` меню, `ItemsPanel` и `ItemContainerStyle` пунктов (рамка палитры, `Hover`, `ForegroundMuted`
  для недоступного пункта, `PART_Popup` подменю), а `Hook()` — общий проход по `ContextMenu.OpenedEvent`
  для меню самого дизайнера (по `Loaded` popup-ребёнку обработчик класса не доставляется). Проход
  ограничен цепочкой родителей/`PlacementTarget` до `DesignerView` и `System.Activities.Presentation`,
  а палитру, заданную фабрикой, не перебивает. Отдельно меню правого клика темизируется **до показа** —
  `ContextMenuService.ContextMenuOpeningEvent` (`PrepareOpen`): иначе генератор контейнеров падает на
  разделителе, если стиль пунктов ещё чужой (`Стиль, заданный для типа "MenuItem", не может применяться
  к типу "Separator"`), поэтому стиль раздаётся `ItemContainerStyleSelector`, а `Separator` получает
  свой hairline-стиль. Стражи — `AiRpaStudio.Tests\WfContextMenuThemeTests.cs`.
  Механизм этого меню зафиксирован документально (`docs\Designer_Variable_ContextMenu_Principles.md`):
  правый клик в ячейке `variableDataGrid`/`argumentsDataGrid` открывает меню программно (штатное меню
  завершает правку и закрывает панель), а завершение правки отменяется флагом, взведённым на
  `DataGrid.PreviewMouseRightButtonDown`. Инварианты — порядок туннелирования, отмена consume-once,
  `RecolorEditingContextMenu` на каждое открытие, снятие глобальных хуков в `Closed`, все обработчики
  на `DataGrid` (в `CellEditingTemplate` и его `Resources` писать нельзя — шаблоны запечатаны,
  дефект 1.0.0.0 «ResourceDictionary доступен только для чтения»; палитра ставится полю ввода
  локальными значениями в `PreparingCellForEdit`); решения вынесены в
  `Core\Services\DesignerVariableContextMenuPolicy.cs` и
  `DesignerContextMenuState.cs`. Стражи — `AiRpaStudio.Tests\DesignerVariableContextMenuTests.cs` и
  `DesignerVariableContextMenuPolicyTests.cs`.
  Ручной чеклист приёмки и карта тестов — разделы «Ручной чеклист приёмки» и «Тесты» этого документа.
  Вызовы: `DocGroupHelper.DesignerView_Loaded`, `MainView.ReapplyWorkflowControlThemes`,
  `App.Application_Startup`, `AppSettings.ApplyTheme`.
  Хлебные крошки (`breadCrumbListBox` — имена активностей `Main › Sequence › Assign`) темизируются
  отдельным проходом `ThemeBreadCrumb`: контейнеры получают `ItemContainerStyle` палитры, содержимое
  уже созданных крошек перекрашивается, а новые (появляются при «провале» в активность) достраиваются
  по `ItemContainerGenerator.StatusChanged`. Светлые кисти внутри крошки гасятся по яркости: цвет
  детей `DataTemplate` (кнопка Aero2, `TextBlock`) приходит из шаблона, а не локально, поэтому прежний
  проход по локальным значениям оставлял белые плашки в тёмной теме. Страж —
  `WfDesignerThemeTests.Apply_BreadCrumbHasNoLightBackgrounds`. «Один белый пиксель» на стыке «крошки/дизайнер» — угловой `Rectangle` **1×1** из шаблона `ScrollViewer` Aero2
  (светло-серый фон темы): при скрытых полосах прокрутки он вырождается в точку в правом нижнем углу
  `ListBox`, а ширина списка равна ширине крошек — поэтому точка «сдвигалась» вместе с последней крошкой.
  Он не разделитель и не шеврон: `WfDesignerTheme.IsTemplateCorner` сливает его с `Surface`
  (`PaintBreadCrumbBand` и проход по содержимому). Стражи —
  `WfDesignerThemeTests.Apply_BreadCrumbJunctionHasNoLightPixel` (полоса рендерится в битмап: в нижних
  строках, где лежит hairline, не должно быть пикселей светлее поверхности) и
  `.Apply_BreadCrumbTemplateCornerDotMergesWithSurface`. Сохраняются и вспомогательные правила: гасятся
  **все** свойства типа `Brush` (`WfDesignerTheme.BrushPropertiesOf`, кисти палитры неприкосновенны),
  полоса обрезает содержимое (`ClipToBounds` + `UseLayoutRounding`), светлая площадь гасится в цвет
  поверхности, а не в `Transparent`, подложка полосы красится `Surface` (`PaintBreadCrumbBand`), элементы,
  созданные после применения темы, достраиваются по `ItemContainerGenerator.StatusChanged` и
  `LayoutUpdated`. Стражи — `WfDesignerThemeTests.Apply_BreadCrumbHasNoLightBrushAnywhere`,
  `.Apply_BreadCrumbClipsArtifactsOverflowingOnTheDivider`,
  `.Apply_BreadCrumbArtifactsCreatedAfterApplyAreNeutralized`,
  `.Apply_BreadCrumbBandStaysOpaqueSurfaceUnderWhiteHost`,
  `.Apply_BreadCrumbBackdropBehindNamesIsSurface`.
  Активности (плита, рамка, подпись, контролы внутри) темизирует отдельный проход
  `Helpers\WfActivityTheme.cs`. Цвета активности берутся из **WF-словаря** `ColorResources.xaml`, который
  WF подмешивает в `ControlTemplate.Resources` **своего** шаблона
  (`WorkflowElementDesignerDefaultTemplate.xaml`), поэтому ключи `WorkflowViewElementBackground` /
  `DesignerViewBackground` из `WfDesignerTheme` до активностей не доходят: плита оставалась белой, плашка
  заголовка `#E9ECEE`, рамка `#D0D7E2`. Кисти WF-словаря заморожены, а сам словарь после загрузки шаблонов
  становится read-only (`InvalidOperationException` на записи) — поэтому тема ставится **локальными
  значениями** на элементы шаблона: плита → `Surface`, полоса заголовка → `SurfaceAlt`,
  рамка и линии → `SeparatorStrong`, глифы → `ForegroundMuted`, буквы → `Foreground`, а `RestoreStale`
  переносит кисти предыдущей темы (иначе возврат к светлой оставил бы тёмные плашки). Активности, созданные
  позже (перетаскивание из панели, «провал», отмена), достраиваются по `LayoutUpdated` (`Marks` + `IsStale`:
  один проход на цикл раскладки, повторный проход идемпотентен). Привязки модели не разрываются
  (`BindingOperations.IsDataBound`), поэтому общий проход по `TextBlock` в `WfDesignerTheme.Apply` больше не
  перезаписывает привязанный `Foreground`.
  Неявные стили MaterialDesign из `App.xaml` внутри активностей и в шапке дизайнера гасятся пустыми
  стилями-заглушками (`UseNativeControlStyles`, список `NativeStyleTypes`). Регресс, ради которого это
  сделано: `CheckBox` из `ActivityTop` получал стиль MaterialDesign 29×29, `Button#AnnotationIndicator` —
  шрифт 14, кнопки «Развернуть все»/«Свернуть все» — `Width = 34`, из-за чего подпись не влезала. Форма и
  шрифт контролов остаются нативными (WF/Aero2), цвета ставит темизатор; `ScrollBar` в списке заглушек нет
  намеренно (плоские скроллы 10 px задаёт `Themes\StudioScrollBar.xaml`). Подпись активности в `ActivityTop`
  больше не красится конвертером: цвет даёт палитра, выключенное состояние показывается `Opacity = 0.55`, а
  сам `BoolToColorConverter` берёт `Studio.Foreground`/`Studio.ForegroundMuted` по строковым ключам (сборки
  активностей не ссылаются на `RPA.Helpers`). Проверка на настоящем `WorkflowDesigner` — пробник
  `tools\designer-ui-diagnostics` (проход «DARK (первое включение)»: `фон чужой поверхности` = 0 и для
  пустой активности, и для workflow с вложенными активностями, и для кастомного дизайнера
  `Activities.Debug.Logger`). Стражи: `AiRpaStudio.Tests\WfActivityThemeTests.cs`.
  Тесты: `AiRpaStudio.Tests\WfDesignerThemeTests.cs`.
  Комбобоксы WF-поверхностей (в дизайнере — масштаб в shell bar, `zoomPicker`) темизируются
  `WfVisualTreeThemer.ThemeComboBoxes` (вызывается из `WfVisualTreeThemer.Apply` и из
  `WfDesignerTheme.ThemeShellBar` — путь `TryApplyShellBarStyle` полного обхода не делает): кисти,
  шрифт и `ItemContainerStyle` палитры, гашение белых плашек шаблона Aero2, `ForegroundMuted` для
  стрелки раскрытия. Причина: WF-стиль `statusBarComboBoxStyle` задаёт только шрифт/`Foreground`/
  прозрачный фон, а шаблон Aero2 рисует под текстом опаковую белую плашку
  (`Border#templateRoot` кнопки раскрытия и `Border#Border` редактируемой части), пока `Foreground`
  наследуется от shell bar — в тёмной теме это белые буквы на белом фоне. Элементы списка получают
  собственный шаблон (в Aero2 подсветку рисует `Border#Bd` светлыми плашками триггерами шаблона),
  а список раскрывается в popup вне визуального дерева комбобокса — он темизируется по
  `DropDownOpened` с отложенным повтором (шаблон popup и полосы прокрутки применяются при раскладке).
  Белые плашки гасит `PaintTemplatePlates`, а не общий `NeutralizeLightBrushes`: чистый белый —
  токен палитры (`CloseHoverForeground`), поэтому здесь цвет сопоставляется с токенами прежней темы.
  Стили, созданные темизатором, помнят палитру (`WfThemeStyles.MarkThemed(style, palette)`): при смене
  темы обёртка пересоздаётся от исходного стиля WF, а не от стиля прежней темы. Стражи —
  `WfThemeTests.Apply_ThemesComboBoxes`, `.Apply_ThemesComboBoxItems`, `.Apply_MakesComboBoxGlyphVisible`,
  `.Apply_ComboBoxThemeIsIdempotent`, `.Apply_SwitchesComboBoxPalette`,
  `WfDesignerThemeTests.Apply_ThemesShellBarZoomCombo`, `.Apply_ShellBarZoomComboIsReadableInBothThemes`,
  `.Apply_ThemesShellBarZoomComboList`.
  Тот же механизм обслуживает комбобоксы таблиц (столбцы `Scope`/`Modifiers` — `DataGridComboBoxColumn`)
  и строк панели «Свойства» (enum-свойства): `WfDataGridTheme.Refresh` вызывает `ThemeComboBoxes`
  последним проходом, `WfDataGridTheme.HookGrid` ставит `PreparingCellForEdit` (палитра ставится до
  первой отрисовки, а не следующим циклом раскладки), `PropertyWindowStyleHelper.ApplyValueEditors`
  отдаёт комбобоксы строк тому же механизму, а `WfVisualTreeThemer.ApplyEditorPalette` — общий вход для
  редактора ячейки (поля ввода + комбобоксы) из таблицы и из `DesignerVariableContextMenuService`.
  Прежние самописные обходы `ComboBox`/`ComboBoxItem` внутри инспектора были мёртвым кодом: элементов
  списка в его визуальном дереве нет — список живёт в popup. Подписка на `Loaded` в `ThemeComboBoxes`
  ставится всегда (в shell bar к моменту применения темы `zoomPicker` уже есть, поэтому прежнее условие
  «комбобоксов нет» не выполнялось никогда, и позже созданные комбобоксы не темизировались). Стражи —
  `WfDesignerTableThemeTests.Apply_LateCreatedCellComboBoxIsThemed`,
  `.Apply_CellComboBoxItemStyleUsesPalette`, `.Sources_TableThemeThemesComboBoxes`,
  `WfThemeTests.SetPropertyStyles_ThemesInspectorComboBoxes`,
  `.SetPropertyStyles_SwitchesInspectorComboBoxPalette`,
  `DesignerVariableContextMenuTests.PreparingCellForEdit_ComboBoxGetsPalette`.

  Панель «Свойства активности» (`DesignTimeVariable`: строки `Modifiers` и `Scope`) потребовала второго
  захода: каркас панели темизировался, а до комбобоксов строк проход не доезжал — WF создаёт строки
  позже (выбор активности, разворот категории), и `ThemeComboBox` срабатывал до реализации шаблона, когда
  внутреннего поля правки `PART_EditableTextBox` ещё нет (отсюда белое поле в тёмной теме), а элементы
  popup (у `Modifiers` это чекбоксы `ReadOnly`/`Mapped`) получали системный чёрный текст. Теперь
  `ThemeComboBox` вызывает `comboBox.ApplyTemplate()` до покраски, `PaintItemForeground` локально красит
  текст popup (включая привязки, которые `NeutralizeLightBrushes` пропускает), подписки
  `ItemContainerGenerator.StatusChanged` и `IsKeyboardFocusWithinChanged` доводят список и режим правки, а
  `PropertyWindowStyleHelper.Attach` вешает доводку комбобоксов на `PreviewMouseDown`/`GotKeyboardFocus`
  панели. Стражи — `WfDesignerTableThemeTests.ThemeComboBoxes_PaintsEditableFieldOfCollapsedComboBox`,
  `.ThemeComboBoxes_PaintsDropDownCheckBoxTextWithPalette`,
  `.SetPropertyStyles_ThemesComboBoxOfRowCreatedAfterSweep`.

  Третье дополнение (WF создаёт редактор значения уже после прохода темы): на запущенной студии левый край
  редакторов `Modifiers` (`FlagEditor`, `autoId=ComboBox_1`) и `Scope` (`autoId=scopeEditoComboBox`) при
  выборе переменной был `#FFFFFF`/`#EFEFEF` до первого клика и становился `#272727` после него. Причина:
  `ThemeComboBoxes` к этому моменту уже прошёл, а WF подставляет редактор значения позже, и отпечаток
  дерева инспектора (`PropertyWindowStyleHelper.ComputeSignature`) при этом не меняется. Решение:
  `WfVisualTreeThemer.HookLayoutSweep` — по `LayoutUpdated` корня ищутся комбобоксы без отметки палитры
  (`ComboBoxMarks`) и темизируются только они (отложенно, флаг `Pending` — одна проверка на цикл
  раскладки), плюс `ScheduleComboBoxRetry` — один повторный проход, если шаблон ещё не материализован
  (`Template == null` или нет `PART_EditableTextBox` при `IsEditable`), с пометкой на палитру от
  бесконечной цепочки. Стражи —
  `WfDesignerTableThemeTests.ThemeComboBoxes_ComboBoxCreatedAfterSweep_IsThemedOnLoad`,
  `.ThemeComboBoxes_ComboBoxOutsideThemedRootsKeepsSystemChrome`,
  `.Sources_ThemerThemesLateCreatedComboBoxesOnLayout`.

  Панели «Переменные», «Аргументы», «Пространства имён» (вкладки shell bar, таблицы и их контекстное
  меню) темизирует `Helpers\WfDataGridTheme.cs` (F-04). Причина отдельного прохода: WF объявляет таблицы
  стилем `defaultDataGridStyle`, который берёт `{StaticResource}` на дефолтные цвета WF
  (`GridViewRowUnderlineColor`, `GridViewRowSelectedColor`, `GridViewRowHoverColor`) — `StaticResource`
  не пересчитывается, поэтому шапка, сетка и выделение оставались светлыми; плашки шапки, угла
  «выделить всё» и строк рисует шаблон Aero2, а триггер шаблона сильнее сеттера и триггера стиля.
  Отсюда: стили шапки/строки/ячейки — обёртки стилей WF (`WfThemeStyles.Wrap`, шаблон WF сохраняется в
  `BasedOn`), цвет материализованных контейнеров ставится локально через `WfThemeStyles.WatchState`,
  светлые кисти гасит `NeutralizeLightBrushes`, проход повторяется по
  `ItemContainerGenerator.StatusChanged` и отпечатку `LayoutUpdated` (строки виртуализованы), а
  панель-контейнер получает непрозрачный `Surface`. Вкладки shell bar — `ThemeShellBarItems` (активная —
  `Selected`, hover — `Hover`, неактивная подпись — `ForegroundMuted`). Контекстное меню обслуживает
  **все** таблицы дизайнера: `DesignerVariableContextMenuService.RefreshTables` ищет их по типу
  (`WfDataGridTheme.FindTables`), а не по именам панелей, поэтому «Пространства имён»
  (`importedNamespacesDataGrid`) больше не остаются без меню. Стражи —
  `AiRpaStudio.Tests\WfDesignerTableThemeTests.cs`, `DesignerVariableContextMenuTests.RefreshTables_*`,
  `Sources_TableTheme*`.

- Сторонние визуальные сборки (этап 8): `LogControlLibrary` и `RPA.Chat` не ссылаются на `RPA.Helpers`,
  поэтому палитра приходит в них строковыми ключами.
  * Панель «Log» (`LogControlLibrary\Controls\LogViewerControl.xaml`) использует `{DynamicResource Studio.*}`
    (ключи пишет `StudioBrushes.ApplyToApplication`), а цвета уровней — семантика в конвертерах
    (`LogLevelToColorConverter`/`...HoverConverter`/`...BackgroundConverter`): средние тона и
    полупрозрачные накладки, чтобы читались и в тёмной, и в светлой теме.
  * Чат (`RPA.Chat`): все цвета — только в `Themes\ChatTheme.Dark.xaml` / `ChatTheme.Light.xaml`
    (ни одного `<SolidColorBrush>` в контролах, иначе переключение темы не доходит до элемента).
    В студии словарь на лету перекрывает мост `RPA.Chat\Themes\ChatStudioBridge.cs`: таблица
    «ключ чата → ключ `Studio.*`» + `Apply(ResourceDictionary, ResourceDictionary)` (зовётся из
    `ChatPanel.ApplyTheme`), без ресурсов студии мост ничего не меняет. Акцентных цветов нет:
    `Dark.Accent`/`Dark.Primary`/`Dark.ControlPressed` — нейтральные `Selected`/`Foreground`,
    режимы Plan/Act/Help различаются яркостью серого в `ChatViewModel`.
    Тесты: `AiRpaStudio.Tests\ChatThemeBridgeTests.cs`, `LogAndChatThemeTests.cs`.
  * Порядок смены темы (обязателен, иначе чат отстаёт на один переключатель): `ChatViewModel.IsDarkTheme`
    сначала вызывает хук `ThemeApplying` (на него подписана студия в `MainView.InitializeControls`:
    `Services.AppSettings.ApplyTheme`), который пересобирает палитру `Studio.*`, и только затем событие
    `ThemeChanged` → `ChatPanel.ApplyTheme` перекрывает словарь чата мостом. Повторный `ApplyTheme` в
    `MainView.OnChatThemeChanged` не нужен — он красил бы студию дважды за клик. Если палитра `Studio.*`
    изменилась уже после создания контрола (сохранённая светлая тема на старте), вызывается
    `ChatPanel.ReapplyTheme()`. Стражи: `ThemeAcceptanceTests.MainView_AppliesPaletteBeforeTheChatSwapsItsDictionary`,
    `ChatThemeBridgeTests.ThemeApplying_RunsBeforeThemeChanged_SoThePaletteIsFresh`.

- Заголовок окон: все окна студии рисуют его сами — `Controls\StudioTitleBar.xaml` (высота 32,
  `Studio.TitleBarBackground`/`TitleBarForeground`, кнопки «свернуть»/«закрыть» 46×32 на `MaterialIcon`
  16×16, hover закрытия `Studio.CloseHoverBackground`, перетаскивание `DragMove()`, DP `ShowMinimize`/
  `ShowClose`). Системный caption Windows не используется: он белый в любой теме и не скругляется.
  Окно с `StudioTitleBar` обязано иметь `WindowChrome` (`CaptionHeight="0"`, `GlassFrameThickness="0"`,
  `CornerRadius="8"`) — иначе `WindowCorner.ApplyCornerRadiusFallback` не скруглит его на Windows 10.
  Заголовок полосы берётся из `Title` окна (`RelativeSource AncestorType=Window`), поэтому `ErrorDialog`
  может менять его без DataContext. Тесты: `AiRpaStudio.Tests\StudioTitleBarTests.cs`.

- Диалоги студии («Создать активность», «Создать проект», «Открыть VS-проект», «Из VS-шаблона») задают
  `Background="{DynamicResource Studio.Surface}"` + `Foreground="{DynamicResource Studio.Foreground}"`,
  внешний отступ 12, кнопку обзора 28px; `Grid.Row` обязан существовать среди `RowDefinition`.
  Базовая тема приложения — `Dark`, а `AppSettings.ApplyMaterialDesignTheme` создаёт тему MaterialDesign
  нейтральными `Primary`/`Secondary` из палитры (акцент в фокус и скроллбары не пробивается).
  Тесты: `AiRpaStudio.Tests\StudioDialogWindowTests.cs`.



- Нативные WF-контролы (`ToolboxControl`, `OutlineView`, `PropertyInspectorView`) стилизуются
  обходом визуального дерева; исходники для изучения их шаблонов:
  `C:\Users\Maxxx\source\repos\.NET-Framework-4.8-RTM-Reference-Source`
  (детали — `AiRpaStudio\docs\Reference_Source_Storage.md`).

- Финальные правила темы (этап 9, чеклист — `AiRpaStudio\docs\Vs2026_Theme_Acceptance.md`):
  * В темизируемых XAML (`Views`, `Controls`, `CSharpExpressionEditor`, `Themes\StudioTheme.xaml`,
    `AvalonDock.Themes.Metro\Theme.xaml`, `AvalonDock\Themes\generic.xaml`, `LogControlLibrary`, `RPA.Chat`)
    запрещены зашитые hex и named-цвета (`White`, `Gray`, `Orange`, …): только `{DynamicResource Studio.*}`.
    Единственные исключения — `#E81123`/`#FFFFFF` кнопки закрытия в `Themes\StudioTheme.xaml`.
  * В студии запрещён `MaterialDesign.Brush.*`: значения берутся из `StudioPalette`
    (`MaterialDesign` остаётся только базой контролов, его `Primary`/`Secondary` нейтральны, см. M-04).
  * Семантика (не акцент!) — `StudioBrushes.SemanticKeys`: `Studio.Semantic.Run`/`Pause`/`Stop`
    (тулбар) и `Studio.Semantic.FolderIcon`/`XamlFileIcon`/`JsonFileIcon` (дерево проекта). Значения
    фиксированы для обеих тем (`chroma > 6`), создаются кодом; декоративные иконки должны использовать
    `Studio.ToolbarForeground`, а не цвет.
  * «Третьих» палитр нет: `Themes\StudioTheme.Dark.xaml`, `Themes\StudioTheme.Light.xaml` и
    `AvalonDock.Themes.Metro\Brushes.xaml` удалены (содержали прежние фиолетовые/светлые значения).
    Fallback-кисти `MetroTheme.GetResourceDictionary()` нейтральны (метод `Neutral`), акцент оттуда убран.
  * Тени (`DropShadowEffect`) в popup не используются: границу даёт hairline (`Studio.Separator`),
    скругление popup/меню — 6.
  * Скроллы — плоские, 10px, без стрелок: неявный стиль `ScrollBar` в `AiRpaStudio\Themes\StudioScrollBar.xaml`
    (`Width/MinWidth/MaxWidth = 10`, у горизонтального — `Height/MinHeight/MaxHeight = 10`, бегунок
    `Studio.SeparatorStrong` → hover `Studio.ForegroundMuted` → drag `Studio.Foreground`, скругление 5).
    Словарь обязан подключаться в `App.xaml` ПОСЛЕДНИМ (после `MaterialDesign2.Defaults.xaml` и
    `Themes\StudioTheme.xaml`), иначе неявный стиль MaterialDesign побеждает по порядку словарей;
    `RepeatButton`/`Thumb` внутри шаблона задают `Style` явно с `OverridesDefaultStyle=True`, иначе их
    раздувают неявные стили MaterialDesign. Чат (`RPA.Chat`) держит свои копии размеров 10px — он не
    ссылается на `RPA.Helpers`. Страж — `AiRpaStudio.Tests\StudioScrollBarTests.cs` (включая runtime-проверку
    реального `ScrollViewer`: 10px по обеим ориентациям).
  * Внутренние `ToggleButton` в шаблонах (`OutlinedComboBoxStyle`, `ReasoningExpanderStyle`) обязаны задавать
    `Style` явно: неявный стиль MaterialDesign для `ToggleButton` — это переключатель с фиксированной
    `Width = 34`, из-за него «коробка» списка в настройках агента сжималась по горизонтали, хотя сам
    `ComboBox` растянут по столбцу. Страж — `StudioScrollBarTests.OutlinedComboBox_InnerToggleButtonHasExplicitStyle`.
  * Каждая докпанель обведена рамкой 1px (`AvalonDock_Separator`, у активной — `AvalonDock_SeparatorStrong`):
    панель главного окна, панель документов, панель автоскрытия и панели внутри откреплённых окон. Рамку
    рисует ровно один элемент (в плавающем окне — `WindowBorder`, иначе панель), ресайзер в покое линии не
    рисует (`Transparent`), активность заливкой не передаётся. Правило 6 плана `docs\Vs2026_Theme_Plan.md`,
    страж — `ThemeXamlTests.ThemeXaml_PaneFramesAreOnePixel`.
  * `App.xaml` не объявляет ресурсы через `{DynamicResource ...}` и не держит `Freezable` (`DrawingImage`
    и т.п.): в `Application.Resources` такая запись остаётся `ResourceReferenceExpression` (не `Brush`),
    заморозить `Freezable` нельзя, а Style-значение валит `XamlParseException`. Кисти здесь создаёт
    `StudioBrushes.ApplyToApplication()`. Страж — `ThemeAcceptanceTests.App_HasNoDynamicResourceLookupsInResourceValues`
    (регрессия этапа 9: мёртвый ресурс `Bulb`).
  * Шеврон панели документов и меню списка открытых документов (P-09): шеврон (`MenuDropDownButton`)
    отступает от правой кромки панели на 3 px (`Margin="0,0,3,0"`), а меню и пункты рисуют **собственные**
    шаблоны темы (`AvalonDock_ThemeMetro_DocumentPaneContextMenuStyle` /
    `AvalonDock_ThemeMetro_DocumentPaneMenuItemStyle`). Иначе работает неявный `MenuItem` MaterialDesign с
    `Padding="24,0,24,0"` и колонкой иконки — между кромкой меню и названием документа оставалась широкая
    пустая полоса. Инвариант: **10 px от внешней кромки меню до начала текста** = 1 px рамки меню +
    9 px `Padding` пункта (паддинг самого меню — 0). Менять эти два числа можно только вместе.
    Стражи — `ThemeXamlTests.ThemeXaml_DocumentPaneChevronHasThreePixelRightInset`,
    `ThemeXaml_DocumentPaneMenuUsesOwnTemplates`,
    `ThemeXaml_DocumentPaneMenuTextStartsTenPixelsFromOuterEdge` (сумма сеттеров) и
    `DocumentPaneMenu_TextStartsTenPixelsFromOuterEdge` (материализация: замер `TransformToAncestor`).
  * Кнопки «Развернуть все»/«Свернуть все» в шапке дизайнера WF (P-10): WF-шаблон
    (`ExpandAllCollapseAllTemplate`) своей ширины не задаёт, поэтому её задаёт неявный `ToggleButton`
    MaterialDesign — `Width = 34` (та же причина, что у «коробки» `OutlinedComboBoxStyle`). Подпись
    «Развернуть все» ≈90 px в такую кнопку не влезает и обрезалась правой кромкой панели — в шапке
    читались только «разв» и «свер». `WfDesignerTheme.ThemeHeaderButtons` (шаг 4б `Apply`) снимает
    `Width` (`NaN`) и `MaxWidth`, ставит `MinWidth` по `FormattedText` ширине подписи + отступы подписи,
    паддинг и рамку кнопки, а правой кнопке пары — `Margin.Right = 5` (сдвигается вся пара, зазор между
    кнопками не растёт). Проход идемпотентен (`Math.Max`), поэтому смена темы не накапливает отступ.
    Стражи — `WfDesignerThemeTests.Apply_HeaderButtonsFitTheirCaptions`,
    `.Apply_HeaderButtonsKeepFivePixelRightInset`; живая проверка — отчёт
    `tools\designer-ui-diagnostics` (`ReportHeaderButtons`, печатает ширину кнопки и «влезает=да/НЕТ»).
  Тесты: `AiRpaStudio.Tests\ThemeAcceptanceTests.cs` (сквозные стражи §9) + стражи этапов 1-8.

## Шаблон MVVM проекта AiRpaStudio (FW.EasyMvvm) — обязательное требование

Проект `AiRpaStudio` использует НЕ классический MVVM, а «модель-центричный» вариант
библиотеки `FW.EasyMvvm` (namespace `FW.EasyMvvm.Core`, проект `FW.EasyMvvm`),
подключённой в `AiRpaStudio.csproj`. В репозитории есть почти идентичная библиотека
`EasyMvvm` (используется в `ProxyChecker`) — в AiRpaStudio применять ТОЛЬКО `FW.EasyMvvm`.

### Распределение ответственности
- Model (`BaseModel`) — главный слой: данные (`INotifyPropertyChanged`) + бизнес-логика.
  Команды — обычные публичные методы модели (`CmdWorkflowSave()`, `CreateActivity()`,
  `Settings()`).
- ViewModel (`BaseViewModel`) — тонкий адаптер: `Model` + `View`, pass-through свойства,
  без бизнес-логики; настройка в `InitializeViewModel()`.
- View (`IView`) — `MetroWindow`/контрол с `Show`, `ShowDialog`, `OnSendMessage`, `Close`.

### Команды отправляются особым образом (по имени метода, через reflection)
- В XAML одна команда `Command="{Binding Cmd}"`, а `CommandParameter` — строка с именем
  публичного метода модели: `CommandParameter="CmdWorkflowSave"`.
- Цепочка: `RelayCommand.Execute` → `BaseViewModel.SendCommand` → `BaseModel.SendCommand`
  → `GetType().GetMethod(name)?.Invoke(this, args)`.
- Команда с параметрами: `CommandParameter` / `Cmd.Execute(...)` передаётся как
  `object[]`/`List<object>` вида `[имяМетода, arg1, arg2, arg3]` (до 3 аргументов),
  например `dc.Cmd?.Execute(new object[] { "CmdAddNewActivity", newFilePath });`.
- Правило: новое действие = публичный метод модели, имя совпадает со строкой
  `CommandParameter`; отдельные `ICommand`-свойства на ViewModel НЕ создаются.

### Сборка окна и жизненный цикл — только через `MvvmFactory`
- `MvvmFactory.CreateWindow(model, viewModel, view, viewMode, childModels)`:
  `model.Run()` → `viewModel.SetModel()` (подписка `SendCommand += model.SendCommand`,
  `Model.ViewModel = this`, `InitializeViewModel()`) → `view.DataContext = viewModel` →
  `model.SendMessage += view.OnSendMessage` → `view.Loaded += model.Loaded()` →
  регистрация дочерних моделей `RegisterModel` → `Show`/`ShowDialog` (`ViewMode`).
- Новые окна открываются из методов модели через `MvvmFactory.CreateWindow(...)`.
- Синглтоны-модели (`CustomWfDesigner.GetInstance()`, `ProjectManager.GetInstance()`,
  `AppSettings.GetInstance()`) подключаются через `childModels`/`RegisterModel` (см. `EntryPoint.cs`).

### Уведомления
- `BaseModel.OnPropertyChanged()` поднимает `PropertyChanged` и на модели, и на ViewModel —
  имена pass-through свойств ViewModel обязаны совпадать с именами свойств модели.
- Сообщения «модель → представление»: `BaseModel.OnSendMessage(string)` →
  `IView.OnSendMessage(string)` (обычно MahApps `ShowMessageAsync`), а не `MessageBox`.

## Состояние вкладок, валидация WF и окна WF (три регресса одной задачи)
- **Состояние вкладок** хранится в одном месте — `AiRpaStudio\Core\WorkflowSessionState.cs`
  (формат: `{version, project, active, documents[{path, kind}]}` в настройке
  `Properties.Settings.Default.DesignerFiles`; старый JSON-массив путей читается как fallback).
  Владелец сохранения/восстановления — `Main.SaveSessionState` / `Main.RestoreSessionState`
  (`AiRpaStudio\Models\Main.cs`): подписка на `CustomWfDesigner.WorkflowDesigners.CollectionChanged` и
  `Main.OpenCodeDocuments.CollectionChanged` живёт в конструкторе `Main`, финальная запись —
  `MainView.OnClosing`. Восстанавливают **оба** режима проекта (project.json и .sln/.csproj).
  Запрещено возвращать сохранение в подписку внутри `CustomWfDesigner.LoadProject` (именно из-за этого
  в VS-проекте вкладки не сохранялись) и писать настройку из двух мест. На время восстановления
  `_isRestoringSession` глушит запись: промежуточное состояние затирало список вкладок.
  Стражи: `AiRpaStudio.Tests\WorkflowSessionStateTests.cs`, `StudioSessionWiringTests.cs`.
- **Иконка валидации на активности** рисуется WF (триггеры `ModelItem.ValidationState` у
  `Border x:Name="validationVisuals"`). Студия (`DesignerValidator`) ставит свои значения **только**
  когда есть её собственные ошибки (выражения, типы аргументов), иначе обязана вызвать
  `ClearValue(VisibilityProperty/BackgroundProperty/ToolTipProperty)`: локальное значение сильнее
  триггера шаблона, и `Visibility = Collapsed` гасил нативную красную иконку (баг «ошибки валидации
  исчезают»). Решение и кисть ошибки — `AiRpaStudio\Core\ValidationIconState.cs`: `ErrorIconBrush()`
  отдаёт родной рисунок WF (`DesignerItems.ActivityErrorValidation`, красный круг с белым «!»), а
  сплошная кисть палитры — только резерв: заливка без глифа и была дефектом «красная рамка без иконки».
  Флаг `_isNeedInitValidation` сбрасывается после первого прохода,
  разбор каждой активности — в собственном `try/catch`. Стражи: `AiRpaStudio.Tests\ValidationIconTests.cs`.
- **Бейдж ошибки C#-выражения** (`CSharpExpressionEditor\RoslynExpressionEditor.xaml`) — глиф
  `materialIcons:MaterialIcon` (`Kind="Error"`) в единственном не-нейтральном цвете палитры
  (`Studio.CloseHoverBackground`, `Style x:Key="errorStyle"`); пустая `Rectangle`-рамка запрещена —
  её и видел пользователь как «красная рамка без иконки». Видимость — `HasValidationError`,
  подсказка — `ValidationErrorMessage`. Проверка выражения запускается по изменению текста
  (`IExpressionEditorInstance.TextChanged`) с паузой-дребезгом: политика в
  `AiRpaStudio\Core\ExpressionValidationTiming.cs` (350 мс, резервный фон — 3000 мс вместо прежних
  10 000 мс, из-за которых ошибка появлялась «через десяток секунд»), таймер останавливается в
  `OnEditorClosing`/`OnEditorUnloaded`, подписка снимается вместе с редактором. `ValidateExpression`
  обязана звать `UpdateValidationError(null)` при отсутствии ошибки — иначе исправленное выражение
  оставляло бейдж. У адаптера `Validation.ErrorTemplate` (`WfDesignerTheme.CreateValidationErrorTemplate`)
  кроме hairline-рамки есть значок ошибки. Стражи: `AiRpaStudio.Tests\ErrorBadgeTests.cs`,
  `ExpressionValidationTimingTests.cs`, `ValidationIconGlyphTests.cs`.

- **Окна WF** (`WorkflowElementDialogWindow`, `EditorDialog` — редактор выражений по «…» в окне
  «Свойства») — обычные `Window` с системным caption, белым в любой теме; `WfDesignerTheme` до них не
  достаёт. Тема ставится `AiRpaStudio\Helpers\WfDialogWindowTheme.cs`: `WindowChrome` с
  `CaptionHeight = 0`, ключи палитры через `SetResourceReference`, содержимое оборачивается в `Grid`
  с полосой `StudioTitleBar` 32 px, скругление — `WindowCorner.Enable`. Точка вызова —
  `RoslynExpressionEditor.OnEditorLoadedForSize` (там же, где восстанавливается размер окна).
  Хелпер идемпотентен и никогда не бросает исключений. Стражи:
  `AiRpaStudio.Tests\WfDialogWindowThemeTests.cs`.
