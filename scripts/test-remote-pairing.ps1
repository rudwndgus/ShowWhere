param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string] $BackendUrl,

    [string] $ClientToken = $env:SHOWWHERE_CLIENT_TOKEN
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ClientToken)) {
    throw 'SHOWWHERE_CLIENT_TOKEN is required for the remote pairing test.'
}

$guideUri = [Uri] $BackendUrl
$baseUri = [Uri] $guideUri.GetLeftPart([UriPartial]::Authority)
$authorization = @{ Authorization = "Bearer $($ClientToken.Trim())" }
$desktop = $null
$mobile = $null
$created = $null

function Convert-ToWebSocketUri([Uri] $base, [string] $query) {
    $builder = [UriBuilder] $base
    $builder.Scheme = if ($base.Scheme -eq 'https') { 'wss' } else { 'ws' }
    $builder.Path = '/api/pairing/ws'
    $builder.Query = $query
    return $builder.Uri
}

function Connect-PairingSocket([Uri] $uri, [string] $secret) {
    $socket = [Net.WebSockets.ClientWebSocket]::new()
    $socket.Options.AddSubProtocol('showwhere-v1')
    $socket.Options.AddSubProtocol($secret)
    $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
    try { $socket.ConnectAsync($uri, $timeout.Token).GetAwaiter().GetResult() }
    finally { $timeout.Dispose() }
    return $socket
}

function Receive-UserMessage([Net.WebSockets.ClientWebSocket] $socket) {
    $buffer = New-Object byte[] 8192
    $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))
    try {
        while ($true) {
            $stream = [IO.MemoryStream]::new()
            try {
                do {
                    $segment = [ArraySegment[byte]]::new($buffer)
                    $result = $socket.ReceiveAsync($segment, $timeout.Token).GetAwaiter().GetResult()
                    if ($result.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) {
                        throw 'Remote pairing socket closed before relaying the test message.'
                    }
                    $stream.Write($buffer, 0, $result.Count)
                } while (-not $result.EndOfMessage)
                $message = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
                if ($message.type -eq 'user_message') { return $message }
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $timeout.Dispose() }
}

try {
    $created = Invoke-RestMethod -Method Post -Uri ([Uri]::new($baseUri, '/api/pairing/sessions')) `
        -Headers $authorization -ContentType 'application/json' -Body '{}'
    if ([string]::IsNullOrWhiteSpace($created.sessionId) -or $created.code -notmatch '^\d{6}$') {
        throw 'The public server returned an invalid pairing session.'
    }
    $mobileUri = [Uri] $created.mobileUrl
    if ($mobileUri.GetLeftPart([UriPartial]::Authority) -ne $baseUri.GetLeftPart([UriPartial]::Authority)) {
        throw 'The QR URL does not point to the configured public backend.'
    }
    $page = Invoke-WebRequest -Uri $mobileUri -UseBasicParsing
    if ($page.StatusCode -ne 200) { throw "Mobile page returned HTTP $($page.StatusCode)." }

    $claimBody = @{ pairingToken = $created.pairingToken; code = $created.code } | ConvertTo-Json -Compress
    $claim = Invoke-RestMethod -Method Post -Uri ([Uri]::new($baseUri, '/api/pairing/claim')) `
        -ContentType 'application/json' -Body $claimBody
    $desktopUri = Convert-ToWebSocketUri $baseUri "role=desktop&sessionId=$([Uri]::EscapeDataString($created.sessionId))"
    $mobileSocketUri = Convert-ToWebSocketUri $baseUri "role=mobile&sessionId=$([Uri]::EscapeDataString($claim.sessionId))"
    $desktop = Connect-PairingSocket $desktopUri $created.desktopSecret
    $mobile = Connect-PairingSocket $mobileSocketUri $claim.mobileSecret

    $payload = [Text.Encoding]::UTF8.GetBytes('{"type":"user_message","id":"remote-ci","text":"pairing test"}')
    $mobile.SendAsync(
        [ArraySegment[byte]]::new($payload),
        [Net.WebSockets.WebSocketMessageType]::Text,
        $true,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $relayed = Receive-UserMessage $desktop
    if ($relayed.id -ne 'remote-ci') { throw 'The public WebSocket relay returned the wrong message.' }
    Write-Host 'Public mobile pairing test passed: page, claim, desktop/mobile sockets, and relay.'
}
finally {
    foreach ($socket in @($desktop, $mobile)) {
        if ($null -ne $socket) { $socket.Dispose() }
    }
    if ($null -ne $created -and -not [string]::IsNullOrWhiteSpace($created.sessionId)) {
        try {
            $cleanupHeaders = @{
                Authorization = $authorization.Authorization
                'x-showwhere-pairing-secret' = $created.desktopSecret
            }
            Invoke-RestMethod -Method Delete `
                -Uri ([Uri]::new($baseUri, "/api/pairing/sessions/$([Uri]::EscapeDataString($created.sessionId))")) `
                -Headers $cleanupHeaders | Out-Null
        }
        catch { }
    }
}
