$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$serverEntry = Join-Path $projectRoot 'services\api\dist\server.js'
$desktopExecutable = Join-Path $projectRoot 'build\windows\ShowWhere.exe'
$localAiLauncher = Join-Path $PSScriptRoot 'start-local-ai.ps1'

try {
    $localAiListener = Get-NetTCPConnection -State Listen -LocalPort 8790 -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -eq $localAiListener) {
        $logDirectory = Join-Path $env:LOCALAPPDATA 'ShowWhere'
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        $localAiOutputLog = Join-Path $logDirectory 'local-bge.out.log'
        $localAiErrorLog = Join-Path $logDirectory 'local-bge.err.log'

        Start-Process -FilePath 'powershell.exe' `
            -ArgumentList @(
                '-NoProfile',
                '-ExecutionPolicy', 'Bypass',
                '-File', $localAiLauncher,
                '-ModelRoot', 'C:\ShowWhere_Models',
                '-BgeOnly'
            ) `
            -WorkingDirectory $projectRoot `
            -WindowStyle Hidden `
            -RedirectStandardOutput $localAiOutputLog `
            -RedirectStandardError $localAiErrorLog

        $localAiReady = $false
        for ($attempt = 0; $attempt -lt 80; $attempt += 1) {
            Start-Sleep -Milliseconds 250
            $localAiReady = $null -ne (Get-NetTCPConnection -State Listen -LocalPort 8790 -ErrorAction SilentlyContinue |
                Select-Object -First 1)
            if ($localAiReady) { break }
        }

        if (-not $localAiReady) {
            throw 'ShowWhere local BGE service did not start.'
        }
    }

    $listener = Get-NetTCPConnection -State Listen -LocalPort 8787 -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -eq $listener) {
        if (-not (Test-Path -LiteralPath $serverEntry)) {
            throw 'ShowWhere API build was not found.'
        }

        $logDirectory = Join-Path $env:LOCALAPPDATA 'ShowWhere'
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        $standardOutputLog = Join-Path $logDirectory 'api-debug.log'
        $standardErrorLog = Join-Path $logDirectory 'api-error.log'

        Start-Process -FilePath 'node.exe' `
            -ArgumentList $serverEntry `
            -WorkingDirectory $projectRoot `
            -WindowStyle Hidden `
            -RedirectStandardOutput $standardOutputLog `
            -RedirectStandardError $standardErrorLog

        $serverReady = $false
        for ($attempt = 0; $attempt -lt 20; $attempt += 1) {
            Start-Sleep -Milliseconds 250
            $serverReady = $null -ne (Get-NetTCPConnection -State Listen -LocalPort 8787 -ErrorAction SilentlyContinue |
                Select-Object -First 1)
            if ($serverReady) { break }
        }

        if (-not $serverReady) {
            throw 'ShowWhere API did not start.'
        }
    }

    if (-not (Test-Path -LiteralPath $desktopExecutable)) {
        throw 'ShowWhere desktop build was not found.'
    }

    $running = Get-Process -Name 'ShowWhere' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $desktopExecutable } |
        Select-Object -First 1

    if ($null -eq $running) {
        Start-Process -FilePath $desktopExecutable -WorkingDirectory (Split-Path $desktopExecutable)
    }
}
catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        'ShowWhere를 시작하지 못했습니다. 프로젝트 빌드와 Node.js 설치 상태를 확인해 주세요.',
        'ShowWhere',
        'OK',
        'Error') | Out-Null
}
