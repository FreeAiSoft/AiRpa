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
- Кисти AvalonDock: студия пишет нейтральные ключи `AvalonDock_*` кодом
  (`Helpers\DockingThemeBrushes.cs`, единственная точка — `AppSettings.ReapplyDockManagerResources`),
  а в изолированные ресурсы плавающих окон / Ctrl+Tab / оверлея их прокидывает
  `AvalonDock\Themes\ThemeBrushBridge.cs` (вызывается из их `UpdateThemeResources`).
  `AvalonDock_ThemeMetro_BaseColor1..7` — только legacy-алиасы, в шаблонах темы не используются.
  Тесты: `AiRpaStudio.Tests\DockingThemeBrushesTests.cs`, `AvalonDockThemeBridgeTests.cs`,
  `ThemeXamlTests.cs` (регекс-стражи по `AvalonDock.Themes.Metro\Theme.xaml` + полный разбор XAML).
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
  `IsMouseOver` → `Hover` и `IsSelected` → `Selected` и умеет `ApplyWhenLoaded()` (дерево `OutlineView`
  строится шаблоном при загрузке). `Helpers\PropertyWindowStyleHelper.cs` ищет элементы по именам
  (`_propertyToolBar`, `PART_ByCategoryViewButton`, `PART_AlphaViewButton`, `PART_SearchClearButton`),
  состояния кнопок — триггеры стиля (не обработчики), повторное применение перекрашивает контрол без
  пересоздания. Тесты: `AiRpaStudio.Tests\WfThemeTests.cs`.
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
  Вызовы: `DocGroupHelper.DesignerView_Loaded`, `MainView.ReapplyWorkflowControlThemes`,
  `App.Application_Startup`, `AppSettings.ApplyTheme`.
  Тесты: `AiRpaStudio.Tests\WfDesignerThemeTests.cs`.

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

