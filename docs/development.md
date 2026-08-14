# Development

Required secret in `.env`:

```dotenv
OPENAI_API_KEY=...
OPENAI_FAST_MODEL=gpt-5.6-luna
OPENAI_MODEL=gpt-5.6-terra
OPENAI_STRONG_MODEL=gpt-5.6-sol
OPENAI_STT_MODEL=gpt-4o-transcribe
OPENAI_STT_TIMEOUT_MS=25000
```

Run `npm test`, `npm run typecheck`, and `npm run test:windows` before pushing. The Windows npm scripts automatically prefer an SDK under `DOTNET_ROOT` or `%LOCALAPPDATA%\ShowWhereDotnet` when the system `dotnet` command exposes only a runtime. Start the app and API together with `scripts/start-showwhere.ps1`; it waits for the API health check before opening the desktop app.

Web crawler validation:

```powershell
npm run crawl:smoke
npm run crawl -- --url https://example.com
```

The smoke command serves `fixtures/web-crawler-site` only inside the Node process and verifies menu state changes plus a four-step order tracking route without external website traffic.
