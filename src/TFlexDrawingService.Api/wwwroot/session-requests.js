const STALE_SESSION_PAYLOAD = Symbol("stale-session-payload");

export function createSessionRequestGuard(fetchImplementation = globalThis.fetch.bind(globalThis)) {
  let generation = 0;
  let controller = new AbortController();
  const responseGenerations = new WeakMap();
  const staleResponse = Object.freeze({
    ok: false,
    status: 0,
    sessionStale: true,
    async json() {
      return STALE_SESSION_PAYLOAD;
    },
    async text() {
      return "";
    }
  });

  function invalidate() {
    generation += 1;
    controller.abort();
    controller = new AbortController();
    return generation;
  }

  function isCurrent(response) {
    return response !== staleResponse
      && responseGenerations.get(response) === generation;
  }

  async function guardedFetch(url, options = {}) {
    const requestGeneration = generation;
    const requestController = controller;

    try {
      const response = await fetchImplementation(url, {
        ...options,
        signal: requestController.signal
      });

      if (requestGeneration !== generation) {
        return staleResponse;
      }

      responseGenerations.set(response, requestGeneration);
      return response;
    } catch (error) {
      if (requestController.signal.aborted || error?.name === "AbortError") {
        return staleResponse;
      }

      throw error;
    }
  }

  async function readJson(response) {
    if (!isCurrent(response)) {
      return STALE_SESSION_PAYLOAD;
    }

    const payload = await response.json();
    return isCurrent(response) ? payload : STALE_SESSION_PAYLOAD;
  }

  return Object.freeze({
    fetch: guardedFetch,
    invalidate,
    isCurrent,
    readJson,
    stalePayload: STALE_SESSION_PAYLOAD
  });
}
