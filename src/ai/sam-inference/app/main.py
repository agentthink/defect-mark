from __future__ import annotations

import base64
import hashlib
import hmac
import io
import json
import os
import re
import tempfile
import threading
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from PIL import Image, ImageOps
from starlette.concurrency import run_in_threadpool
from starlette.requests import Request


ROOT_DIR = Path(__file__).resolve().parents[2]
MODEL_PATH = Path(os.getenv("SAM3_MODEL", ROOT_DIR / "sam3.pt")).resolve()
MAX_INFERENCE_SIDE = int(os.getenv("SAM3_MAX_INFERENCE_SIDE", "1024"))
ENABLE_CLIP_CLASSIFICATION = os.getenv("SAM3_ENABLE_CLIP_CLASSIFICATION", "true").lower() in {
    "1",
    "true",
    "yes",
    "on",
}
INCLUDE_CATEGORY_OVERLAYS = os.getenv("SAM3_INCLUDE_CATEGORY_OVERLAYS", "true").lower() in {
    "1",
    "true",
    "yes",
    "on",
}
ALLOW_CLIP_DOWNLOAD = os.getenv("SAM3_ALLOW_CLIP_DOWNLOAD", "false").lower() in {
    "1",
    "true",
    "yes",
    "on",
}
CLIP_MODEL_SHA256 = "40d365715913c9da98579312b702a82c18be219cc2a73407c4526f58eba950af"
INTERNAL_KEY = os.getenv("SAM3_INTERNAL_KEY", "")
REQUIRE_INTERNAL_KEY = os.getenv("SAM3_REQUIRE_INTERNAL_KEY", "false").lower() in {
    "1",
    "true",
    "yes",
    "on",
}

if REQUIRE_INTERNAL_KEY and not INTERNAL_KEY:
    raise RuntimeError("SAM3_INTERNAL_KEY is required when SAM3_REQUIRE_INTERNAL_KEY is enabled.")

app = FastAPI(title="SAM 3 Backend")


@app.middleware("http")
async def verify_internal_key(request: Request, call_next):
    if request.url.path.startswith("/api/") and (REQUIRE_INTERNAL_KEY or INTERNAL_KEY):
        if not INTERNAL_KEY:
            return JSONResponse(
                status_code=503,
                content={"detail": "SAM3_INTERNAL_KEY is not configured."},
            )
        provided = request.headers.get("X-Internal-Key", "")
        if not hmac.compare_digest(provided, INTERNAL_KEY):
            return JSONResponse(status_code=401, content={"detail": "Unauthorized"})
    return await call_next(request)


app.add_middleware(
    CORSMiddleware,
    allow_origins=os.getenv(
        "SAM3_CORS_ORIGINS",
        "http://localhost:3000,http://127.0.0.1:3000",
    ).split(","),
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)

_predictor: Any | None = None
_auto_model: Any | None = None
_clip_model: Any | None = None
_clip_preprocess: Any | None = None
_model_lock = threading.Lock()
_inference_lock = threading.Lock()

AUTO_LABEL_CANDIDATES = [
    "person",
    "bicycle",
    "car",
    "motorcycle",
    "airplane",
    "bus",
    "train",
    "truck",
    "boat",
    "traffic light",
    "bench",
    "bird",
    "cat",
    "dog",
    "horse",
    "sheep",
    "cow",
    "backpack",
    "umbrella",
    "handbag",
    "tie",
    "suitcase",
    "frisbee",
    "skis",
    "snowboard",
    "ball",
    "kite",
    "baseball bat",
    "baseball glove",
    "skateboard",
    "surfboard",
    "tennis racket",
    "bottle",
    "wine glass",
    "cup",
    "fork",
    "knife",
    "spoon",
    "bowl",
    "banana",
    "apple",
    "sandwich",
    "orange",
    "broccoli",
    "carrot",
    "pizza",
    "donut",
    "cake",
    "chair",
    "couch",
    "potted plant",
    "bed",
    "dining table",
    "toilet",
    "tv",
    "laptop",
    "mouse",
    "remote",
    "keyboard",
    "cell phone",
    "microwave",
    "oven",
    "toaster",
    "sink",
    "refrigerator",
    "book",
    "clock",
    "vase",
    "scissors",
    "teddy bear",
    "hair dryer",
    "toothbrush",
    "hammer",
    "screwdriver",
    "wrench",
    "pliers",
    "drill",
    "saw",
    "ruler",
    "pen",
    "pencil",
    "paper",
    "box",
    "bag",
    "shoe",
    "hat",
    "glasses",
    "watch",
    "key",
    "coin",
]

