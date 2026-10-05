import assert from "node:assert/strict";
import test from "node:test";
import { createNativePreviewController } from "../../src/TFlexDrawingService.Api/wwwroot/native-preview.js";

const ok = payload => ({ ok: true, status: 200, json: async () => payload });
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const snapshot = value => ({ allowed: true, templateId: "T", parameters: { X: value } });

test("drops a stale classification and runs only the latest coalesced snapshot", async () => {
  let releaseFirst;
  let firstStarted;
  const started = new Promise(resolve => { firstStarted = resolve; });
  const calls = [];
  const pdfs = [];
  const fetch = async (url, options) => {
    calls.push([url, options]);
    if (url.endsWith("classify") && options.body.includes('"X":1')) {
      return new Promise(resolve => { releaseFirst = () => resolve(ok({ hardErrors: [], overridableDeviations: [] })); firstStarted(); });
    }
    if (url.endsWith("classify")) return ok({ hardErrors: [], overridableDeviations: [] });
    if (url.endsWith("health/ready")) return ok({ checks: { worker: { ready: true } } });
    if (url === "/api/jobs") return ok({ id: "new" });
    if (url.endsWith("/new")) return ok({ status: "Completed", resultFiles: [{ format: "pdf", downloadUrl: "/api/jobs/new/files/f/download" }] });
    throw new Error(`Unexpected ${url}`);
  };
  const controller = createNativePreviewController({ fetch, delayMs: 0, pollIntervalMs: 0, onPdf: url => pdfs.push(url) });
  controller.schedule(snapshot(1));
  await started;
  controller.schedule(snapshot(2));
  releaseFirst();
  await pause(40);

  assert.equal(calls.filter(([url]) => url === "/api/jobs").length, 1);
  assert.equal(JSON.parse(calls.find(([url]) => url === "/api/jobs")[1].body).parameters.X, 2);
  assert.equal(pdfs.at(-1), "/api/jobs/new/files/f/download");
  controller.dispose();
});

test("invalidating immediately hides an already displayed stale PDF", () => {
  const pdfs = [];
  const controller = createNativePreviewController({ fetch: async () => ok({}), onPdf: url => pdfs.push(url) });
  controller.schedule(snapshot(1));
  controller.invalidate();
  assert.equal(pdfs.at(-1), null);
  controller.dispose();
});

test("retry cannot revive a snapshot invalidated by a session or template change", async () => {
  let requests = 0;
  const controller = createNativePreviewController({ fetch: async () => { requests += 1; return ok({}); }, delayMs: 0 });
  controller.schedule(snapshot(1));
  controller.invalidate();
  controller.retry();
  await pause(10);
  assert.equal(requests, 0);
  controller.dispose();
});


test("keeps the current PDF for routine updates of the same snapshot", async () => {
  const pdfs = [];
  const fetch = async url => {
    if (url.endsWith("classify")) return ok({ hardErrors: [], overridableDeviations: [] });
    if (url.endsWith("health/ready")) return ok({ checks: { worker: { ready: true } } });
    if (url === "/api/jobs") return ok({ id: "same" });
    if (url.endsWith("/same")) return ok({ status: "Completed", resultFiles: [{ format: "pdf", downloadUrl: "/same.pdf" }] });
    throw new Error(`Unexpected ${url}`);
  };
  const controller = createNativePreviewController({ fetch, delayMs: 0, pollIntervalMs: 0, onPdf: url => pdfs.push(url) });
  controller.schedule(snapshot(4));
  await pause(20);
  controller.schedule(snapshot(4));
  assert.equal(pdfs.at(-1), "/same.pdf");
  controller.dispose();
});

test("reuses a completed PDF after temporarily switching to schematic preview", async () => {
  let jobs = 0;
  const pdfs = [];
  const fetch = async url => {
    if (url.endsWith("classify")) return ok({ hardErrors: [], overridableDeviations: [] });
    if (url.endsWith("health/ready")) return ok({ checks: { worker: { ready: true } } });
    if (url === "/api/jobs") { jobs += 1; return ok({ id: "cached" }); }
    if (url.endsWith("/cached")) return ok({ status: "Completed", resultFiles: [{ format: "pdf", downloadUrl: "/cached.pdf" }] });
    throw new Error(`Unexpected ${url}`);
  };
  const controller = createNativePreviewController({ fetch, delayMs: 0, pollIntervalMs: 0, onPdf: url => pdfs.push(url) });
  controller.schedule(snapshot(5));
  await pause(20);
  controller.invalidate({ preserveReady: true });
  controller.schedule(snapshot(5));
  assert.equal(jobs, 1);
  assert.equal(pdfs.at(-1), "/cached.pdf");
  controller.dispose();
});

