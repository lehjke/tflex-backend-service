import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  evaluateDrawingConfigurationValidation,
  resolveDrawingDoorCount,
  resolveDrawingConfigurationValues,
  toTravelHeightMillimeters
} from "../../src/TFlexDrawingService.Api/wwwroot/drawing-configuration-values.js";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const catalog = JSON.parse(
  fs.readFileSync(path.join(repositoryRoot, "templates/templates.json"), "utf8"));

test("resolves calculated shaft and car dimensions from a saved drawing configuration", () => {
  const template = catalog.templates.find(item => item.id === "lehy_l_pro_320_1050");
  const configuration = {
    templateId: template.id,
    parameters: {
      cap: 1050,
      $car_type_1050: "P14D",
      $PPP: "Нет",
      NE: 1,
      HL: 2700,
      $door_type: "ТО",
      JJ: 900,
      $fire_rating_1: "EI60",
      HH: 2400,
      $cwt_sg: "Да",
      WW_3: 260,
      WG_3: 650,
      $s: "Слева",
      dim: false,
      $speed_1050: "2.50",
      TR: 30,
      $load_type: "Крюки",
      $load_mount: "Нет",
      TM_mount: 0,
      ceil_thick: 100,
      floor: 0,
      mass: 0
    }
  };

  const values = resolveDrawingConfigurationValues(configuration, template);

  assert.deepEqual(
    Object.fromEntries(["AH", "BH", "OH", "PD", "AA", "BB"].map(name => [name, values[name]])),
    { AH: 1765, BH: 2500, OH: 4250, PD: 1300, AA: 1100, BB: 2100 });
  assert.equal(values.speed, 2.5);

  const issues = evaluateDrawingConfigurationValidation(
    configuration,
    template,
    { HL: 2400, HH: 2400 },
    { r_HH_HL: ["HL", "HH"] });
  assert.deepEqual(issues, [{
    name: "r_HH_HL",
    message: "HL-HH = 0. Должно быть HL-HH ≥ 100.",
    fieldNames: ["HL", "HH"],
    severity: "error"
  }]);

  const withoutCwtSafetyGear = resolveDrawingConfigurationValues(
    configuration,
    template,
    { $cwt_sg: "Нет" });
  const withCwtSafetyGear = resolveDrawingConfigurationValues(
    configuration,
    template,
    { $cwt_sg: "Да" });
  assert.equal(withoutCwtSafetyGear.min_AH, 1715);
  assert.equal(withCwtSafetyGear.min_AH, 1765);

  const commonOverrides = {
    AH: 1740,
    BH: 2500,
    AA: 1100,
    BB: 2100,
    JJ: 900,
    HH: 2400,
    HL: 2700,
    TR: 30,
    OH: 4250,
    PD: 1300,
    speed: 2.5,
    stops: 6
  };
  assert.deepEqual(evaluateDrawingConfigurationValidation(
    configuration,
    template,
    { ...commonOverrides, $cwt_sg: "Нет" },
    { r_AH: ["AH"] }), []);
  assert.equal(evaluateDrawingConfigurationValidation(
    configuration,
    template,
    { ...commonOverrides, $cwt_sg: "Да" },
    { r_AH: ["AH"] })[0]?.message, "AH = 1740. Должно быть 1765 ≤ AH ≤ 2693.");
});

test("normalizes drawing travel height to pricing millimeters", () => {
  assert.equal(toTravelHeightMillimeters(30), 30000);
  assert.equal(toTravelHeightMillimeters("30,5"), 30500);
  assert.equal(toTravelHeightMillimeters(30000), 30000);
  assert.equal(toTravelHeightMillimeters(""), null);
  assert.equal(toTravelHeightMillimeters("invalid"), null);
});

test("counts only active drawing stop doors during pricing transfer", () => {
  const template = catalog.templates.find(item => item.id === "lehy_l_pro_320_1050");
  const singleEntrance = {
    templateId: template.id,
    parameters: { stops: 6, NE: 1 }
  };
  assert.equal(resolveDrawingDoorCount(singleEntrance, template), 6);

  const twoEntrances = {
    templateId: template.id,
    parameters: {
      stops: 10,
      NE: 2,
      s_top_rear_1: true,
      s09_front_1: true,
      s09_rear_1: true,
      s08_front_1: false,
      s08_rear_1: true,
      s07_front_1: false,
      s07_rear_1: true,
      s06_front_1: false,
      s06_rear_1: true,
      s05_front_1: false,
      s05_rear_1: true,
      s04_front_1: false,
      s04_rear_1: true,
      s03_front_1: false,
      s03_rear_1: true,
      s02_front_1: false,
      s02_rear_1: true,
      s01_front_1: false,
      s01_rear_1: true
    }
  };
  const resolved = resolveDrawingConfigurationValues(twoEntrances, template);
  const inactiveDefaults = Object.keys(resolved).filter(name =>
    /^s(?:\d+|_top)_(?:front|rear)_1$/i.test(name) && resolved[name]).length;
  assert.ok(inactiveDefaults > 12, "fixture must expose the inactive-default regression");
  assert.equal(resolveDrawingDoorCount(twoEntrances, template, resolved), 12);
});

test("prefers an explicit stored drawing door count", () => {
  assert.equal(resolveDrawingDoorCount({ parameters: { stops: 10, NE: 2, Doors: 7 } }, null), 7);
});
