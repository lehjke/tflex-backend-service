export function createNativePreviewController({
  fetch: fetcher,
  setTimer = setTimeout,
  clearTimer = clearTimeout,
  onStatus = () => {},
  onPdf = () => {},
  delayMs = 900,
  pollIntervalMs = 1000,
  timeoutMs = 10 * 60 * 1000,
  queueRetryDeadlineMs = 10 * 60 * 1000
}) {
  let revision = 0;
  let debounceTimer = null;
  let latest = null;
  let lastSnapshot = null;
  let lastSnapshotKey = null;
  let readySnapshotKey = null;
  let readyUrl = null;
  let running = false;
  let stopped = false;

  function invalidate({ preserveReady = false } = {}) {
    revision += 1;
    latest = null;
    lastSnapshot = null;
    lastSnapshotKey = null;
    if (!preserveReady) { readySnapshotKey = null; readyUrl = null; }
    if (debounceTimer !== null) clearTimer(debounceTimer);
    debounceTimer = null;
    onPdf(null);
  }

  function schedule(snapshot) {
    if (stopped || !snapshot || !snapshot.allowed) { invalidate(); return; }
    const key = JSON.stringify({ templateId: snapshot.templateId, parameters: snapshot.parameters });
    if (key === lastSnapshotKey) return;
    invalidate({ preserveReady: true });
    lastSnapshotKey = key;
    const current = { ...snapshot, revision };
    lastSnapshot = current;
    latest = current;
    if (readySnapshotKey === key && readyUrl) {
      latest = null;
      onPdf(readyUrl);
      onStatus("Предпросмотр обновлён.");
      return;
    }
    onStatus("Ожидание изменения параметров…");
    debounceTimer = setTimer(() => {
      debounceTimer = null;
      run(current);
    }, delayMs);
  }

  async function request(url, options) {
    let timeout;
    const controller = new AbortController();
    try {
      return await Promise.race([
        fetcher(url, { ...options, signal: controller.signal }),
        new Promise((_, reject) => {
          timeout = setTimer(() => { controller.abort(); reject(new Error("Preview request timed out")); }, 15000);
        })
      ]);
    } finally {
      if (timeout !== undefined) clearTimer(timeout);
    }
  }

  async function run(snapshot) {
    if (running) { latest = snapshot; return; }
    running = true;
    try {
      let current = snapshot;
      while (current && current.revision === revision && !stopped) {
        latest = null;
        await generate(current);
        current = latest;
        latest = null;
        if (current && debounceTimer !== null) {
          clearTimer(debounceTimer);
          debounceTimer = null;
        }
      }
    } finally {
      running = false;
      if (latest && latest.revision === revision && !stopped) {
        const next = latest;
        latest = null;
        run(next);
      }
    }
  }

  async function generate(snapshot) {
    const live = () => !stopped && snapshot.revision === revision;
    try {
      const classified = await request("/api/drawings/classify", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ templateId: snapshot.templateId, outputFormat: "pdf", parameters: snapshot.parameters })
      });
      if (!live()) return;
      if (!classified.ok) { onStatus("Не удалось проверить параметры для предпросмотра. Можно повторить попытку.", true); return; }
      const classification = await classified.json();
      if (!live()) return;
      if ((classification.hardErrors || classification.HardErrors || []).length) {
        onStatus("Исправьте ошибки параметров, чтобы увидеть предпросмотр."); return;
      }
      if ((classification.overridableDeviations || classification.OverridableDeviations || []).length) {
        onStatus("Предпросмотр недоступен: параметры выходят за типовой диапазон."); return;
      }
      const readiness = await request("/api/health/ready", { method: "GET" });
      if (!live()) return;
      let health = {};
      try { health = await readiness.json(); } catch { /* readiness response may be unavailable */ }
      if (!readiness.ok || !health.checks?.worker?.ready) {
        onStatus("Сервис предпросмотра временно недоступен. Можно повторить попытку.", true); return;
      }
      let created;
      const queueDeadline = Date.now() + queueRetryDeadlineMs;
      let attempt = 0;
      while (Date.now() < queueDeadline) {
        created = await request("/api/jobs", {
          method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ templateId: snapshot.templateId, outputFormat: "pdf", parameters: snapshot.parameters, isPreview: true })
        });
        if (!live()) return;
        if (created.status !== 429) break;
        onStatus("Сервис занят. Предпросмотр запустится автоматически, когда освободится очередь.");
        await new Promise(resolve => setTimer(resolve, Math.min(8000, 1000 * (2 ** Math.min(attempt++, 3)), Math.max(0, queueDeadline - Date.now()))));
        if (!live()) return;
      }
      if (!created?.ok) { onStatus(created?.status === 429 ? "Сервис всё ещё занят. Можно повторить попытку." : "Не удалось запустить предпросмотр. Можно повторить попытку.", true); return; }
      const job = await created.json();
      if (!live()) return;
      onStatus("Формирование PDF…");
      const deadline = Date.now() + timeoutMs;
      let failures = 0;
      while (live() && Date.now() < deadline) {
        await new Promise(resolve => setTimer(resolve, Math.min(pollIntervalMs * (2 ** Math.min(failures, 3)), 8000)));
        if (!live()) return;
        const response = await request(`/api/jobs/${encodeURIComponent(job.id)}`, { method: "GET" });
        if (!live()) return;
        if (response.status === 429) { failures += 1; onStatus("Сервер занят. Ожидание статуса PDF…"); continue; }
        if (!response.ok) { failures += 1; continue; }
        const result = await response.json();
        if (!live()) return;
        failures = 0;
        const status = String(result.status || "").toLowerCase();
        if (["failed", "cancelled"].includes(status)) { onStatus("Не удалось сформировать PDF предпросмотра. Можно повторить попытку.", true); return; }
        if (["completed", "succeeded", "success"].includes(status)) {
          const file = (result.resultFiles || []).find(item => String(item.format || "").toLowerCase() === "pdf");
          if (file?.downloadUrl) { readySnapshotKey = JSON.stringify({ templateId: snapshot.templateId, parameters: snapshot.parameters }); readyUrl = file.downloadUrl; onPdf(file.downloadUrl); onStatus("Предпросмотр обновлён."); return; }
          onStatus("PDF предпросмотра не найден. Можно повторить попытку.", true); return;
        }
      }
      if (live()) onStatus("Формирование PDF заняло слишком много времени. Можно повторить попытку.", true);
    } catch {
      if (live()) onStatus("Не удалось получить предпросмотр. Проверьте соединение и повторите попытку.", true);
    }
  }

  function retry() {
    if (!lastSnapshot || stopped) return;
    const snapshot = lastSnapshot;
    lastSnapshotKey = null;
    schedule(snapshot);
  }

  function dispose() { stopped = true; invalidate(); }
  return Object.freeze({ schedule, retry, invalidate, dispose });
}
