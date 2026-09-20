export function matchesAnnotationFilters(item, filters, imageWidth, imageHeight) {
  const box = item.bbox;
  if (!box) return false;
  if (filters.label && String(item.label ?? "").trim().toLowerCase() !== filters.label.trim().toLowerCase()) return false;
  const value = (key, percent = true) => {
    if (String(filters[key] ?? "").trim() === "") return null;
    const number = Number(filters[key]);
    return Number.isFinite(number) ? number / (percent ? 100 : 1) : null;
  };
  const range = (number, min, max) => (min === null || number >= min) && (max === null || number <= max);
  const minAspect = value("minAspect", false);
  const maxAspect = value("maxAspect", false);
  if ((minAspect !== null || maxAspect !== null) && !(imageWidth > 0 && imageHeight > 0 && box.height > 0)) return false;
  const aspect = box.width * imageWidth / (box.height * imageHeight);
  return range(box.width * box.height, value("minArea"), value("maxArea"))
    && range(box.width, value("minWidth"), value("maxWidth"))
    && range(box.height, value("minHeight"), value("maxHeight"))
    && range(box.cx, value("minCenterX"), value("maxCenterX"))
    && range(box.cy, value("minCenterY"), value("maxCenterY"))
    && range(aspect, minAspect, maxAspect)
    && range(Number(item.confidence ?? item.Confidence ?? 1), value("minConfidence", false), null);
}
