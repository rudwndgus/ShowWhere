# ShowWhere Learning Pipeline

This directory turns a small human-approved seed library into synthetic UI-state scenarios, independently judges them, routes uncertain cases to review, and measures model behavior. It does **not** fine-tune a model.

## Layout

- `contracts.ts`: runtime-validated seed, scenario, judgment, benchmark, review, and privacy-minimized session schemas.
- `provider.ts`: OpenAI-compatible Featherless JSON client with timeout and retry limits.
- `pipeline.ts`: generation, privacy checks, deduplication, blind multi-model judging, review routing, deterministic family splits, and evaluation.
- `cli.ts`: development commands.
- `data/seeds/`: human-authored source of truth. Do not place synthetic examples here.
- `data/generated/`: untrusted model output and resumable generation manifest.
- `data/accepted/`, `rejected/`, `judged/`: validation results.
- `data/human-reviewed/`: review queue and future explicit reviewer decisions.
- `data/training/`, `validation/`, `evaluation/`: family-separated datasets. Permanent benchmark seed families are held out of training.
- `playbooks/`: deterministic troubleshooting branches which should not be memorized by a model.

## Safe workflow

```powershell
npm.cmd run learning:validate
npm.cmd run learning:generate -- --seed windows_default_printer --count 5 --dry-run
npm.cmd run learning:generate -- --seed windows_default_printer --count 5
npm.cmd run learning:judge
npm.cmd run learning:review-queue
npm.cmd run learning:review -- --scenario <id> --action correct --target <candidate-id> --instruction "수정 안내" --comment "검토 메모"
npm.cmd run learning:build-dataset
npm.cmd run eval -- --dry-run
npm.cmd run eval
```

`npm.cmd` is shown because some Windows PowerShell execution policies block `npm.ps1`. Plain `npm` is fine in terminals without that restriction.

Configure `LEARNING_GENERATOR_MODEL`, `LEARNING_JUDGE_A_MODEL`, and `LEARNING_JUDGE_B_MODEL` in the untracked root `.env`. Judges should be genuinely independent models where possible. The judge request never contains the generator's proposed action/target, and benchmark requests never contain expected answers.

Reviewer actions are `accept`, `reject`, `correct`, and `mark_ambiguous`. A correction cannot be saved with a target outside the scenario candidate list. Re-reviewing the same scenario replaces its prior decision instead of duplicating it.

Defaults are deliberately small: at most 10 variants per seed, 50 examples per run, concurrency 2, and bounded output tokens. Increase limits explicitly only after reviewing a dry run. Generation resumes by merging valid prior output and removing exact structural duplicates.

## Trust levels

1. Human-authored seed or explicit correction: trusted source material.
2. Synthetic generated scenario: untrusted.
3. Auto-accepted scenario: schema-valid and agreed upon by generator plus two judges; useful for experiments, but not equivalent to a human label.
4. Human-reviewed accepted scenario: preferred future training/few-shot material.
5. Permanent evaluation benchmark: never exposed as an answer to evaluated models and never included in training/few-shot data.

Full screenshots, passwords, tokens, payment data, message/document bodies, and unnecessary personal information do not belong here. Real-session collection is schema-ready but is not enabled automatically.