ZH_LABELS = {
    "person": "人",
    "bicycle": "自行车",
    "car": "汽车",
    "motorcycle": "摩托车",
    "bus": "公交车",
    "truck": "卡车",
    "cat": "猫",
    "dog": "狗",
    "bottle": "瓶子",
    "cup": "杯子",
    "knife": "刀",
    "spoon": "勺子",
    "apple": "苹果",
    "chair": "椅子",
    "tv": "电视",
    "laptop": "笔记本电脑",
    "mouse": "鼠标",
    "keyboard": "键盘",
    "cell phone": "手机",
    "book": "书",
    "scissors": "剪刀",
    "hammer": "锤子",
    "screwdriver": "螺丝刀",
    "wrench": "扳手",
    "pliers": "钳子",
    "drill": "电钻",
    "saw": "锯子",
    "ruler": "尺子",
    "pen": "笔",
    "pencil": "铅笔",
    "paper": "纸",
    "box": "盒子",
    "bag": "包",
    "shoe": "鞋",
    "hat": "帽子",
    "glasses": "眼镜",
    "watch": "手表",
    "key": "钥匙",
}


def _get_device() -> str:
    try:
        import torch

        return "cuda" if torch.cuda.is_available() else "cpu"
    except Exception:
        return "cpu"


def _get_predictor(conf: float, half: bool) -> Any:
    global _predictor

    if not MODEL_PATH.exists():
        raise HTTPException(status_code=404, detail=f"Model file not found: {MODEL_PATH}")

    with _model_lock:
        if _predictor is None:
            try:
                from ultralytics.models.sam import SAM3SemanticPredictor
            except Exception as exc:
                raise HTTPException(
                    status_code=500,
                    detail="Ultralytics SAM 3 dependencies are not installed.",
                ) from exc

            device = _get_device()
            overrides = {
                "conf": conf,
                "task": "segment",
                "mode": "predict",
                "model": str(MODEL_PATH),
                "half": bool(half and device == "cuda"),
                "device": device,
                "save": False,
                "verbose": False,
            }
            _predictor = SAM3SemanticPredictor(overrides=overrides)

    return _predictor


def _get_auto_model() -> Any:
    global _auto_model

    if not MODEL_PATH.exists():
        raise HTTPException(status_code=404, detail=f"Model file not found: {MODEL_PATH}")

    with _model_lock:
        if _auto_model is None:
            try:
                from ultralytics import SAM
            except Exception as exc:
                raise HTTPException(
                    status_code=500,
                    detail="Ultralytics SAM dependencies are not installed.",
                ) from exc

            _auto_model = SAM(str(MODEL_PATH))

    return _auto_model


def _get_clip() -> tuple[Any, Any]:
    global _clip_model, _clip_preprocess

    with _model_lock:
        if _clip_model is None or _clip_preprocess is None:
            try:
                import clip
            except Exception as exc:
                raise HTTPException(status_code=500, detail="CLIP is not installed.") from exc

            cache_path = Path.home() / ".cache" / "clip" / "ViT-B-32.pt"
            if cache_path.exists():
                digest = hashlib.sha256(cache_path.read_bytes()).hexdigest()
                if digest != CLIP_MODEL_SHA256:
                    try:
                        cache_path.unlink()
                    except Exception:
                        pass
                    raise RuntimeError("CLIP model cache is incomplete. Please pre-download ViT-B-32.pt.")
            elif not ALLOW_CLIP_DOWNLOAD:
                raise RuntimeError("CLIP model is not cached. Please pre-download ViT-B-32.pt or use text prompts.")

            model, preprocess = clip.load("ViT-B/32", device=_get_device())
            model.eval()
            _clip_model = model
            _clip_preprocess = preprocess

    return _clip_model, _clip_preprocess


def _pil_from_bytes(content: bytes) -> Image.Image:
    try:
        image = Image.open(io.BytesIO(content))
        return ImageOps.exif_transpose(image).convert("RGB")
    except Exception as exc:
        raise HTTPException(status_code=400, detail="Uploaded file is not a valid image.") from exc


