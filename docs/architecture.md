# ShowWhere architecture

## Semantic knowledge architecture

Runtime guidance and learning share semantic identifiers instead of persisted coordinates:

```text
live UIA candidates
  -> normalized labels and concepts
  -> task/concept/Gold retrieval
  -> purpose-based model router
  -> semantic target decision
  -> resolve semantic target against the current live candidate list
  -> highlight only a request-owned targetId
```

The source of truth consists of a multilingual concept dictionary, explicit task state graphs, and human-approved Semantic v2 Gold records. AI teaching output remains a draft until deterministic validation and developer approval. Screenshots, UI snapshots, raw feedback, and unapproved drafts remain local and are ignored by Git.

Model routing is role-based: guide, fast, reasoning, vision, embedding, learning generator, and independent judge. Every provider response is runtime validated; unavailable or malformed models use a bounded fallback, and raw provider errors never reach the desktop client.

ShowWhere is a Windows-only desktop guidance application with a local Node backend.

## Runtime flow

```text
Floating WPF assistant
  -> GuidanceViewModel
  -> WindowsUiObserver
  -> normalized UiCandidate list + local CandidateRegistry
  -> Windows Settings catalog and WindowsFastPathResolver when possible
  -> verified developer correction memory before normal resolution
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

## Developer correction memory

`JsonlDeveloperCorrectionStore` appends every explicit answer O/X rating and persists separately confirmed intent and target corrections under the user's local application-data directory. An X rating and its correction are linked but saved as separate records: selection remains an in-memory preview until the developer presses Save. The runtime applies an exact intent correction before observation and then attempts to match a verified target against the new observation. A match requires compatible process and source scope plus a stable label or Automation ID; recorded coordinates are never replayed directly. `DeveloperRegionSelectionService` provides the full-virtual-desktop drag surface used to generate visual grounding labels. See [developer corrections](developer-corrections.md) for the dataset schema and privacy rules.

## Backend

```text
Windows app -> local ShowWhere backend -> Featherless
```

The backend owns all provider credentials. DeepSeek handles semantic candidate decisions. UI-TARS uses its native action protocol for screenshot grounding, with Qwen VL models as fallbacks.

All requests and decisions are runtime validated. A semantic highlight can reference only a candidate ID from the current observation. A visual highlight requires a matching screenshot and normalized in-bounds coordinates.

## Contracts

TypeScript Zod contracts live in `src/contracts`. Their C# representation lives in `ShowWhere.Core`. Both currently accept only the `windows` platform.
