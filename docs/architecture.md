# ShowWhere 2.0 architecture

`GuidanceViewModel` observes the current Windows UI and always attaches a full virtual-desktop screenshot. `GuideApiClient` sends that request to the local TypeScript API. `OpenAiGuideProvider` calls the OpenAI Responses API with image input and a strict JSON Schema response. The API validates the selected candidate and confidence before returning it.

For semantic targets, `CandidateRegistry` rereads the live UI Automation element immediately before display and rejects it if its name or automation ID no longer matches the observed candidate. Pixel-only targets use normalized full-screenshot coordinates. `HighlightOverlayWindow` renders the final marker above ordinary windows.

Developer feedback remains an annotation mechanism, not a second decision engine. The branch begins with no inherited gold, corrections, completions, model memories, or synthetic examples.
