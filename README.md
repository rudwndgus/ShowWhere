# ShowWhere 2.0

ShowWhere is a Windows 10/11 guidance app. It captures the complete desktop, collects live Microsoft UI Automation candidates, and highlights one verified next action. Local Windows/Web Knowledge and human-verified memory handle known paths first; Hugging Face and OpenAI GPT handle unresolved states. The user-facing app never clicks automatically.

## Architecture

```text
User goal
  -> WPF desktop captures full screen + UI Automation candidates
  -> local Node API POST /api/guide
  -> Windows Knowledge / Web Navigation Knowledge / local matching
  -> Hugging Face semantic routing + OpenAI Responses API fallback
  -> contract/confidence/target validation
  -> live UI element identity revalidation
  -> always-on-top overlay
```

The separate Playwright crawler builds Git-tracked website `State -> Action -> Next State` maps. See [Web Knowledge](docs/web-knowledge.md) for crawling, normalization, Hugging Face model selection, safety, and multi-computer Git synchronization.

## Setup

1. Install Node.js 20+, npm, and .NET 8 SDK.
2. Copy `.env.example` to `.env` and set `OPENAI_API_KEY`.
3. Run `npm install`.
4. Start both processes with `powershell -ExecutionPolicy Bypass -File scripts/start-showwhere.ps1`.

For separate terminals, use `npm run dev:api` and `npm run run:windows`.

## Web crawling

```powershell
npm run crawl -- --url https://example.com
```

Use `npm run crawl:install` once if no compatible Chromium/Edge browser is installed. Every generated learning artifact under `training/` and `knowledge/web/` is intended to be committed. Only credentials and reproducible runtime/build files stay excluded.

## Data

Developer-mode O/X/completion records, raw events, drafts, legacy records, exports, and future learning snapshots are written under `training/` and tracked by Git. Crawl observations, normalized catalogs, navigation graphs, common patterns, failures, and pipeline events under `knowledge/web/` are also tracked. `.env`, API credentials, cookies, authenticated browser state, and private form secrets must never be committed.
