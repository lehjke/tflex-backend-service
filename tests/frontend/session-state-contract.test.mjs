import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

function readWebSource(fileName) {
  return fs.readFileSync(
    path.join(repositoryRoot, "src/TFlexDrawingService.Api/wwwroot", fileName),
    "utf8");
}

function functionBody(source, name) {
  const start = source.indexOf(`function ${name}(`);
  assert.notEqual(start, -1, `${name} is missing`);
  const bodyStart = source.indexOf("{", start);
  let depth = 1;
  let quote = "";

  for (let index = bodyStart + 1; index < source.length; index += 1) {
    const current = source[index];
    const previous = source[index - 1];
    if (quote) {
      if (current === quote && previous !== "\\") quote = "";
      continue;
    }
    if (current === "\"" || current === "'" || current === "`") {
      quote = current;
    } else if (current === "{") {
      depth += 1;
    } else if (current === "}") {
      depth -= 1;
      if (depth === 0) return source.slice(bodyStart + 1, index);
    }
  }

  assert.fail(`${name} has an unterminated body`);
}

test("frontend code contains no dynamic JavaScript evaluator", () => {
  for (const fileName of ["app.js", "account.js", "pricing.js", "safe-expression.js"]) {
    const source = readWebSource(fileName);
    assert.doesNotMatch(source, /\b(?:eval|Function)\s*\(/u, fileName);
  }
});

test("pricing logout clears credentials, owner data and calculated DOM before the request", () => {
  const source = readWebSource("pricing.js");
  const clearBody = functionBody(source, "clearPricingSessionState");
  const logoutBody = functionBody(source, "logout");

  for (const expected of [
    "loginForm?.reset()",
    "pricingForm?.reset()",
    "state.projects = []",
    "state.pricingByProjectId = new Map()",
    "state.lastCalculation = null",
    "savedPricingList?.replaceChildren()",
    "pricingLines?.replaceChildren()"
  ]) {
    assert.ok(clearBody.includes(expected), `pricing clear is missing: ${expected}`);
  }
  assert.ok(
    logoutBody.indexOf("clearPricingSessionState()") < logoutBody.indexOf("apiFetch("),
    "private pricing state must be removed before the logout request can stall or fail");
});

test("editor and account session clears remove private state and draft controls", () => {
  const editorClear = functionBody(readWebSource("app.js"), "clearEditorSessionState");
  const accountClear = functionBody(readWebSource("account.js"), "clearAccountSessionState");

  for (const expected of [
    "state.latestJob = null",
    "state.jobs = []",
    "statusPanel.textContent = \"\"",
    "loginForm?.reset()",
    "registerForm?.reset()"
  ]) {
    assert.ok(editorClear.includes(expected), `editor clear is missing: ${expected}`);
  }
  for (const expected of [
    "state.projects = []",
    "state.adminUsers = []",
    "templateImportForm?.reset()",
    "projectNameInput.value = \"\"",
    "projectsList.replaceChildren()"
  ]) {
    assert.ok(accountClear.includes(expected), `account clear is missing: ${expected}`);
  }
});

test("job submission always restores its submit control", () => {
  const submitBody = functionBody(readWebSource("app.js"), "submitJob");
  assert.match(submitBody, /finally\s*\{\s*submitButton\.disabled = false;/u);
});