def _save_inference_temp(image: Image.Image, suffix: str = ".jpg") -> tuple[Path, float]:
    """Save a thumbnail for inference. Returns (path, scale) where scale maps original → inference pixels."""
    if suffix not in {".jpg", ".jpeg", ".png", ".webp", ".bmp"}:
        suffix = ".jpg"

    original_w, original_h = image.size
    inference_image = image.copy()
    inference_image.thumbnail((MAX_INFERENCE_SIDE, MAX_INFERENCE_SIDE), Image.Resampling.LANCZOS)
    inf_w, _ = inference_image.size
    scale = float(inf_w) / float(max(original_w, 1))

    temp = tempfile.NamedTemporaryFile(delete=False, suffix=suffix)
    temp_path = Path(temp.name)
    temp.close()
    inference_image.save(temp_path)
    return temp_path, scale


def _image_to_temp_file(upload: UploadFile, content: bytes) -> tuple[Path, Image.Image]:
    image = _pil_from_bytes(content)
    suffix = Path(upload.filename or "image.jpg").suffix.lower()
    temp_path, _scale = _save_inference_temp(image, suffix)
    return temp_path, image


def _image_to_temp_file_with_scale(upload: UploadFile, content: bytes) -> tuple[Path, Image.Image, float]:
    image = _pil_from_bytes(content)
    suffix = Path(upload.filename or "image.jpg").suffix.lower()
    temp_path, scale = _save_inference_temp(image, suffix)
    return temp_path, image, scale


def _parse_prompt_labels(prompt: str) -> list[str]:
    labels = [item.strip() for item in re.split(r"[,，;；\n]+", prompt) if item.strip()]
    return labels or [prompt.strip()]


def _label_display(label: str) -> str:
    return ZH_LABELS.get(label, label)


def _mask_bbox(mask: np.ndarray) -> tuple[int, int, int, int] | None:
    ys, xs = np.where(mask > 0.5)
    if len(xs) == 0 or len(ys) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def _extract_instances(
    results: Any,
    fallback_labels: list[str] | None = None,
) -> list[dict[str, Any]]:
    instances: list[dict[str, Any]] = []
    fallback_labels = fallback_labels or []

    for result in results or []:
        result_masks = getattr(result, "masks", None)
        if result_masks is None or getattr(result_masks, "data", None) is None:
            continue

        data = result_masks.data
        try:
            data = data.detach().cpu().numpy()
        except AttributeError:
            data = np.asarray(data)

        if data.ndim == 2:
            data = data[None, :, :]

        names = getattr(result, "names", {}) or {}
        boxes = getattr(result, "boxes", None)
        cls_values: list[int] = []
        if boxes is not None and getattr(boxes, "cls", None) is not None:
            cls_data = boxes.cls
            try:
                cls_values = [int(value) for value in cls_data.detach().cpu().numpy().tolist()]
            except AttributeError:
                cls_values = [int(value) for value in np.asarray(cls_data).tolist()]

        for index, mask in enumerate(data):
            label = None
            if index < len(cls_values):
                class_id = cls_values[index]
                if isinstance(names, dict):
                    label = names.get(class_id, str(class_id))
                elif isinstance(names, list) and class_id < len(names):
                    label = names[class_id]
            if label is None and fallback_labels:
                label = fallback_labels[min(index, len(fallback_labels) - 1)]
            instances.append({"mask": mask.astype(np.float32), "label": label or "object"})

    return instances


def _classify_masks(image: Image.Image, masks: list[np.ndarray]) -> list[str]:
    if not masks:
        return []

    import clip
    import torch

    model, preprocess = _get_clip()
    device = _get_device()
    text_tokens = clip.tokenize([f"a photo of a {name}" for name in AUTO_LABEL_CANDIDATES]).to(device)
    with torch.no_grad():
        text_features = model.encode_text(text_tokens)
        text_features = text_features / text_features.norm(dim=-1, keepdim=True)

    image_rgb = image.convert("RGB")
    width, height = image_rgb.size
    labels: list[str] = []

    for mask in masks:
        resized = cv2.resize(mask, (width, height), interpolation=cv2.INTER_LINEAR)
        bbox = _mask_bbox(resized)
        if bbox is None:
            labels.append("unknown")
            continue

        x1, y1, x2, y2 = bbox
        pad = max(8, int(max(x2 - x1, y2 - y1) * 0.08))
        x1, y1 = max(0, x1 - pad), max(0, y1 - pad)
        x2, y2 = min(width - 1, x2 + pad), min(height - 1, y2 + pad)
        crop = image_rgb.crop((x1, y1, x2 + 1, y2 + 1))

        with torch.no_grad():
            image_input = preprocess(crop).unsqueeze(0).to(device)
            image_features = model.encode_image(image_input)
            image_features = image_features / image_features.norm(dim=-1, keepdim=True)
            scores = (100.0 * image_features @ text_features.T).softmax(dim=-1)
            index = int(scores[0].argmax().item())
        labels.append(AUTO_LABEL_CANDIDATES[index])

    return labels


