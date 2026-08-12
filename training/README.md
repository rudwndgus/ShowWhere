# Fresh GPT feedback data

This directory intentionally starts empty on the `ShowWhere2.0` branch.
The Windows developer-mode O/X/completion controls create new JSONL records here at runtime.
No legacy provider, synthetic, or previous gold records are retained.

Runtime order is exact replay, conservative semantic-intent replay, optional Hugging Face candidate ranking, then GPT fallback.

- `corrections.jsonl`: human-corrected and O-approved target signatures used by runtime memory.
- `answer-feedback.jsonl`: every developer O/X evaluation.
- `completions.jsonl`: developer-verified task completion states used to stop repeated guidance.

These JSONL records stay repository-local so approved learning can be synchronized between development computers. Screenshots remain ignored because they can contain private information. API keys, tokens, cookies, passwords, raw screenshots, and private chain-of-thought must never be written here.

An O record is replayed only when its semantic action/target intent is compatible and its target signature can be resolved again from the current live UI. X records are negative feedback and never become positive runtime memory automatically.

Runtime guarantees:

- O immediately creates a `human_gold` correction and is available in the current process and after restart.
- Similar wording can reuse O when action and target concepts remain compatible; different actions remain separate.
- X is reloaded as negative memory and removes the same rejected target ID for the same intent/application.
- A newer O for that target supersedes the older X.
- Saved correction comments retain raw text, normalized text, issue tags, semantic target signature, and learning labels.
- End creates a verified completion record. Completion replay requires specific live evidence; generic titles such as `Settings` are insufficient.
- Coordinates are observations only. Every replay resolves the semantic target against the current live UI.
