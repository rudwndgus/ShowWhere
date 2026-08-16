param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string] $BackendUrl,

    [string] $ClientToken = $env:SHOWWHERE_CLIENT_TOKEN,

    [string] $Version,

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetRunner = Join-Path $PSScriptRoot 'dotnet.ps1'
if (-not [string]::IsNullOrWhiteSpace($ClientToken) -and $ClientToken.Trim().Length -lt 24) {
    throw 'SHOWWHERE_CLIENT_TOKEN must contain at least 24 characters when server authentication is enabled.'
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

$properties = "/property:PublishSingleFile=true;IncludeNativeLibrariesForSelfExtract=true;DebugType=None;DebugSymbols=false;ShowWhereBackendUrl=$BackendUrl"
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use major.minor.patch format.' }
    $properties += ";ShowWhereVersion=$Version"
}
if (-not [string]::IsNullOrWhiteSpace($ClientToken)) {
    $properties += ";ShowWhereClientToken=$($ClientToken.Trim())"
}
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