test("does not rerun a cached snapshot when it is restored during a stale in-flight request", async () => {
  let releaseB;
  let startedB;
  const bStarted = new Promise(resolve => { startedB = resolve; });
  let jobs = 0;
  const fetch = async (url, options) => {
    if (url.endsWith("classify") && options.body.includes('"X":2')) {
      return new Promise(resolve => { releaseB = () => resolve(ok({ hardErrors: [], overridableDeviations: [] })); startedB(); });
    }
    if (url.endsWith("classify")) return ok({ hardErrors: [], overridableDeviations: [] });
    if (url.endsWith("health/ready")) return ok({ checks: { worker: { ready: true } } });
    if (url === "/api/jobs") { jobs += 1; return ok({ id: "a" }); }
    if (url.endsWith("/a")) return ok({ status: "Completed", resultFiles: [{ format: "pdf", downloadUrl: "/a.pdf" }] });
    throw new Error(`Unexpected ${url}`);
  };
  const controller = createNativePreviewController({ fetch, delayMs: 0, pollIntervalMs: 0 });
  controller.schedule(snapshot(1));
  await pause(20);
  assert.equal(jobs, 1);
  controller.schedule(snapshot(2));
  await bStarted;
  controller.invalidate({ preserveReady: true });
  controller.schedule(snapshot(1));
  releaseB();
  await pause(20);
  assert.equal(jobs, 1);
  controller.dispose();
});

test("retries a busy queue after an older preview and publishes the latest snapshot", async () => {
  let busyResponses = 0;
  let createdSnapshot;
  let oldPreviewStarted;
  const pdfs = [];
  const oldPreviewPending = new Promise(resolve => { oldPreviewStarted = resolve; });
  const fetch = async (url, options) => {
    if (url.endsWith("classify")) return ok({ hardErrors: [], overridableDeviations: [] });
    if (url.endsWith("health/ready")) return ok({ checks: { worker: { ready: true } } });
    if (url === "/api/jobs") {
      const value = JSON.parse(options.body).parameters.X;
      if (value === 1) return ok({ id: "old" });
      if (busyResponses++ < 5) return { ok: false, status: 429 };
      createdSnapshot = value;
      return ok({ id: "latest" });
    }
    if (url.endsWith("/old")) { oldPreviewStarted(); return ok({ status: "Running", resultFiles: [] }); }
    if (url.endsWith("/latest")) return ok({ status: "Completed", resultFiles: [{ format: "pdf", downloadUrl: "/latest.pdf" }] });
    throw new Error(`Unexpected ${url}`);
  };
  const immediateTimer = callback => { queueMicrotask(callback); return 1; };
  const controller = createNativePreviewController({ fetch, setTimer: immediateTimer, clearTimer() {}, delayMs: 0, pollIntervalMs: 0, onPdf: url => pdfs.push(url) });
  controller.schedule(snapshot(1));
  await oldPreviewPending;
  controller.schedule(snapshot(9));
  await pause(20);
  assert.equal(busyResponses, 6);
  assert.equal(createdSnapshot, 9);
  assert.equal(pdfs.at(-1), "/latest.pdf");
  controller.dispose();
});

test("retry reruns the same snapshot after service availability returns", async () => {
  let ready = false;
  let created;
  const fetch = async url => {
    if (url.endsWith("classify")) return ok({ hardErrors: [], overridableDeviations: [] });
    if (url.endsWith("health/ready")) return ok({ checks: { worker: { ready } } });
    if (url === "/api/jobs") { created = true; return ok({ id: "retried" }); }
    if (url.endsWith("/retried")) return ok({ status: "Completed", resultFiles: [{ format: "pdf", downloadUrl: "/retried.pdf" }] });
    throw new Error(`Unexpected ${url}`);
  };
  const statuses = [];
  const controller = createNativePreviewController({ fetch, delayMs: 0, pollIntervalMs: 0, onStatus: (...status) => statuses.push(status) });
  controller.schedule(snapshot(3));
  await pause(10);
  assert.equal(statuses.at(-1)[1], true);
  ready = true;
  controller.retry();
  await pause(20);
  assert.equal(created, true);
  controller.dispose();
});
