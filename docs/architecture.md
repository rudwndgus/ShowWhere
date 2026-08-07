# ShowWhere architecture

ShowWhere is now Windows-first, with the Chrome extension retained as an optional browser adapter. Both clients use the same normalized guidance contract and the same secure backend.

## Windows guidance loop

```text
Floating assistant / compact panel
  -> foreground non-ShowWhere window
  -> bounded Microsoft UI Automation observation
  -> normalized Windows UiCandidate list + local element registry
  -> known Windows goal? taskbar/settings fast-path decision
  -> otherwise GuideRequest { platform: "windows" }
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

Known Windows goals such as Network, Volume, Bluetooth, Display/Brightness, Clock, Battery, Notifications, Windows Update, Accessibility, and Settings use a deterministic fast path before any provider request. When the foreground application is unrelated, the observer initially scans only the small Windows taskbar tree. It expands to the complete foreground tree and Featherless only when the fast path cannot resolve a real visible candidate. The fast path never uses fixed coordinates; it still highlights only a validated candidate ID from the current machine.

The Windows observer also contributes a bounded overview of other open top-level windows. These overview candidates contain only application/title-bar context, not every descendant control. This lets the decision layer switch from an unrelated foreground app to an already open Photos, File Explorer, browser, or other relevant window before performing a deep scan. Photo, screenshot, Downloads, and Documents goals have deterministic shell routes; unknown goals can still use the validated window overview through the AI decision layer.

Camera-photo and screenshot routes are intentionally separate, and conflicting window titles are excluded from each route. UI Automation source keys include the owning process ID so a runtime ID reused by Chrome and File Explorer cannot resolve to the wrong application. Generic photo requests are clarified with source choices before observation. AI decisions may also return two to four validated `alternativeTargetIds`; both clients render these as buttons and guide only the option explicitly selected by the user.

Printer goals use a deterministic Windows route through Start, Settings, Bluetooth & devices, and Printers & scanners. The resolver emits only the deepest currently visible next target, excludes unrelated Network/Wi-Fi controls, and consults session facts so a control already selected during the task is not highlighted again.

The initial product scope is deliberately Windows-first. Built-in applications and standard Settings destinations are represented by an offline navigation catalog of multilingual intent aliases and visible UI labels. Known Windows goals do not fall through to the remote provider when a transient shell surface is still loading. The client detects visible Start/Search shell windows independently of ShowWhere's own foreground panel, retries local observation briefly, and either resolves a validated UIA target or reports a local observation problem without exposing a provider failure.

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
