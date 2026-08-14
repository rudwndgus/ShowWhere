param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string] $BackendUrl,

    [string] $ClientToken = $env:SHOWWHERE_CLIENT_TOKEN,

    [string] $SttAudioPath
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
    try { $null = $socket.ConnectAsync($uri, $timeout.Token).GetAwaiter().GetResult() }
    finally { $timeout.Dispose() }
    return ,$socket
}

function Receive-PairingMessage([Net.WebSockets.ClientWebSocket] $socket, [string] $type, [bool] $requireConnected = $false) {
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
                if ($message.type -eq $type -and (-not $requireConnected -or $message.connected -eq $true)) { return $message }
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $timeout.Dispose() }
}

try {
    $guideBody = @{
        session = @{
            sessionId = 'production-ci'; originalUserMessage = 'Open settings'; goal = 'Open settings'
            mode = 'guidance'; status = 'waiting_for_ai'; completedSteps = @(); knownFacts = @(); failureCount = 0
        }
        context = @{ platform = 'windows'; applicationName = 'SystemSettings'; windowTitle = 'Settings' }
        candidates = @(@{
            id = 'candidate-settings'; label = 'Settings'; role = 'button'; enabled = $true; visible = $true; clickable = $true
            bounds = @{ x = 10; y = 10; width = 120; height = 40 }
        })
    } | ConvertTo-Json -Depth 8 -Compress
    $guideDecision = Invoke-RestMethod -Method Post -Uri $guideUri -Headers $authorization -ContentType 'application/json' -Body $guideBody
    if ($guideDecision.action -notin @('highlight', 'ask_user', 'complete', 'wait')) { throw 'The public Guide API returned an invalid action.' }

    $knowledge = Invoke-RestMethod -Method Get -Uri ([Uri]::new($baseUri, '/api/knowledge/sync?cursor=0')) -Headers $authorization
    if ($null -eq $knowledge.cursor -or $null -eq $knowledge.records) { throw 'The public Central Knowledge sync response is invalid.' }

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
    $desktopStatus = Receive-PairingMessage $desktop 'connection_status' $true
    $mobileStatus = Receive-PairingMessage $mobile 'connection_status' $true
    if (-not $desktopStatus.connected -or -not $mobileStatus.connected) { throw 'Both pairing peers did not reach connected=true.' }

    $payload = [Text.Encoding]::UTF8.GetBytes('{"type":"user_message","id":"remote-ci","text":"pairing test"}')
    $null = $mobile.SendAsync(
        [ArraySegment[byte]]::new($payload),
        [Net.WebSockets.WebSocketMessageType]::Text,
        $true,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $relayed = Receive-PairingMessage $desktop 'user_message'
    if ($relayed.id -ne 'remote-ci') { throw 'The public WebSocket relay returned the wrong message.' }

    $replyBytes = [Text.Encoding]::UTF8.GetBytes('{"type":"chat_message","id":"desktop-ci","role":"assistant","text":"pairing reply","isPending":false}')
    $null = $desktop.SendAsync(
        [ArraySegment[byte]]::new($replyBytes),
        [Net.WebSockets.WebSocketMessageType]::Text,
        $true,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $reply = Receive-PairingMessage $mobile 'chat_message'
    if ($reply.id -ne 'desktop-ci' -or $reply.text -ne 'pairing reply') { throw 'The desktop-to-mobile relay returned the wrong message.' }

    if (-not [string]::IsNullOrWhiteSpace($SttAudioPath)) {
        $audio = Resolve-Path -LiteralPath $SttAudioPath
        $sttHeaders = @{ 'x-showwhere-pairing-secret' = $claim.mobileSecret }
        $stt = Invoke-RestMethod -Method Post `
            -Uri ([Uri]::new($baseUri, "/api/mobile/transcribe?sessionId=$([Uri]::EscapeDataString($claim.sessionId))")) `
            -Headers $sttHeaders -ContentType 'audio/wav' -InFile $audio
        if ([string]::IsNullOrWhiteSpace($stt.text)) { throw 'The public STT endpoint returned no transcript.' }
    }

    Write-Host 'Public production test passed: Guide, Central Knowledge, mobile page, claim, connected=true, two-way WebSocket relay, and optional STT.'
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
