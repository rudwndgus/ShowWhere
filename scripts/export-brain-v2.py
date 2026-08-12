"""Validate, merge, classify, and export private Brain v2 runtime data.

All output stays below data/brain-v2 by default and is ignored by Git. Only
verified/gold data is eligible for training exports; raw predictions never are.
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import statistics
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

SECRET_PATTERNS = [
    re.compile(r"\b(?:sk|hf|ghp|gho|github_pat)_[A-Za-z0-9_-]{12,}\b", re.I),
    re.compile(r"\bBearer\s+[A-Za-z0-9._~+/=-]+", re.I),
    re.compile(r"(?:api[_ -]?key|password|passwd|token|secret|cvv)\s*[:=]\s*[^\s,;\"']{4,}", re.I),
    re.compile(r"\b(?:\d[ -]*?){13,19}\b"),
    re.compile(r"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", re.I),
]
TRAINABLE_AUTHORITIES = {"human_gold", "verified_real", "synthetic_validated"}


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    records = []
    with path.open(encoding="utf-8") as source:
        for line_number, line in enumerate(source, 1):
            if line.strip():
                try:
                    records.append(json.loads(line))
                except json.JSONDecodeError as error:
                    raise ValueError(f"Invalid JSON at {path}:{line_number}") from error
    return records


def contains_secret(value: Any) -> bool:
    if isinstance(value, str):
        return any(pattern.search(value) for pattern in SECRET_PATTERNS)
    if isinstance(value, list):
        return any(contains_secret(item) for item in value)
    if isinstance(value, dict):
        return any(contains_secret(item) for item in value.values())
    return False


def write_jsonl(path: Path, records: Iterable[dict[str, Any]]) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with path.open("w", encoding="utf-8") as output:
        for record in records:
            output.write(json.dumps(record, ensure_ascii=False) + "\n")
            count += 1
    return count


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, round((len(ordered) - 1) * fraction))]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", default="data/brain-v2")
    args = parser.parse_args()
    root = Path(args.data_root).resolve()
    export_root = root / "exports"
    events = read_jsonl(root / "events.jsonl")
    updates = read_jsonl(root / "outcome-updates.jsonl")
    trajectories = read_jsonl(root / "trajectories.jsonl")

    by_id = {event.get("eventId"): event for event in events if event.get("eventId")}
    for update in updates:
        target = by_id.get(update.get("eventId"))
        if target is not None:
            target.update(update.get("patch", {}))

    rejected, validated = [], []
    for event in by_id.values():
        if contains_secret(event):
            event = {**event, "dataQualityStatus": "rejected", "authority": "rejected", "rejectionReason": "secret_detected"}
            rejected.append(event)
        elif event.get("authority") in TRAINABLE_AUTHORITIES and event.get("dataQualityStatus") == "verified":
            validated.append(event)

    reranker, brain, vision, failures = [], [], [], []
    for event in validated:
        selected = event.get("selectedCandidate")
        candidates = event.get("candidates", [])
        if selected and event.get("finalOutcome") in {"success", "corrected"}:
            negatives = [item for item in candidates if item.get("id") != selected.get("id")]
            reranker.append({
                "query": f"{event.get('userQuestion', '')}\nTarget: {event.get('selectedSemanticTarget', '')}",
                "positive": selected,
                "negatives": negatives,
                "hardNegatives": sorted(
                    negatives,
                    key=lambda item: next((score.get("score", 0) for score in event.get("rerankerScores", []) if score.get("candidateId") == item.get("id")), 0),
                    reverse=True,
                )[:5],
                "authority": event.get("authority"),
            })
        if event.get("brainOutput") and event.get("finalOutcome") in {"success", "corrected"}:
            brain.append({
                "input": {
                    "userQuestion": event.get("userQuestion"), "state": event.get("stateBefore"),
                    "visibleConcepts": event.get("visibleConcepts"), "retrievedMemoryIds": event.get("retrievedMemoryIds"),
                },
                "output": event.get("brainOutput"), "authority": event.get("authority"),
            })
        if event.get("visionUsed") and event.get("selectedBounds") and event.get("finalOutcome") in {"success", "corrected"}:
            vision.append({"screenshotReference": event.get("screenshotReference"), "semanticTarget": event.get("selectedSemanticTarget"), "region": event.get("selectedBounds")})
        if event.get("finalOutcome") == "failure":
            failures.append(event)

    write_jsonl(export_root / "learning-events.jsonl", validated)
    write_jsonl(export_root / "reranker-training.jsonl", reranker)
    write_jsonl(export_root / "brain-sft.jsonl", brain)
    write_jsonl(export_root / "trajectories.jsonl", [item for item in trajectories if item.get("authority") in TRAINABLE_AUTHORITIES])
    write_jsonl(export_root / "vision-grounding.jsonl", vision)
    write_jsonl(root / "rejected" / "events.jsonl", rejected)

    latency = [float(event.get("totalLatencyMs", 0)) for event in by_id.values()]
    tasks = Counter(event.get("taskId", "unknown") for event in by_id.values())
    outcomes = Counter(event.get("finalOutcome", "unknown") for event in by_id.values())
    report = {
        "events": len(by_id), "validated": len(validated), "rejected": len(rejected),
        "outcomes": outcomes, "taskFrequency": tasks,
        "meanLatencyMs": statistics.fmean(latency) if latency else 0,
        "p50LatencyMs": percentile(latency, .50), "p95LatencyMs": percentile(latency, .95),
        "visionFallbackRate": sum(bool(event.get("visionUsed")) for event in by_id.values()) / max(1, len(by_id)),
        "memoryReuseRate": sum(event.get("fallbackUsed") == "memory" for event in by_id.values()) / max(1, len(by_id)),
        "koreanQuestionCount": sum(bool(re.search(r"[가-힣]", event.get("userQuestion", ""))) for event in by_id.values()),
        "exports": {"reranker": len(reranker), "brainSft": len(brain), "vision": len(vision), "trajectories": len(trajectories)},
    }
    export_root.mkdir(parents=True, exist_ok=True)
    (export_root / "analysis.json").write_text(json.dumps(report, ensure_ascii=False, indent=2, default=dict), encoding="utf-8")
    with (export_root / "eval-results.csv").open("w", encoding="utf-8-sig", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=["eventId", "taskId", "finalOutcome", "confidence", "latencyMs", "fallbackUsed", "visionUsed"])
        writer.writeheader()
        for event in by_id.values():
            writer.writerow({
                "eventId": event.get("eventId"), "taskId": event.get("taskId"), "finalOutcome": event.get("finalOutcome"),
                "confidence": event.get("brainConfidence"), "latencyMs": event.get("totalLatencyMs"),
                "fallbackUsed": event.get("fallbackUsed"), "visionUsed": event.get("visionUsed"),
            })
    try:
        import pandas as pd
        pd.DataFrame(validated).to_parquet(export_root / "learning-events.parquet", index=False)
        pd.DataFrame(failures).to_parquet(export_root / "failures.parquet", index=False)
        report["parquet"] = "written"
    except (ImportError, ModuleNotFoundError):
        report["parquet"] = "install pandas and pyarrow to enable parquet"
    (export_root / "analysis.json").write_text(json.dumps(report, ensure_ascii=False, indent=2, default=dict), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2, default=dict))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
