param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $DotnetArguments
)

$ErrorActionPreference = 'Stop'

function Test-DotnetSdk([string] $command) {
    if (-not (Test-Path -LiteralPath $command -PathType Leaf)) { return $false }
    $sdks = & $command --list-sdks 2>$null
    return $LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($sdks -join ''))
}

$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
    $candidates += Join-Path $env:DOTNET_ROOT 'dotnet.exe'
}
if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    $candidates += Join-Path $env:LOCALAPPDATA 'ShowWhereDotnet\dotnet.exe'
}
$systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -ne $systemDotnet) { $candidates += $systemDotnet.Source }

$dotnet = $candidates |
    Select-Object -Unique |
    Where-Object { Test-DotnetSdk $_ } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($dotnet)) {
    throw '.NET 8 SDK was not found. Install the SDK, not only the runtime: https://dotnet.microsoft.com/download/dotnet/8.0'
}

& $dotnet @DotnetArguments
exit $LASTEXITCODE
