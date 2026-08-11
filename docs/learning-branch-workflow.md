# Learning branch workflow

ShowWhere separates production behavior from data collection:

- `window-back-kyung` is the production Windows application. It contains no developer rating or correction UI.
- `ai-learning` is based on `window-back-kyung` and adds O/X answer feedback, correction drafts, verified saves, and dataset export.

Use `ai-learning` during development to collect examples under the repository `training` directory and improve deterministic rules, prompts, candidate validation, and model selection. Commit reviewed JSONL records to synchronize correction memory between development computers. Full-screen screenshots and generated exports remain ignored because they can contain private information.

After an improvement passes the untouched evaluation split:

1. Implement and validate the runtime accuracy change on `ai-learning`.
2. Keep developer-only UI, local datasets, and exporters out of the production patch.
3. Cherry-pick or merge only the validated runtime change and its tests into `window-back-kyung`.
4. Run Windows, API, type, and lint checks on the production branch.
5. Publish the new Windows build from `window-back-kyung`.

This makes learning iterative without shipping private training data or developer controls to end users.
