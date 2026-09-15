import assert from "node:assert/strict";
import test from "node:test";

import {
  expandValidationParameterNames
} from "../../src/TFlexDrawingService.Api/wwwroot/parameter-labels.js";

test("expands parameter abbreviations in validation messages", () => {
  assert.equal(
    expandValidationParameterNames(
      "JJ = 1300. Должно быть 800 ≤ JJ ≤ 1200.",
      { parameters: [{ name: "JJ", displayName: "Двери / JJ" }] }),
    "Ширина дверей (в чистоте) = 1300. Должно быть 800 ≤ Ширина дверей (в чистоте) ≤ 1200.");
});

test("expands every parameter in compound expressions", () => {
  assert.equal(
    expandValidationParameterNames(
      "AA-JJ = 500. Должно быть AA-JJ ≥ 125. HL-HH ≥ 100. AA*BB ≤ 2.5.",
      { parameters: [] }),
    "Разница между шириной кабины и шириной дверей (в чистоте) = 500. Должно быть Разница между шириной кабины и шириной дверей (в чистоте) ≥ 125. Разница между высотой кабины и высотой дверей (в чистоте) ≥ 100. Площадь пола кабины ≤ 2.5.");
});

test("uses readable labels discovered from the selected template", () => {
  assert.equal(
    expandValidationParameterNames(
      "Dpit должно быть не менее 1400 мм.",
      { parameters: [{ name: "Dpit", displayName: "Приямок / Глубина приямка" }] }),
    "Глубина приямка должно быть не менее 1400 мм.");
});

test("does not replace abbreviations inside other identifiers", () => {
  assert.equal(
    expandValidationParameterNames("min_JJ и S_TR остаются служебными значениями.", null),
    "min_JJ и S_TR остаются служебными значениями.");
});

test("expands additional lift and escalator dimension codes", () => {
  assert.equal(
    expandValidationParameterNames(
      "A1 ≥ 100; BW ≥ 140; TJ ≤ 3000; TK ≤ 2500.",
      null),
    "Противовес по ширине ≥ 100; Ширина противовеса ≥ 140; Длина верхней входной площадки ≤ 3000; Длина нижней входной площадки ≤ 2500.");
});
