param(
    [string]$ModelRoot = $(if ($env:SHOWWHERE_MODEL_ROOT) { $env:SHOWWHERE_MODEL_ROOT } else { 'C:\ShowWhere_Models' }),
    [int]$Port = 8790,
    [switch]$Install,
    [switch]$BgeOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$serviceRoot = Join-Path $projectRoot 'services\local-ai'
$venv = Join-Path $ModelRoot '.venv'
$python = Join-Path $venv 'Scripts\python.exe'
& (Join-Path $PSScriptRoot 'prepare-local-models.ps1') -ModelRoot $ModelRoot

if ($Install -or -not (Test-Path -LiteralPath $python)) {
    New-Item -ItemType Directory -Path $ModelRoot -Force | Out-Null
    python -m venv $venv
    & $python -m pip install --upgrade pip
    $hasNvidia = $null -ne (Get-Command nvidia-smi -ErrorAction SilentlyContinue)
    if ($hasNvidia) {
        & $python -m pip install torch==2.7.1 torchvision==0.22.1 --index-url https://download.pytorch.org/whl/cu128
    }
    & $python -m pip install -r (Join-Path $serviceRoot 'requirements.txt')
}

$env:SHOWWHERE_MODEL_ROOT = $ModelRoot
if ($BgeOnly) {
    # Keep both lightweight BGE specialists warm and prevent heavyweight
    # Domyn/POINTS requests from evicting them on constrained GPUs.
    $env:SHOWWHERE_LOCAL_AI_MAX_LOADED = '2'
    $env:SHOWWHERE_LOCAL_AI_ALLOWED_ROLES = 'embedding,reranker'
    $env:SHOWWHERE_LOCAL_AI_PRELOAD = 'embedding,reranker'
}
& $python -m uvicorn showwhere_ai_service:app --app-dir $serviceRoot --host 127.0.0.1 --port $Port
