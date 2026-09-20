import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir } from "node:fs/promises";
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || "playwright");
const base = process.env.TEST_BASE_URL || "http://127.0.0.1:3010";
const output = process.env.TEST_OUTPUT || "test-results";
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: "chrome" });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const errors = [];
page.on("pageerror", e => errors.push(e.message));
const user = { id: 1, username: "Review", plan: "Free" };
await page.addInitScript(user => localStorage.setItem("maskflow.session", JSON.stringify({ token: "test-only", user })), user);
const files = [1, 2].map(id => ({ id, name: `${id}.jpg`, size: 2000, annotated: true, annotationCount: 1, projectId: "p", downloadUrl: `/api/files/${id}/download` }));
const annotation = id => ({ fileId: id, width: id === 1 ? 1920 : 1080, height: id === 1 ? 1080 : 1920,
  annotations: [{ id: `ann${id}`, label: "针尖", classId: 0, confirmed: true, confidence: 1,
    bbox: { cx: .5, cy: .5, width: 100 / 1920, height: 100 / 1080 }, segment: [.1, .1, .5, .1, .5, .5] }] });
let releaseA;
let aRequested;
const startedA = new Promise(resolve => { aRequested = resolve; });
const holdA = new Promise(resolve => { releaseA = resolve; });
let delayA = true;
let putBodies = [];
let failAnalysis = false;
const analysisFixture = request => {
  const classes = ["针尖", "瓶盖", "缺损"].map((label, i) => ({ label, images: [100, 30, 5][i], annotations: [200, 60, 10][i],
    objectPercent: [74.1, 22.2, 3.7][i], imagePercent: [74.1, 22.2, 3.7][i], suggestedAdditionalImages: [0, 4, 29][i], targetImages: 34,
    splits: { train: { images: [70, 21, 3][i] }, val: { images: [20, 6, 1][i] }, test: { images: [10, 3, 1][i] } },
    recommendedSplits: { train: { images: [80, 24, 4][i] }, val: { images: [20, 6, 1][i] }, test: { images: 0 } } }));
  if (request.split.test === 0) for (const item of classes) item.splits = item.recommendedSplits;
  return { totalImages: 140, exportImages: 135, totalAnnotations: 270, unconfirmedAnnotations: 12, classes,
    recommendationAvailable: true, recommendedSplit: { train: 80, val: 20, test: 0 },
    recommendationReason: "最少类别不足 15 张原图，先保留训练与验证两组，暂不单独留测试集。",
    preview: { images: request.split.test === 0 ? { train: 108, val: 27, test: 0 } : { train: 94, val: 27, test: 14 } },
    supplementBasis: "补样参考目标为每类至少 34 张原图，优先增加不同批次、光照、角度和困难场景的真实图片。",
    limitation: "建议基于已保存标签统计，不代表最优比例。多标签原图占比之和可超过 100%。",
    warnings: ["有 5 张图片尚未保存标注，不会参与本次导出。", "有 12 个目标尚未人工确认。"],
    canExport: request.format !== "yolo-segment" };
};
await page.route("**/api/**", async route => {
  const request = route.request();
  const path = new URL(request.url()).pathname;
  let data = {};
  if (path === "/api/projects") data = { projects: [{ id: "p", name: "针尖检测 · 测试数据", dataType: "detection", imageCount: 140 }] };
  else if (path.endsWith("/labels")) data = { labels: ["针尖", "瓶盖", "缺损"] };
  else if (path === "/api/files") data = { files, user };
  else if (path.endsWith("/download")) {
    await route.fulfill({ contentType: "image/png", body: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII=", "base64") });
    return;
  } else if (/\/annotations\/file\/\d+$/.test(path)) {
    const id = Number(path.split("/").at(-1));
    if (request.method() === "PUT") { const body = request.postDataJSON(); putBodies.push(body); data = { annotation: body }; }
    else {
      if (id === 1 && delayA) { aRequested(); await holdA; }
      data = { annotation: annotation(id) };
    }
  } else if (path === "/api/export/analyze") {
    if (failAnalysis) { await route.fulfill({ status: 503, json: { detail: "分析服务暂不可用" } }); return; }
    data = { analysis: analysisFixture(request.postDataJSON()) };
  } else if (path === "/api/export") data = { exports: [] };
  await route.fulfill({ json: data });
});

try {
  await page.goto(`${base}/annotate.html`);
  await page.locator(".file-row-btn").first().waitFor();
  await page.locator(".file-row-btn").first().click();
  await startedA;
  await page.locator(".file-row-btn").nth(1).click();
  await page.waitForFunction(() => document.querySelector("#app").__vue_app__._instance.provides.annotate.ready);
  releaseA(); delayA = false;
  await page.waitForTimeout(100);
  assert.deepEqual(await page.evaluate(() => {
    const a = document.querySelector("#app").__vue_app__._instance.provides.annotate;
    return [a.current.id, a.fileId, a.annotations[0].id];
  }), [2, 2, "ann2"]);
  await page.locator(".confirm-dot").click();
  await page.locator(".annotate-topbar").getByRole("button", { name: "保存", exact: true }).click();
  await page.waitForFunction(() => !document.querySelector("#app").__vue_app__._instance.provides.loading.value);
  assert.equal(putBodies[0].fileId, 2);
  assert.equal(putBodies[0].annotations[0].confirmed, false);
  assert.equal(await page.locator(".confirm-dot.unconfirmed").count(), 1);

  putBodies = [];
  const matches = await page.evaluate(async () => {
    const app = document.querySelector("#app").__vue_app__._instance.provides;
    Object.assign(app.annotate.reviewFilters, { minAspect: .99, maxAspect: 1.01 });
    const counts = await app.collectReviewFilterMatches();
    await app.deleteReviewFilterMatches();
    return counts;
  });
  assert.deepEqual(matches, { files: 1, annotations: 1 });
  assert.equal(putBodies.length, 1);
  assert.equal(putBodies[0].fileId, 1);
  assert.equal(putBodies[0].annotations.length, 0);

  await page.goto(`${base}/export.html`);
  await page.getByRole("heading", { name: "建议比例", exact: false }).waitFor();
  await page.getByRole("button", { name: "应用建议比例" }).click();
  await page.waitForFunction(() => !document.querySelector("#app").__vue_app__._instance.provides.exportPage.analysisLoading);
  assert.equal(await page.getByLabel("测试集 test %").inputValue(), "0");
  await page.waitForFunction(() => document.querySelector("#app").__vue_app__._instance.provides.exportPage.analysis?.preview.images.test === 0);
  for (const [name, width, height] of [["desktop", 1440, 1000], ["mobile", 390, 844]]) {
    await page.setViewportSize({ width, height });
    await page.screenshot({ path: `${output}/export-${name}.png`, fullPage: true });
    const sizes = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
    assert.ok(sizes[0] <= sizes[1] + 1, `${name} page overflows: ${sizes}`);
  }
  await page.getByLabel("YOLO 分割", { exact: false }).check();
  await page.waitForFunction(() => !document.querySelector("#app").__vue_app__._instance.provides.exportPage.analysisLoading);
  assert.equal(await page.getByRole("button", { name: "导出当前项目 ZIP" }).isDisabled(), true);
  failAnalysis = true;
  await page.getByRole("button", { name: "重新分析" }).click();
  await page.getByRole("alert").waitFor();
  assert.equal(await page.getByRole("button", { name: "导出当前项目 ZIP" }).isDisabled(), true);
  assert.deepEqual(errors, []);
  console.log("PASS: delayed image selection, confirmation persistence, global aspect deletion, recommendation application, error states, desktop/mobile layout.");
} finally {
  releaseA();
  await browser.close();
}
