import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { resolveDrawingConfigurationValues } from "../../src/TFlexDrawingService.Api/wwwroot/drawing-configuration-values.js";
import { getLehyPreviewGeometry } from "../../src/TFlexDrawingService.Api/wwwroot/lehy-preview.js";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const catalog = JSON.parse(fs.readFileSync(path.join(root, "templates/templates.json"), "utf8"));
const getTemplate = id => catalog.templates.find(template => template.id === id);
const getDefaults = id => {
  const template = getTemplate(id);
  return { template, context: resolveDrawingConfigurationValues({ parameters: {} }, template) };
};

test("uses measured LEHY cabin and counterweight axes across all four templates", () => {
  for (const id of ["lehy_l_pro_320_1050", "lehy_l_pro_1050_2500", "lehy_pro_side_cwt", "lehy_pro_rear_cwt"]) {
    const { template, context } = getDefaults(id);
    const geometry = getLehyPreviewGeometry(template, context);
    assert.ok(geometry, `${id} should have sufficient source dimensions`);
    if (id.startsWith("lehy_l_pro_")) {
      assert.equal(geometry.cabinAxisX, context.AH - context.A3);
      assert.equal(geometry.cabinAxisX, context.A1 + context.A2);
      assert.equal(geometry.cwtAxisX, context.A1);
    } else if (id === "lehy_pro_side_cwt") {
      assert.equal(geometry.cabinAxisX, context.AH - context.CB);
      assert.equal(geometry.cwtAxisX, context.BW);
    } else {
      assert.equal(geometry.cabinAxisX, context.AH - context.CB);
      assert.equal(geometry.cwtAxisX, context.AH - context.A8);
    }
  }
});

test("retains signed counterweight coordinates and reports cabin collisions without clamping", () => {
  const { template, context } = getDefaults("lehy_l_pro_320_1050");
  const geometry = getLehyPreviewGeometry(template, { ...context, A1: -40 });
  assert.equal(geometry.cwtAxisX, -40);
  assert.equal(geometry.cwtRect.x, -40 - context.WW / 2);
  assert.ok(geometry.cwtRect.x < 0);
});

test("uses rear counterweight A8 independently from the cabin CB anchor", () => {
  const { template, context } = getDefaults("lehy_pro_rear_cwt");
  const geometry = getLehyPreviewGeometry(template, { ...context, A8: 900 });
  assert.equal(geometry.cabinAxisX, context.AH - context.CB);
  assert.equal(geometry.cwtAxisX, context.AH - 900);
  assert.notEqual(geometry.cwtAxisX, geometry.cabinAxisX);
  assert.equal(geometry.cwtRect.x, context.AH - 900 - context.WG / 2);
});

test("treats CC=-1 as unavailable and uses the existing rear cabin placement fallback", () => {
  const { template, context } = getDefaults("lehy_pro_rear_cwt");
  const unavailable = getLehyPreviewGeometry(template, { ...context, CC: -1 });
  const missing = getLehyPreviewGeometry(template, { ...context, CC: undefined });
  assert.deepEqual(unavailable.cabinOuterRect, missing.cabinOuterRect);
  assert.deepEqual(unavailable.cwtRect, missing.cwtRect);
});

test("returns mirrored source rectangles and both through-passage door styles", () => {
  const { template, context } = getDefaults("lehy_l_pro_320_1050");
  const to = getLehyPreviewGeometry(template, { ...context, NE: 2, $s: "Справа", $door_type: "ТО" });
  assert.equal(to.mirrorX, true);
  assert.equal(to.doorPairs.length, 2);
  assert.equal(to.carDoorDepth, 96);
  assert.equal(to.landingDoorDepth, 120);
  assert.equal(to.cabinInnerRect.x, context.AH - to.source.cabinInnerRect.x - context.AA);
  assert.equal(to.doorPairs[1].side, "rear");

  const co = getLehyPreviewGeometry(template, { ...context, NE: 2, $door_type: "ЦО", KK: 55, DK: 115, B1: 100 });
  assert.equal(co.centerOpening, true);
  assert.equal(co.carDoorDepth, 60);
  assert.equal(co.landingDoorDepth, 100);
  assert.equal(co.doorPairs.length, 2);
});


test("front landing door stays at the shaft boundary and invalid counterweight sizes stay absent", () => {
  for (const id of ["lehy_l_pro_320_1050", "lehy_l_pro_1050_2500", "lehy_pro_side_cwt"]) {
    const {template, context} = getDefaults(id);
    const geometry = getLehyPreviewGeometry(template, context);
    const front = geometry.doorPairs[0].landingDoor;
    assert.equal(front.y+front.height, context.BH);
    assert.equal(getLehyPreviewGeometry(template, {...context,WW:-1}).cwtRect,null);
    assert.equal(getLehyPreviewGeometry(template, {...context,NE:3}),null);
  }
});
