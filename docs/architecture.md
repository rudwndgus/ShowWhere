# ShowWhere architecture

ShowWhere is a Windows-only desktop guidance application with a local Node backend.

## Runtime flow

```text
Floating WPF assistant
  -> GuidanceViewModel
  -> WindowsUiObserver
  -> normalized UiCandidate list + local CandidateRegistry
  -> Windows Settings catalog and WindowsFastPathResolver when possible
  -> otherwise POST /api/guide
  -> DeepSeek candidate decision
  -> full-screen capture when semantic candidates fail
  -> UI-TARS native click-coordinate grounding
  -> validated targetId or normalized visualTarget
  -> DPI-aware physical-pixel overlay
  -> user click monitoring
  -> next observation and guidance step
```

## Windows projects

- `ShowWhere.Desktop`: WPF composition, extension-inspired floating UI, chat, session loop, and diagnostics.
- `ShowWhere.Core`: C# contracts, validation, confidence policy, and task state transitions.
- `ShowWhere.WindowsAutomation`: foreground discovery, UIA traversal, candidate registry, browser scope classification, screen capture, and click monitoring.
- `ShowWhere.Overlay`: transparent click-through topmost overlay and multi-monitor DPI placement.
- `ShowWhere.ApiClient`: validated HTTP access to the local backend.
- `ShowWhere.Windows.Tests`: contract, navigation, observation, and native overlay tests.

Chrome and Edge are handled as ordinary Windows applications. Native UI Automation distinguishes browser chrome from document content; full-screen vision is used when browser accessibility data is insufficient. No browser extension or injected page code is used.

## Windows Settings guidance

`WindowsSettingsCatalog` maps common Windows 10/11 goals to the official Settings category hierarchy and documented `ms-settings:` page identifiers. It contains Korean and English UI aliases, but never highlights a catalog coordinate directly: every step must match a visible UI Automation candidate collected from the current computer. `WindowsCandidatePrioritizer` removes title-bar caption controls such as minimize, maximize, restore, and close before either the deterministic resolver or AI sees a Settings request.

The catalog provides stable navigation knowledge while the live-candidate requirement accounts for Windows version, edition, device, language, and policy differences. When no unique visible target exists, ShowWhere asks the user or falls back to full-screen visual grounding instead of fabricating a location.

## Backend

```text
Windows app -> local ShowWhere backend -> Featherless
```

The backend owns all provider credentials. DeepSeek handles semantic candidate decisions. UI-TARS uses its native action protocol for screenshot grounding, with Qwen VL models as fallbacks.

All requests and decisions are runtime validated. A semantic highlight can reference only a candidate ID from the current observation. A visual highlight requires a matching screenshot and normalized in-bounds coordinates.

## Contracts

TypeScript Zod contracts live in `src/contracts`. Their C# representation lives in `ShowWhere.Core`. Both currently accept only the `windows` platform.
