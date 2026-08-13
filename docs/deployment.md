# Server and Windows deployment

ShowWhere's public deployment has two artifacts:

1. A Dockerized Node API on Railway or Render. It owns `OPENAI_API_KEY` and the Git-tracked web knowledge.
2. A self-contained single-file Windows client. It contains the HTTPS API URL and a separate rotatable beta client token, never the OpenAI key.

## Railway server

Create a Railway service from this GitHub repository and select the `ShowWhere2.0` branch. Railway detects `Dockerfile` and `railway.json`. Generate a public domain, then configure these service variables:

```dotenv
OPENAI_API_KEY=your-server-only-openai-key
OPENAI_FAST_MODEL=gpt-5.6-luna
OPENAI_MODEL=gpt-5.6-terra
OPENAI_STRONG_MODEL=gpt-5.6-sol
OPENAI_STT_MODEL=gpt-4o-transcribe
OPENAI_STT_TIMEOUT_MS=25000
OPENAI_BASE_URL=https://api.openai.com/v1
OPENAI_REQUEST_TIMEOUT_MS=30000
OPENAI_MAX_RETRIES=1
SHOWWHERE_CLIENT_TOKEN=generate-a-random-value-with-at-least-24-characters
SHOWWHERE_MAX_REQUEST_BYTES=12000000
SHOWWHERE_WEB_KNOWLEDGE_DIR=knowledge/web
SHOWWHERE_RATE_LIMIT_WINDOW_MS=60000
SHOWWHERE_RATE_LIMIT_MAX_REQUESTS=20
SHOWWHERE_TRUST_PROXY=true
SHOWWHERE_DEBUG=false
```

Do not set `PORT`; Railway supplies it. The API automatically binds to `0.0.0.0:$PORT`. Verify `https://YOUR-DOMAIN/health` returns `{"status":"ok"}`.

Generate a beta token locally without printing the OpenAI key:

```powershell
$bytes = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
$generator.GetBytes($bytes)
$generator.Dispose()
[Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
```

## Single Windows executable

Set the same beta token only for the current terminal, then publish:

```powershell
$env:SHOWWHERE_CLIENT_TOKEN='the-same-random-beta-token'
powershell -ExecutionPolicy Bypass -File scripts/publish-remote-windows.ps1 `
  -BackendUrl 'https://YOUR-DOMAIN'
```

The resulting `build/remote-windows/ShowWhere.exe` includes the .NET runtime, server URL, and beta access token. A recipient can run that single file. They do not need Node.js, .NET, `.env`, or an OpenAI key.

The beta token is extractable from a distributed executable. Use an OpenAI project budget and rate limits, rotate this token when necessary, and move to signed-in users with per-device server-issued credentials before a public launch.
