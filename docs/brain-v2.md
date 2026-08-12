# ShowWhere 2.0 Brain v2

## Purpose

Brain v2 is a guidance system, not a desktop-control agent. It observes UIA/DOM candidates, decides the next semantic action, selects or visually grounds one target, highlights it, then verifies the next observation. The user remains in control.

## Runtime flow

```text
GuideRequest
  -> verified semantic memory (BGE-M3, lexical fallback)
  -> high-authority reusable experience OR Domyn task-state decision
  -> BGE candidate reranker over visible/clickable UIA or DOM controls
  -> POINTS-GUI-G only when structured candidates are absent/uncertain
  -> validated GuideDecision
  -> overlay/highlight (existing Windows application)
  -> next GuideRequest verifies expected evidence
  -> scrubbed LearningEventV2 + outcome update + trajectory
```

The existing Featherless/mock provider remains intact. `SHOWWHERE_BRAIN_MODE` controls migration:

- `legacy`: existing provider only; default and safest production setting.
- `shadow`: return legacy output and evaluate Brain v2 asynchronously.
- `v2`: return Brain v2 output, falling back to legacy on specialist failure.

No Chrome Extension code was reintroduced. The repository's current Windows-only product, overlay, UI Automation collection, session flow, and Developer Teaching UI remain unchanged.

## Code map

- `src/brain-v2/contracts.ts`: strict Zod contracts for semantic decisions, memory, reranking, grounding, learning events, and trajectories.
- `src/brain-v2/interfaces.ts`: replaceable Brain, retriever, reasoner, reranker, grounder, verifier, and recorder boundaries.
- `src/brain-v2/localMemory.ts`: project-local semantic memory store with BGE-M3 embedding and lexical failure fallback.
- `src/brain-v2/outcomeVerifier.ts`: compares expected evidence/state with the next real observation.
- `src/brain-v2/learningEventRecorder.ts`: scrubbed append-only JSONL event/trajectory/outcome journal.
- `services/api/src/providers/BrainV2Provider.ts`: memory -> reasoner -> reranker -> vision routing, immediate experience reuse, outcome update, legacy fallback.
- `services/api/src/providers/LocalModelClient.ts`: stable HTTP boundary; changing localhost to HTTPS needs configuration only.
- `services/local-ai/showwhere_ai_service.py`: lazy, one-model-at-a-time local inference endpoints.
- `config/models.json`: exact model IDs, pinned Hugging Face revisions, roles, directories, and runtimes.
- `config/model-registry.json`: future ShowWhere model metadata and human-only promotion policy.
- `learning/showwhereBench.ts`: legacy/v2 product benchmark runner.
- `scripts/export-brain-v2.py`: verified-only SFT/reranker/vision/trajectory exports and analysis.

## Model installation and startup

The default external model root is `C:\ShowWhere_Models`. Weights and the Python virtual environment are outside Git.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/download-models.ps1 -ModelRoot C:\ShowWhere_Models
powershell -ExecutionPolicy Bypass -File scripts/check-models.ps1 -ModelRoot C:\ShowWhere_Models
powershell -ExecutionPolicy Bypass -File scripts/start-local-ai.ps1 -ModelRoot C:\ShowWhere_Models -Install
```

Run the existing TypeScript API in another terminal:

```powershell
$env:SHOWWHERE_BRAIN_MODE='v2'
$env:SHOWWHERE_AI_BASE_URL='http://127.0.0.1:8790'
npm.cmd run dev:api
```

For a remote server, use HTTPS and set the same `SHOWWHERE_AI_TOKEN` on the client/API and AI service. The service enforces an optional bearer token, request-size limit, timeout through the client, and `/health`. Put rate limiting and TLS at the reverse proxy before production exposure.

## Data lifecycle and authority

Runtime files live under `data/brain-v2/` inside the project so both development environments behave consistently. That directory is ignored because UI state can contain personal information. Only deliberately sanitized fixtures under `fixtures/` are shared by Git.

```text
events.jsonl / outcome-updates.jsonl / trajectories.jsonl
  -> secret scrub + environment outcome verification
  -> raw | rejected | verified
  -> authority: human_gold > verified_real > synthetic_validated > raw
  -> verified-only exporters
