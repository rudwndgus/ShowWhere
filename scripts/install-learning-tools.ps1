param([string]$ModelRoot = $(if ($env:SHOWWHERE_MODEL_ROOT) { $env:SHOWWHERE_MODEL_ROOT } else { 'C:\ShowWhere_Models' }))

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $ModelRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw "Local AI environment is missing. Run scripts/start-local-ai.ps1 -Install first." }
& $python -m pip install -r (Join-Path $projectRoot 'learning\requirements.txt')

