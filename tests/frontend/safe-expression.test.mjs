import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  evaluateTFlexExpression,
  validateTFlexExpression
} from "../../src/TFlexDrawingService.Api/wwwroot/safe-expression.js";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

test("all shipped template expressions use the supported safe grammar", () => {
  const catalog = JSON.parse(
    fs.readFileSync(path.join(repositoryRoot, "templates/templates.json"), "utf8"));
  const failures = [];
  let expressionCount = 0;

  for (const template of catalog.templates) {
    const definitions = [
      ...(template.parameters || []),
      ...(template.calculatedVariables || []),
      ...(template.validationRules || [])
    ];
    for (const definition of definitions) {
      for (const field of ["expression", "levelExpression"]) {
        const expression = definition?.[field];
        if (typeof expression !== "string" || !expression.trim()) continue;
        expressionCount += 1;
        if (!validateTFlexExpression(expression)) {
          failures.push(`${template.id}:${definition.name || "<unnamed>"}:${field}`);
        }
      }
    }
  }

  assert.ok(expressionCount > 6000, "the complete shipped expression set was not inspected");
  assert.deepEqual(failures, []);
});

test("evaluates arithmetic, conditional and case-insensitive variables without dynamic code", () => {
  const result = evaluateTFlexExpression(
    "CAP == 1050 ? round((AA * bb) / 100, 0.5) : 0",
    { cap: 1050, aa: 1600, BB: 1500 });

  assert.equal(result, 24000);
});

test("prefers exact-case T-FLEX symbols when case-distinct names coexist", () => {
  const context = {
    S: 1400,
    s: 1,
    $Mode: "Нормальный",
    $MODE: "Н",
    $mode: "нормального"
  };

  assert.equal(evaluateTFlexExpression("S - s", context), 1399);
  assert.equal(
    evaluateTFlexExpression(
      "$Mode == \"Нормальный\" && $MODE == \"Н\" && $mode == \"нормального\"",
      context),
    true);
  assert.equal(evaluateTFlexExpression("$MoDe", context), undefined);
  assert.equal(
    evaluateTFlexExpression("FOO", { foo: 1, Foo: 2, $FOO: 3 }),
    undefined);
});

test("supports T-FLEX parenthesized branch pairs and interpolated strings", () => {
  assert.equal(
    evaluateTFlexExpression(
      "$Vid==\"комбинированная\"?($Comb:$Vid)+\" / {cap}\"",
      { $Vid: "комбинированная", $Comb: "комбо", cap: 1050 }),
    "комбо / 1050");
});

test("supports bounded lookup-table expressions", () => {
  const result = evaluateTFlexExpression(
    "find(TH.AA, (TH.cap == cap)&&(TH.car_type == $car_type))",
    { cap: 320, $car_type: "P04D" },
    {
      lookupTables: {
        TH: [
          { cap: 450, car_type: "P06D", AA: 950 },
          { cap: 320, car_type: "P04D", AA: 850 }
        ]
      }
    });

  assert.equal(result, 850);
});

test("matches the server-side landing helper semantics", () => {
  const context = {
    stops: 3,
    s_top_level_1: 9000,
    S_HF: 2500
  };

  assert.equal(evaluateTFlexExpression("ch_HF(0, 3000, 2)", context), 1);
  assert.equal(evaluateTFlexExpression("ch_HF(3000, 5000, 3)", context), 0);
  assert.equal(evaluateTFlexExpression("ch_level(6000, 3)", context), 9000);
});

test("rejects executable JavaScript and resource-amplifying expressions", () => {
  assert.equal(validateTFlexExpression("globalThis.alert(1)"), false);
  assert.equal(validateTFlexExpression("constructor.constructor('return 1')()"), false);
  assert.equal(evaluateTFlexExpression("\"x\" + \"y\"".repeat(5000), {}), undefined);
  assert.equal(evaluateTFlexExpression("2 / 0", {}), undefined);
});
