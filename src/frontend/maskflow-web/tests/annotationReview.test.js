import test from "node:test";
import assert from "node:assert/strict";
import { matchesAnnotationFilters } from "../src/lib/annotationReview.js";
import { buildYoloLine, buildYoloTxt } from "../src/lib/yoloFormat.js";

test("pixel aspect ratio handles portrait and landscape images", () => {
  const filters = { minAspect: 0.99, maxAspect: 1.01 };
  const square = { bbox: { cx: .5, cy: .5, width: 100 / 1920, height: 100 / 1080 } };
  assert.equal(matchesAnnotationFilters(square, filters, 1920, 1080), true);
  assert.equal(matchesAnnotationFilters(square, filters, 1080, 1920), false);
  assert.equal(matchesAnnotationFilters(square, filters, 0, 0), false);
});

test("area and location filters remain normalized and combine with labels", () => {
  const item = { label: "A", bbox: { cx: .2, cy: .3, width: .2, height: .1 } };
  assert.equal(matchesAnnotationFilters(item, { label: "a", maxArea: 3, minCenterX: 10 }, 1920, 1080), true);
  assert.equal(matchesAnnotationFilters(item, { label: "a", maxArea: 1 }, 1920, 1080), false);
});

test("segment export rejects a mixed polygon and rectangle dataset", () => {
  const box = { classId: 0, bbox: { cx: .5, cy: .5, width: .2, height: .2 } };
  const polygon = { ...box, segment: [.1, .1, .5, .1, .5, .5] };
  assert.match(buildYoloLine(box, "detection"), /^0 0.500000/);
  assert.equal(buildYoloLine(polygon, "segmentation").split(" ").length, 7);
  assert.throws(() => buildYoloTxt([polygon, box], "segmentation", () => true), /分割轮廓/);
});
