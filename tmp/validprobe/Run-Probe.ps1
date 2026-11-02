param(
    [string]$Workflow = 'RPA-1\Main.xaml',
    [string]$Mode = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bin = Join-Path $repoRoot 'AiRpaStudio.Tests\bin\Debug\net48'
$outDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$csc = (Get-ChildItem 'C:\Program Files\Microsoft Visual Studio\18\*\MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1).FullName
if (-not $csc) { throw 'csc.exe not found' }

$framework = (Get-ChildItem 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework' -Directory |
    Sort-Object Name -Descending | Select-Object -First 1).FullName

function Resolve-Ref([string]$name) {
    foreach ($dir in @($bin, (Join-Path $repoRoot 'AiRpaStudio\bin\Debug'), $framework)) {
        $path = Join-Path $dir $name
        if (Test-Path $path) { return (Resolve-Path $path).Path }
    }
    $inGac = Get-ChildItem 'C:\Windows\Microsoft.NET\assembly' -Filter $name -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($inGac) { return $inGac.FullName }
    throw "reference not found: $name"
}

$refs = @(
    'PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll',
    'System.Xaml.dll', 'System.Activities.dll', 'System.Activities.Presentation.dll',
    'System.Activities.Core.Presentation.dll', 'MaterialDesignThemes.Wpf.dll',
    'MaterialDesignColors.dll', 'AiRpaStudio.exe'
) | ForEach-Object { '/r:' + (Resolve-Ref $_) }

$probeExe = Join-Path $outDir 'ValidationIconProbe.exe'
& $csc '/nologo' '/t:exe' '/langversion:7.3' ('/out:' + $probeExe) $refs (Join-Path $PSScriptRoot 'Program6.cs')
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }

$runtimeExe = Join-Path $bin 'ValidationIconProbe.exe'
Copy-Item $probeExe $runtimeExe -Force
$runtimeConfig = $runtimeExe + '.config'
Copy-Item (Join-Path $bin 'AiRpaStudio.exe.config') $runtimeConfig -Force

$workflowPath = Join-Path $repoRoot $Workflow

try {
    Push-Location $bin
    & '.\ValidationIconProbe.exe' $workflowPath $Mode
    Write-Host ('exit=' + $LASTEXITCODE)
}
finally {
    Pop-Location
    Remove-Item $runtimeExe -Force -ErrorAction SilentlyContinue
    Remove-Item $runtimeConfig -Force -ErrorAction SilentlyContinue
}






