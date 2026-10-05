import * as pdfjsLib from "/vendor/pdfjs/pdf.mjs";
import { clampZoom, cropRect, cropTransform, fitScale, INITIAL_CROPS } from "/native-pdf-viewer-layout.js";

pdfjsLib.GlobalWorkerOptions.workerSrc = "/vendor/pdfjs/pdf.worker.mjs";

const params = new URLSearchParams(window.location.search);
const file = params.get("file");
const mode = params.get("mode") === "sheet" ? "sheet" : "crop";
const template = params.get("template") || "";
const cropFractions = mode === "crop" ? INITIAL_CROPS[template] : null;
const pageInput = document.querySelector("#pageNumber");
const pageCount = document.querySelector("#pageCount");
const canvas = document.querySelector("#pdfCanvas");
const context = canvas.getContext("2d", { alpha: false });
const message = document.querySelector("#pdfMessage");
const scroll = document.querySelector("#pdfScroll");
const zoomLabel = document.querySelector("#zoomLabel");
const previous = document.querySelector("#previousPage");
const next = document.querySelector("#nextPage");
const closeButton = document.querySelector("#closeViewer");
document.querySelector("#pageControls").hidden = mode === "crop";
closeButton.hidden = mode !== "sheet";
let documentProxy;
let page = 1;
let zoom = null;
let currentScale = 1;
let renderTask;
let renderRevision = 0;
let resizeTimer;

function safePdfUrl(value) {
  if (!value) return null;
  try {
    const url = new URL(value, window.location.origin);
    if (url.origin !== window.location.origin || !/^\/api\/jobs\/[^/]+\/files\/[^/]+\/download$/.test(url.pathname)) return null;
    if ([...url.searchParams.keys()].some(key => key !== "inline")) return null;
    url.searchParams.set("inline", "true");
    url.hash = "";
    return url.href;
  } catch { return null; }
}

function updateControls() {
  pageInput.value = String(page);
  pageInput.max = String(documentProxy.numPages);
  pageCount.textContent = `/ ${documentProxy.numPages}`;
  previous.disabled = page <= 1;
  next.disabled = page >= documentProxy.numPages;
  zoomLabel.textContent = zoom === null ? "По ширине" : `${Math.round(zoom * 100)}%`;
  document.querySelector("#pageControls").hidden = mode === "crop";
}

async function render() {
  const revision = ++renderRevision;
  if (renderTask) { renderTask.cancel(); renderTask = null; }
  if (!documentProxy) return;
  try {
    const pdfPage = await documentProxy.getPage(page);
    if (revision !== renderRevision) return;
    scroll.hidden = false;
    const base = pdfPage.getViewport({ scale: 1 });
    const baseCrop = cropRect(base.width, base.height, cropFractions);
    const scale = zoom === null
      ? fitScale(baseCrop.width, scroll.clientWidth, scroll.parentElement.clientWidth)
      : zoom;
    currentScale = scale;
    const viewport = pdfPage.getViewport({ scale });
    const crop = cropRect(viewport.width, viewport.height, cropFractions);
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const paintCanvas = document.createElement("canvas");
    paintCanvas.width = Math.ceil(crop.width * dpr);
    paintCanvas.height = Math.ceil(crop.height * dpr);
    const transform = cropTransform(crop, dpr);
    renderTask = pdfPage.render({ canvasContext: paintCanvas.getContext("2d", { alpha: false }), viewport, transform, background: "#ffffff" });
    await renderTask.promise;
    if (revision === renderRevision) {
      canvas.width = paintCanvas.width;
      canvas.height = paintCanvas.height;
      canvas.style.width = `${Math.ceil(crop.width)}px`;
      canvas.style.height = `${Math.ceil(crop.height)}px`;
      context.drawImage(paintCanvas, 0, 0);
      message.hidden = true;
      scroll.hidden = false;
      updateControls();
    }
  } catch (error) {
    if (error?.name !== "RenderingCancelledException" && revision === renderRevision) showError("Не удалось отобразить страницу PDF.");
  } finally {
    if (revision === renderRevision) renderTask = null;
  }
}

function showError(text) {
  scroll.hidden = true;
  message.textContent = text;
  message.hidden = false;
}

previous.addEventListener("click", () => { if (mode === "sheet" && page > 1) { page -= 1; render(); } });
next.addEventListener("click", () => { if (mode === "sheet" && documentProxy && page < documentProxy.numPages) { page += 1; render(); } });
pageInput.addEventListener("change", () => {
  if (!documentProxy || mode !== "sheet") return;
  page = Math.min(documentProxy.numPages, Math.max(1, Number.parseInt(pageInput.value, 10) || 1));
  render();
});
document.querySelector("#zoomOut").addEventListener("click", () => { zoom = clampZoom((zoom ?? currentScale) - 0.1); render(); });
document.querySelector("#zoomIn").addEventListener("click", () => { zoom = clampZoom((zoom ?? currentScale) + 0.1); render(); });
document.querySelector("#fitWidth").addEventListener("click", () => { zoom = null; render(); });
closeButton.addEventListener("click", () => {
  window.close();
  setTimeout(() => {
    if (!window.closed) window.location.replace("/drawings");
  }, 0);
});
window.addEventListener("keydown", event => {
  if (mode === "sheet" && event.key === "Escape") closeButton.click();
});
let observedWidth;
new ResizeObserver(() => {
  if (zoom !== null || !documentProxy) return;
  const width = scroll.parentElement.clientWidth;
  if (width === observedWidth) return;
  observedWidth = width;
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(render, 100);
}).observe(scroll.parentElement);

const pdfUrl = safePdfUrl(file);
if (!pdfUrl) showError("Недопустимая ссылка на файл PDF.");
else if (mode === "crop" && !cropFractions) showError("Предпросмотр для этого шаблона недоступен.");
else {
  const requestedPage = Number.parseInt(params.get("page") || "1", 10);
  page = Number.isFinite(requestedPage) ? Math.max(1, requestedPage) : 1;
  pdfjsLib.getDocument({ url: pdfUrl, withCredentials: true, cMapUrl: "/vendor/pdfjs/cmaps/", cMapPacked: true, standardFontDataUrl: "/vendor/pdfjs/standard_fonts/", wasmUrl: "/vendor/pdfjs/wasm/" }).promise
    .then(pdf => { documentProxy = pdf; page = Math.min(page, pdf.numPages); observedWidth = scroll.parentElement.clientWidth; render(); })
    .catch(() => showError("Не удалось загрузить PDF. Обновите предпросмотр и повторите попытку."));
}
