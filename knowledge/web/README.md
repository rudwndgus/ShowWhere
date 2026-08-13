# ShowWhere Web Knowledge data

This directory is intentionally tracked by Git. Every generated crawl, normalization, Knowledge, pattern, failure, and training artifact belongs in Git so another development computer receives the same learning state after `git pull`.

```text
raw/<site>/<run>.json.gz    append-only lossless-compressed crawl observations
normalized/<site>.json.gz   merged and lossless-compressed site graph
catalogs/<site>.json        runtime Web Knowledge catalog
patterns/common.json        cross-site semantic path patterns
training/*.jsonl            append-only pipeline and future model-training events
```

The current crawler does not generate screenshots or complete HTML, but if those are added as learning artifacts later they are tracked here as well. API keys, cookies, authentication storage, and form secrets are credentials rather than learning data and belong only in `.env`, `.crawl/`, `playwright/.auth/`, or `knowledge/web/private/`.

Each raw run has a unique filename, so data gathered on another computer is added instead of replacing earlier observations. Always `git pull` before crawling. Normalization reads both legacy `.json` and compressed `.json.gz` runs and deterministically rebuilds the catalog from the merged history. `npm run crawl:compress` safely converts legacy raw/normalized JSON after validating each gzip file. Runtime catalogs remain uncompressed for immediate startup.

Do not hand-edit raw runs. Correct a semantic rule in `src/web-knowledge/taxonomy.ts`, rerun `npm run crawl:normalize -- --site <site-id>`, and commit the rebuilt files.
