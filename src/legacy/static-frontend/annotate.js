const statusText = document.querySelector("#statusText");
const folderInput = document.querySelector("#folderInput");
const folderName = document.querySelector("#folderName");
const prevImageButton = document.querySelector("#prevImageButton");
const nextImageButton = document.querySelector("#nextImageButton");
const autoMaskButton = document.querySelector("#autoMaskButton");
const saveAnnotationButton = document.querySelector("#saveAnnotationButton");
const exportYoloButton = document.querySelector("#exportYoloButton");
const exportDatasetButton = document.querySelector("#exportDatasetButton");
const newLabelInput = document.querySelector("#newLabelInput");
const addLabelButton = document.querySelector("#addLabelButton");
const labelList = document.querySelector("#labelList");
const imageList = document.querySelector("#imageList");
const annotatorTitle = document.querySelector("#annotatorTitle");
const annotatorMeta = document.querySelector("#annotatorMeta");
const annotationCanvas = document.querySelector("#annotationCanvas");
const annotationEmptyState = document.querySelector("#annotationEmptyState");
const maskList = document.querySelector("#maskList");
const annotationList = document.querySelector("#annotationList");
const annotationContext = annotationCanvas.getContext("2d");

const LABEL_COLORS = [
  "#117a65",
  "#326fd1",
  "#f58220",
  "#b547b8",
  "#d1495b",
  "#2a9d8f",
  "#6f4bd8",
  "#8a6d1d",
  "#1f8ac0",
  "#c2571a",
];

const annotator = {
  files: [],
  index: 0,
  image: null,
  imageUrl: "",
  masks: [],
  selectedMaskId: "",
  labels: loadLabels(),
  activeLabel: "",
  annotations: [],
};

async function loadStatus() {
  try {
    const response = await fetch("/api/status");
    const data = await response.json();
    const device = data.device === "cuda" ? "CUDA GPU" : "CPU";
    statusText.textContent = data.modelExists ? `模型就绪 · ${device}` : "未找到 sam3.pt";
    statusText.classList.toggle("error", !data.modelExists);
  } catch (error) {
    statusText.textContent = "服务状态读取失败";
    statusText.classList.add("error");
  }
}

function colorForLabel(label) {
  const index = Math.max(0, annotator.labels.indexOf(label));
  return LABEL_COLORS[index % LABEL_COLORS.length];
}

