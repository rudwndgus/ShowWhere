# Semantic v2 knowledge boundaries

- `raw/`: untrusted O/X and session events; never used as Gold automatically.
- `drafts/`: AI-generated or migrated semantic drafts awaiting review.
- `gold/`: only schema-valid, human-approved one-step guidance truth.
- `concepts/`: reusable multilingual UI meaning and confusable concepts.
- `knowledge/task-playbooks/`: deterministic state graphs for reaching a task outcome.
- `evidence/`: local/runtime UI snapshots and screenshots; coordinates never enter semantic Gold.
- `evaluation/`: held-out benchmark cases; never used for training or retrieval examples.
- `legacy/v1/`: immutable copies of the original coordinate-oriented records.

Runtime retrieval uses a small relevant subset of concepts, a likely task node, and related Gold steps. It never injects the entire dataset into a provider request.
