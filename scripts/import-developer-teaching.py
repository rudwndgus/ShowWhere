"""Promote only explicitly developer-verified corrections into Brain v2 gold data."""
from __future__ import annotations

import argparse
import json
import re
import uuid
from datetime import datetime, timezone
from pathlib import Path

SECRET_PATTERNS = [
    (re.compile(r"\b(?:sk|hf|ghp|gho|github_pat)_[A-Za-z0-9_-]{12,}\b", re.I), "[REDACTED_TOKEN]"),
    (re.compile(r"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", re.I), "[REDACTED_EMAIL]"),
    (re.compile(r"\b(?:\d[ -]*?){13,19}\b"), "[REDACTED_CARD]"),
]


def scrub(value):
    if isinstance(value, str):
        for pattern, replacement in SECRET_PATTERNS:
            value = pattern.sub(replacement, value)
        return value
    if isinstance(value, list):
        return [scrub(item) for item in value]
    if isinstance(value, dict):
        return {key: scrub(item) for key, item in value.items()}
    return value


def task_slug(text: str) -> str:
    ascii_words = re.findall(r"[a-z0-9]+", text.lower())
    return "developer." + (".".join(ascii_words[:8]) if ascii_words else uuid.uuid5(uuid.NAMESPACE_URL, text).hex[:16])


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", default="training/corrections.jsonl")
    parser.add_argument("--output", default="data/brain-v2/gold/developer-teaching.jsonl")
    parser.add_argument("--memory", default="data/brain-v2/memory-v2.json")
    args = parser.parse_args()
    source, output = Path(args.input), Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    accepted = 0
    memory_entries = []
    with source.open(encoding="utf-8") as records, output.open("w", encoding="utf-8") as target:
        for line in records:
            correction = json.loads(line)
            # AI drafts, unapproved drags, and feedback-only records never become gold.
            if correction.get("developerVerified") is not True or not correction.get("correctTarget"):
                continue
            concept = correction["correctTarget"].get("label") or correction.get("correctedIntent")
            gold = {
                "schemaVersion": "showwhere-developer-gold-v2",
                "id": correction.get("id"),
                "importedAt": datetime.now(timezone.utc).isoformat(),
                "authority": "human_gold",
                "immutableSource": True,
                "userQuestion": correction.get("originalGoal"),
                "correctedIntent": correction.get("refinedComment") or correction.get("correctedIntent"),
                "taskId": task_slug(correction.get("effectiveGoal") or correction.get("originalGoal", "")),
                "state": correction.get("context"),
                "targetConcept": concept,
                "correctTarget": correction.get("correctTarget"),
                "selectedBounds": correction.get("selectedBounds"),
                "normalizedVisualTarget": correction.get("normalizedVisualTarget"),
                "developerComment": correction.get("developerComment"),
            }
            gold = scrub(gold)
            target.write(json.dumps(gold, ensure_ascii=False) + "\n")
            memory_entries.append(scrub({
                "id": correction.get("id"), "score": 1.0, "authority": "human_gold",
                "taskId": gold["taskId"],
                "stateId": ".".join(filter(None, [correction.get("context", {}).get("applicationName"), correction.get("context", {}).get("windowTitle")])) or "unknown",
                "targetConcept": concept,
                "expectedNextState": correction.get("refinedComment") or f"UI changes after selecting {concept}",
                "text": " | ".join(filter(None, [correction.get("originalGoal"), correction.get("effectiveGoal"), correction.get("correctedIntent"), correction.get("refinedComment"), concept])),
            }))
            accepted += 1
    memory_path = Path(args.memory)
    memory_path.parent.mkdir(parents=True, exist_ok=True)
    memory_path.write_text(json.dumps({"schemaVersion": 1, "entries": memory_entries}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"acceptedHumanGold": accepted, "output": str(output.resolve()), "memory": str(memory_path.resolve())}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
