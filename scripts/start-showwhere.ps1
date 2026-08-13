$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetRunner = Join-Path $PSScriptRoot 'dotnet.ps1'
$environmentPath = Join-Path $projectRoot '.env'

if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw 'Create .env from .env.example and set OPENAI_API_KEY first.'
}

function Stop-ProcessTree([int] $ProcessId) {
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue
    foreach ($child in $children) { Stop-ProcessTree $child.ProcessId }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

$apiHost = '127.0.0.1'
$apiPort = '8787'
foreach ($line in Get-Content -LiteralPath $environmentPath) {
    if ($line -match '^\s*SHOWWHERE_API_HOST\s*=\s*(.+?)\s*$') { $apiHost = $matches[1].Trim().Trim('"').Trim("'") }
    if ($line -match '^\s*SHOWWHERE_API_PORT\s*=\s*(\d+)\s*$') { $apiPort = $matches[1] }
}
$healthHost = if ($apiHost -in @('0.0.0.0', '::')) { '127.0.0.1' } else { $apiHost }
$healthUrl = "http://${healthHost}:${apiPort}/health"

$api = Start-Process -FilePath 'npm.cmd' -ArgumentList @('run', 'dev:api') -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($api.HasExited) {
            throw "ShowWhere API failed to start. Exit code: $($api.ExitCode)"
        }
        try {
            $response = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 1
            if ($response.status -eq 'ok') {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) { throw 'ShowWhere API did not become ready within 20 seconds.' }

    & powershell -NoProfile -ExecutionPolicy Bypass -File $dotnetRunner run --project (Join-Path $projectRoot 'apps\windows\ShowWhere.Desktop\ShowWhere.Desktop.csproj')
    if ($LASTEXITCODE -ne 0) { throw "ShowWhere desktop app exited with code $LASTEXITCODE." }
}
finally {
    if (-not $api.HasExited) { Stop-ProcessTree $api.Id }
}
