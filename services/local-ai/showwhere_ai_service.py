"""Local specialist-model service for ShowWhere Brain v2.

Models are loaded lazily so downloading and API startup remain possible on modest
developer hardware. Set SHOWWHERE_LOCAL_AI_DEVICE=cpu to force CPU inference.
"""
from __future__ import annotations

import base64
import gc
import io
import json
import os
import re
import tempfile
import time
from pathlib import Path
from threading import Lock
from typing import Any

import torch
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from PIL import Image
from transformers import AutoModel, AutoModelForCausalLM, AutoModelForSequenceClassification, AutoTokenizer, BitsAndBytesConfig, Qwen2VLImageProcessor

MODEL_ROOT = Path(os.getenv("SHOWWHERE_MODEL_ROOT", r"C:\ShowWhere_Models"))
DEVICE = os.getenv("SHOWWHERE_LOCAL_AI_DEVICE", "auto")
MAX_LOADED = int(os.getenv("SHOWWHERE_LOCAL_AI_MAX_LOADED", "1"))

PROJECT_ROOT = Path(__file__).resolve().parents[2]
with (PROJECT_ROOT / "config" / "models.json").open(encoding="utf-8-sig") as manifest_file:
    MODEL_MANIFEST = json.load(manifest_file)["models"]
MODEL_IDS = {role: os.getenv(f"SHOWWHERE_{role.upper()}_MODEL", model["id"]) for role, model in MODEL_MANIFEST.items()}
MODEL_PATHS = {
    role: Path(os.getenv(f"SHOWWHERE_{role.upper()}_MODEL_PATH", str(MODEL_ROOT / model["directory"])))
    for role, model in MODEL_MANIFEST.items()
}
API_TOKEN = os.getenv("SHOWWHERE_AI_TOKEN")
MAX_REQUEST_BYTES = int(os.getenv("SHOWWHERE_LOCAL_AI_MAX_REQUEST_BYTES", "10000000"))


class ModelPool:
    def __init__(self) -> None:
        self.loaded: dict[str, tuple[Any, Any, Any | None]] = {}
        self.last_used: dict[str, float] = {}
        self.lock = Lock()

    def _device_map(self) -> str | None:
        if DEVICE == "cpu" or not torch.cuda.is_available():
            return None
        return "auto"

    def _dtype(self) -> torch.dtype:
        return torch.float16 if torch.cuda.is_available() and DEVICE != "cpu" else torch.float32

    def _evict_if_needed(self, keep: str) -> None:
        while len(self.loaded) >= MAX_LOADED and keep not in self.loaded:
            victim = min(self.last_used, key=self.last_used.get)
            _tokenizer, model, _processor = self.loaded.pop(victim)
            try:
                model.to("cpu")
            except (RuntimeError, ValueError, NotImplementedError):
                pass
            del model
            del self.last_used[victim]
            gc.collect()
            if torch.cuda.is_available():
                torch.cuda.empty_cache()

    def get(self, role: str) -> tuple[Any, Any, Any | None]:
        with self.lock:
            if role in self.loaded:
                self.last_used[role] = time.time()
                return self.loaded[role]
            path = MODEL_PATHS[role]
            marker = path / "showwhere-download.json"
            if not marker.exists():
                raise RuntimeError(f"Model {role} is not completely downloaded at {path}")
            self._evict_if_needed(role)
            tokenizer = AutoTokenizer.from_pretrained(path, local_files_only=True, trust_remote_code=role == "grounder")
            common = {"local_files_only": True, "trust_remote_code": role == "grounder"}
            quantization = None
            if torch.cuda.is_available() and DEVICE != "cpu" and role in {"brain", "grounder"}:
                gpu_gb = torch.cuda.get_device_properties(0).total_memory / 1024**3
                if gpu_gb < 16:
                    quantization = BitsAndBytesConfig(
                        load_in_4bit=True, bnb_4bit_compute_dtype=torch.float16,
                        bnb_4bit_quant_type="nf4", llm_int8_enable_fp32_cpu_offload=True,
                    )
            constrained_memory = {0: "4800MiB", "cpu": "10GiB"} if quantization is not None else None
            if role == "reranker":
                model = AutoModelForSequenceClassification.from_pretrained(
                    path, dtype=self._dtype(), device_map=self._device_map(), low_cpu_mem_usage=True, **common
                )
            elif role == "brain":
                model = AutoModelForCausalLM.from_pretrained(
                    path, dtype=self._dtype(), device_map=self._device_map(), low_cpu_mem_usage=True,
                    quantization_config=quantization, max_memory=constrained_memory,
                    attn_implementation="sdpa", **common
                )
            elif role == "grounder":
                model = AutoModelForCausalLM.from_pretrained(
                    path, dtype=self._dtype(), device_map=self._device_map(), low_cpu_mem_usage=True,
                    quantization_config=quantization, max_memory=constrained_memory,
                    attn_implementation="eager", **common
                )
            else:
                model = AutoModel.from_pretrained(
                    path, dtype=self._dtype(), device_map=self._device_map(), low_cpu_mem_usage=True, **common
                )
            if self._device_map() is None:
                model.to("cpu")
            model.eval()
            image_processor = Qwen2VLImageProcessor.from_pretrained(path, local_files_only=True) if role == "grounder" else None
            self.loaded[role] = (tokenizer, model, image_processor)
            self.last_used[role] = time.time()
            return self.loaded[role]


