# Legacy v1 preservation

The migration command copies original O/X feedback, coordinate corrections, and E2E history here locally without deleting or rewriting their source files. These records can contain machine-specific or personal text and are ignored by Git.

`semantic-review-queue.jsonl` contains coordinate-free proposals only, but remains local until a developer reviews each proposal in Teaching Mode. Migration never writes to `training/gold`.
