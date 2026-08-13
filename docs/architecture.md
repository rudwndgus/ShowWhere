# ShowWhere 2.0 architecture

`GuidanceViewModel` observes the current Windows UI. Before using a remote model it replays developer-verified targets and completion states from `training/`. `GuideApiClient` sends unresolved requests to the local TypeScript API.

The API uses a cost-ordered router:

1. `WindowsKnowledgeResolver` resolves known Windows settings and troubleshooting routes.
2. `WebKnowledgeResolver` searches Git-tracked site navigation maps and cross-site patterns against live browser candidates.
3. `LocalGuideResolver` selects only unique, visible, interactive, semantically explicit targets (zero model calls).
4. When `HF_TOKEN` is configured, `HuggingFaceEmbeddingClient` ranks otherwise unresolved candidates. A result is used only when its absolute score and margin over the runner-up exceed configured thresholds.
5. `OpenAiGuideProvider` handles novel intent reasoning, troubleshooting, ambiguous state, and visual grounding. It calls the OpenAI Responses API with a compact candidate set, full-desktop image, and strict JSON Schema output. The cost-aware model router uses Luna with no reasoning for one dominant live candidate, Terra with low reasoning for ordinary ambiguous screens, and Sol with low reasoning only when no live UI Automation candidate exists and pure visual grounding is required. It selects one tier per request rather than chaining model calls.

The independent `services/crawler` Playwright pipeline creates Web Knowledge. It stores sanitized observations, normalizes semantic controls, records state transitions, builds navigation routes, and derives shared patterns only after multiple sites support a sequence. Runtime never reuses crawler coordinates or selectors directly; it matches semantic names and roles against the current live UI Automation candidates.

After local resolution misses, Hugging Face and GPT begin concurrently. A confident valid HF target may win, but a slow or unavailable HF provider never postpones an already available GPT decision.

For semantic targets, `CandidateRegistry` rereads the live UI Automation element immediately before display and rejects it if its name or automation ID no longer matches the observed candidate. Pixel-only targets use normalized full-screenshot coordinates. `HighlightOverlayWindow` renders the final marker above ordinary windows.

Browser/window chrome is excluded from automatic local/HF selection unless the user explicitly asks for controls such as the address bar, minimize, maximize, or close.

Developer O/X/end feedback is stored in repository-local JSONL files under `training/`. Similar-intent replay compares action and target concepts so opening YouTube Music does not become equivalent to searching or playing a song inside it. Stored coordinates are never treated as portable truth; live UIA candidates are resolved again on each computer.

O creates positive `human_gold` memory, X creates persistent negative memory for the rejected target, saved corrections preserve developer comments and replacement target signatures, and end creates completion memory. Completion replay requires specific evidence from the current context/UI candidates rather than a generic application title.