def _as_float_mask(mask: np.ndarray) -> np.ndarray:
    """OpenCV resize does not accept bool masks; normalize to float32 in [0, 1]."""
    array = np.asarray(mask)
    if array.dtype == np.bool_ or array.dtype == bool:
        return array.astype(np.float32)
    return array.astype(np.float32, copy=False)


def _odd_kernel(size: int, minimum: int = 3) -> int:
    value = max(minimum, int(size))
    return value if value % 2 == 1 else value + 1


def _refine_binary_mask(mask: np.ndarray, width: int, height: int) -> np.ndarray:
    """Upsample, soft-blur and morphologically smooth a SAM mask to reduce jagged edges."""
    soft = cv2.resize(_as_float_mask(mask), (width, height), interpolation=cv2.INTER_CUBIC)
    blur_k = _odd_kernel(max(3, round(min(width, height) * 0.004)))
    soft = cv2.GaussianBlur(soft, (blur_k, blur_k), 0)
    binary = (soft > 0.5).astype(np.uint8)
    if not np.any(binary):
        return binary

    morph_k = _odd_kernel(max(3, round(min(width, height) * 0.005)))
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (morph_k, morph_k))
    # Close fills tiny bites along the rim; open removes speckles sticking out.
    binary = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, kernel, iterations=1)
    binary = cv2.morphologyEx(binary, cv2.MORPH_OPEN, kernel, iterations=1)
    return binary


