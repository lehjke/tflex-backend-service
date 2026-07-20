import assert from "node:assert/strict";
import test from "node:test";

import {
  createSessionRequestGuard
} from "../../src/TFlexDrawingService.Api/wwwroot/session-requests.js";

function deferred() {
  let resolve;
  const promise = new Promise(resolvePromise => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

function responseWithJson(payload) {
  return {
    ok: true,
    status: 200,
    async json() {
      return payload;
    }
  };
}

test("stale authenticated responses cannot overwrite a replacement session", async () => {
  for (const surface of ["editor", "account", "pricing"]) {
    const pending = [];
    const guard = createSessionRequestGuard((_url, options) => {
      const request = deferred();
      pending.push({ ...request, signal: options.signal });
      return request.promise;
    });
    const rendered = { owner: null, surface };

    const userARequest = guard.fetch("/private/user-a");
    guard.invalidate();
    assert.equal(pending[0].signal.aborted, true, `${surface} must abort user A requests`);

    const userBRequest = guard.fetch("/private/user-b");
    pending[1].resolve(responseWithJson({ owner: "B" }));
    const userBResponse = await userBRequest;
    const userBPayload = await guard.readJson(userBResponse);
    if (userBPayload !== guard.stalePayload) {
      rendered.owner = userBPayload.owner;
    }

    pending[0].resolve(responseWithJson({ owner: "A" }));
    const userAResponse = await userARequest;
    const userAPayload = await guard.readJson(userAResponse);
    if (userAPayload !== guard.stalePayload) {
      rendered.owner = userAPayload.owner;
    }

    assert.equal(rendered.owner, "B", `${surface} must keep user B state and DOM`);
  }
});

test("a session change during response body parsing suppresses the payload", async () => {
  const body = deferred();
  const guard = createSessionRequestGuard(async () => ({
    ok: true,
    status: 200,
    json: () => body.promise
  }));

  const response = await guard.fetch("/private/projects");
  const reading = guard.readJson(response);
  guard.invalidate();
  body.resolve({ owner: "A" });

  assert.equal(await reading, guard.stalePayload);
});
