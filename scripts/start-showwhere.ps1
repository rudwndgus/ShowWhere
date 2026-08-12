$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.env'))) {
    throw 'Create .env from .env.example and set OPENAI_API_KEY first.'
}

$api = Start-Process -FilePath 'npm.cmd' -ArgumentList @('run', 'dev:api') -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Milliseconds 1200
    dotnet run --project (Join-Path $projectRoot 'apps\windows\ShowWhere.Desktop\ShowWhere.Desktop.csproj')
}
finally {
    if (-not $api.HasExited) { Stop-Process -Id $api.Id }
}
