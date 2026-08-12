param(
    [string]$ModelRoot = $(if ($env:SHOWWHERE_MODEL_ROOT) { $env:SHOWWHERE_MODEL_ROOT } else { 'C:\ShowWhere_Models' }),
    [string[]]$Roles = @('reranker', 'embedding', 'brain', 'grounder')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $projectRoot 'config\models.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
New-Item -ItemType Directory -Path $ModelRoot -Force | Out-Null

python -c 'import huggingface_hub' 2>$null
if ($LASTEXITCODE -ne 0) {
    python -m pip install --upgrade huggingface_hub hf_xet
}

foreach ($role in $Roles) {
    $model = $manifest.models.$role
    if (-not $model) { throw "Unknown model role: $role" }
    $destination = Join-Path $ModelRoot $model.directory
    $completionMarker = Join-Path $destination 'showwhere-download.json'
    if (Test-Path -LiteralPath $completionMarker) {
        $completed = Get-Content -LiteralPath $completionMarker -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($completed.status -eq 'downloaded' -and $completed.revision -eq $model.revision) {
            Write-Host "[$role] Already complete; skipping $destination" -ForegroundColor DarkGreen
            continue
        }
    }
    $started = Get-Date
    Write-Host "[$role] Downloading $($model.id) -> $destination" -ForegroundColor Cyan
    $env:SHOWWHERE_HF_MODEL_ID = $model.id
    $env:SHOWWHERE_HF_REVISION = $model.revision
    $env:SHOWWHERE_HF_DESTINATION = $destination
    @'
import os
from huggingface_hub import snapshot_download

snapshot_download(
    repo_id=os.environ["SHOWWHERE_HF_MODEL_ID"],
    revision=os.environ["SHOWWHERE_HF_REVISION"],
    local_dir=os.environ["SHOWWHERE_HF_DESTINATION"],
    max_workers=4,
)
'@ | python -
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $($model.id)" }
    $size = (Get-ChildItem -LiteralPath $destination -Recurse -File | Measure-Object Length -Sum).Sum
    $status = [ordered]@{
        schemaVersion = 1
        role = $role
        modelId = $model.id
        revision = $model.revision
        path = $destination
        sizeBytes = $size
        completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        durationSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 2)
        status = 'downloaded'
    }
    $status | ConvertTo-Json | Set-Content -LiteralPath $completionMarker -Encoding UTF8
    Write-Host "[$role] Complete: $([math]::Round($size / 1GB, 2)) GB" -ForegroundColor Green
}

Write-Host "All requested models are available under $ModelRoot" -ForegroundColor Green
& (Join-Path $PSScriptRoot 'prepare-local-models.ps1') -ModelRoot $ModelRoot
