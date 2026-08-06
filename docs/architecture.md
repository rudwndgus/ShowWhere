# ShowWhere architecture

ShowWhere is now Windows-first, with the Chrome extension retained as an optional browser adapter. Both clients use the same normalized guidance contract and the same secure backend.

## Windows guidance loop

```text
Floating assistant / compact panel
  -> foreground non-ShowWhere window
  -> bounded Microsoft UI Automation observation
  -> normalized Windows UiCandidate list + local element registry
  -> GuideRequest { platform: "windows" }
  -> ShowWhere backend POST /api/guide
  -> MockAiProvider or server-only FeatherlessProvider
  -> runtime-validated GuideDecision
  -> targetId lookup in the current Windows registry
  -> DPI-aware, click-through highlight overlay
  -> user performs the action
  -> debounced UIA events + bounded low-frequency observation
  -> next guidance step
```

The model never receives an `AutomationElement`, selector, window handle, or authority to click. It can select only a candidate ID supplied by the current observation.

## Windows projects

- `ShowWhere.Desktop`: WPF application composition, floating assistant, panel, commands, and multi-step session orchestration.
- `ShowWhere.Core`: browser-compatible contracts, runtime validation, confidence policy, unknown-target rejection, and task transitions.
- `ShowWhere.WindowsAutomation`: foreground-window discovery, bounded UIA traversal, candidate filtering/normalization, local registry, and meaningful-change monitoring.
- `ShowWhere.Overlay`: transparent topmost click-through overlay, monitor working-area placement, and DPI conversion.
- `ShowWhere.ApiClient`: backend-only HTTP client with serialization, validation, cancellation, timeout, bounded retry, and safe errors.

## Browser adapter

The React/Manifest V3 extension remains under `src/content`, `src/background`, and `public`. Its content script owns DOM observation and highlighting. Cross-origin backend traffic is sent through the extension service worker using a fixed endpoint; the content script cannot request arbitrary URLs.

Native Messaging is intentionally deferred. A later browser/desktop bridge can prefer DOM candidates for web content while the Windows adapter remains responsible for browser chrome and other native applications.

## Backend and secrets

```text
Windows app or extension -> ShowWhere backend -> Featherless
```

Only the Node backend reads `FEATHERLESS_API_KEY`. The Windows executable and extension contain a non-secret backend URL only. Both clients reject malformed decisions and target IDs absent from the current request.

## Contract ownership

The current TypeScript Zod schemas live in `src/contracts`; the Windows-compatible C# representation and validators live in `ShowWhere.Core`. Compatibility is verified through serialization and validation tests. A later phase can generate both representations from one versioned schema package without changing the adapter boundaries.
