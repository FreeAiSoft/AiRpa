<#
.SYNOPSIS
    Компилирует и запускает пробник темы дизайнера WF (проходы light → dark → light).

.DESCRIPTION
    Пробник открывает настоящий WorkflowDesigner с пустой активностью, применяет темизацию студии
    (RPA.Helpers.WfDesignerTheme) тремя проходами и печатает для каждого прохода:
      * размер и фон плиты пустой активности («Перетащите сюда действие»);
      * элементы, оставшиеся на цвете ЧУЖОЙ (противоположной) палитры:
        фон = Surface, рамка = SeparatorStrong, текст = Foreground.
    Если после прохода в активной светлой теме счётчик «фон чужой поверхности» не ноль или плита
    тёмная — темизатор не откатывает собственную работу предыдущей темы (баг round-trip).

.PARAMETER BinDir
    Папка, из которой запускается пробник (AiRpaStudio.exe и зависимости). По умолчанию — папка
    тестов (AiRpaStudio.Tests\bin\Release\net48): там есть все зависимости и .config с redirect'ами.

.PARAMETER Configuration
    Конфигурация сборки, которая ищется, если -BinDir не задан (по умолчанию Release).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\designer-ui-diagnostics\Invoke-DesignerThemeProbe.ps1
#>
[CmdletBinding()]
param(
    [string]$BinDir,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Кириллица в отчётах при перенаправлении вывода: иначе PowerShell пишет их в CP866.
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$probeSource = Join-Path $PSScriptRoot 'probe\DesignerThemeProbe.cs'
$outDir = Join-Path $PSScriptRoot 'probe\bin'

function Find-RoslynCsc {
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe'
    )

    foreach ($candidate in $candidates) {
        $found = Get-ChildItem -Path $candidate -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    throw 'Не найден Roslyn csc.exe (нужна установленная Visual Studio). Поправьте Find-RoslynCsc.'
}

function Resolve-BinDir {
    if ($BinDir) {
        return (Resolve-Path $BinDir).Path
    }

    # Приоритет — папка тестов: в ней есть и AiRpaStudio.exe, и все зависимости, и .config с redirect'ами.
    $candidates = @(
        (Join-Path $repoRoot "AiRpaStudio.Tests\bin\$Configuration\net48"),
        (Join-Path $repoRoot "AiRpaStudio\bin\$Configuration")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'AiRpaStudio.exe')) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Не найден AiRpaStudio.exe. Соберите решение или укажите -BinDir."
}

# Ссылка ищется в папке запуска, затем в выходной папке студии, затем в GAC: WF-сборки лежат в GAC,
# WPF — в reference assemblies, а копии есть не в каждой выходной папке.
function Resolve-Reference([string]$fileName) {
    $searchDirs = @(
        $bin,
        (Join-Path $repoRoot "AiRpaStudio\bin\$Configuration"),
        $framework
    )

    foreach ($dir in $searchDirs) {
        $path = Join-Path $dir $fileName
        if (Test-Path $path) { return (Resolve-Path $path).Path }
    }

    $inGac = Get-ChildItem 'C:\Windows\Microsoft.NET\assembly' -Filter $fileName -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($inGac) { return $inGac.FullName }

    throw "Не найдена сборка $fileName. Соберите решение (Release)."
}

$bin = Resolve-BinDir
$csc = Find-RoslynCsc
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$probeExe = Join-Path $outDir 'DesignerThemeProbe.exe'

$frameworkRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework'
$framework = ''
if (Test-Path $frameworkRoot) {
    $framework = (Get-ChildItem $frameworkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
}

$referenceNames = @(
    'PresentationFramework.dll',
    'PresentationCore.dll',
    'WindowsBase.dll',
    'System.Xaml.dll',
    'System.Activities.dll',
    'System.Activities.Presentation.dll',
    'System.Activities.Core.Presentation.dll',
    'MaterialDesignThemes.Wpf.dll',
    'MaterialDesignColors.dll',
    'AiRpaStudio.exe'
)

$refArgs = @()
foreach ($name in $referenceNames) {
    $refArgs += '/r:' + (Resolve-Reference $name)
}

$compileArgs = @('/nologo', '/t:exe', '/langversion:7.3', ('/out:' + $probeExe)) + $refArgs + $probeSource

Write-Host ('Запуск из: ' + $bin)
Write-Host ('csc:       ' + $csc)
Write-Host ('Пробник:   ' + $probeExe)
Write-Host ''

$compileLog = & $csc $compileArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    $compileLog | ForEach-Object { Write-Host $_ }
    throw 'Не удалось скомпилировать пробник.'
}

# Запуск из папки студии: там лежат зависимости и .config с binding redirect'ами
# (без redirect'ов WF падает на ICSharpCode.AvalonEdit в ImportDesigner).
$runtimeExe = Join-Path $bin 'DesignerThemeProbe.exe'
Copy-Item $probeExe $runtimeExe -Force
$reportPath = Join-Path $outDir 'probe-report.txt'

$runtimeConfig = $runtimeExe + '.config'
if (-not (Test-Path $runtimeConfig)) {
    $sourceConfig = Join-Path $bin 'AiRpaStudio.exe.config'
    if (Test-Path $sourceConfig) {
        Copy-Item $sourceConfig $runtimeConfig -Force
    }
}

try {
    Push-Location $bin
    & '.\DesignerThemeProbe.exe' $reportPath
    Write-Host ''
    Write-Host ('Отчёт: ' + $reportPath)
    if (Test-Path $reportPath) { Get-Content $reportPath -Encoding UTF8 | Write-Host }
    Write-Host ('exit=' + $LASTEXITCODE)
}
finally {
    Pop-Location
    Remove-Item $runtimeExe -Force -ErrorAction SilentlyContinue
    Remove-Item $runtimeConfig -Force -ErrorAction SilentlyContinue
}
