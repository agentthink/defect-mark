export function normalizeProjectDataType(dataType) {
  return String(dataType || "").toLowerCase() === "segmentation" ? "segmentation" : "detection";
}

export function projectDataTypeLabel(dataType) {
  return normalizeProjectDataType(dataType) === "segmentation" ? "实例分割" : "目标检测";
}

export function projectYoloExportHint(dataType) {
  return normalizeProjectDataType(dataType) === "segmentation"
    ? "YOLO segment（多边形坐标）"
    : "YOLO detect（矩形框 cx cy w h）";
}

export function hasValidSegment(item) {
  const points = item.segment;
  if (!Array.isArray(points) || points.length < 6 || points.length % 2 || points.some(x => !Number.isFinite(x) || x < 0 || x > 1)) return false;
  let area = 0;
  for (let i = 0; i < points.length; i += 2) {
    const next = (i + 2) % points.length;
    area += points[i] * points[next + 1] - points[next] * points[i + 1];
  }
  return Math.abs(area) > 1e-12;
}

export function buildYoloLine(item, dataType) {
  if (normalizeProjectDataType(dataType) === "segmentation") {
    if (!hasValidSegment(item)) throw new Error("目标缺少有效分割轮廓，请补充分割或改用检测格式。");
    return `${item.classId} ${item.segment.map((value) => Number(value).toFixed(6)).join(" ")}`;
  }

  const box = item.bbox || { cx: 0.5, cy: 0.5, width: 0.2, height: 0.2 };
  return `${item.classId} ${Number(box.cx).toFixed(6)} ${Number(box.cy).toFixed(6)} ${Number(box.width).toFixed(6)} ${Number(box.height).toFixed(6)}`;
}

export function buildYoloTxt(annotations, dataType, isExportable) {
  return (annotations || [])
    .filter(isExportable)
    .map((item) => buildYoloLine(item, dataType))
    .join("\n");
}
