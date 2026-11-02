$ErrorActionPreference = 'Stop'

$path = 'C:\Users\Maxxx\Desktop\AI-RPA\AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs'
$text = [System.IO.File]::ReadAllText($path)

function Read-Block([string]$file) {
    $content = [System.IO.File]::ReadAllText($file)
    return ($content -replace "`r`n", "`n") -replace "`n", "`r`n"
}

function Find-DocCommentStart([string]$source, [int]$beforeIndex) {
    $marker = "`r`n        /// <summary>`r`n"
    return $source.LastIndexOf($marker, $beforeIndex, [StringComparison]::Ordinal)
}

# --- Region 1: timer fields, ctor, InitializeValidationTimer, tick ---
$block1 = Read-Block 'C:\Users\Maxxx\Desktop\AI-RPA\tmp\patch\block1.txt'
$start1 = $text.IndexOf("        private DispatcherTimer _validationTimer;", [StringComparison]::Ordinal)
$disposeIndex = $text.IndexOf("        public void Dispose()", [StringComparison]::Ordinal)
$end1 = Find-DocCommentStart $text $disposeIndex
if ($start1 -lt 0 -or $end1 -lt 0 -or $end1 -le $start1) { throw "region1 not found ($start1, $end1)" }
$text = $text.Substring(0, $start1) + $block1 + $text.Substring($end1 + 2)

# --- Region 2: the validation pass itself ---
$block2 = Read-Block 'C:\Users\Maxxx\Desktop\AI-RPA\tmp\patch\block2.txt'
$start2 = $text.IndexOf("        private void Validate()", [StringComparison]::Ordinal)
$endMarker2 = "        private static List<string> GetActivityErrors(ModelItem modelItem)"
$end2 = $text.IndexOf($endMarker2, [StringComparison]::Ordinal)
if ($start2 -lt 0 -or $end2 -lt 0 -or $end2 -le $start2) { throw "region2 not found ($start2, $end2)" }
$text = $text.Substring(0, $start2) + $block2 + $text.Substring($end2)

[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($true)))
Write-Host 'patched'
