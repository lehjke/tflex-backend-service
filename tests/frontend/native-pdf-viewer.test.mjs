import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../../src/TFlexDrawingService.Api/wwwroot/", import.meta.url);
const viewer = await readFile(new URL("native-pdf-viewer.js", root), "utf8");
const app = await readFile(new URL("app.js", root), "utf8");
const layout = await import(new URL("native-pdf-viewer-layout.js", root));

test("fit width works before the hidden scroll area has a measured width", () => {
  assert.equal(layout.fitScale(1200, 0, 600), 0.48);
  assert.equal(layout.fitScale(1200, 824, 600), 2 / 3);
  assert.equal(layout.clampZoom(0.1), 0.2);
  assert.equal(layout.clampZoom(4.1), 4);
});

test("template crops stay within viewport bounds and preserve requested normalized geometry", () => {
  const crop = layout.cropRect(500, 400, layout.INITIAL_CROPS.razvertki_lehy);
  assert.deepEqual(crop, {
    x: 55, y: 44, width: 95, height: 124
  });
  assert.deepEqual(layout.cropTransform(crop, 2), [2, 0, 0, 2, -110, -88]);
  const bounded = layout.cropRect(100, 80, [0.9, 0.9, 0.5, 0.5]);
  assert.deepEqual(bounded, { x: 90, y: 72, width: 10, height: 8 });
  assert.equal(Object.keys(layout.INITIAL_CROPS).length, 9);
});

test("native PDF preview routes through the local canvas viewer", () => {
  assert.match(app, /nativePreviewFrame\.src = `\/native-pdf-viewer\.html\?file=/u);
  assert.match(app, /nativePreviewOpen\.href = `\/native-pdf-viewer\.html\?file=.*mode=sheet/u);
  assert.match(viewer, /url\.origin !== window\.location\.origin/u);
  assert.match(viewer, /\\\/api\\\/jobs\\\//u);
  assert.match(viewer, /pdfPage\.render\(/u);
  assert.match(viewer, /Math\.min\(window\.devicePixelRatio \|\| 1, 2\)/u);
  assert.match(viewer, /new ResizeObserver/u);
});