function rgba(hex, alpha) {
  const value = hex.replace("#", "");
  const r = parseInt(value.slice(0, 2), 16);
  const g = parseInt(value.slice(2, 4), 16);
  const b = parseInt(value.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function storageKeyForFile(file) {
  const relative = file.webkitRelativePath || file.name;
  return `maskflow.annotation.${relative}.${file.size}.${file.lastModified}`;
}

function loadLabels() {
  const saved = localStorage.getItem("maskflow.labels") || localStorage.getItem("sam3.labels");
  if (saved) {
    try {
      return JSON.parse(saved);
    } catch (error) {
      return ["scissors", "hammer"];
    }
  }
  return ["scissors", "hammer"];
}

function saveLabels() {
  localStorage.setItem("maskflow.labels", JSON.stringify(annotator.labels));
}

function loadAnnotations(file) {
  if (!file) {
    return [];
  }
  const saved = localStorage.getItem(storageKeyForFile(file));
  if (!saved) {
    return [];
  }
  try {
    return JSON.parse(saved);
  } catch (error) {
    return [];
  }
}

function persistAnnotations() {
  const file = annotator.files[annotator.index];
  if (!file) {
    return;
  }
  localStorage.setItem(storageKeyForFile(file), JSON.stringify(annotator.annotations));
  renderImageList();
  renderAnnotationList();
  drawAnnotationCanvas();
}

function isImageFile(file) {
  return file.type.startsWith("image/") || /\.(jpe?g|png|webp|bmp)$/i.test(file.name);
}

function renderLabels() {
  labelList.innerHTML = "";
  if (!annotator.activeLabel && annotator.labels.length) {
    annotator.activeLabel = annotator.labels[0];
  }

  annotator.labels.forEach((label, index) => {
    const row = document.createElement("div");
    row.className = "label-row";
    row.classList.toggle("active", label === annotator.activeLabel);

    const button = document.createElement("button");
    button.className = "label-chip";
    button.type = "button";
    button.innerHTML = `<i style="background:${colorForLabel(label)}"></i><span>${index}: ${label}</span>`;
    button.addEventListener("click", () => {
      annotator.activeLabel = label;
      renderLabels();
    });

    const removeButton = document.createElement("button");
    removeButton.className = "label-delete";
    removeButton.type = "button";
    removeButton.textContent = "删除";
    removeButton.addEventListener("click", () => {
      const inUse = annotator.annotations.some((annotation) => annotation.label === label);
      if (inUse) {
        annotatorMeta.textContent = `标签 ${label} 已被当前图片使用，先删除对应标注`;
        return;
      }
      annotator.labels = annotator.labels.filter((item) => item !== label);
      if (annotator.activeLabel === label) {
        annotator.activeLabel = annotator.labels[0] || "";
      }
      saveLabels();
      renderLabels();
      drawAnnotationCanvas();
    });

    row.appendChild(button);
    row.appendChild(removeButton);
    labelList.appendChild(row);
  });
}

function renderImageList() {
  imageList.innerHTML = "";
  annotator.files.forEach((file, index) => {
    const button = document.createElement("button");
    const saved = loadAnnotations(file).length;
    button.className = "image-item";
    button.type = "button";
    button.classList.toggle("active", index === annotator.index);
    button.innerHTML = `<span>${file.webkitRelativePath || file.name}</span><b>${saved}</b>`;
    button.addEventListener("click", () => {
      saveBeforeMove();
      annotator.index = index;
      loadCurrentImage();
    });
    imageList.appendChild(button);
  });
}

function renderMaskList() {
  maskList.innerHTML = "";
  if (!annotator.masks.length) {
    maskList.innerHTML = `<div class="empty-note">点击自动分割当前图</div>`;
    return;
  }

  annotator.masks.forEach((mask, index) => {
    const button = document.createElement("button");
    button.className = "mask-item";
    button.type = "button";
    button.classList.toggle("active", mask.id === annotator.selectedMaskId);
    button.innerHTML = `<span>Mask ${index + 1}</span><b>${Math.round(mask.area || 0)}</b>`;
    button.addEventListener("click", () => {
      annotator.selectedMaskId = mask.id;
      renderMaskList();
      drawAnnotationCanvas();
    });
    maskList.appendChild(button);
  });
}

function renderAnnotationList() {
  annotationList.innerHTML = "";
  if (!annotator.annotations.length) {
    annotationList.innerHTML = `<div class="empty-note">暂无标注</div>`;
    return;
  }

  annotator.annotations.forEach((annotation) => {
    const row = document.createElement("div");
    row.className = "annotation-row";
    row.innerHTML = `<span><i style="background:${annotation.color}"></i>${annotation.label}</span><button type="button">删除</button>`;
    row.querySelector("button").addEventListener("click", () => {
      annotator.annotations = annotator.annotations.filter((item) => item.id !== annotation.id);
      persistAnnotations();
    });
    annotationList.appendChild(row);
  });
}

function saveBeforeMove() {
  if (annotator.files.length) {
    persistAnnotations();
  }
}

function setCanvasImage(image) {
  annotationCanvas.width = image.naturalWidth;
  annotationCanvas.height = image.naturalHeight;
  annotationCanvas.style.display = "block";
  annotationEmptyState.style.display = "none";
  drawAnnotationCanvas();
}

function drawPolygon(points, fillStyle, strokeStyle, lineWidth = 3) {
  if (!points || points.length < 3) {
    return;
  }
  annotationContext.beginPath();
  annotationContext.moveTo(points[0][0], points[0][1]);
  for (let index = 1; index < points.length; index += 1) {
    annotationContext.lineTo(points[index][0], points[index][1]);
  }
  annotationContext.closePath();
  annotationContext.fillStyle = fillStyle;
  annotationContext.strokeStyle = strokeStyle;
  annotationContext.lineWidth = lineWidth;
  annotationContext.fill();
  annotationContext.stroke();
}

function drawBox(box, strokeStyle, lineWidth = 3) {
  if (!box) {
    return;
  }
  annotationContext.save();
  annotationContext.strokeStyle = strokeStyle;
  annotationContext.lineWidth = lineWidth;
  annotationContext.strokeRect(box.x, box.y, box.width, box.height);
  annotationContext.restore();
}

function drawAnnotationCanvas() {
  if (!annotator.image) {
    return;
  }
  annotationContext.clearRect(0, 0, annotationCanvas.width, annotationCanvas.height);
  annotationContext.drawImage(annotator.image, 0, 0);

  const selectedMask = annotator.masks.find((mask) => mask.id === annotator.selectedMaskId);
  if (selectedMask) {
    const color = annotator.activeLabel ? colorForLabel(annotator.activeLabel) : "#117a65";
    selectedMask.polygons.forEach((polygon) => drawPolygon(polygon, rgba(color, 0.35), color, 5));
    drawBox(selectedMask.box, color, 5);
  }

  annotator.annotations.forEach((annotation) => {
    drawBox(annotation.box, annotation.color, 5);
    annotationContext.fillStyle = annotation.color;
    annotationContext.font = "24px Segoe UI, Arial";
    annotationContext.fillText(annotation.label, annotation.box.x, Math.max(26, annotation.box.y - 8));
  });
}

function pointInPolygon(point, polygon) {
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i, i += 1) {
    const xi = polygon[i][0];
    const yi = polygon[i][1];
    const xj = polygon[j][0];
    const yj = polygon[j][1];
    const intersects = yi > point.y !== yj > point.y && point.x < ((xj - xi) * (point.y - yi)) / (yj - yi + 0.00001) + xi;
    if (intersects) {
      inside = !inside;
    }
  }
  return inside;
}

annotationCanvas.addEventListener("click", (event) => {
  if (!annotator.masks.length) {
    return;
  }
  const rect = annotationCanvas.getBoundingClientRect();
  const point = {
    x: ((event.clientX - rect.left) / rect.width) * annotationCanvas.width,
    y: ((event.clientY - rect.top) / rect.height) * annotationCanvas.height,
  };
  const hit = annotator.masks.find((mask) => mask.polygons.some((polygon) => pointInPolygon(point, polygon)));
  if (hit) {
    annotator.selectedMaskId = hit.id;
    renderMaskList();
    drawAnnotationCanvas();
  }
});

function loadCurrentImage() {
  const file = annotator.files[annotator.index];
  if (!file) {
    return;
  }

  if (annotator.imageUrl) {
    URL.revokeObjectURL(annotator.imageUrl);
  }
  annotator.imageUrl = URL.createObjectURL(file);
  annotator.image = new Image();
  annotator.image.onload = () => setCanvasImage(annotator.image);
  annotator.image.src = annotator.imageUrl;
  annotator.masks = [];
  annotator.selectedMaskId = "";
  annotator.annotations = loadAnnotations(file);

  annotatorTitle.textContent = file.webkitRelativePath || file.name;
  annotatorMeta.textContent = `${annotator.index + 1} / ${annotator.files.length} · 已保存 ${annotator.annotations.length} 个标注`;
  renderImageList();
  renderMaskList();
  renderAnnotationList();
}

folderInput.addEventListener("change", () => {
  const files = Array.from(folderInput.files || []).filter(isImageFile);
  files.sort((a, b) => (a.webkitRelativePath || a.name).localeCompare(b.webkitRelativePath || b.name, "zh-CN"));
  annotator.files = files;
  annotator.index = 0;
  folderName.textContent = files.length ? `已加载 ${files.length} 张图片` : "选择图片文件夹";
  if (files.length) {
    loadCurrentImage();
  }
});

prevImageButton.addEventListener("click", () => {
  if (!annotator.files.length) {
    return;
  }
  saveBeforeMove();
  annotator.index = Math.max(0, annotator.index - 1);
  loadCurrentImage();
});

nextImageButton.addEventListener("click", () => {
  if (!annotator.files.length) {
    return;
  }
  saveBeforeMove();
  annotator.index = Math.min(annotator.files.length - 1, annotator.index + 1);
  loadCurrentImage();
});

autoMaskButton.addEventListener("click", async () => {
  const file = annotator.files[annotator.index];
  if (!file) {
    return;
  }
  const body = new FormData();
  body.append("image", file);
  body.append("conf", "0.25");

  autoMaskButton.disabled = true;
  autoMaskButton.textContent = "分割中";
  annotatorMeta.textContent = "正在自动分割当前图片";

  try {
    const response = await fetch("/api/annotation/masks", {
      method: "POST",
      headers: MaskFlowAuth.authHeaders(),
      body,
    });
    const data = await response.json();
    if (!response.ok) {
      throw new Error(data.detail || "自动分割失败");
    }
    annotator.masks = data.masks || [];
    annotator.selectedMaskId = annotator.masks[0]?.id || "";
    annotatorMeta.textContent = `找到 ${annotator.masks.length} 个 mask · 当前只显示选中的 mask`;
    renderMaskList();
    drawAnnotationCanvas();
  } catch (error) {
    annotatorMeta.textContent = error.message;
  } finally {
    autoMaskButton.disabled = false;
    autoMaskButton.textContent = "自动分割当前图";
  }
});

addLabelButton.addEventListener("click", () => {
  const label = newLabelInput.value.trim();
  if (!label || annotator.labels.includes(label)) {
    return;
  }
  annotator.labels.push(label);
  annotator.activeLabel = label;
  newLabelInput.value = "";
  saveLabels();
  renderLabels();
  drawAnnotationCanvas();
});

saveAnnotationButton.addEventListener("click", () => {
  const mask = annotator.masks.find((item) => item.id === annotator.selectedMaskId);
  if (!mask || !annotator.activeLabel) {
    annotatorMeta.textContent = "请先选择 mask 和标签";
    return;
  }
  const classId = annotator.labels.indexOf(annotator.activeLabel);
  const color = colorForLabel(annotator.activeLabel);
  const annotation = {
    id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    maskId: mask.id,
    label: annotator.activeLabel,
    classId,
    color,
    box: mask.box,
    yoloBox: mask.yoloBox,
    polygons: mask.polygons,
  };
  annotator.annotations.push(annotation);
  annotatorMeta.textContent = `已保存 ${annotator.activeLabel}`;
  persistAnnotations();
});

function yoloTextForAnnotations(annotations) {
  return annotations
    .map((annotation) => {
      const box = annotation.yoloBox;
      return `${annotation.classId} ${box.cx.toFixed(6)} ${box.cy.toFixed(6)} ${box.width.toFixed(6)} ${box.height.toFixed(6)}`;
    })
    .join("\n");
}

exportYoloButton.addEventListener("click", () => {
  const file = annotator.files[annotator.index];
  if (!file || !annotator.annotations.length) {
    annotatorMeta.textContent = "当前图片还没有可导出的标注";
    return;
  }
  const blob = new Blob([`${yoloTextForAnnotations(annotator.annotations)}\n`], { type: "text/plain;charset=utf-8" });
  downloadBlob(blob, `${file.name.replace(/\.[^.]+$/, "")}.txt`);
});

exportDatasetButton.addEventListener("click", async () => {
  saveBeforeMove();
  const labeled = annotator.files
    .map((file) => ({ file, annotations: loadAnnotations(file) }))
    .filter((item) => item.annotations.length > 0);
  if (!labeled.length) {
    annotatorMeta.textContent = "还没有已保存的标注，无法导出数据集";
    return;
  }

  exportDatasetButton.disabled = true;
  exportDatasetButton.textContent = "打包中";
  try {
    const zip = new ZipBuilder();
    const splits = splitDataset(labeled);
    const root = "maskflow_dataset";

    zip.addText(`${root}/classes.txt`, `${annotator.labels.join("\n")}\n`);
    zip.addText(`${root}/dataset.yaml`, buildDatasetYaml());
    zip.addText(`${root}/requirements.txt`, "ultralytics>=8.3.0\n");
    zip.addText(`${root}/README.md`, buildDatasetReadme());
    zip.addText(`${root}/train.py`, buildTrainScript());

    for (const [split, items] of Object.entries(splits)) {
      for (const item of items) {
        const imageName = safeFileName(item.file.name);
        const labelName = `${imageName.replace(/\.[^.]+$/, "")}.txt`;
        zip.addFile(`${root}/images/${split}/${imageName}`, new Uint8Array(await item.file.arrayBuffer()));
        zip.addText(`${root}/labels/${split}/${labelName}`, `${yoloTextForAnnotations(item.annotations)}\n`);
      }
    }

    downloadBlob(zip.build(), "maskflow_dataset.zip");
    annotatorMeta.textContent = `已导出 ${labeled.length} 张已标注图片`;
  } finally {
    exportDatasetButton.disabled = false;
    exportDatasetButton.textContent = "下载数据集 ZIP";
  }
});

function splitDataset(items) {
  const sorted = [...items].sort((a, b) => safeFileName(a.file.name).localeCompare(safeFileName(b.file.name)));
  const total = sorted.length;
  const trainCount = Math.max(1, Math.floor(total * 0.8));
  const valCount = total >= 3 ? Math.max(1, Math.floor(total * 0.1)) : 0;
  return {
    train: sorted.slice(0, trainCount),
    val: sorted.slice(trainCount, trainCount + valCount),
    test: sorted.slice(trainCount + valCount),
  };
}

function buildDatasetYaml() {
  const names = annotator.labels.map((label, index) => `  ${index}: ${label}`).join("\n");
  return `path: .\ntrain: images/train\nval: images/val\ntest: images/test\nnames:\n${names}\n`;
}

function buildTrainScript() {
  return `from ultralytics import YOLO\n\nmodel = YOLO("yolo11n.pt")\nmodel.train(data="dataset.yaml", epochs=100, imgsz=640, batch=16)\n`;
}

function buildDatasetReadme() {
  return `# MaskFlow Dataset\n\nThis dataset is exported by MaskFlow.\n\n## Train\n\npip install -r requirements.txt\npython train.py\n\n## Structure\n\n- images/train, images/val, images/test\n- labels/train, labels/val, labels/test\n- dataset.yaml\n`;
}

function safeFileName(name) {
  return name.replace(/[\\/:*?"<>|]+/g, "_");
}

function downloadBlob(blob, filename) {
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = filename;
  link.click();
  URL.revokeObjectURL(link.href);
}

class ZipBuilder {
  constructor() {
    this.files = [];
  }

  addText(path, text) {
    this.addFile(path, new TextEncoder().encode(text));
  }

  addFile(path, bytes) {
    this.files.push({ path, bytes, crc: crc32(bytes) });
  }

  build() {
    const chunks = [];
    const central = [];
    let offset = 0;
    for (const file of this.files) {
      const name = new TextEncoder().encode(file.path);
      const local = concatBytes([
        u32(0x04034b50),
        u16(20),
        u16(0),
        u16(0),
        u16(0),
        u16(0),
        u32(file.crc),
        u32(file.bytes.length),
        u32(file.bytes.length),
        u16(name.length),
        u16(0),
        name,
        file.bytes,
      ]);
      chunks.push(local);

      central.push(
        concatBytes([
          u32(0x02014b50),
          u16(20),
          u16(20),
          u16(0),
          u16(0),
          u16(0),
          u16(0),
          u32(file.crc),
          u32(file.bytes.length),
          u32(file.bytes.length),
          u16(name.length),
          u16(0),
          u16(0),
          u16(0),
          u16(0),
          u32(0),
          u32(offset),
          name,
        ])
      );
      offset += local.length;
    }

    const centralOffset = offset;
    const centralBytes = concatBytes(central);
    const end = concatBytes([
      u32(0x06054b50),
      u16(0),
      u16(0),
      u16(this.files.length),
      u16(this.files.length),
      u32(centralBytes.length),
      u32(centralOffset),
      u16(0),
    ]);

    return new Blob([...chunks, centralBytes, end], { type: "application/zip" });
  }
}

function u16(value) {
  const bytes = new Uint8Array(2);
  new DataView(bytes.buffer).setUint16(0, value, true);
  return bytes;
}

function u32(value) {
  const bytes = new Uint8Array(4);
  new DataView(bytes.buffer).setUint32(0, value >>> 0, true);
  return bytes;
}

function concatBytes(parts) {
  const length = parts.reduce((sum, part) => sum + part.length, 0);
  const output = new Uint8Array(length);
  let offset = 0;
  for (const part of parts) {
    output.set(part, offset);
    offset += part.length;
  }
  return output;
}

function crc32(bytes) {
  let crc = -1;
  for (let i = 0; i < bytes.length; i += 1) {
    crc = (crc >>> 8) ^ CRC_TABLE[(crc ^ bytes[i]) & 0xff];
  }
  return (crc ^ -1) >>> 0;
}

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n += 1) {
    let c = n;
    for (let k = 0; k < 8; k += 1) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c >>> 0;
  }
  return table;
})();

renderLabels();
renderMaskList();
renderAnnotationList();
loadStatus();
MaskFlowAuth.renderAccountStrip("annotateAccountTitle", "annotateAccountMeta");
