# ShowWhere shared learning data

The `ai-learning` application writes its persistent learning records here so they can be committed and synchronized between development computers.

Tracked through Git:

- `answer-feedback.jsonl`: every explicit O/X answer rating.
- `corrections.jsonl`: corrections confirmed with the Save button.

Kept local and ignored by Git:

- `screenshots/`: full-screen captures that can contain account names, email addresses, API keys, or other private information.
- `exports/`: reproducible model-oriented exports.

After collecting records on one computer, review the JSONL diff and commit/push it on `ai-learning`. Pull `ai-learning` on the other computer before starting ShowWhere so its correction memory loads the shared records.

Set `SHOWWHERE_TRAINING_DIR` only when an alternate dataset directory is intentionally required.
