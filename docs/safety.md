# Safety and privacy

- The complete desktop screenshot and visible UI metadata are sent to OpenAI for each decision.
- API requests set `store: false`.
- The key stays server-side in ignored `.env` and is never sent to the Windows client.
- ShowWhere highlights but does not click controls or execute arbitrary actions.
- Low-confidence or stale targets are rejected instead of displayed.
- The developer crawler is separate from the user-facing guide. It honors `robots.txt`, stays on one origin, and blocks purchase, payment, submission, deletion, cancellation, logout, password, and file-upload actions.
- All generated learning artifacts are Git-tracked for multi-computer development. Credentials are not learning data: `.env`, API keys, cookies, authenticated browser state, and private form secrets remain ignored.