def _smooth_contour_points(contour: np.ndarray, window: int = 7) -> np.ndarray:
    """Circular moving-average to soften stair-step contour vertices."""
    points = contour.reshape(-1, 2).astype(np.float64)
    count = len(points)
    if count < max(6, window * 2):
        return contour

    radius = max(1, window // 2)
    smoothed = np.empty_like(points)
    for index in range(count):
        indices = [(index + offset) % count for offset in range(-radius, radius + 1)]
        smoothed[index] = points[indices].mean(axis=0)

    return np.round(smoothed).astype(np.int32).reshape(-1, 1, 2)


def _contours_from_binary(binary: np.ndarray) -> list[np.ndarray]:
    contours, _ = cv2.findContours(binary, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    refined: list[np.ndarray] = []
    for contour in contours:
        area = float(cv2.contourArea(contour))
        if area < 20:
            continue
        perimeter = float(cv2.arcLength(contour, True))
        window = _odd_kernel(max(5, round(perimeter * 0.01)), minimum=5)
        smoothed = _smooth_contour_points(contour, window=window)
        # Light Douglas-Peucker keeps curves smooth without reintroducing jagged steps.
        epsilon = max(0.6, 0.0008 * cv2.arcLength(smoothed, True))
        approx = cv2.approxPolyDP(smoothed, epsilon, True)
        if len(approx) >= 3:
            refined.append(approx)
    return refined


def _render_overlay(image: Image.Image, masks: list[np.ndarray]) -> str:
    rgb = np.array(image)
    overlay = rgb.copy()
    height, width = rgb.shape[:2]
    colors = [
        (0, 177, 106),
        (245, 130, 32),
        (64, 145, 255),
        (220, 72, 91),
        (146, 96, 255),
        (247, 199, 72),
    ]

    for index, mask in enumerate(masks):
        binary = _refine_binary_mask(mask, width, height)
        if not np.any(binary):
            continue

        color = np.array(colors[index % len(colors)], dtype=np.uint8)
        overlay[binary.astype(bool)] = (overlay[binary.astype(bool)] * 0.48 + color * 0.52).astype(np.uint8)

        contours = _contours_from_binary(binary)
        if contours:
            cv2.drawContours(overlay, contours, -1, tuple(int(c) for c in color), 2)

    output = Image.fromarray(overlay)
    buffer = io.BytesIO()
    output.save(buffer, format="PNG")
    return "data:image/png;base64," + base64.b64encode(buffer.getvalue()).decode("ascii")


def _mask_to_geometry(mask: np.ndarray, width: int, height: int) -> dict[str, Any] | None:
    binary = _refine_binary_mask(mask, width, height)
    if not np.any(binary):
        return None

    contours = _contours_from_binary(binary)
    polygons: list[list[list[float]]] = []
    yolo_segments: list[list[float]] = []

    for contour in contours:
        approx = contour.reshape(-1, 2)
        if len(approx) < 3:
            continue

        polygon = [[float(x), float(y)] for x, y in approx]
        segment: list[float] = []
        for x, y in approx:
            segment.extend(
                [
                    max(0.0, min(1.0, float(x) / max(width, 1))),
                    max(0.0, min(1.0, float(y) / max(height, 1))),
                ]
            )
        polygons.append(polygon)
        yolo_segments.append(segment)

    bbox = _mask_bbox(binary.astype(np.float32))
    if bbox is None or not polygons:
        return None

    xs = [point[0] for polygon in polygons for point in polygon]
    ys = [point[1] for polygon in polygons for point in polygon]
    mask_x1, mask_y1, mask_x2, mask_y2 = bbox
    x1 = int(np.floor(min([mask_x1, *xs])))
    y1 = int(np.floor(min([mask_y1, *ys])))
    x2 = int(np.ceil(max([mask_x2, *xs])))
    y2 = int(np.ceil(max([mask_y2, *ys])))

    # The browser draws mask outlines with stroke width, so keep a visible margin
    # around the true mask geometry instead of returning a just-touching box.
    pad = max(8, int(round(max(width, height) * 0.012)))
    x1 = max(0, x1 - pad)
    y1 = max(0, y1 - pad)
    x2 = min(width - 1, x2 + pad)
    y2 = min(height - 1, y2 + pad)
    return {
        "box": {
            "x": x1,
            "y": y1,
            "width": max(1, x2 - x1 + 1),
            "height": max(1, y2 - y1 + 1),
        },
        "yoloBox": {
            "cx": ((x1 + x2) / 2) / max(width, 1),
            "cy": ((y1 + y2) / 2) / max(height, 1),
            "width": (x2 - x1 + 1) / max(width, 1),
            "height": (y2 - y1 + 1) / max(height, 1),
        },
        "polygons": polygons,
        "yoloSegments": yolo_segments,
        "area": int(binary.sum()),
    }


def _build_response(
    prompt: str,
    mode: str,
    image: Image.Image,
    instances: list[dict[str, Any]],
    warning: str | None = None,
) -> dict[str, Any]:
    grouped: dict[str, list[np.ndarray]] = {}
    all_masks: list[np.ndarray] = []
    for instance in instances:
        label = str(instance["label"])
        mask = instance["mask"]
        grouped.setdefault(label, []).append(mask)
        all_masks.append(mask)

    categories = [
        {
            "id": f"cat-{index}",
            "label": _label_display(label),
            "rawLabel": label,
            "count": len(masks),
        }
        for index, (label, masks) in enumerate(grouped.items())
    ]
    overlays = {"all": _render_overlay(image, all_masks)}
    if INCLUDE_CATEGORY_OVERLAYS:
        for category in categories:
            overlays[category["id"]] = _render_overlay(image, grouped[category["rawLabel"]])

    response = {
        "prompt": prompt,
        "mode": mode,
        "count": len(instances),
        "categories": categories,
        "overlays": overlays,
        "overlay": overlays["all"],
    }
    if warning:
        response["warning"] = warning
    return response


@app.get("/")
def index() -> dict[str, str]:
    return {"name": "SAM 3 Backend", "docs": "/docs"}


@app.get("/health")
def health() -> dict[str, bool]:
    return {"ok": True}


@app.get("/api/status")
def status() -> dict[str, Any]:
    return {
        "modelPath": str(MODEL_PATH),
        "modelExists": MODEL_PATH.exists(),
        "modelLoaded": _predictor is not None or _auto_model is not None,
        "device": _get_device(),
    }


@app.post("/api/segment")
async def segment(
    image: UploadFile = File(...),
    prompt: str = Form(""),
    conf: float = Form(0.25),
    half: bool = Form(True),
) -> dict[str, Any]:
    prompt = prompt.strip()
    if not 0.01 <= conf <= 0.95:
        raise HTTPException(status_code=400, detail="Confidence must be between 0.01 and 0.95.")

    content = await image.read()
    temp_path, pil_image = _image_to_temp_file(image, content)

    try:
        result = await run_in_threadpool(_run_segmentation, temp_path, pil_image, prompt, conf, half)
    finally:
        try:
            temp_path.unlink(missing_ok=True)
        except Exception:
            pass

    return result


@app.post("/api/annotation/masks")
async def annotation_masks(
    image: UploadFile = File(...),
    conf: float = Form(0.25),
) -> dict[str, Any]:
    if not 0.01 <= conf <= 0.95:
        raise HTTPException(status_code=400, detail="Confidence must be between 0.01 and 0.95.")

    content = await image.read()
    temp_path, pil_image = _image_to_temp_file(image, content)

    try:
        result = await run_in_threadpool(_run_annotation_masks, temp_path, pil_image, conf)
    finally:
        try:
            temp_path.unlink(missing_ok=True)
        except Exception:
            pass

    return result


def _parse_point_prompt_payload(points_raw: str, labels_raw: str) -> tuple[list[list[float]], list[int]]:
    try:
        points_data = json.loads(points_raw) if isinstance(points_raw, str) else points_raw
        labels_data = json.loads(labels_raw) if isinstance(labels_raw, str) else labels_raw
    except Exception as exc:
        raise HTTPException(status_code=400, detail="points/labels must be valid JSON.") from exc

    if not isinstance(points_data, list) or not isinstance(labels_data, list):
        raise HTTPException(status_code=400, detail="points and labels must be JSON arrays.")
    if len(points_data) == 0:
        raise HTTPException(status_code=400, detail="At least one prompt point is required.")
    if len(points_data) != len(labels_data):
        raise HTTPException(status_code=400, detail="points and labels length must match.")

    points: list[list[float]] = []
    labels: list[int] = []
    for point, label in zip(points_data, labels_data):
        if not isinstance(point, (list, tuple)) or len(point) < 2:
            raise HTTPException(status_code=400, detail="Each point must be [x, y] in original pixels.")
        try:
            x = float(point[0])
            y = float(point[1])
            label_int = int(label)
        except Exception as exc:
            raise HTTPException(status_code=400, detail="Invalid point or label value.") from exc
        if label_int not in (0, 1):
            raise HTTPException(status_code=400, detail="Point labels must be 0 (negative) or 1 (positive).")
        points.append([x, y])
        labels.append(label_int)

    if 1 not in labels:
        raise HTTPException(status_code=400, detail="At least one positive point (label=1) is required.")

    return points, labels


@app.post("/api/segment/points")
async def segment_points(
    image: UploadFile = File(...),
    points: str = Form(...),
    labels: str = Form(...),
    conf: float = Form(0.25),
) -> dict[str, Any]:
    """Interactive point-prompt segmentation. points/labels are original-image pixel coords."""
    if not 0.01 <= conf <= 0.95:
        raise HTTPException(status_code=400, detail="Confidence must be between 0.01 and 0.95.")

    point_coords, point_labels = _parse_point_prompt_payload(points, labels)
    content = await image.read()
    temp_path, pil_image, scale = _image_to_temp_file_with_scale(image, content)

    try:
        result = await run_in_threadpool(
            _run_point_segmentation,
            temp_path,
            pil_image,
            point_coords,
            point_labels,
            conf,
            scale,
        )
    finally:
        try:
            temp_path.unlink(missing_ok=True)
        except Exception:
            pass

    return result


def _run_auto_predict(temp_path: Path, conf: float) -> Any:
    model = _get_auto_model()
    with _inference_lock:
        return model.predict(
            source=str(temp_path),
            conf=conf,
            device=_get_device(),
            retina_masks=True,
            verbose=False,
        )


def _run_segmentation(
    temp_path: Path,
    pil_image: Image.Image,
    prompt: str,
    conf: float,
    half: bool,
) -> dict[str, Any]:
    warning = None
    try:
        if prompt:
            prompt_labels = _parse_prompt_labels(prompt)
            predictor = _get_predictor(conf=conf, half=half)
            if hasattr(predictor, "args"):
                predictor.args.conf = conf
            with _inference_lock:
                predictor.set_image(str(temp_path))
                results = predictor(text=prompt_labels)
            mode = "text"
            instances = _extract_instances(results, fallback_labels=prompt_labels)
        else:
            results = _run_auto_predict(temp_path, conf)
            mode = "auto"
            instances = _extract_instances(results)
            if ENABLE_CLIP_CLASSIFICATION:
                try:
                    labels = _classify_masks(pil_image, [item["mask"] for item in instances])
                    for instance, label in zip(instances, labels):
                        instance["label"] = label
                except Exception as exc:
                    warning = f"Automatic classification failed: {exc}"
                    for instance in instances:
                        instance["label"] = "object"
            else:
                for instance in instances:
                    instance["label"] = "object"
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    return _build_response(prompt, mode, pil_image, instances, warning)


def _run_annotation_masks(temp_path: Path, pil_image: Image.Image, conf: float) -> dict[str, Any]:
    try:
        results = _run_auto_predict(temp_path, conf)
        instances = _extract_instances(results)
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    width, height = pil_image.size
    masks: list[dict[str, Any]] = []
    for index, instance in enumerate(instances):
        geometry = _mask_to_geometry(instance["mask"], width, height)
        if geometry is None:
            continue
        masks.append({"id": f"mask-{index}", **geometry})

    return {
        "width": width,
        "height": height,
        "count": len(masks),
        "masks": masks,
    }


def _extract_mask_arrays(results: Any) -> tuple[list[np.ndarray], list[float]]:
    masks: list[np.ndarray] = []
    scores: list[float] = []

    for result in results or []:
        result_masks = getattr(result, "masks", None)
        if result_masks is None or getattr(result_masks, "data", None) is None:
            continue

        data = result_masks.data
        try:
            data = data.detach().cpu().numpy()
        except AttributeError:
            data = np.asarray(data)

        if data.ndim == 2:
            data = data[None, :, :]

        result_scores: list[float] = []
        boxes = getattr(result, "boxes", None)
        if boxes is not None and getattr(boxes, "conf", None) is not None:
            try:
                result_scores = [float(value) for value in boxes.conf.detach().cpu().numpy().tolist()]
            except AttributeError:
                result_scores = [float(value) for value in np.asarray(boxes.conf).tolist()]

        for index in range(data.shape[0]):
            masks.append(data[index])
            scores.append(result_scores[index] if index < len(result_scores) else float(1.0 - index * 0.01))

    return masks, scores


def _run_point_segmentation(
    temp_path: Path,
    pil_image: Image.Image,
    points: list[list[float]],
    labels: list[int],
    conf: float,
    scale: float,
) -> dict[str, Any]:
    """Run SAM visual point prompts. Input points are original-image pixels; scale maps to inference size."""
    scaled_points = [[float(x) * scale, float(y) * scale] for x, y in points]
    point_labels = [int(label) for label in labels]
    model = _get_auto_model()

    # Ultralytics SAM treats points shaped (N, 2) as N separate objects (each with 1 point).
    # For interactive refinement we need ONE object with N points: shape (1, N, 2) / (1, N).
    prompt_points = [scaled_points]
    prompt_labels = [point_labels]

    predict_kwargs: dict[str, Any] = {
        "source": str(temp_path),
        "points": prompt_points,
        "labels": prompt_labels,
        "conf": conf,
        "device": _get_device(),
        "retina_masks": True,
        "verbose": False,
    }

    try:
        with _inference_lock:
            results = model.predict(**predict_kwargs)
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    mask_arrays, scores = _extract_mask_arrays(results)
    if not mask_arrays:
        raise HTTPException(status_code=422, detail="SAM returned no mask for the given points.")

    # Point-prompt mode edits one target at a time; keep the best mask only.
    best_index = max(range(len(mask_arrays)), key=lambda index: scores[index])
    width, height = pil_image.size
    geometry = _mask_to_geometry(mask_arrays[best_index], width, height)
    if geometry is None:
        raise HTTPException(status_code=422, detail="SAM masks could not be converted to geometry.")

    overlay = _render_overlay(pil_image, [mask_arrays[best_index]])
    candidate = {
        "id": "cand-0",
        "score": float(scores[best_index]),
        "overlay": overlay,
        **geometry,
    }

    return {
        "mode": "points",
        "width": width,
        "height": height,
        "count": 1,
        "candidates": [candidate],
        "overlay": overlay,
        "points": points,
        "labels": labels,
    }