pool = ModelPool()
app = FastAPI(title="ShowWhere Local AI", version="2.0")


@app.middleware("http")
async def protect_api(request: Request, call_next: Any) -> Any:
    if API_TOKEN and request.headers.get("authorization") != f"Bearer {API_TOKEN}":
        return JSONResponse(status_code=401, content={"detail": "Unauthorized"})
    content_length = request.headers.get("content-length")
    if content_length and int(content_length) > MAX_REQUEST_BYTES:
        return JSONResponse(status_code=413, content={"detail": "Request too large"})
    return await call_next(request)


class RerankCandidate(BaseModel):
    id: str
    label: str | None = None
    description: str | None = None
    role: str


class RerankRequest(BaseModel):
    query: str = Field(max_length=5000)
    candidates: list[RerankCandidate] = Field(max_length=200)


class EmbedRequest(BaseModel):
    texts: list[str] = Field(min_length=1, max_length=64)


class BrainRequest(BaseModel):
    guideRequest: dict[str, Any]
    memories: list[dict[str, Any]] = []
    reasoningMode: str = "off"


class GroundRequest(BaseModel):
    screenshot: str
    targetConcept: str = Field(max_length=500)


def model_device(model: Any) -> torch.device:
    device_map = getattr(model, "hf_device_map", None)
    if isinstance(device_map, dict):
        for mapped in device_map.values():
            if isinstance(mapped, int):
                return torch.device(f"cuda:{mapped}")
            if isinstance(mapped, str) and mapped.startswith("cuda"):
                return torch.device(mapped)
    try:
        device = next(model.parameters()).device
        return torch.device("cpu") if device.type == "meta" else device
    except StopIteration:
        return torch.device("cpu")


def parse_json_object(text: str) -> dict[str, Any]:
    cleaned = re.sub(r"^```(?:json)?|```$", "", text.strip(), flags=re.MULTILINE).strip()
    start, end = cleaned.find("{"), cleaned.rfind("}")
    if start < 0 or end <= start:
        raise ValueError("Brain did not return a JSON object")
    return json.loads(cleaned[start : end + 1])


@app.get("/health")
def health() -> dict[str, Any]:
    return {
        "status": "ok",
        "modelRoot": str(MODEL_ROOT),
        "cuda": torch.cuda.is_available(),
        "gpu": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
        "models": {
            role: {"id": MODEL_IDS[role], "ready": (path / "showwhere-download.json").exists(), "loaded": role in pool.loaded}
            for role, path in MODEL_PATHS.items()
        },
    }


@app.post("/rerank")
def rerank(request: RerankRequest) -> dict[str, Any]:
    started = time.perf_counter()
    if not request.candidates:
        return {"scores": [], "confidence": 0, "margin": 0, "latencyMs": 0, "model": MODEL_IDS["reranker"]}
    try:
        tokenizer, model, _ = pool.get("reranker")
        pairs = [[request.query, " | ".join(filter(None, [item.label, item.description, item.role]))] for item in request.candidates]
        inputs = tokenizer(pairs, padding=True, truncation=True, max_length=1024, return_tensors="pt")
        inputs = {key: value.to(model_device(model)) for key, value in inputs.items()}
        with torch.inference_mode():
            logits = model(**inputs).logits.view(-1).float().cpu()
        probabilities = (torch.softmax(logits, dim=0) if len(logits) > 1 else torch.sigmoid(logits)).tolist()
        ranked = sorted(zip(request.candidates, probabilities), key=lambda item: item[1], reverse=True)
        top = ranked[0]
        runner_up = ranked[1][1] if len(ranked) > 1 else 0.0
        return {
            "scores": [{"candidateId": item.id, "score": float(score)} for item, score in ranked],
            "selectedId": top[0].id,
            "confidence": float(top[1]),
            "margin": float(max(0.0, top[1] - runner_up)),
            "latencyMs": round((time.perf_counter() - started) * 1000),
            "model": MODEL_IDS["reranker"],
        }
    except Exception as error:
        raise HTTPException(status_code=503, detail=str(error)) from error


@app.post("/memory/embed")
def embed(request: EmbedRequest) -> dict[str, Any]:
    try:
        tokenizer, model, _ = pool.get("embedding")
        inputs = tokenizer(request.texts, padding=True, truncation=True, max_length=8192, return_tensors="pt")
        inputs = {key: value.to(model_device(model)) for key, value in inputs.items()}
        with torch.inference_mode():
            hidden = model(**inputs).last_hidden_state
            vectors = torch.nn.functional.normalize(hidden[:, 0], p=2, dim=1)
        return {"embeddings": vectors.float().cpu().tolist(), "model": MODEL_IDS["embedding"]}
    except Exception as error:
        raise HTTPException(status_code=503, detail=str(error)) from error


