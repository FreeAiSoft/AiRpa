# Compiles and runs the WF colour-dictionary cache override probes (variant 1).
# Studio code is untouched: probes reference WPF/WF only and run from the tests output folder
# (it has .config with binding redirects).
#
# powershell -ExecutionPolicy Bypass -File tmp\cacheprobe\Run-Probe.ps1                 # both probes
# powershell -ExecutionPolicy Bypass -File tmp\cacheprobe\Run-Probe.ps1 -Probe probe2

[CmdletBinding()]
param(
    [string]$Probe = 'all',
    [string]$BinDir,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outDir = Join-Path $PSScriptRoot 'bin'
$bin = if ($BinDir) { (Resolve-Path $BinDir).Path } else { Join-Path $repoRoot "AiRpaStudio.Tests\bin\$Configuration\net48" }

if (-not (Test-Path (Join-Path $bin 'AiRpaStudio.exe'))) {
    throw "AiRpaStudio.exe not found in $bin"
}

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

    throw 'Roslyn csc.exe not found.'
}

function Resolve-Reference([string]$fileName) {
    $dirs = @(
        $bin,
        (Join-Path $repoRoot "AiRpaStudio\bin\$Configuration"),
        $framework
    )

    foreach ($dir in $dirs) {
        $path = Join-Path $dir $fileName
        if (Test-Path $path) { return (Resolve-Path $path).Path }
    }

    $inGac = Get-ChildItem 'C:\Windows\Microsoft.NET\assembly' -Filter $fileName -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($inGac) { return $inGac.FullName }

    throw "Assembly not found: $fileName"
}

$frameworkRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework'
$framework = ''
if (Test-Path $frameworkRoot) {
    $framework = (Get-ChildItem $frameworkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
}

$csc = Find-RoslynCsc
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$referenceNames = @(
    'PresentationFramework.dll',
    'PresentationCore.dll',
    'WindowsBase.dll',
    'System.Xaml.dll',
    'System.Activities.dll',
    'System.Activities.Presentation.dll',
    'System.Activities.Core.Presentation.dll'
)

$refArgs = @()
foreach ($name in $referenceNames) {
    $refArgs += '/r:' + (Resolve-Reference $name)
}

$probes = @(
    @{ Name = 'probe1'; Source = (Join-Path $PSScriptRoot 'CacheOverrideProbe.cs'); Modes = @('override', 'control') },
    @{ Name = 'probe2'; Source = (Join-Path $PSScriptRoot 'CacheOverrideProbe2.cs'); Modes = @('single') },
    @{ Name = 'probe3'; Source = (Join-Path $PSScriptRoot 'CacheOverrideProbe3.cs'); Modes = @('single') },
    @{ Name = 'probe4'; Source = (Join-Path $PSScriptRoot 'CacheOverrideProbe4.cs'); Modes = @('single') }
)

if ($Probe -ne 'all') {
    $probes = $probes | Where-Object { $_.Name -eq $Probe }
    if (-not $probes) { throw "Unknown probe: $Probe" }
}

Write-Host ('Run from: ' + $bin)

foreach ($item in $probes) {
    $exeName = 'CacheProbe-' + $item.Name + '.exe'
    $exePath = Join-Path $outDir $exeName
    $compileArgs = @('/nologo', '/t:exe', '/langversion:7.3', ('/out:' + $exePath)) + $refArgs + $item.Source

    $compileLog = & $csc $compileArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $compileLog | ForEach-Object { Write-Host $_ }
        throw "Compilation failed: $($item.Name)"
    }

    $runtimeExe = Join-Path $bin $exeName
    Copy-Item $exePath $runtimeExe -Force

    $runtimeConfig = $runtimeExe + '.config'
    $sourceConfig = Join-Path $bin 'AiRpaStudio.exe.config'
    if ((Test-Path $sourceConfig) -and -not (Test-Path $runtimeConfig)) {
        Copy-Item $sourceConfig $runtimeConfig -Force
    }

    try {
        Push-Location $bin
        foreach ($mode in $item.Modes) {
            $report = Join-Path $outDir ("report-{0}-{1}.txt" -f $item.Name, $mode)
            Write-Host ''
            Write-Host ("=== {0} / {1} ===" -f $item.Name, $mode)
            & ('.\' + $exeName) $report $mode | Out-Null
            Write-Host ('exit=' + $LASTEXITCODE + '  report: ' + $report)
        }
    }
    finally {
        Pop-Location
        Remove-Item $runtimeExe -Force -ErrorAction SilentlyContinue
        Remove-Item $runtimeConfig -Force -ErrorAction SilentlyContinue
    }
}
