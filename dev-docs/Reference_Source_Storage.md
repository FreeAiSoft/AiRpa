# Хранилище исходников / Reference Source

Нативные WF-контролы студии (`System.Activities.Presentation`) имеют шаблоны и стили,
зашитые в сборку `System.Activities.Presentation.dll`. Чтобы понять их внутреннюю структуру
(имена `PART_*`, вложенность визуального дерева, ключи ресурсов) и корректно применять темы
обходом визуального дерева, используются исходники .NET Framework 4.8 Reference Source.

## Расположение

`C:\Users\Maxxx\source\repos\.NET-Framework-4.8-RTM-Reference-Source`

## Ключевые исходники `System.Activities.Presentation`

Базовый путь:

`ndp\cdf\src\NetFx40\Tools\System.Activities.Presentation\`

| Область | Исходники |
|---|---|
| ToolboxControl | `System\Activities\Presentation\Toolbox\ToolboxControl.cs`, `ToolboxCategory.cs`, `ToolboxItemWrapper.cs` |
| Шаблон Toolbox | `Themes\ToolboxDefaultTemplate.xaml` |
| OutlineView | `System\Activities\Presentation\View\OutlineView\*` |
| WorkflowViewElement | `System\Activities\Presentation\WorkflowViewElement.cs`, `WorkflowViewElementDefaultTemplate.xaml` |
| Общие стили/шаблоны WF | `Themes\Generic.xaml` |
| Сопутствующее | `System\Activities\Presentation\View\*`, `System\Activities\Presentation\Converters\*` |

> Полное дерево исходников доступно по базовому пути выше; PropertyInspectorView и
> DesignerView находятся в `System\Activities\Presentation\View\*` внутри той же сборки.
