# ShowWhere 2.0

ShowWhere is a Windows 10/11 guidance app. It captures the complete desktop, collects live Microsoft UI Automation candidates, and asks one OpenAI GPT vision model for exactly one next action. It never clicks automatically; it highlights the verified location for the user.

## Architecture

```text
User goal
  -> WPF desktop captures full screen + UI Automation candidates
  -> local Node API POST /api/guide
  -> OpenAI Responses API (GPT-5.6, image input, strict JSON schema)
  -> contract/confidence/target validation
  -> live UI element identity revalidation
  -> always-on-top overlay
```

Only the OpenAI provider is active on this branch. Previous providers, local model services, synthetic pipelines, model registries, hard-coded answer routes, and accumulated datasets were removed.

## Setup

1. Install Node.js 20+, npm, and .NET 8 SDK.
2. Copy `.env.example` to `.env` and set `OPENAI_API_KEY`.
3. Run `npm install`.
4. Start both processes with `powershell -ExecutionPolicy Bypass -File scripts/start-showwhere.ps1`.

For separate terminals, use `npm run dev:api` and `npm run run:windows`.

## Data

Developer-mode feedback starts empty. New O/X/completion records are written under `training/`; `.env` and screenshots/API credentials must never be committed.
