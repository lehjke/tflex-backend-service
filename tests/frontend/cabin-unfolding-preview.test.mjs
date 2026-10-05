import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { resolveDrawingConfigurationValues } from "../../src/TFlexDrawingService.Api/wwwroot/drawing-configuration-values.js";
import { getCabinUnfoldingGeometry, renderCabinUnfoldingPreviewSvg } from "../../src/TFlexDrawingService.Api/wwwroot/cabin-unfolding-preview.js";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const templates = JSON.parse(fs.readFileSync(path.join(root, "templates/templates.json"), "utf8")).templates;
const template = templates.find(item => item.id === "razvertki_lehy");
const base = {
  AA: 1100, BB: 2100, HL: 2600, HH: 2400, JJ: 900, A4: 0,
  "$Opening": "2S", "$DoorsType": "1D-1G", "$CopPlace": "Side", "$COP": "ZCB-ND10",
  WB: 220, M: 1000, B: 550, F: 175, "$CeilingType": "ZCL-DN02", "$FloorType": "Concave down",
  "$DecorCode": "SUS-H", "$ColorCode": "Y002", "$TitanimCoatedCode": "Regular"
};

test("resolves default LEHY catalog configuration", () => {
  const values = resolveDrawingConfigurationValues({ parameters: {} }, template);
  const geometry = getCabinUnfoldingGeometry(values);
  assert.ok(geometry);
  assert.equal(geometry.opening, "2S");
  assert.equal(geometry.doorsType, "1D-1G");
  assert.equal(geometry.through, false);
  assert.equal(geometry.rearDoor, null);
  assert.equal(geometry.copWidth, 220);
  assert.equal(geometry.ceilingHeight, 200);
  assert.equal(geometry.A4, 75);
});

test("models 2S aperture position, zero offset, and sliding direction", () => {
  const geometry = getCabinUnfoldingGeometry(base);
  assert.equal(geometry.A4, 0);
  assert.equal(geometry.frontDoor.wall, "A");
  assert.equal(geometry.doorCenter, 550);
  const svg = renderCabinUnfoldingPreviewSvg(geometry);
  assert.match(svg, /role="img"/u);
  assert.match(svg, /A4 \+0 мм/u);
  assert.match(svg, /AA 1100 × BB 2100 мм/u);
  assert.match(svg, /h-12/u);
});

test("models CO signed offset, front COP, and through rear door", () => {
  const geometry = getCabinUnfoldingGeometry({ ...base, AA:1600, A4: 40, "$Opening": "CO", "$DoorsType": "1D-2G", "$CopPlace": "Front", B: 400, F:350 });
  assert.equal(geometry.doorCenter, 840);
  assert.equal(geometry.rearDoor.wall, "C");
  assert.equal(geometry.copWall, "A");
  assert.equal(geometry.copCenter, 1445);
  assert.ok(geometry.copCenter-geometry.copWidth/2>geometry.doorCenter+geometry.JJ/2);
  assert.equal(geometry.frontClearance, 350);
  assert.match(renderCabinUnfoldingPreviewSvg(geometry), /A \/ C/u);
  assert.match(renderCabinUnfoldingPreviewSvg(geometry), /WB220 @1445 F350/u);
});

test("accepts native wide 2S offset and renders elevations in a common scale", () => {
  const geometry = getCabinUnfoldingGeometry({ ...base, AA: 2100, BB: 1400, HL: 2700, HH: 2400, JJ: 900, A4: 575 });
  assert.ok(geometry);
  assert.equal(geometry.doorCenter, 475);
  const svg = renderCabinUnfoldingPreviewSvg(geometry);
  const rectangles = [...svg.matchAll(/<rect x="[^"]+" y="[^"]+" width="([\d.]+)" height="([\d.]+)" fill="#f8fafc" stroke="#465360"/gu)];
  const front = rectangles[1].slice(1).map(Number);
  const side = rectangles[2].slice(1).map(Number);
  assert.ok(Math.abs(front[0] / 2100 - front[1] / 2700) < 1e-12);
  assert.ok(Math.abs(side[0] / 1400 - side[1] / 2700) < 1e-12);
  assert.equal(front[1], 72);
});

test("rejects bad geometry and only applies the 150 mm offset limit to CO", () => {
  assert.equal(getCabinUnfoldingGeometry({ ...base, A4: undefined }), null);
  for (const value of [0, -1, "bad", Infinity]) {
    assert.equal(getCabinUnfoldingGeometry({ ...base, AA: value }), null);
    assert.equal(getCabinUnfoldingGeometry({ ...base, HL: value }), null);
  }
  assert.equal(getCabinUnfoldingGeometry({ ...base, A4: 151, "$Opening": "CO" }), null);
  assert.ok(getCabinUnfoldingGeometry({ ...base, AA: 2100, JJ: 900, A4: 575 }));
  assert.equal(getCabinUnfoldingGeometry({ ...base, "$DoorsType": "unknown" }), null);
  assert.equal(getCabinUnfoldingGeometry({ ...base, "$Opening": "unknown" }), null);
  for (const value of [0, -1, "bad", Infinity, 2600]) {
    assert.equal(getCabinUnfoldingGeometry({ ...base, CeilingHeight: value }), null);
  }
});

test("uses paint color only for painted steel and escapes labels", () => {
  const metal = getCabinUnfoldingGeometry({ ...base, "$DecorCode": "SUS-H", "$ColorCode": "<script>&\"", "$TitanimCoatedCode": "ZDT-003" });
  assert.equal(metal.colorCode, "");
  assert.equal(metal.tint, "#d9dce1");
  const painted = getCabinUnfoldingGeometry({ ...base, "$DecorCode": "Painted steel", "$ColorCode": "Y002" });
  assert.equal(painted.colorCode, "Y002");
  const svg = renderCabinUnfoldingPreviewSvg(getCabinUnfoldingGeometry({ ...base, "$COP": "<script>&\"" }));
  assert.match(svg, /&lt;script&gt;&amp;&quot;/u);
  assert.doesNotMatch(svg, /<script>/u);
});