@app.post("/brain/decide")
def decide(request: BrainRequest) -> dict[str, Any]:
    started = time.perf_counter()
    guide = request.guideRequest
    prompt = {
        "goal": guide.get("session", {}).get("originalUserMessage"),
        "current_step": guide.get("session", {}).get("currentStep"),
        "application": guide.get("context"),
        "visible_controls": [
            {key: item.get(key) for key in ("id", "label", "description", "role")}
            for item in guide.get("candidates", [])
        ],
        "trusted_memories": request.memories,
    }
    system = (
        "You are ShowWhere's semantic task-state reasoner. Decide exactly one next GUI action. "
        "Never invent a visible control. Distinguish browser address bars from in-app search. "
        "If intent or destination is ambiguous, use nextSemanticAction=clarify. Return only JSON with keys: "
        "intent, taskId, stateId, nextSemanticAction, targetConcept, expectedNextState, expectedEvidence, "
        "confidence, needsVision, reasoningMode. Confidence is 0..1."
    )
    try:
        tokenizer, model, _ = pool.get("brain")
        system += " thinking on" if request.reasoningMode == "on" else " thinking off"
        messages = [{"role": "system", "content": system}, {"role": "user", "content": json.dumps(prompt, ensure_ascii=False)}]
        rendered = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        inputs = tokenizer(rendered, return_tensors="pt", truncation=True, max_length=8192)
        inputs = {key: value.to(model_device(model)) for key, value in inputs.items()}
        with torch.inference_mode():
            generated = model.generate(**inputs, max_new_tokens=450, do_sample=False, temperature=None, top_p=None)
        output = tokenizer.decode(generated[0][inputs["input_ids"].shape[1] :], skip_special_tokens=True)
        decision = parse_json_object(output)
        return {"decision": decision, "model": MODEL_IDS["brain"], "latencyMs": round((time.perf_counter() - started) * 1000)}
    except Exception as error:
        raise HTTPException(status_code=503, detail=str(error)) from error


def decode_image(value: str) -> Image.Image:
    payload = value.split(",", 1)[1] if value.startswith("data:") and "," in value else value
    return Image.open(io.BytesIO(base64.b64decode(payload))).convert("RGB")


def parse_grounding(text: str, image: Image.Image) -> tuple[dict[str, float] | None, dict[str, float] | None]:
    numbers = [float(value) for value in re.findall(r"-?\d+(?:\.\d+)?", text)]
    if len(numbers) < 2:
        return None, None
    coordinate_max = max(numbers[: min(4, len(numbers))])
    if coordinate_max <= 1:
        scale_x = scale_y = 1.0
    elif coordinate_max <= 1000:
        scale_x = scale_y = 1000.0
    else:
        scale_x, scale_y = float(image.width), float(image.height)
    if len(numbers) >= 4:
        x1, y1, x2, y2 = numbers[:4]
        return None, {"x": max(0, x1 / scale_x), "y": max(0, y1 / scale_y), "width": max(.005, (x2-x1)/scale_x), "height": max(.005, (y2-y1)/scale_y)}
    return {"x": max(0, min(1, numbers[0] / scale_x)), "y": max(0, min(1, numbers[1] / scale_y))}, None


@app.post("/ground")
def ground(request: GroundRequest) -> dict[str, Any]:
    started = time.perf_counter()
    try:
        tokenizer, model, image_processor = pool.get("grounder")
        image = decode_image(request.screenshot)
        system_prompt = (
            "You are a GUI agent. Based on the UI screenshot provided, locate the exact position of the element "
            "matching the instruction. Return only the normalized center point as strictly (x, y)."
        )
        if not hasattr(model, "chat") or image_processor is None:
            raise RuntimeError("POINTS-GUI-G remote model does not expose the expected chat method")
        temporary_path = ""
        try:
            with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as temporary:
                image.save(temporary, format="PNG")
                temporary_path = temporary.name
            messages = [
                {"role": "system", "content": [{"type": "text", "text": system_prompt}]},
                {"role": "user", "content": [{"type": "image", "image": temporary_path}, {"type": "text", "text": request.targetConcept}]},
            ]
            output = model.chat(messages, tokenizer, image_processor, {"max_new_tokens": 128, "do_sample": False})
        finally:
            if temporary_path:
                Path(temporary_path).unlink(missing_ok=True)
        text = output[0] if isinstance(output, tuple) else str(output)
        point, bounds = parse_grounding(text, image)
        if point is None and bounds is None:
            raise RuntimeError(f"Could not parse grounding coordinates: {text[:200]}")
        result: dict[str, Any] = {
            "label": request.targetConcept[:160], "confidence": 0.78,
            "latencyMs": round((time.perf_counter() - started) * 1000), "model": MODEL_IDS["grounder"],
        }
        if point is not None:
            result["point"] = point
        if bounds is not None:
            bounds["width"] = min(bounds["width"], 1 - bounds["x"])
            bounds["height"] = min(bounds["height"], 1 - bounds["y"])
            result["bounds"] = bounds
        return result
    except Exception as error:
        raise HTTPException(status_code=503, detail=str(error)) from error
