param([string]$ModelRoot = $(if ($env:SHOWWHERE_MODEL_ROOT) { $env:SHOWWHERE_MODEL_ROOT } else { 'C:\ShowWhere_Models' }))

$ErrorActionPreference = 'Stop'
$pointsLoader = Join-Path $ModelRoot 'points-gui-g\modeling_points_gui.py'
if (-not (Test-Path -LiteralPath $pointsLoader)) { throw "POINTS loader is missing: $pointsLoader" }
$source = Get-Content -LiteralPath $pointsLoader -Raw -Encoding UTF8
$prepared = $source.Replace('"flash_attention_2"', '"eager"')
if ($prepared -ne $source) {
    Set-Content -LiteralPath $pointsLoader -Value $prepared -Encoding UTF8 -NoNewline
    Write-Host 'POINTS-GUI-G: replaced unsupported Windows FlashAttention2 with eager attention.' -ForegroundColor Green
} else {
    Write-Host 'POINTS-GUI-G: Windows attention compatibility already prepared.' -ForegroundColor DarkGreen
}
$status = [ordered]@{
    schemaVersion = 1
    model = 'tencent/POINTS-GUI-G'
    patch = 'windows-eager-attention'
    preparedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$status | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ModelRoot 'points-gui-g\showwhere-runtime-preparation.json') -Encoding UTF8
