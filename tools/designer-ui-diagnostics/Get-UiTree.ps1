<#
.SYNOPSIS
    Дамп дерева UI Automation окна студии: что за элемент, где он и как называется.

.DESCRIPTION
    Нужен, чтобы привязать «странный прямоугольник» со снимка к конкретному элементу визуального дерева:
    в дампе видно ControlType, ClassName, AutomationId, Name и BoundingRectangle (экранные координаты).
    Через RawViewWalker попадают и те элементы, которые в обычном (Control) представлении UIA скрыты.

    Пример из практики (баг пустой активности): прямоугольник снимка 398,110 873x927 содержал
        DesignerView 'UserControl_1'               398,110  873x927
          ScrollViewer 'scrollViewer'              398,137  873x872
            ActivityTypeDesigner 'ActivityBuilder' 624,363  420x420
              WorkflowItemPresenter 'Перетащите сюда действие' 634,373 400x400
    Плита 440x440 (заливка чужого цвета) лежит на 10 px наружу от ActivityTypeDesigner — по дампу
    видно, что это Border шаблона, а не отдельный контрол.

.PARAMETER ProcessName
    Имя процесса студии (по умолчанию AiRpaStudio).

.PARAMETER ProcessId
    PID, если процессов несколько.

.PARAMETER OutFile
    Файл отчёта (по умолчанию tmp\ui-tree\tree.txt в корне репозитория).

.PARAMETER MaxDepth
    Глубина обхода (по умолчанию 40).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\designer-ui-diagnostics\Get-UiTree.ps1
#>
[CmdletBinding()]
param(
    [string]$ProcessName = 'AiRpaStudio',
    [int]$ProcessId,
    [string]$OutFile,
    [int]$MaxDepth = 40
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutFile) {
    $OutFile = Join-Path $repoRoot 'tmp\ui-tree\tree.txt'
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutFile) -Force | Out-Null

if ($ProcessId) {
    $process = Get-Process -Id $ProcessId
}
else {
    $process = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -eq $ProcessName -and $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1
}

if (-not $process) {
    throw "Не найден процесс '$ProcessName' с окном. Студия запущена?"
}

$lines = New-Object System.Collections.Generic.List[string]
$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)

if ($null -eq $window) {
    throw "У процесса $($process.Id) нет окна верхнего уровня, видимого через UIA."
}

$lines.Add(('WINDOW: {0} | {1} | {2}' -f $window.Current.Name, $window.Current.ClassName, $window.Current.BoundingRectangle))

$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker

function Walk-Element($element, $depth) {
    if ($depth -gt $MaxDepth) { return }

    $rect = $element.Current.BoundingRectangle
    $lines.Add(('{0}{1} [{2}] name="{3}" id="{4}" rect={5},{6} {7}x{8}' -f
        ('  ' * $depth),
        $element.Current.ControlType.ProgrammaticName,
        $element.Current.ClassName,
        $element.Current.Name,
        $element.Current.AutomationId,
        [int]$rect.X, [int]$rect.Y, [int]$rect.Width, [int]$rect.Height))

    $child = $walker.GetFirstChild($element)
    while ($null -ne $child) {
        Walk-Element $child ($depth + 1)
        $child = $walker.GetNextSibling($child)
    }
}

Walk-Element $window 0
[System.IO.File]::WriteAllLines($OutFile, $lines, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ('PID:    ' + $process.Id)
Write-Host ('Дерево: ' + $OutFile + ' (' + $lines.Count + ' строк)')
Write-Host ''
Write-Host (($lines | Select-Object -First 40) -join [Environment]::NewLine)
