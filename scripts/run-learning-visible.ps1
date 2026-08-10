$ErrorActionPreference = 'Continue'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot

$env:LEARNING_GENERATOR_MODEL = if ($env:LEARNING_GENERATOR_MODEL) { $env:LEARNING_GENERATOR_MODEL } else { 'deepseek-ai/DeepSeek-V3.2' }
$env:LEARNING_JUDGE_A_MODEL = if ($env:LEARNING_JUDGE_A_MODEL) { $env:LEARNING_JUDGE_A_MODEL } else { 'deepseek-ai/DeepSeek-V3.2' }
$env:LEARNING_JUDGE_B_MODEL = if ($env:LEARNING_JUDGE_B_MODEL) { $env:LEARNING_JUDGE_B_MODEL } else { 'Qwen/Qwen3-VL-30B-A3B-Instruct' }
$env:LEARNING_MAX_EXAMPLES_PER_RUN = '50'
$env:LEARNING_CONCURRENCY = '1'
$env:FEATHERLESS_REQUEST_TIMEOUT_MS = '12000'
$env:FEATHERLESS_MAX_RETRIES = '0'

Clear-Host
Write-Host 'ShowWhere Learning Pipeline - visible test run' -ForegroundColor Cyan
Write-Host "Learning data: $projectRoot\learning\data" -ForegroundColor Yellow
Write-Host "O/X corrections: $projectRoot\training" -ForegroundColor Yellow
Write-Host 'API keys are loaded from the local .env and are never printed.' -ForegroundColor DarkGray
Write-Host ''

Write-Host '[1/6] Validating human seeds and permanent benchmark...' -ForegroundColor Cyan
npm.cmd run learning:validate

Write-Host '[2/6] Converting all approved seeds into canonical human-provenance scenarios...' -ForegroundColor Cyan
npm.cmd run learning:bootstrap

$seeds = @(
    'file_attach_pdf_filter',
    'windows_default_printer',
    'browser_site_search'
)

Write-Host '[3/6] Trying one AI-generated state variation for three different task families...' -ForegroundColor Cyan
foreach ($seed in $seeds) {
    Write-Host "Generating: $seed" -ForegroundColor Green
    npm.cmd run learning:generate -- --seed $seed --count 1
}

$generatedPath = Join-Path $projectRoot 'learning\data\generated\scenarios.jsonl'
$hasGenerated = (Test-Path -LiteralPath $generatedPath) -and ((Get-Item -LiteralPath $generatedPath).Length -gt 0)
if ($hasGenerated) {
    Write-Host '[4/6] Running blind independent judgments...' -ForegroundColor Cyan
    npm.cmd run learning:judge
    Write-Host '[5/6] Building review queue and family-separated datasets...' -ForegroundColor Cyan
    npm.cmd run learning:review-queue
    npm.cmd run learning:build-dataset
    Write-Host '[6/6] Running the permanent benchmark...' -ForegroundColor Cyan
    npm.cmd run eval -- --model $env:LEARNING_JUDGE_A_MODEL
    npm.cmd run eval:report
} else {
    Write-Host '[4-6/6] No AI scenarios were returned. Featherless may still be rate-limited.' -ForegroundColor Red
    Write-Host 'No failed model response was saved as training truth.' -ForegroundColor Yellow
    Write-Host 'Building family-separated datasets from the approved human seed scenarios...' -ForegroundColor Cyan
    npm.cmd run learning:build-dataset
}

Write-Host ''
Write-Host 'Current files:' -ForegroundColor Cyan
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'learning\data') -Recurse -File |
    Select-Object FullName, Length, LastWriteTime |
    Format-Table -AutoSize
Write-Host ''
Write-Host 'Test window will remain open for inspection.' -ForegroundColor Green
Read-Host 'Press Enter to close'
