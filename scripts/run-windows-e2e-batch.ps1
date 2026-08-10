param(
    [string]$Category = '',
    [int]$MaxCases = 0
)

$ErrorActionPreference = 'Continue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$catalogPath = Join-Path $projectRoot 'training\windows-e2e-cases.json'
$summaryPath = Join-Path $projectRoot 'training\e2e-batch-runs.jsonl'
$caseRunner = Join-Path $PSScriptRoot 'showwhere-e2e-case.ps1'
$parsedCases = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cases = @()
foreach ($parsedCase in $parsedCases) { $cases += $parsedCase }
if ($Category) { $cases = @($cases | Where-Object category -eq $Category) }
if ($MaxCases -gt 0) { $cases = @($cases | Select-Object -First $MaxCases) }

$batchId = [Guid]::NewGuid().ToString('N')
$started = [DateTime]::UtcNow
$passed = 0
$failed = 0
foreach ($case in $cases) {
    Write-Host "BATCH CASE $($case.id) [$($case.category)]" -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $caseRunner `
        -Question $case.question `
        -SettingsUri $case.settingsUri `
        -ExpectedLabels $case.expectedLabels
    if ($LASTEXITCODE -eq 0) { $passed++ } else { $failed++ }
}

$summary = [ordered]@{
    schemaVersion = 'showwhere-e2e-batch-v1'
    batchId = $batchId
    startedAtUtc = $started.ToString('o')
    completedAtUtc = [DateTime]::UtcNow.ToString('o')
    category = $Category
    total = $cases.Count
    passed = $passed
    failed = $failed
}
$summaryLine = ($summary | ConvertTo-Json -Compress) + [Environment]::NewLine
[System.IO.File]::AppendAllText(
    $summaryPath,
    $summaryLine,
    (New-Object System.Text.UTF8Encoding($false)))
Write-Host "BATCH RESULT: $passed passed, $failed failed. $summaryPath" -ForegroundColor Yellow
if ($failed -gt 0) { exit 2 }
