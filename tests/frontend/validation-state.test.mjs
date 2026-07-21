import assert from "node:assert/strict";
import test from "node:test";

import {
  isValidationPassed,
  normalizeValidationSeverity,
  partitionValidationIssues
} from "../../src/TFlexDrawingService.Api/wwwroot/validation-state.js";

test("unknown validation results fail closed", () => {
  assert.equal(isValidationPassed(undefined), false);
  assert.equal(isValidationPassed(null), false);
  assert.equal(isValidationPassed(Number.NaN), false);
  assert.equal(isValidationPassed(0), false);
  assert.equal(isValidationPassed(1), true);
});

test("warning rules remain visible but are not blocking errors", () => {
  const issues = [
    { name: "r_required", severity: normalizeValidationSeverity(undefined) },
    { name: "warn01", severity: normalizeValidationSeverity("warning") }
  ];
  const result = partitionValidationIssues(issues);

  assert.deepEqual(result.errors.map(issue => issue.name), ["r_required"]);
  assert.deepEqual(result.warnings.map(issue => issue.name), ["warn01"]);
});
