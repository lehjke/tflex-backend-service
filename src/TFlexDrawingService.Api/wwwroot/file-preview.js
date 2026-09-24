import { t } from "./i18n.js?v=20260924-sidebar-collapse-1";

let previewDialog = null;
let previousFocus = null;

function ensurePreviewDialog() {
  if (previewDialog) return previewDialog;

  previewDialog = document.createElement("dialog");
  previewDialog.className = "file-preview-dialog";
  previewDialog.setAttribute("aria-labelledby", "filePreviewTitle");
  previewDialog.innerHTML = `
    <div class="file-preview-dialog__surface">
      <header class="file-preview-dialog__header">
        <h2 id="filePreviewTitle">Предпросмотр сформированного PDF</h2>
        <button class="file-preview-dialog__close" type="button" data-preview-close aria-label="Закрыть">×</button>
      </header>
      <div class="file-preview-dialog__content">
        <object class="file-preview-dialog__object" type="application/pdf" data="" aria-label="Предпросмотр сформированного PDF">
          <p>Браузер не может показать PDF. Откройте файл в новой вкладке или скачайте его.</p>
        </object>
      </div>
      <footer class="file-preview-dialog__actions">
        <label>Масштаб PDF <select data-preview-zoom aria-label="Масштаб PDF"><option value="page-width">По ширине</option><option value="100">100%</option><option value="150">150%</option><option value="200">200%</option></select></label>
        <a class="secondary button-link" data-preview-open target="_blank" rel="noopener">Открыть в новой вкладке</a>
        <a class="primary-link" data-preview-download download>Скачать файл</a>
      </footer>
    </div>
  `;

  previewDialog.querySelector("[data-preview-zoom]").addEventListener("change", event => {
    const object = previewDialog.querySelector(".file-preview-dialog__object");
    const url = new URL(object.data, window.location.origin);
    url.hash = event.target.value === "page-width" ? "view=FitH" : `zoom=${event.target.value}`;
    object.data = url.href;
  });
  const close = () => previewDialog.close();
  previewDialog.querySelector("[data-preview-close]").addEventListener("click", close);
  previewDialog.addEventListener("click", event => {
    if (event.target === previewDialog) close();
  });
  previewDialog.addEventListener("close", () => {
    previewDialog.querySelector(".file-preview-dialog__object").data = "";
    previousFocus?.focus({ preventScroll: true });
    previousFocus = null;
  });
  previewDialog.addEventListener("cancel", event => {
    event.preventDefault();
    close();
  });

  document.body.append(previewDialog);
  return previewDialog;
}

export function isPdfFile(file) {
  const format = String(file?.format || "").replace(/^\./, "").toLowerCase();
  const fileName = String(file?.fileName || "").toLowerCase();
  return format === "pdf" || fileName.endsWith(".pdf");
}

function getInlinePreviewUrl(downloadUrl) {
  const url = new URL(downloadUrl, window.location.origin);
  url.searchParams.set("inline", "true");
  return `${url.pathname}${url.search}${url.hash}`;
}

export function openGeneratedFilePreview(file, trigger = document.activeElement) {
  if (!file?.downloadUrl || !isPdfFile(file)) return false;

  const dialog = ensurePreviewDialog();
  previousFocus = trigger instanceof HTMLElement ? trigger : null;
  const object = dialog.querySelector(".file-preview-dialog__object");
  const openLink = dialog.querySelector("[data-preview-open]");
  const downloadLink = dialog.querySelector("[data-preview-download]");

  const inlineUrl = getInlinePreviewUrl(file.downloadUrl);
  object.data = `${inlineUrl.split("#")[0]}#view=FitH`;
  dialog.querySelector("[data-preview-zoom]").value = "page-width";
  openLink.href = inlineUrl;
  downloadLink.href = file.downloadUrl;
  downloadLink.download = file.fileName || "drawing.pdf";
  dialog.querySelector("#filePreviewTitle").textContent = file.fileName
    ? `${t("Предпросмотр PDF")}: ${file.fileName}`
    : t("Предпросмотр сформированного PDF");

  if (!dialog.open) dialog.showModal();
  dialog.querySelector("[data-preview-close]").focus({ preventScroll: true });
  return true;
}
