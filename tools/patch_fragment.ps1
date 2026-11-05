param(
    [Parameter(Mandatory=$true)][string]$Target,
    [Parameter(Mandatory=$true)][string]$OldFile,
    [Parameter(Mandatory=$true)][string]$NewFile,
    [switch]$All
)

# Правка UTF-8 файла фрагментом: старый и новый текст берутся из отдельных файлов,
# чтобы не зависеть от кодовой страницы консоли и от переводов строк.
$utf8 = New-Object System.Text.UTF8Encoding($false)

$targetText = [IO.File]::ReadAllText($Target, [Text.Encoding]::UTF8)
$old = [IO.File]::ReadAllText($OldFile, [Text.Encoding]::UTF8)
$new = [IO.File]::ReadAllText($NewFile, [Text.Encoding]::UTF8)

$hadCrLf = $targetText.Contains("`r`n")
$text = $targetText -replace "`r`n", "`n"
$old = $old -replace "`r`n", "`n"
$new = $new -replace "`r`n", "`n"

if (-not $text.Contains($old)) {
    Write-Output "OLD_NOT_FOUND"
    exit 2
}

if ($All) {
    $result = $text.Replace($old, $new)
} else {
    $index = $text.IndexOf($old)
    $result = $text.Substring(0, $index) + $new + $text.Substring($index + $old.Length)
}

if ($hadCrLf) {
    $result = $result -replace "`n", "`r`n"
}

[IO.File]::WriteAllText($Target, $result, $utf8)
Write-Output "OK"
