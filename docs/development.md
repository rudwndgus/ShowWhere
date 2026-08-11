# Local development

## Canonical environment file

Only the repository-root `.env` is loaded. Copy `.env.example` to `.env` and enter the Featherless key there. `.env`, `.env.*`, and `*.local` are ignored; `.env.example` is the only environment template that may be committed.

The desktop app never receives `FEATHERLESS_API_KEY`. It calls the loopback backend, and only that backend reads provider configuration.

The default role configuration is:

```dotenv
FEATHERLESS_GUIDE_MODEL=deepseek-ai/DeepSeek-V3.2
FEATHERLESS_GUIDE_FALLBACK_MODEL=Qwen/Qwen3.6-35B-A3B
FEATHERLESS_REASONING_MODEL=deepseek-ai/DeepSeek-V3.2
FEATHERLESS_VISION_MODELS=ByteDance-Seed/UI-TARS-1.5-7B,Qwen/Qwen3-VL-30B-A3B-Instruct,Qwen/Qwen3-VL-8B-Instruct
LEARNING_GENERATOR_MODEL=Qwen/Qwen3.6-35B-A3B
LEARNING_JUDGE_A_MODEL=deepseek-ai/DeepSeek-V3.2
```

All IDs remain configurable. Verify the exact live Featherless catalog before runtime:

```powershell
npm run ai:models:check
```

Semantic data commands:

```powershell
npm run semantic:migrate-legacy
npm run semantic:export
npm run eval:compare
```

Legacy migration never promotes data directly to Gold. The export command only includes validated, human-approved Gold records and keeps evaluation examples out of training output.

## Prerequisites

- Windows 10 or later
- Node.js and npm
- .NET 8 SDK with Windows Desktop support

Install and verify:

```powershell
npm install
npm test
npm run typecheck
npm run lint
npm run build:api
& "C:\Program Files\dotnet\dotnet.exe" test apps/windows/ShowWhere.Windows.Tests/ShowWhere.Windows.Tests.csproj -c Release
& "C:\Program Files\dotnet\dotnet.exe" build apps/windows/ShowWhere.Windows.sln -c Release
```

## Backend

Mock mode needs no provider key:

```dotenv
SHOWWHERE_AI_MODE=mock
SHOWWHERE_API_HOST=127.0.0.1
SHOWWHERE_API_PORT=8787
```

Live Featherless mode:

```dotenv
SHOWWHERE_AI_MODE=featherless
FEATHERLESS_API_KEY=your-server-only-key
FEATHERLESS_BASE_URL=https://api.featherless.ai/v1
FEATHERLESS_GUIDE_MODEL=deepseek-ai/DeepSeek-V3.2
FEATHERLESS_VISION_MODELS=ByteDance-Seed/UI-TARS-1.5-7B,Qwen/Qwen3-VL-30B-A3B-Instruct,Qwen/Qwen3-VL-8B-Instruct
```

Run the backend:

```powershell
npm run dev:api
```

## Windows desktop

```powershell
npm run run:windows
```

The floating red `?` opens the compact guidance panel. Enter sends a goal and Shift+Enter inserts a line break. ShowWhere highlights only one next target and waits for the user to click it.

Optional non-secret client settings:

```dotenv
SHOWWHERE_BACKEND_URL=http://127.0.0.1:8787/api/guide
SHOWWHERE_BACKEND_TIMEOUT_SECONDS=75
```

Publish:

```powershell
npm run build:api
npm run publish:windows
```

Output: `build/windows/ShowWhere.exe`.

Use `scripts/start-showwhere.ps1` to start the built backend and desktop app together. Diagnostic logs are written under `%LOCALAPPDATA%\ShowWhere` without provider credentials.
