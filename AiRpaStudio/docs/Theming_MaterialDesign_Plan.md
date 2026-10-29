# План темизации визуальных контролов AiRpaStudio (MaterialDesign.Themes.Wpf)

Цель: применить темы (светлую/тёмную) на базе `MaterialDesign.Themes.Wpf` ко **всем**
визуальным контролам проекта `AiRpaStudio`, включая нативные WF-контролы, пункты меню и текст.

## Реестр визуальных контролов

### Окна/диалоги (`Window`)
| Контрол | Файл |
|---|---|
| Главное окно | `Views\MainView.xaml(.cs)` |
| Создать активность | `Views\ActivityCreatorView.xaml` |
| Создать проект | `Views\ProjectCreatorView.xaml` |
| Открыть VS-проект | `Views\VsProjectOpenView.xaml` |
| Создать из VS-шаблона | `Views\VsTemplateProjectCreatorView.xaml` |

### Кастомные контролы (`UserControl`)
| Контрол | Файл |
|---|---|
| Активности (панель + нативный ToolboxControl) | `Controls\ToolboxPanel.xaml(.cs)` |
| Проекты | `Controls\ProjectManagerControl.xaml(.cs)` |
| Поисковый ComboBox | `Controls\SearchableComboBox.xaml(.cs)` |

### Нативные WF-контролы (стилизуются обходом визуального дерева)
| Контрол | Где создаётся |
|---|---|
| `ToolboxControl` (Активности) | `Helpers\WFToolboxHelper.cs` → `ToolboxPanel.SetToolboxControl` |
| `OutlineView` (Структура) | `MainView.SyncWFControls` |
| `PropertyInspectorView` (Свойства) | `MainView.SyncWFControls` (стилизует `Helpers\PropertyWindowStyleHelper.cs`) |
| `DesignerView` (поверхность дизайнера) | `WorkflowDesigner.View` через `Helpers\DocGroupHelper.cs` |
| `RoslynExpressionEditor` (редактор выражений) | `CSharpExpressionEditor\RoslynExpressionEditor.xaml` |

### Ресурсы стилей
| Ресурс | Файл |
|---|---|
| Кнопки | `Resources\ButtonStyles.xaml` |
| Алиасы тем | `Helpers\StudioBrushes.cs` (создаются в рантайме) + `Themes\StudioTheme.xaml` |
| Старые палитры | `Themes\StudioTheme.Dark.xaml`, `Themes\StudioTheme.Light.xaml` (заменяются) |

### Внешние панели (другие сборки, встроены в главное окно)
`RPA.Chat\ChatPanel`, `RPA.NuGet\NuGetPanel`, `LogControlLibrary\LogViewerControl` —
вне scope «проект AiRpaStudio»; их палитры выравниваются отдельной задачей.

### Не визуальные XAML (не темизируются)
`Activity1.xaml`, `Main.xaml`, `TestProcess\*.xaml`, `VSTemplates\*\*.xaml` — WF-процессы/шаблоны.

## Пошаговый план

### Этап 0. Документация
1. `docs\Reference_Source_Storage.md` — хранилище исходников нативных WF-контролов.
2. Ссылка в `AGENTS.md`.
3. Этот документ.

### Этап 1. База MaterialDesign
1. `App.xaml`: `BundledTheme` + `MaterialDesign2.Defaults.xaml` + `Themes\StudioTheme.xaml`.
2. `Helpers\StudioBrushes.cs`: алиасы `Studio.*` → реальные Brush из ключей MaterialDesign
   (в рантайме; в XAML объявлять `<DynamicResource x:Key=.../>` нельзя — падает `XamlParseException`).
3. `Models\AppSettings.cs`: переключение Light/Dark через `PaletteHelper`; кисти AvalonDock
   Metro берутся из MaterialDesign-палитры.

### Этап 2. Палитра-адаптер (TDD)
1. `Helpers\MaterialThemePalette.cs`: `MaterialThemeColors` + `Resolve(ResourceDictionary)` +
   чистые `Lighten/Darken/ToHex/TryParseHex`.
2. Тесты `AiRpaStudio.Tests\MaterialThemePaletteTests.cs`.

### Этап 3. Окна и диалоги
1. Проверить 4 диалоговых окна: стандартные `TextBox/Button/ComboBox` подхватывают
   MaterialDesign-дефолты; убрать оставшиеся зашитые цвета.
2. `MainView.xaml`: перевести оставшиеся зашитые цвета и шаблоны `MenuItem` на `Studio.*`.

### Этап 4. Кастомные контролы
1. `ToolboxPanel.xaml` — кнопки `+`/`−` на `DynamicResource`.
2. `ProjectManagerControl.xaml` — иконки/текст/контекстные меню на MaterialDesign.
3. `SearchableComboBox.xaml` — все зашитые цвета на `DynamicResource`.

### Этап 5. Нативные WF-контролы
1. `WfToolboxStyleHelper` — `ToolboxControl`.
2. `WfOutlineStyleHelper` — `OutlineView`.
3. Рефакторинг `PropertyWindowStyleHelper` — `PropertyInspectorView` на `MaterialThemePalette`
   + реакция на смену темы.
4. `WfDesignerViewStyleHelper` — `DesignerView` (фон/сетка/шелл-бар).
5. `RoslynExpressionEditor.xaml` — зашитые кисти → палитра.

### Этап 6. Ресурсы стилей
1. `Resources\ButtonStyles.xaml` — зашитые цвета → `DynamicResource`.

### Этап 7. Пункты меню и текст
1. Неявные стили `MenuItem/ContextMenu/TreeViewItem/ListBoxItem/TextBox` — из
   MaterialDesign-дефолтов.
2. `SystemColors.MenuBrushKey/MenuTextBrushKey` — переопределить цветами MaterialDesign.
3. Единый шрифт/цвет текста через палитру.

### Этап 8. Переключение темы в рантайме
1. `AppSettings.ApplyTheme(bool)` → `PaletteHelper` + повторный прогон WF-хелперов +
   обновление AvalonDock; `MainView` хранит ссылки на активные контролы/хелперы.

### Этап 9. Проверка
1. Юнит-тесты палитры — зелёные.
2. Сборка MSBuild + `vstest.console.exe`.
3. Ручная проверка всех окон/панелей/меню в светлой и тёмной теме.

## Trade-offs
- Нативные WF-контролы не используют WPF/MaterialDesign стили приложения → обход визуального
  дерева с цветами MaterialDesign и повторным применением при смене темы.
- Алиасы `Studio.*` сохраняются для минимальных правок существующего XAML; значения следуют
  MaterialDesign.
