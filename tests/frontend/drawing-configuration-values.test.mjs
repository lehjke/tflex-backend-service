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
import { evaluateTFlexExpression } from "../../src/TFlexDrawingService.Api/wwwroot/safe-expression.js";

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
    { AH: 1765, BH: 2500, OH: 5550, PD: 1850, AA: 1100, BB: 2100 });
  assert.equal(values.speed, 2.5);

  const lowSpeedValues = resolveDrawingConfigurationValues({
    ...configuration,
    parameters: { ...configuration.parameters, $speed_1050: "1.00" }
  }, template);
  assert.equal(lowSpeedValues.OH, 4550);
  assert.equal(lowSpeedValues.PD, 1300);

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

test("recalculates automatic headroom and pit for LEHY-L-PRO 1050-2500 speed changes", () => {
  const template = catalog.templates.find(item => item.id === "lehy_l_pro_1050_2500");
  const parameters = {
    cap: 1200,
    $speed_1200: "1.00",
    TR: 30,
    HL: 2700,
    ceil_thick: 100,
    $load_type: "Крюки",
    $load_mount: "Нет",
    TM_mount: 0,
    floor: 0,
    dim: false,
    $cwt_sg: "Нет"
  };

  const lowSpeedValues = resolveDrawingConfigurationValues({ parameters }, template);
  const highSpeedValues = resolveDrawingConfigurationValues({
    parameters: { ...parameters, $speed_1200: "3.00" }
  }, template);

  assert.deepEqual(
    { OH: lowSpeedValues.OH, PD: lowSpeedValues.PD },
    { OH: 4900, PD: 1350 });
  assert.deepEqual(
    { OH: highSpeedValues.OH, PD: highSpeedValues.PD },
    { OH: 5350, PD: 2300 });
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

test("all production parameter variants keep levels and constraints evaluable", () => {
  const failures = [];
  let scenarioCount = 0;
  let ruleEvaluationCount = 0;
  let levelEvaluationCount = 0;

  const getVariantValues = definition => {
    const values = [];
    const allowedValues = definition.allowedValues || [];
    if (allowedValues.length > 0) {
      values.push(...(allowedValues.length <= 12
        ? allowedValues
        : [allowedValues[0], allowedValues[Math.floor(allowedValues.length / 2)], allowedValues.at(-1)]));
    }
    if (definition.minValue !== undefined && definition.minValue !== null) {
      values.push(definition.minValue);
    }
    if (definition.maxValue !== undefined && definition.maxValue !== null) {
      values.push(definition.maxValue);
    }
    return [...new Map(values.map(value => [JSON.stringify(value), value])).values()];
  };

  for (const template of catalog.templates) {
    const defaults = Object.fromEntries((template.parameters || [])
      .filter(definition => !definition.isReadOnly
        && definition.defaultValue !== undefined
        && definition.defaultValue !== null)
      .map(definition => [definition.name, definition.defaultValue]));
    const scenarios = [{ label: "defaults", parameters: defaults }];

    for (const definition of template.parameters || []) {
      if (definition.isReadOnly) continue;
      for (const value of getVariantValues(definition)) {
        scenarios.push({
          label: `${definition.name}=${String(value)}`,
          parameters: { ...defaults, [definition.name]: value }
        });
      }
    }

    for (const scenario of scenarios) {
      scenarioCount += 1;
      const context = resolveDrawingConfigurationValues(
        { parameters: scenario.parameters },
        template);
      const options = { lookupTables: template.lookupTables };

      for (const definition of [
        ...(template.parameters || []),
        ...(template.calculatedVariables || [])
      ]) {
        if (!definition.levelExpression) continue;
        levelEvaluationCount += 1;
        if (evaluateTFlexExpression(definition.levelExpression, context, options) === undefined) {
          failures.push(`${template.id}/${scenario.label}/${definition.name}: levelExpression`);
        }
      }

      for (const rule of template.validationRules || []) {
        ruleEvaluationCount += 1;
        if (evaluateTFlexExpression(rule.expression, context, options) === undefined) {
          failures.push(`${template.id}/${scenario.label}/${rule.name}: validationRule`);
        }
      }
    }
  }

  assert.ok(scenarioCount >= 750, `only ${scenarioCount} production scenarios were checked`);
  assert.ok(ruleEvaluationCount >= 18_000, `only ${ruleEvaluationCount} rules were checked`);
  assert.ok(levelEvaluationCount >= 170_000, `only ${levelEvaluationCount} levels were checked`);
  assert.deepEqual(failures, []);
});

test("each capacity-specific speed is covered by the automatic OH and PD lookup", () => {
  const templateIds = [
    "lehy_l_pro_320_1050",
    "lehy_l_pro_1050_2500",
    "lehy_pro_side_cwt",
    "lehy_pro_rear_cwt"
  ];
  const failures = [];

  for (const templateId of templateIds) {
    const template = catalog.templates.find(item => item.id === templateId);
    const capacities = template.parameters.find(item => item.name === "cap")?.allowedValues || [];
    const lookupSpeeds = new Set((template.lookupTables?.OH || []).map(row => Number(row.speed)));

    for (const capacity of capacities) {
      const speedDefinition = template.parameters.find(item =>
        item.name.startsWith("$speed_")
        && Number(item.name.slice("$speed_".length)) === Number(capacity));
      if (!speedDefinition) {
        failures.push(`${templateId}: missing speed field for ${capacity} kg`);
        continue;
      }
      for (const speed of speedDefinition.allowedValues || []) {
        if (!lookupSpeeds.has(Number(speed))) {
          failures.push(`${templateId}: speed ${speed} for ${capacity} kg is absent from OH/PD lookup`);
        }
      }
    }
  }

  assert.deepEqual(failures, []);
});

test("CWT safety gear changes calculated shaft constraints for every applicable template", () => {
  const templateIds = [
    "lehy_l_pro_320_1050",
    "lehy_l_pro_1050_2500",
    "lehy_pro_side_cwt",
    "lehy_pro_rear_cwt"
  ];
  const dimensionNames = ["AH", "BH", "min_AH", "min_BH", "OH", "PD"];

  for (const templateId of templateIds) {
    const template = catalog.templates.find(item => item.id === templateId);
    const safetyGearParameter = template.parameters.find(item =>
      item.name.toLowerCase() === "$cwt_sg");
    assert.ok(safetyGearParameter, `${templateId}: CWT safety gear parameter is missing`);
    const defaults = Object.fromEntries((template.parameters || [])
      .filter(definition => !definition.isReadOnly
        && definition.defaultValue !== undefined
        && definition.defaultValue !== null)
      .map(definition => [definition.name, definition.defaultValue]));
    const disabled = resolveDrawingConfigurationValues(
      { parameters: { ...defaults, [safetyGearParameter.name]: "Нет" } },
      template);
    const enabled = resolveDrawingConfigurationValues(
      { parameters: { ...defaults, [safetyGearParameter.name]: "Да" } },
      template);

    assert.ok(
      dimensionNames.some(name => disabled[name] !== enabled[name]),
      `${templateId}: CWT safety gear does not affect shaft constraints`);
  }
});
