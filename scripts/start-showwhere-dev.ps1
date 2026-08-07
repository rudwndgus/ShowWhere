$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $PSScriptRoot 'start-showwhere.ps1'
$logPath = Join-Path $env:LOCALAPPDATA 'ShowWhere\api-debug.log'

& $launcher

Write-Host ''
Write-Host 'ShowWhere 개발 로그' -ForegroundColor Cyan
Write-Host '창을 닫아도 ShowWhere 앱은 계속 실행됩니다.' -ForegroundColor DarkGray
Write-Host '------------------------------------------------------------'

for ($attempt = 0; $attempt -lt 20 -and -not (Test-Path -LiteralPath $logPath); $attempt += 1) {
    Start-Sleep -Milliseconds 250
}

if (-not (Test-Path -LiteralPath $logPath)) {
    Write-Host '로그 파일을 찾지 못했습니다.' -ForegroundColor Red
    Read-Host 'Enter를 누르면 닫힙니다'
    exit 1
}

Get-Content -LiteralPath $logPath -Encoding UTF8 -Tail 30 -Wait
