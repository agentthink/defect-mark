FROM pytorch/pytorch:2.9.1-cuda12.8-cudnn9-runtime

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV SAM3_MODEL=/models/sam3.pt
ENV SAM3_MAX_INFERENCE_SIDE=1024
ENV SAM3_REQUIRE_INTERNAL_KEY=true

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends git libglib2.0-0 libgl1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements-docker.txt .
RUN pip install --no-cache-dir -r requirements-docker.txt

# Pre-cache CLIP ViT-B-32 when available. Network flakes should not fail the image build;
# auto-labeling can download on first use, or disable via SAM3_ENABLE_CLIP_CLASSIFICATION=false.
RUN --mount=type=bind,target=/tmp/build-context python - <<'PY'
import hashlib
import shutil
import time
import urllib.request
from pathlib import Path

expected = "40d365715913c9da98579312b702a82c18be219cc2a73407c4526f58eba950af"
url = f"https://openaipublic.azureedge.net/clip/models/{expected}/ViT-B-32.pt"
dest = Path.home() / ".cache" / "clip" / "ViT-B-32.pt"
partial = dest.with_suffix(".pt.partial")
bundled = Path("/tmp/build-context/clip-cache/ViT-B-32.pt")
dest.parent.mkdir(parents=True, exist_ok=True)

def is_ready(path: Path = dest) -> bool:
    return path.exists() and path.stat().st_size > 300_000_000 and hashlib.sha256(path.read_bytes()).hexdigest() == expected

if bundled.exists() and is_ready(bundled):
    shutil.copy2(bundled, dest)
    print(f"CLIP model copied from build context: {dest}")
elif is_ready():
    print(f"CLIP model already present: {dest}")
else:
    for attempt in range(1, 6):
        if is_ready():
            break
        print(f"Downloading CLIP ViT-B-32.pt (attempt {attempt}/5)...")
        try:
            if partial.exists():
                partial.unlink()
            urllib.request.urlretrieve(url, partial)
            if not is_ready(partial):
                raise RuntimeError("incomplete or checksum mismatch")
            partial.replace(dest)
        except Exception as exc:
            print(f"CLIP download failed: {exc}")
            time.sleep(min(60, 5 * attempt))

if is_ready():
    print(f"CLIP model ready at {dest}")
else:
    print("WARNING: CLIP weights unavailable at build time; first auto-classify may download at runtime.")
PY
RUN python -c "from pathlib import Path; p=Path.home()/'.cache'/'clip'/'ViT-B-32.pt';\
import clip; clip.load('ViT-B/32', device='cpu') if p.exists() and p.stat().st_size>300000000 else print('skip clip.load')"

COPY app ./app

EXPOSE 8001

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001"]
