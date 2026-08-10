# Developer correction mode

Developer correction mode collects answer preferences and verified corrections while ShowWhere is being developed. It improves repeated requests immediately without changing provider model weights online.

This mode belongs to the `ai-learning` branch. The production `window-back-kyung` branch intentionally excludes it. See [learning branch workflow](learning-branch-workflow.md) for promoting validated accuracy improvements without shipping developer controls or private datasets.

## Workflow

1. Run a normal guidance request.
2. Mark every assistant response with `O 정답` or `X 수정`. The answer text, goal, context, action, and target are appended immediately to `answer-feedback.jsonl`.
3. `X 수정` opens a draft linked to that exact answer. Optionally enter the intended meaning and a free-form developer comment.
4. Select `정답 영역 선택` and drag around the correct control. Repeating selection replaces the in-memory draft; it does not write a correction or screenshot.
5. Inspect the highlighted preview and selection summary, then select `저장`.
6. Only the explicit save writes the correction, selected bounds, optional screenshot, normalized visual target, and matching UI Automation signature.

The raw developer comment is retained. A whitespace-normalized version and issue tags such as `wrong_intent`, `wrong_application`, `wrong_scope`, `wrong_target`, and `overlay_missing` are stored alongside it so later cleanup never destroys the original label.

The next exact request first applies the saved intent. A saved target is reused only when a live candidate on the current computer matches its semantic signature. Process and UI scope boundaries are strict, so a `browser_content` correction cannot be substituted with a Chrome address-bar (`browser_chrome`) candidate. Stored pixel coordinates are training labels and are never blindly replayed on another screen.

## Dataset location

```text
%LOCALAPPDATA%\ShowWhere\training\
├─ answer-feedback.jsonl
├─ corrections.jsonl
└─ screenshots\
   └─ <correction-id>.jpg
```

Each JSONL line is an independent schema-versioned record. `answer-feedback.jsonl` is append-only and records every explicit O/X choice even when an X correction draft is later cancelled. The screenshot path in `corrections.jsonl` is relative to the training directory, which makes the directory portable as one dataset.

Create model-oriented exports at any time:

```powershell
npm run export:corrections
```

This writes timestamped `answer-preferences.jsonl`, `intent-corrections.jsonl`, `visual-grounding.jsonl`, and `summary.json` files under `training\exports`. The preference file contains O/X labels for every evaluated answer. The intent file contains exact query-to-meaning pairs. The grounding file pairs each saved screenshot and instruction with a normalized click target and the rejected previous target.

Important fields:

- `originalGoal`: the exact developer query.
- `effectiveGoal`: the goal used by the guidance engine at correction time.
- `correctedIntent`: optional developer-written meaning.
- `context`: application, window title, locale, and platform.
- `previous*`: the rejected action, target, label, and bounds.
- `selectedBounds`: physical-pixel developer selection.
- `normalizedVisualTarget`: selection normalized to the captured virtual desktop.
- `correctTarget`: stable UI Automation label, role, automation ID, process, browser scope, and container.
- `screenshotPath`: optional local JPEG paired with the record.
- `developerVerified`: distinguishes a human correction from inferred telemetry.
- `feedbackId`: links an X answer rating to its explicitly saved correction.
- `developerComment`, `refinedComment`, `issueTags`: lossless raw feedback plus structured cleanup.

## Privacy and training

Full-screen captures can contain private information. The correction editor exposes a checkbox that disables screenshot persistence. Semantic correction records are still saved when screenshots are disabled.

Do not fine-tune directly on every collected record. Review and deduplicate the dataset, split it by application and Windows version into train/validation/test sets, and retain hard negative examples such as browser address bar versus in-page search. Promote a model only when it improves task success and wrong-application rates on the untouched test split.
