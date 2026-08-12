# ShowWhere codebase map

## Top-level directories

| Path | Responsibility |
|---|---|
| `apps/windows/` | .NET 8 WPF product, UI Automation observation, overlay, API client, session state, and Developer Teaching UI. |
| `services/api/` | Node/TypeScript `/api/guide` backend, configuration, legacy Featherless provider, and Brain v2 adapter. |
| `services/local-ai/` | Python localhost inference service for the four pinned specialist models. |
| `src/contracts/` | Language-neutral API semantics represented as strict Zod request/decision contracts. |
| `src/guide-api/` | Provider interface, request validation, safety/context guard, and mock provider. |
| `src/brain-v2/` | New semantic reasoning, memory, outcome, event, and trajectory contracts/implementations. |
| `learning/` | Existing synthetic pipeline and new ShowWhereBench runner/analysis dependencies. |
| `training/` | Existing developer-approved answer feedback/corrections shared between development PCs. |
| `data/brain-v2/` | Private runtime events, memory, evaluations, and exports; created locally and ignored by Git. |
| `fixtures/` | Safe synthetic benchmark inputs that may be committed. |
| `config/` | Pinned model manifest and future ShowWhere model registry. |
| `scripts/` | Model download/start/check, teaching import, smoke test, and dataset export commands. |

## Windows application

### `ShowWhere.Desktop`

- `App.xaml(.cs)`: composition root. Creates observer, API client, overlay, correction store, view model, and windows.
- `GuidanceViewModel.cs`: central user workflow. It accepts the question, creates a `TaskSession`, observes the desktop, checks clarification and developer corrections, tries Windows fast paths, calls the backend when needed, resolves the returned target, displays the overlay, watches state changes, and handles O/X feedback plus teaching saves.
- `GuidancePanelWindow.xaml(.cs)`: movable chat/developer panel and bindings.
- `FloatingAssistantWindow.xaml(.cs)` and `AssistantGlyph.xaml(.cs)`: floating entry control.
- `AssistantPositionStore.cs`: persistent assistant-window position.
- `WindowCaptureProtection.cs`: prevents ShowWhere's own UI from polluting captures where supported.

### `ShowWhere.Core`

- `Contracts.cs`: C# equivalents of session, candidate, request, decision, bounds, and visual-target contracts.
- `TaskSessionStateMachine.cs`: task status and step progression.
- `ContractValidator.cs`: protects the app from malformed or unsafe decisions.
- `DeveloperCorrections.cs`: JSONL correction/feedback store, matching, comment refinement, and project `training/` discovery.

### `ShowWhere.WindowsAutomation`

- `WindowsUiObserver.cs`: collects actual controls through Microsoft UI Automation.
- `WindowsScreenCapture.cs`: captures the virtual desktop and reports its coordinate bounds.
- `WindowsObservation.cs`: observation/snapshot representation and state registry.
- `CandidateNormalizer.cs`: converts native UIA rectangles/metadata into stable candidates.
- `WindowsCandidatePrioritizer.cs`: filters chrome/noise and prioritizes actionable controls.
- `BrowserCandidateScopeClassifier.cs`: distinguishes browser chrome, page content, and application search surfaces.
- `WindowsFastPathResolver.cs`: deterministic Windows task routing before an AI call.
- `WindowsSettingsCatalog.cs`: known Windows settings/tasks and expected navigation concepts.
- `WindowsGoalClarificationResolver.cs`: asks when a Windows request has materially different interpretations.
- `WindowsChangeMonitor.cs`: waits for and identifies a real post-click observation change.
- `WindowsSystemOutcomeResolver.cs`: decides whether a known Windows system goal completed.

### `ShowWhere.Overlay`

- `HighlightOverlayWindow.cs`: topmost, click-through highlight and guidance card drawn in virtual-screen coordinates.
- `OverlayPlacementCalculator.cs`: keeps the annotation visible and away from the target where possible.
- `DeveloperRegionSelectionService.cs`: explicit developer drag selection; it returns a preview and does not save until the teaching UI confirms.

### `ShowWhere.ApiClient`

- `GuideApiClient.cs`: serializes C# `GuideRequest`, calls `/api/guide`, and deserializes the validated `GuideDecision`.

## Backend and AI

Request call chain:

```text
GuidanceViewModel
 -> GuideApiClient
 -> services/api/createApiServer
 -> handleGuideApiRequest
 -> createAiProvider
    -> legacy: FeatherlessProvider or MockAiProvider
    -> shadow/v2: BrainV2Provider
       -> JsonMemoryProvider + LocalModelClient /memory/embed
       -> LocalModelClient /brain/decide
       -> LocalModelClient /rerank
       -> LocalModelClient /ground only if needed
       -> JsonlLearningEventRecorder
 -> contextualDecisionGuard + contract validation
 -> GuidanceViewModel resolves desktop coordinates
 -> HighlightOverlayWindow
```

- `services/api/src/config.ts`: server-only environment parsing. The API key never goes to WPF.
- `FeatherlessProvider.ts`: preserved legacy teacher/fallback, including DeepSeek text decision and configured vision adapters.
- `guidePrompt.ts`: legacy structured prompt construction.
- `uiTarsAdapter.ts`: normalizes legacy visual-model output.
- `BrainV2Provider.ts`: new orchestration and legacy failure boundary.
- `LocalModelClient.ts`: retry, timeout, optional bearer authentication, and response validation for the local/remote specialist service.

The Python worker reads `config/models.json`; no model ID is scattered through product routing code. It exposes `/health`, `/memory/embed`, `/brain/decide`, `/rerank`, and `/ground`. Models are lazy-loaded and evicted so only one heavyweight role is resident on a constrained development PC.

## Learning and feedback

The original `training/` flow remains the user-facing teaching source. A saved correction is explicit human input and can be imported as immutable `human_gold`. Brain v2 records every attempted v2 step separately in `data/brain-v2` and only promotes environment-verified or explicit gold records.

```text
O/X + saved developer correction
 -> training/*.jsonl (tracked developer source)
 -> import-developer-teaching.py
 -> private human_gold + semantic memory

runtime step
 -> scrubbed event
 -> next observation/outcome verification
 -> verified_real or raw failure
 -> export-brain-v2.py
 -> reranker / Brain SFT / trajectory / vision datasets
```

`learning/cli.ts` and `learning/pipeline.ts` remain the existing synthetic generation/judging/review workflow. Synthetic output does not outrank human or environment-verified data and is not automatically promoted to production.

## Where to change behavior

- Wrong semantic next step: Brain prompt/service or a verified Developer Teaching record.
- Correct concept but wrong visible control: reranker data/routing and candidate metadata.
- No accessible candidate: POINTS grounding adapter and screenshot coordinate normalization.
- Highlight shifted or hidden: Windows capture bounds, candidate normalization, or overlay placement—not the language model.
- Windows setting navigation error: `WindowsSettingsCatalog`/`WindowsFastPathResolver` first; Brain v2 remains fallback for unknown phrasing/state.
- Training-quality bug: contracts, scrubber, outcome verifier, authority rules, or exporters—never solve it by blindly learning raw predictions.

