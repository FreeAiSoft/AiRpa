# Приёмка темы Visual Studio 2026 (студия AiRpaStudio)

Ручная часть плана `docs\Vs2026_Theme_Plan.md` §9. Пункты 1-8 — обязательная проверка глазами;
всё, что можно проверить без запуска, автоматизировано в `AiRpaStudio.Tests\ThemeAcceptanceTests.cs`
(сквозные стражи) и в тестах этапов 1-8 (`StudioPaletteTests`, `ThemeXamlTests`, `WindowCornerTests`,
`WfThemeTests`, `WfDesignerThemeTests`, `LogAndChatThemeTests`, `ChatThemeBridgeTests`,
`StudioDialogWindowTests`, `DockingThemeBrushesTests`, `AvalonDockThemeBridgeTests`).

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
Ни один прямоугольник не должен быть светлее `Studio.Surface` (`#272727`) — кроме hairline 1px.

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

Автоматически: `ThemeAcceptanceTests.DialogsAndMainWindow_UseSingleSurfaceBackground`,
`WfDesignerThemeTests` (ключи поверхности, отсутствие акцента в словаре).

### 4. Скруглённые углы: главное окно, четыре диалога, откреплённый документ и панель, Ctrl+Tab

Windows 11 — DWM (`DwmSetWindowAttribute`, 8px); Windows 10 — region через
`WindowChrome.CornerRadius` для окон со своим chrome. Диалоги «Создать активность», «Создать проект»,
«Открыть VS-проект», «Из VS-шаблона» и Ctrl+Tab на Windows 10 остаются прямоугольными — осознанное
ограничение (системный chrome не переопределяется), см. отклонения этапа 5.

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