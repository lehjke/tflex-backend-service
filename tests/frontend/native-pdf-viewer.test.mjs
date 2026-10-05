import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";

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

function runViewer(mode, { closable = false } = {}) {
  const elements = new Map();
  const timers = [];
  const windowListeners = new Map();
  let observer;
  let renders = 0;
  const element = id => {
    if (!elements.has(id)) elements.set(id, {
      hidden: false, disabled: false, value: "1", max: "", textContent: "", style: {},
      handlers: new Map(), addEventListener(type, callback) { this.handlers.set(type, callback); },
      getContext() { return { drawImage() {} }; },
      click() { this.handlers.get("click")?.(); }
    });
    return elements.get(id);
  };
  const scroll = element("#pdfScroll");
  scroll.clientWidth = 620;
  scroll.parentElement = { clientWidth: 700 };
  const source = viewer.replace(/^import \* as pdfjsLib from .*;\nimport \{.*\} from .*;\n/mu, "");
  const window = {
    location: { origin: "https://example.test", search: `?file=${encodeURIComponent("/api/jobs/1/files/output/download")}&mode=${mode}&template=razvertki_lehy`, replace(path) { this.replaced = path; } },
    devicePixelRatio: 1, closed: false,
    close() { if (closable) this.closed = true; },
    addEventListener(type, callback) { windowListeners.set(type, callback); }
  };
  const pdfPage = {
    getViewport({ scale }) { return { width: 1200 * scale, height: 1600 * scale }; },
    render() { renders++; return { promise: Promise.resolve(), cancel() {} }; }
  };
  const pdfjsLib = {
    GlobalWorkerOptions: {},
    getDocument() { return { promise: Promise.resolve({ numPages: 2, getPage: async () => pdfPage }) }; }
  };
  class ResizeObserverStub {
    constructor(callback) { observer = callback; }
    observe(target) { this.target = target; }
  }
  vm.runInNewContext(source, {
    URL, URLSearchParams, window, pdfjsLib, ResizeObserver: ResizeObserverStub,
    document: { querySelector: element, createElement: () => ({ width: 0, height: 0, getContext: () => ({}) }) },
    clampZoom: layout.clampZoom, cropRect: layout.cropRect, cropTransform: layout.cropTransform,
    fitScale: layout.fitScale, INITIAL_CROPS: layout.INITIAL_CROPS,
    setTimeout(callback, delay) { timers.push({ callback, delay }); return timers.length; },
    clearTimeout() {}
  });
  return {
    elements, timers, window, windowListeners, get renders() { return renders; },
    resize: () => observer(),
    async settle() { await new Promise(resolve => setImmediate(resolve)); await new Promise(resolve => setImmediate(resolve)); },
    runTimers() { for (const timer of timers.splice(0)) timer.callback(); }
  };
}

test("sheet dismissal closes the tab or falls back to /drawings; crop view stays unchanged", async () => {
  const html = await readFile(new URL("native-pdf-viewer.html", root), "utf8");
  const css = await readFile(new URL("native-pdf-viewer.css", root), "utf8");
  const drawings = await readFile(new URL("drawings.html", root), "utf8");
  const styles = await readFile(new URL("styles.css", root), "utf8");
  assert.match(html, /id="closeViewer"[^>]*hidden/u);
  assert.match(css, /scrollbar-gutter: stable/u);
  assert.match(drawings, /id="nativePreviewOpen" class="button-link secondary native-preview-open"/u);
  assert.match(styles, /\.native-preview-open \{[^}]*min-height: 44px/su);

  const sheet = runViewer("sheet", { closable: true });
  await sheet.settle();
  assert.equal(sheet.elements.get("#closeViewer").hidden, false);
  sheet.windowListeners.get("keydown")({ key: "Escape" });
  sheet.runTimers();
  assert.equal(sheet.window.closed, true);
  assert.equal(sheet.window.location.replaced, undefined);

  const direct = runViewer("sheet");
  await direct.settle();
  direct.elements.get("#closeViewer").click();
  direct.runTimers();
  assert.equal(direct.window.location.replaced, "/drawings");

  const crop = runViewer("crop");
  await crop.settle();
  assert.equal(crop.elements.get("#closeViewer").hidden, true);
  crop.windowListeners.get("keydown")({ key: "Escape" });
  crop.runTimers();
  assert.equal(crop.window.location.replaced, undefined);
});

test("resize observer schedules a render only when the stable container width changes", async () => {
  const run = runViewer("sheet");
  await run.settle();
  const initialRenders = run.renders;
  run.resize();
  assert.equal(run.timers.length, 0);
  run.elements.get("#pdfScroll").parentElement.clientWidth += 24;
  run.resize();
  assert.equal(run.timers.length, 1);
  run.runTimers();
  await run.settle();
  assert.equal(run.renders, initialRenders + 1);
});
