param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string] $BackendUrl,

    [string] $ClientToken = $env:SHOWWHERE_CLIENT_TOKEN,

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetRunner = Join-Path $PSScriptRoot 'dotnet.ps1'
if ([string]::IsNullOrWhiteSpace($ClientToken) -or $ClientToken.Trim().Length -lt 24) {
    throw 'Set SHOWWHERE_CLIENT_TOKEN to the same 24+ character token configured on the server.'
}
if (-not $BackendUrl.EndsWith('/api/guide', [StringComparison]::OrdinalIgnoreCase)) {
    $BackendUrl = $BackendUrl.TrimEnd('/') + '/api/guide'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'build\remote-windows'
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}

$properties = "/property:PublishSingleFile=true;IncludeNativeLibrariesForSelfExtract=true;DebugType=None;DebugSymbols=false;ShowWhereBackendUrl=$BackendUrl;ShowWhereClientToken=$($ClientToken.Trim())"
& powershell -NoProfile -ExecutionPolicy Bypass -File $dotnetRunner publish `
    (Join-Path $projectRoot 'apps\windows\ShowWhere.Desktop\ShowWhere.Desktop.csproj') `
    -c Release -r win-x64 --self-contained true `
    $properties `
    --output $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Remote Windows publish failed with exit code $LASTEXITCODE." }

$executable = Join-Path $OutputDirectory 'ShowWhere.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Publish completed without producing ShowWhere.exe.'
}
Write-Host "Remote ShowWhere executable: $executable"
