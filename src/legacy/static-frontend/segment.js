const statusText = document.querySelector("#statusText");
const form = document.querySelector("#segmentForm");
const imageInput = document.querySelector("#imageInput");
const promptInput = document.querySelector("#promptInput");
const confInput = document.querySelector("#confInput");
const confValue = document.querySelector("#confValue");
const halfInput = document.querySelector("#halfInput");
const fileName = document.querySelector("#fileName");
const submitButton = document.querySelector("#submitButton");
const resultTitle = document.querySelector("#resultTitle");
const resultMeta = document.querySelector("#resultMeta");
const previewImage = document.querySelector("#previewImage");
const canvasWrap = document.querySelector(".canvas-wrap");
const categoryList = document.querySelector("#categoryList");

let latestOverlays = {};

function renderCategories(categories = [], total = 0, overlays = {}) {
  latestOverlays = overlays;
  categoryList.innerHTML = "";

  const items = [{ id: "all", label: "全部", count: total }, ...categories];
  for (const item of items) {
    const button = document.createElement("button");
    button.className = "category-item";
    button.type = "button";
    button.dataset.overlayId = item.id;
    button.innerHTML = `<span>${item.label}</span><b>${item.count}</b>`;
    if (!overlays[item.id]) {
      button.disabled = true;
    }
    button.addEventListener("click", () => {
      const overlay = latestOverlays[item.id];
      if (!overlay) {
        return;
      }
      previewImage.src = overlay;
      document.querySelectorAll(".category-item").forEach((node) => {
        node.classList.toggle("active", node === button);
      });
      resultMeta.textContent = item.id === "all" ? `显示全部 ${total} 个 mask` : `显示 ${item.label} · ${item.count} 个 mask`;
    });
    categoryList.appendChild(button);
  }

  const first = categoryList.querySelector(".category-item");
  if (first) {
    first.classList.add("active");
  }
}

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

confInput.addEventListener("input", () => {
  confValue.textContent = Number(confInput.value).toFixed(2);
});

imageInput.addEventListener("change", () => {
  const file = imageInput.files[0];
  fileName.textContent = file ? file.name : "选择或拖入图片";
  if (file) {
    previewImage.src = URL.createObjectURL(file);
    canvasWrap.classList.add("has-image");
    resultTitle.textContent = "原图预览";
    resultMeta.textContent = "可直接开始分割";
    renderCategories([], 0, {});
  }
});

form.addEventListener("submit", async (event) => {
  event.preventDefault();

  const file = imageInput.files[0];
  const prompt = promptInput.value.trim();
  if (!file) {
    return;
  }

  const body = new FormData();
  body.append("image", file);
  body.append("prompt", prompt);
  body.append("conf", confInput.value);
  body.append("half", halfInput.checked ? "true" : "false");

  submitButton.disabled = true;
  submitButton.textContent = "分割中";
  resultTitle.textContent = "正在推理";
  resultMeta.textContent = "首次加载模型会慢一些";

  try {
    const response = await fetch("/api/segment", {
      method: "POST",
      headers: MaskFlowAuth.authHeaders(),
      body,
    });
    const data = await response.json();
    if (!response.ok) {
      throw new Error(data.detail || "推理失败");
    }

    previewImage.src = data.overlay;
    canvasWrap.classList.add("has-image");
    resultTitle.textContent = data.mode === "auto" ? "自动分割结果" : `结果：${data.prompt}`;
    resultMeta.textContent = `显示全部 ${data.count} 个 mask`;
    renderCategories(data.categories || [], data.count || 0, data.overlays || { all: data.overlay });
  } catch (error) {
    resultTitle.textContent = "推理失败";
    resultMeta.textContent = error.message;
  } finally {
    submitButton.disabled = false;
    submitButton.textContent = "开始分割";
    loadStatus();
  }
});

loadStatus();
MaskFlowAuth.renderAccountStrip("segmentAccountTitle", "segmentAccountMeta");
