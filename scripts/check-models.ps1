param([string]$ModelRoot = $(if ($env:SHOWWHERE_MODEL_ROOT) { $env:SHOWWHERE_MODEL_ROOT } else { 'C:\ShowWhere_Models' }))

$projectRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'config\models.json') -Raw | ConvertFrom-Json
$failed = $false
foreach ($role in @('brain', 'reranker', 'embedding', 'grounder')) {
    $model = $manifest.models.$role
    $path = Join-Path $ModelRoot $model.directory
    $marker = Join-Path $path 'showwhere-download.json'
    $ready = Test-Path -LiteralPath $marker
    $size = if (Test-Path -LiteralPath $path) { (Get-ChildItem -LiteralPath $path -File -Recurse | Measure-Object Length -Sum).Sum } else { 0 }
    [pscustomobject]@{ Role=$role; Model=$model.id; Ready=$ready; SizeGB=[math]::Round($size/1GB,2); Runtime=$model.runtime; Path=$path }
    if (-not $ready) { $failed = $true }
}
if ($failed) { exit 1 }
