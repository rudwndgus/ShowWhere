# Migration plan

## Completed

- Phase 0 repository audit
- Shared runtime-validated contracts
- TaskSession state transitions
- Browser observation normalization and local target registry
- Overlay geometry separation
- Provider/client abstractions
- Deterministic mock `/api/guide` pipeline
- Extension request/decision integration
- Contract, filtering, clickable-target, session, and endpoint safety tests
- Add a separately deployed backend service.
- Implement the HTTP `POST /api/guide` route using the existing contracts.
- Add `FeatherlessProvider` on the server only.
- Read provider configuration and secrets from server environment variables.
- Add timeout, bounded retries, response-size limits, and sanitized error handling.
- Keep deterministic mock mode and the offline extension fallback.
- Add CORS allowlisting and extension-side response validation.

## Native Windows phase completed

- WPF floating assistant and compact guidance panel
- Microsoft UI Automation observation and local target registry
- Click-through, DPI-aware Windows highlight overlay
- Browser-compatible Windows contracts and backend client
- Event-driven plus bounded polling change observation
- Candidate, contract, overlay, API, and task-session tests

## Next

- Hash meaningful UI observations.
- Re-observe after user actions and expected changes.
- Debounce noisy mutations and prevent duplicate decisions.
- Restore active sessions safely after navigation where possible.
- Version one source schema and generate TypeScript/C# contract bindings.
- Add signed packaging and startup/update policy after native UX validation.
- Design the optional DOM-to-desktop Native Messaging bridge.