```

`human_gold` originates only from an explicit Developer Teaching save. AI drafts and drag-selection previews are not gold. `scripts/import-developer-teaching.py` copies approved records into the private gold directory and labels the source immutable. Runtime code never overwrites the original teaching record.

Exports:

```powershell
npm.cmd run brain-v2:import-teaching
npm.cmd run brain-v2:export
```

The exporter creates `learning-events.jsonl`, `reranker-training.jsonl`, `brain-sft.jsonl`, `trajectories.jsonl`, `vision-grounding.jsonl`, `eval-results.csv`, analysis JSON, and—with pandas/pyarrow installed—Parquet event/failure files. Raw predictions are excluded to prevent self-poisoning.

## Evaluation

`ShowWhereBench` contains known task, paraphrase, Korean/English cross-UI, ambiguity, wrong state, recovery, unknown task, icon-only, and UIA-failure scenarios. Every run is persisted privately with output, expected result, correctness, confidence, latency, and fallback.

```powershell
npm.cmd run bench:showwhere:legacy
npm.cmd run bench:showwhere:v2
npm.cmd run bench:showwhere
```

Model smoke tests must be run one role at a time on limited hardware:

```powershell
C:\ShowWhere_Models\.venv\Scripts\python.exe scripts/smoke-local-models.py reranker
C:\ShowWhere_Models\.venv\Scripts\python.exe scripts/smoke-local-models.py embedding
C:\ShowWhere_Models\.venv\Scripts\python.exe scripts/smoke-local-models.py brain
C:\ShowWhere_Models\.venv\Scripts\python.exe scripts/smoke-local-models.py grounder
```

Reported latency is measured wall time and never estimated.

## Distillation roadmap

1. Fine-tune BGE reranker with successful target positives and confusing visible hard negatives -> `ShowWhere-Reranker-v1`.
2. Fine-tune Domyn with verified step decisions and full trajectories -> `ShowWhere-Brain-v1`.
3. Add recovery, expected-transition, task-planning, and multilingual state data -> later Brain versions.
4. Only with explicit screenshot-training consent, train target/region examples -> `ShowWhere-Grounder-v1`.

Training may produce a candidate automatically. Promotion is never automatic: the candidate must beat production on ShowWhereBench and receive human approval in the model registry.

## Known development-PC constraint

The selected Brain and grounder weights are much larger than a 6GB GPU. Lazy loading prevents simultaneous VRAM pressure, but full-precision local inference may require CPU/offload and can be slow or exceed available RAM. A GPU server can run the same endpoints; change only `SHOWWHERE_AI_BASE_URL`, token, and deployment controls.

Measured on the current GTX 1660 Ti development machine:

- BGE reranker: correct `My Tickets` selection, 620 ms warm wall time, confidence 0.936.
- BGE-M3: correct Korean-to-English order-tracking retrieval, 397 ms warm wall time (5.72 s cold), 1024 dimensions.
- POINTS-GUI-G: correct synthetic Settings point `(0.763, 0.433)`, 104.77 s cold wall time.
- Domyn: all four shards loaded in 285 seconds with 4-bit/CPU offload, but Transformers generation failed with a remaining custom Nemotron `meta tensor` offload incompatibility. Brain v2 therefore falls back to legacy on this PC; deploy Domyn on a larger GPU/vLLM worker for production evaluation.

The initial legacy ShowWhereBench baseline measured 5/9 (55.6%) correct next actions at 6,135 ms average. A v2 end-to-end accuracy claim is intentionally withheld until Domyn runs on compatible server hardware; the benchmark saves future runs for an exact comparison.
