# Web navigation Knowledge and crawler

ShowWhere's crawler maps user-visible, interactive navigation rather than copying page text. It records states, safe actions, and observed next states with Playwright, normalizes duplicate controls, assigns semantic function labels, and builds a compact catalog that runs before GPT.

## First run

Install the browser once if Chromium or a local Microsoft Edge installation is unavailable:

```powershell
npm run crawl:install
```

Start a bounded crawl:

```powershell
npm run crawl -- --url https://example.com
```

Useful options:

```powershell
npm run crawl -- --url https://example.com --headed --max-depth 3 --max-states 30 --max-actions 12
npm run crawl -- --url https://example.com --locale en-US --channel msedge
npm run crawl:normalize -- --site example-com
npm run crawl:patterns
npm run crawl:smoke
```

Defaults are 30 unique states, depth 3, and 12 safe actions per state. `--headed` shows the browser for inspection. Each path starts in an isolated page and replays its preceding actions, preventing one explored branch from contaminating another.

## What is collected

- accessible name and normalized role;
- semantic page region and landmark/container;
- a coarse top/left/center/right/bottom region, never portable pixel coordinates;
- sanitized same-site URL path;
- state evidence before and after an action;
- `State -> Action -> Next State` transitions;
- successful, blocked, no-change, and failed observations;
- complete discovered navigation paths.

Selectors prioritize Playwright's user-facing role, accessible name, label, placeholder, and test ID. CSS IDs are only fallback hints; long DOM CSS/XPath chains are never generated.

## Semantic labeling

The checked-in taxonomy covers common website functions such as account, order history, tracking, returns, settings, search, and privacy. Exact rules label obvious controls first.

When `HF_TOKEN` is available, the crawler queries the current Hugging Face Hub for warm multilingual feature-extraction models. It benchmarks up to four compatible candidates on Korean/English UI synonym groups, measuring semantic separation and latency. The best compatible model embeds unknown controls against the taxonomy. The chosen model, candidates, compatibility, quality score, latency, and errors are written into the catalog for reproducibility.

Set `CRAWLER_HF_MODEL_CANDIDATES` only when a controlled shortlist is required. If Hugging Face is unavailable, normalization still completes with deterministic rules and marks unknown functions as generated site-local labels.

## Runtime order

```text
developer-verified O/X/completion replay in the Windows client
  -> Windows Knowledge
  -> site-specific Web Knowledge
  -> cross-site common Web patterns (requires at least two supporting sites)
  -> conservative exact local matching
  -> Hugging Face/GPT fallback
```

The Web Knowledge resolver requires the live browser URL/domain for a site-specific catalog, compares only current `browser_content` candidates, and requires a clear score margin. Common patterns are lower confidence and activate only after the same semantic path appears on at least two sites. Stored coordinates are never reused.

## Safe crawling boundary

The crawler respects `robots.txt`, stays on the seed origin, dismisses dialogs, and does not automatically click controls associated with purchases, checkout, payment, submission, posting, sending, deletion, cancellation, logout, or unsubscribe. Password/file inputs and state-changing checkboxes/switches are also excluded. Blocked and failed relationships remain useful negative training observations.

Use test accounts only. Authentication state may be supplied with `--storage-state`, but that file can contain impersonation-capable cookies and must stay under `playwright/.auth/` or another ignored private directory. Crawling does not bypass CAPTCHAs, access controls, rate limits, or site terms.

## Git workflow

```powershell
git pull
npm run crawl -- --url https://target.example
git add knowledge/web
git commit -m "add target.example web navigation knowledge"
git push
```

On another computer, pull first and crawl again. Raw runs are append-only and uniquely named; the normalization stage loads all runs and rebuilds the site catalog and common patterns. All learning artifacts, including raw data, drafts, legacy records, exports, failures, and future learning snapshots, are committed. Only credentials such as `.env`, API tokens, cookies, authenticated browser state, and private form secrets remain excluded.

Existing ShowWhere O/X/end feedback continues to record actual runtime success and failure under `training/`. Together, those human-verified records and `knowledge/web/training/pipeline-events.jsonl` provide future datasets for intent classification, UI semantic labeling, candidate ranking, and next-state prediction.
