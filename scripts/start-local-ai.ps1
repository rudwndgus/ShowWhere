param(
    [string]$ModelRoot = $(if ($env:SHOWWHERE_MODEL_ROOT) { $env:SHOWWHERE_MODEL_ROOT } else { 'C:\ShowWhere_Models' }),
    [int]$Port = 8790,
    [switch]$Install
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
& $python -m uvicorn showwhere_ai_service:app --app-dir $serviceRoot --host 127.0.0.1 --port $Port
