param(
    [string] $OutputDirectory = 'build/production'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$buildRoot = [IO.Path]::GetFullPath((Join-Path $root 'build'))
if (-not $output.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Production output must stay inside the repository build directory.'
}

$headers = @{ 'User-Agent' = 'ShowWhere-production-downloader' }
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/rudwndgus/ShowWhere/releases/latest' -Headers $headers
$asset = @($release.assets | Where-Object name -eq 'ShowWhere.exe')
if ($asset.Count -ne 1) { throw 'Latest release must contain exactly one ShowWhere.exe asset.' }
if ($asset[0].digest -notmatch '^sha256:([0-9a-fA-F]{64})$') { throw 'Latest ShowWhere.exe has no valid GitHub SHA-256 digest.' }
$expected = $matches[1].ToLowerInvariant()

[IO.Directory]::CreateDirectory($output) | Out-Null
$temporary = Join-Path $output 'ShowWhere.exe.download'
$destination = Join-Path $output 'ShowWhere.exe'
try {
    Invoke-WebRequest -Uri $asset[0].browser_download_url -Headers $headers -OutFile $temporary
    $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw 'Downloaded ShowWhere.exe failed SHA-256 verification.' }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
} finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}

Write-Host "Official ShowWhere $($release.tag_name): $destination"
