# ShowWhere 2.0 architecture

`GuidanceViewModel` observes the current Windows UI. Before using a remote model it replays developer-verified targets and completion states from `training/`. `GuideApiClient` sends unresolved requests to the local TypeScript API.

The API uses a cost-ordered router:

1. `LocalGuideResolver` selects only unique, visible, interactive, semantically explicit targets (zero model calls).
2. When `HF_TOKEN` is configured, `HuggingFaceEmbeddingClient` ranks otherwise unresolved candidates. A result is used only when its absolute score and margin over the runner-up exceed configured thresholds.
3. `OpenAiGuideProvider` handles novel intent reasoning, troubleshooting, ambiguous state, and visual grounding. It calls the OpenAI Responses API with a compact candidate set, low-detail full-desktop image, and strict JSON Schema output.

For semantic targets, `CandidateRegistry` rereads the live UI Automation element immediately before display and rejects it if its name or automation ID no longer matches the observed candidate. Pixel-only targets use normalized full-screenshot coordinates. `HighlightOverlayWindow` renders the final marker above ordinary windows.

Browser/window chrome is excluded from automatic local/HF selection unless the user explicitly asks for controls such as the address bar, minimize, maximize, or close.

Developer O/X/end feedback is stored in repository-local JSONL files under `training/`. Similar-intent replay compares action and target concepts so opening YouTube Music does not become equivalent to searching or playing a song inside it. Stored coordinates are never treated as portable truth; live UIA candidates are resolved again on each computer.
