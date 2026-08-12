# Development

Required secret in `.env`:

```dotenv
OPENAI_API_KEY=...
OPENAI_MODEL=gpt-5.6
```

Run `npm test`, `npm run typecheck`, and `npm run test:windows` before pushing. Start the app and API together with `scripts/start-showwhere.ps1`.

Web crawler validation:

```powershell
npm run crawl:smoke
npm run crawl -- --url https://example.com
```

The smoke command serves `fixtures/web-crawler-site` only inside the Node process and verifies menu state changes plus a four-step order tracking route without external website traffic.
