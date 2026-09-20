export const LABEL_COLOR_PALETTE = [
  { stroke: "#2563eb", fill: "rgba(37, 99, 235, 0.28)", boxBg: "rgba(37, 99, 235, 0.14)" },
  { stroke: "#16a34a", fill: "rgba(22, 163, 74, 0.28)", boxBg: "rgba(22, 163, 74, 0.14)" },
  { stroke: "#dc2626", fill: "rgba(220, 38, 38, 0.28)", boxBg: "rgba(220, 38, 38, 0.14)" },
  { stroke: "#7c3aed", fill: "rgba(124, 58, 237, 0.28)", boxBg: "rgba(124, 58, 237, 0.14)" },
  { stroke: "#db2777", fill: "rgba(219, 39, 119, 0.28)", boxBg: "rgba(219, 39, 119, 0.14)" },
  { stroke: "#0d9488", fill: "rgba(13, 148, 136, 0.28)", boxBg: "rgba(13, 148, 136, 0.14)" },
  { stroke: "#ea580c", fill: "rgba(234, 88, 12, 0.28)", boxBg: "rgba(234, 88, 12, 0.14)" },
  { stroke: "#0891b2", fill: "rgba(8, 145, 178, 0.28)", boxBg: "rgba(8, 145, 178, 0.14)" },
  { stroke: "#65a30d", fill: "rgba(101, 163, 13, 0.28)", boxBg: "rgba(101, 163, 13, 0.14)" },
  { stroke: "#4f46e5", fill: "rgba(79, 70, 229, 0.28)", boxBg: "rgba(79, 70, 229, 0.14)" },
  { stroke: "#e11d48", fill: "rgba(225, 29, 72, 0.28)", boxBg: "rgba(225, 29, 72, 0.14)" },
  { stroke: "#ca8a04", fill: "rgba(202, 138, 4, 0.28)", boxBg: "rgba(202, 138, 4, 0.14)" }
];

export const UNASSIGNED_LABEL_COLOR = {
  stroke: "#94a3b8",
  fill: "rgba(148, 163, 184, 0.22)",
  boxBg: "rgba(148, 163, 184, 0.12)"
};

export function resolveLabelColorIndex(label, labels) {
  if (label === null || label === undefined || String(label).trim() === "") return -1;
  const normalized = String(label).trim().toLowerCase();
  return labels.findIndex((item) => item.toLowerCase() === normalized);
}

export function getLabelColor(label, labels) {
  const index = resolveLabelColorIndex(label, labels);
  if (index < 0) return UNASSIGNED_LABEL_COLOR;
  return LABEL_COLOR_PALETTE[index % LABEL_COLOR_PALETTE.length];
}

export function buildAnnotationBoxStyle(item, labels, isActive) {
  const box = item.bbox || { cx: 0.5, cy: 0.5, width: 0.2, height: 0.2 };
  const color = getLabelColor(item.label, labels);
  return {
    left: `${(box.cx - box.width / 2) * 100}%`,
    top: `${(box.cy - box.height / 2) * 100}%`,
    width: `${box.width * 100}%`,
    height: `${box.height * 100}%`,
    borderColor: color.stroke,
    backgroundColor: color.boxBg,
    color: "#fff",
    textShadow: "0 1px 2px rgba(0, 0, 0, 0.55)",
    borderWidth: isActive ? "3px" : "2px",
    boxShadow: isActive ? `0 0 0 2px ${color.stroke}, 0 0 0 4px rgba(255, 255, 255, 0.9)` : undefined
  };
}

export function buildAnnotationPolygonStyle(item, labels, isActive) {
  const color = getLabelColor(item.label, labels);
  return {
    fill: color.fill,
    stroke: color.stroke,
    strokeWidth: isActive ? 1 : 0.65
  };
}

export function buildLabelChipStyle(label, labels) {
  const color = getLabelColor(label, labels);
  return {
    borderColor: color.stroke,
    backgroundColor: color.boxBg,
    color: color.stroke
  };
}

export function buildAnnotationRowAccentStyle(item, labels) {
  const color = getLabelColor(item.label, labels);
  return { borderLeftColor: color.stroke };
}

export function buildLabelSwatchStyle(label, labels) {
  const color = getLabelColor(label, labels);
  return { backgroundColor: color.stroke };
}
