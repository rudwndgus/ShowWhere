"""Run and persist one-at-a-time smoke tests against ShowWhere Local AI."""
from __future__ import annotations

import argparse
import base64
import io
import json
import time
import urllib.request
import urllib.error
import uuid
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image, ImageDraw


def post(base_url: str, path: str, payload: dict) -> tuple[dict, int]:
    started = time.perf_counter()
    last_error = None
    for attempt in range(2):
        request = urllib.request.Request(
            base_url.rstrip("/") + path,
            data=json.dumps(payload, ensure_ascii=False).encode(),
            headers={"content-type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=600) as response:
                result = json.load(response)
            return result, round((time.perf_counter() - started) * 1000)
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")
            last_error = RuntimeError(f"HTTP {error.code}: {detail}")
            if error.code < 500:
                raise last_error from error
            if attempt == 0:
                time.sleep(.5)
    raise last_error or RuntimeError("Local model smoke request failed")


def synthetic_settings() -> str:
    image = Image.new("RGB", (800, 500), "#f4f4f4")
    draw = ImageDraw.Draw(image)
    draw.rectangle((50, 80, 750, 440), fill="white", outline="#aaaaaa", width=2)
    draw.text((75, 105), "Windows Start", fill="black")
    draw.rectangle((520, 170, 700, 260), fill="#e6e6e6", outline="#333333", width=3)
    draw.text((570, 205), "Settings", fill="black")
    output = io.BytesIO()
    image.save(output, format="PNG")
    return base64.b64encode(output.getvalue()).decode()


def cosine(left: list[float], right: list[float]) -> float:
    return sum(a * b for a, b in zip(left, right))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("role", choices=["reranker", "embedding", "brain", "grounder", "all"], default="all", nargs="?")
    parser.add_argument("--base-url", default="http://127.0.0.1:8790")
    parser.add_argument("--data-root", default="data/brain-v2")
    args = parser.parse_args()
    roles = ["reranker", "embedding", "brain", "grounder"] if args.role == "all" else [args.role]
    records = []
    for role in roles:
        started = time.perf_counter()
        try:
            if role == "reranker":
                result, latency = post(args.base_url, "/rerank", {
                    "query": "사용자가 구매한 티켓을 확인하고 싶다",
                    "candidates": [{"id": name.lower().replace(" ", "-"), "label": name, "role": "button"} for name in ["Home", "My Tickets", "Account", "Buy Tickets", "Settings"]],
                })
                passed = result.get("selectedId") == "my-tickets"
            elif role == "embedding":
                texts = ["저번주에 주문한 신발이 어디 있는지 보고 싶어", "track previous order", "printer status", "connect wifi"]
                result, latency = post(args.base_url, "/memory/embed", {"texts": texts})
                embeddings = result.pop("embeddings")
                scores = [cosine(embeddings[0], vector) for vector in embeddings[1:]]
                result["scores"] = scores
                result["dimensions"] = len(embeddings[0])
                passed = scores.index(max(scores)) == 0
            elif role == "brain":
                result, latency = post(args.base_url, "/brain/decide", {
                "guideRequest": {
                    "session": {"sessionId": "smoke", "originalUserMessage": "프린터 연결 상태를 확인하고 싶어", "mode": "guidance", "status": "waiting_for_ai", "completedSteps": [], "knownFacts": [], "failureCount": 0},
                    "context": {"platform": "windows", "applicationName": "Windows Start", "windowTitle": "Start"},
                    "candidates": [{"id": "settings", "label": "Settings", "role": "button", "enabled": True, "visible": True, "clickable": True, "bounds": {"x": 1, "y": 1, "width": 1, "height": 1}}],
                },
                "memories": [{"id": "printer", "score": .9, "authority": "human_gold", "taskId": "windows.printer.check_status", "stateId": "windows.start.menu", "targetConcept": "windows.settings", "expectedNextState": "windows.settings.home", "text": "printer status via Settings"}],
                "reasoningMode": "off",
                })
                required = {"taskId", "nextSemanticAction", "targetConcept", "expectedNextState", "confidence"}
                passed = required.issubset(result.get("decision", {}))
            else:
                result, latency = post(args.base_url, "/ground", {"screenshot": synthetic_settings(), "targetConcept": "Settings button"})
                point = result.get("point")
                bounds = result.get("bounds")
                # Synthetic Settings button is x=520..700, y=170..260 on an 800x500 image.
                passed = bool(bounds) or bool(point and .65 <= point.get("x", -1) <= .875 and .34 <= point.get("y", -1) <= .52)
        except Exception as error:
            result = {"error": str(error)}
            latency = round((time.perf_counter() - started) * 1000)
            passed = False
        records.append({
            "schemaVersion": "showwhere-model-smoke-v1", "testId": str(uuid.uuid4()),
            "timestamp": datetime.now(timezone.utc).isoformat(), "role": role,
            "passed": passed, "wallLatencyMs": latency, "result": result,
        })
        print(json.dumps(records[-1], ensure_ascii=False))
    output = Path(args.data_root) / "eval" / "model-smoke-results.jsonl"
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("a", encoding="utf-8") as target:
        for record in records:
            target.write(json.dumps(record, ensure_ascii=False) + "\n")
    return 0 if all(record["passed"] for record in records) else 1


if __name__ == "__main__":
    raise SystemExit(main())
