<#
.SYNOPSIS
    Снимок окна запущенной студии + пиксельный анализ: гистограмма цветов и поиск «чужих» прямоугольников.

.DESCRIPTION
    Метод диагностики визуальных багов темы:
      1) снимок окна процесса (окно поднимается наверх: CopyFromScreen не снимает перекрытое);
      2) топ цветов: цвет, которого нет в активной палитре, — первый подозреваемый;
      3) для каждого подозрительного цвета — bounding box и проверка «рамка ли это»: центр bbox другого
         цвета, а кольцо по периметру — подозрительного. Так нашёлся баг «чёрный прямоугольник толщиной
         с бордер вокруг подсказки "Перетащите сюда действие"»: кольцо #272727 (Surface тёмной палитры)
         шириной 11–12 px вокруг белой внутренности.

    Готовый снимок можно разобрать повторно через -ImagePath (без запущенной студии).

.PARAMETER ProcessName
    Имя процесса студии (по умолчанию AiRpaStudio).

.PARAMETER ProcessId
    PID, если процессов несколько.

.PARAMETER ImagePath
    Разбирать существующий PNG вместо снятия нового.

.PARAMETER OutDir
    Куда писать снимок и отчёт (по умолчанию tmp\ui-snapshot в корне репозитория).

.PARAMETER SuspectColor
    Цвета-подозреваемые в виде #RRGGBB. По умолчанию — цвета тёмной палитры студии, которых нет в
    светлой: #272727 (Dark.Surface), #48484F (Dark.SeparatorStrong), #F1F1F1 (Dark.Foreground).

.PARAMETER Step
    Шаг сетки сканирования в пикселях (по умолчанию 2). 1 — точные координаты, но медленнее.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\designer-ui-diagnostics\Get-UiSnapshot.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\designer-ui-diagnostics\Get-UiSnapshot.ps1 -ImagePath tmp\shot.png
