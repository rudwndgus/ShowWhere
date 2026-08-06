# Safety boundaries

- The mock provider and future live providers may select only an ID supplied in the current `GuideRequest`.
- Platform code resolves the selected ID to current bounds. A stale or missing element is not highlighted.
- Low-confidence highlight decisions are changed to `ask_user`.
- Malformed provider responses and provider failures return concise safe fallbacks.
- ShowWhere never clicks page elements automatically.
- No shell commands, payment confirmation, deletion, or account removal can be executed by a provider decision.
- Candidate payloads use an attribute allowlist and exclude cookies, storage, authentication tokens, password values, and normal form values.
- Browser URLs omit query strings and fragments before entering `ApplicationContext`.
- The extension reads only the public `VITE_SHOWWHERE_GUIDE_API_URL`; it never receives or stores `FEATHERLESS_API_KEY`.
- `FeatherlessProvider` exists only in the separately built Node service and reads its base URL, model, key, timeout, retry, and output-limit settings from server environment variables.
- The HTTP endpoint uses an exact origin allowlist, request-size limit, no-store responses, and sanitized user-facing failures.
- Live and mock decisions pass through the same runtime request/decision schemas and unknown-target check.
- The Windows client never invokes UI Automation action patterns; it resolves a validated target ID only to fresh bounds and waits for the user to act.
- Windows observation is limited to the active non-ShowWhere window, a bounded tree traversal, and at most 100 normalized candidates.
- Password values, edit values, hidden application trees, clipboard contents, screenshots, raw AutomationElements, and window handles are never sent to the backend.
- Windows overlays use no-activate and click-through window styles so they do not intercept target interaction.