#>
[CmdletBinding()]
param(
    [string]$ProcessName = 'AiRpaStudio',
    [int]$ProcessId,
    [string]$ImagePath,
    [string]$OutDir,
    [string[]]$SuspectColor = @('#272727', '#48484F', '#F1F1F1', '#000000'),
    [int]$Step = 2
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutDir) {
    $OutDir = Join-Path $repoRoot 'tmp\ui-snapshot'
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

function Capture-Window {
    Add-Type -AssemblyName System.Windows.Forms

    $signature = @'
using System;
using System.Runtime.InteropServices;
public class SnapshotWin32 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@
    if (-not ('SnapshotWin32' -as [type])) {
        Add-Type -TypeDefinition $signature
    }

    if ($ProcessId) {
        $process = Get-Process -Id $ProcessId
    }
    else {
        $process = Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -eq $ProcessName -and $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
    }

    if (-not $process) {
        throw "Не найдено окно процесса '$ProcessName'. Студия запущена? Для разбора готового снимка используйте -ImagePath."
    }

    $handle = $process.MainWindowHandle
    [void][SnapshotWin32]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 700

    $rect = New-Object SnapshotWin32+RECT
    [void][SnapshotWin32]::GetWindowRect($handle, [ref]$rect)
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $captured = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($captured)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $graphics.Dispose()

    return [pscustomobject]@{
        Bitmap = $captured
        Origin = ('{0},{1}' -f $rect.Left, $rect.Top)
        Source = ('окно {0} (pid {1}) "{2}"' -f $process.ProcessName, $process.Id, $process.MainWindowTitle)
    }
}

function Get-LockedPixels($bitmap) {
    # CopyFromScreen/загрузка PNG дают 32bpp; фиксируем 24bpp RGB, чтобы индексация была без сюрпризов.
    $normalized = New-Object System.Drawing.Bitmap($bitmap.Width, $bitmap.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [System.Drawing.Graphics]::FromImage($normalized)
    $graphics.DrawImage($bitmap, 0, 0, $bitmap.Width, $bitmap.Height)
    $graphics.Dispose()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $normalized.Width, $normalized.Height)
    $data = $normalized.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $bytes = New-Object byte[] ($data.Stride * $normalized.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $normalized.UnlockBits($data)

    return [pscustomobject]@{
        Bytes = $bytes
        Stride = $data.Stride
        Width = $normalized.Width
        Height = $normalized.Height
        Bitmap = $normalized
    }
}

if ($ImagePath) {
    $resolved = (Resolve-Path $ImagePath).Path
    $image = [System.Drawing.Image]::FromFile($resolved)
    $bitmap = New-Object System.Drawing.Bitmap($image)
    $image.Dispose()
    $source = ('файл ' + $resolved)
    $origin = 'n/a'
}
else {
    $captured = Capture-Window
    $bitmap = $captured.Bitmap
    $source = $captured.Source
    $origin = $captured.Origin
}

$pixels = Get-LockedPixels $bitmap
$bytes = $pixels.Bytes
$stride = $pixels.Stride
$width = $pixels.Width
$height = $pixels.Height

# Цвет BGR-порядка в памяти; ключ — int RGB без альфы (сравнение по int в разы быстрее строк).
function Color-At([int]$x, [int]$y) {
    $offset = ($y * $stride) + ($x * 3)
    return ([int]$bytes[$offset + 2] * 65536) + ([int]$bytes[$offset + 1] * 256) + [int]$bytes[$offset]
}

function To-Hex([int]$value) {
    return ('#{0:X2}{1:X2}{2:X2}' -f (($value -shr 16) -band 0xFF), (($value -shr 8) -band 0xFF), ($value -band 0xFF))
}

$suspects = @{}
foreach ($color in $SuspectColor) {
    $hex = $color.TrimStart('#')
    $value = [Convert]::ToInt32($hex, 16)
    $suspects[$value] = [pscustomobject]@{
        Hex = '#' + $hex.ToUpperInvariant()
        Count = 0
        MinX = $width
        MinY = $height
        MaxX = -1
        MaxY = -1
    }
}

$histogram = @{}
$report = New-Object System.Collections.Generic.List[string]

for ($y = 0; $y -lt $height; $y += $Step) {
    for ($x = 0; $x -lt $width; $x += $Step) {
        $value = Color-At $x $y

        if ($histogram.ContainsKey($value)) { $histogram[$value]++ } else { $histogram[$value] = 1 }

        if ($suspects.ContainsKey($value)) {
            $entry = $suspects[$value]
            $entry.Count++
            if ($x -lt $entry.MinX) { $entry.MinX = $x }
            if ($x -gt $entry.MaxX) { $entry.MaxX = $x }
            if ($y -lt $entry.MinY) { $entry.MinY = $y }
            if ($y -gt $entry.MaxY) { $entry.MaxY = $y }
        }
    }
}

$report.Add(('источник: {0}' -f $source))
$report.Add(('экран:    {0}' -f $origin))
$report.Add(('размер:   {0}x{1}, шаг сканирования {2} px' -f $width, $height, $Step))
$report.Add('')

$report.Add('--- топ цветов ---')
foreach ($entry in ($histogram.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20)) {
    $report.Add(('{0} : {1}' -f (To-Hex $entry.Key), $entry.Value))
}

$report.Add('')
$report.Add('--- подозрительные цвета (есть ли они в активной палитре?) ---')

foreach ($entry in ($suspects.Values | Sort-Object Hex)) {
    if ($entry.Count -eq 0) {
        $report.Add(('{0}: не найден' -f $entry.Hex))
        continue
    }

    $boxWidth = $entry.MaxX - $entry.MinX + $Step
    $boxHeight = $entry.MaxY - $entry.MinY + $Step
    $report.Add(('{0}: пикселей={1} bbox=({2},{3})-({4},{5}) = {6}x{7}' -f
        $entry.Hex, $entry.Count, $entry.MinX, $entry.MinY, $entry.MaxX, $entry.MaxY, $boxWidth, $boxHeight))

    $centerX = [int](($entry.MinX + $entry.MaxX) / 2)
    $centerY = [int](($entry.MinY + $entry.MaxY) / 2)
    $center = To-Hex (Color-At $centerX $centerY)
    $edge = To-Hex (Color-At $centerX ($entry.MinY + 3))

    # Внутренность: самый частый цвет в сетке внутренних точек — центр может попасть в текст подсказки.
    $samples = @{}
    foreach ($fx in 0.25, 0.5, 0.75) {
        foreach ($fy in 0.25, 0.5, 0.75) {
            $color = To-Hex (Color-At ([int]($entry.MinX + $boxWidth * $fx)) ([int]($entry.MinY + $boxHeight * $fy)))
            if ($samples.ContainsKey($color)) { $samples[$color]++ } else { $samples[$color] = 1 }
        }
    }

    $inside = ($samples.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key

    if ($inside -ne $entry.Hex) {
        # Кольцо по периметру, внутри другой цвет → это Border, а не сплошная заливка.
        $thickness = 0
        while (($thickness -lt ($boxHeight / 2)) -and ((To-Hex (Color-At $centerX ($entry.MinY + $thickness))) -eq $entry.Hex)) {
            $thickness++
        }

        $report.Add(('    это РАМКА: внутри {0}, толщина кольца ~{1} px' -f $inside, $thickness))
        $report.Add(('    → прямоугольник {0}x{1} цвета {2} с внутренностью {3}' -f $boxWidth, $boxHeight, $entry.Hex, $inside))
    }
}

$bitmap.Dispose()
$pixels.Bitmap.Dispose()

$pngPath = Join-Path $OutDir 'window.png'
if (-not $ImagePath) {
    $captured.Bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $report.Insert(0, ('png:      {0}' -f $pngPath))
}

$reportPath = Join-Path $OutDir 'snapshot.txt'
[System.IO.File]::WriteAllLines($reportPath, $report, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ('Отчёт: ' + $reportPath)
Write-Host ''
Write-Host (($report | Select-Object -First 35) -join [Environment]::NewLine)
