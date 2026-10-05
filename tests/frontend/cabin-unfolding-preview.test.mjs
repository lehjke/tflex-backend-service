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
  AA: 1100, BB: 2100, HL: 2600, HH: 2400, JJ: 900, A4: 0, "$Opening": "2S", "$DoorsType": "1D-1G",
  "$CopPlace": "Side", "$COP": "ZCB-ND10", WB: 220, M: 700, J: 767.5, G: 411.5, L: 0,
  A: 0, B: 550, C1: 700, C2: 0, D1: 700, D2: 700, F: 175, KK: 55,
  "$CeilingType": "ZCL-DN02", "$FloorType": "Concave down", "$DecorCode": "SUS-H", "$ColorCode": "Y002", "$TitanimCoatedCode": "Regular"
};

test("resolves default catalog values and the native baseline panel chains", () => {
  const geometry = getCabinUnfoldingGeometry(resolveDrawingConfigurationValues({ parameters: {} }, template));
  assert.ok(geometry);
  assert.equal(geometry.A4, 75);
  assert.deepEqual(geometry.rearSegments, [550, 550]);
  assert.deepEqual(geometry.leftSegments, [700, 700, 700]);
  assert.deepEqual(geometry.sideSegments, [693, 760.5, 235, 411.5]);
  assert.equal(geometry.copCenter, 1571);
  assert.equal(geometry.planCopCenter, 1578);
});

test("renders plan, roof, floor and correctly labeled A-D elevations with source dimensions", () => {
  const geometry = getCabinUnfoldingGeometry(base);
  const svg = renderCabinUnfoldingPreviewSvg(geometry);
  assert.match(svg, /Вид A · задняя/u);
  assert.match(svg, /Вид B · правая/u);
  assert.match(svg, /Вид C · левая/u);
  assert.match(svg, /Вид D · фронтальная/u);
  assert.match(svg, /JJ 900 · HH 2400/u);
  assert.match(svg, /HL 2600/u);
  assert.match(svg, /Потолок/u);
  assert.match(svg, /Напольное покрытие/u);
  assert.match(svg, /data-door-wall/u);
  assert.doesNotMatch(svg, /(?:NaN|undefined|Infinity)/u);
});

test("2S offset follows F and a through CO adds the rear opening", () => {
  const g = getCabinUnfoldingGeometry({ ...base, AA: 1600, A4: 40, "$Opening": "CO", "$DoorsType": "1D-2G", A: 0, B: 800, M: 700, J: 767.5, G: 411.5, WB: 220, D1: 700, D2: 700, C1: 700, C2: 0 });
  assert.ok(g);
  assert.equal(g.doorCenter, 840);
  assert.equal(g.frontDoor.wall, "D");
  assert.equal(g.rearDoor.wall, "A");
  const svg = renderCabinUnfoldingPreviewSvg(g);
  assert.match(svg, /Проём CO/u);
  assert.match(renderCabinUnfoldingPreviewSvg(g, { planOnly: true }), /План кабины/u);
});

test("rejects invalid dimensions and inconsistent source panel chains", () => {
  for (const value of [0, -1, "bad", Infinity]) {
    assert.equal(getCabinUnfoldingGeometry({ ...base, AA: value }), null);
    assert.equal(getCabinUnfoldingGeometry({ ...base, HL: value }), null);
  }
  assert.equal(getCabinUnfoldingGeometry({ ...base, "$Opening": "unknown" }), null);
  assert.equal(getCabinUnfoldingGeometry({ ...base, D1: 900 }), null);
  assert.equal(getCabinUnfoldingGeometry({ ...base, "$Opening": "CO", A4: 151 }), null);
});

test("escapes user-supplied labels", () => {
  const g = getCabinUnfoldingGeometry({ ...base, "$COP": "<script>&\"" });
  assert.ok(g);
  const svg = renderCabinUnfoldingPreviewSvg(g);
  assert.match(svg, /&lt;script&gt;&amp;/u);
  assert.doesNotMatch(svg, /<script>/u);
});

const resolved = parameters => getCabinUnfoldingGeometry(resolveDrawingConfigurationValues({ parameters }, template));
const panelLabels = (svg, id) => [...svg.matchAll(new RegExp(`<text[^>]*data-panel-size="${id}"[^>]*>([^<]+)</text>`, "gu"))].map(match => Number(match[1]));

test("top plan dimensions and seams match the native 1100x2100 panel layout", () => {
  const g = resolved({});
  const svg = renderCabinUnfoldingPreviewSvg(g, { planOnly: true });
  assert.deepEqual(panelLabels(svg, "plan-A"), [550, 550]);
  assert.deepEqual(panelLabels(svg, "plan-B"), [700, 767.5, 221, 411.5]);
  assert.deepEqual(panelLabels(svg, "plan-C"), [700, 700, 700]);
  assert.deepEqual(panelLabels(svg, "plan-D"), [175, 900, 25]);
  assert.equal([...svg.matchAll(/data-plan-seam="B"/gu)].length, 3);
  assert.deepEqual(g.elevationFrontSegments, [25, 900, 175]);
  const full = renderCabinUnfoldingPreviewSvg(g);
  assert.deepEqual(panelLabels(full, "wall-B"), [693, 760.5, 235, 411.5]);
  assert.doesNotMatch(svg, /<style>/u);
});

test("front COP uses regular side panels and the native front flank centre", () => {
  const g = resolved({ AA: 1600, $CopPlace_v_1: "Front" });
  assert.ok(g);
  assert.equal(g.copPlace, "Front");
  assert.deepEqual(g.frontSegments, [675, 900, 25]);
  assert.equal(g.planCopCenter, 337.5);
  assert.equal(g.copCenter, 1262.5);
  const svg = renderCabinUnfoldingPreviewSvg(g);
  assert.deepEqual(panelLabels(svg, "plan-B"), [700, 700, 700]);
  assert.deepEqual(panelLabels(svg, "plan-C"), [700, 700, 700]);
  assert.deepEqual(panelLabels(svg, "plan-COP-center"), [337.5]);
  assert.match(svg, /data-wall-cop="D"/u);
  assert.doesNotMatch(svg, /data-wall-cop="B"/u);
});

test("through CO mirrors the plan COP panels and keeps the native elevations centred", () => {
  const g = resolved({ AA: 1600, BB: 1500, HH: 2200, A4_v: 40, $Opening: "CO", $DoorsType: "1D-2G" });
  assert.ok(g);
  const svg = renderCabinUnfoldingPreviewSvg(g);
  assert.deepEqual(panelLabels(svg, "plan-A"), [390, 900, 310]);
  assert.deepEqual(panelLabels(svg, "plan-D"), [390, 900, 310]);
  assert.deepEqual(panelLabels(svg, "plan-B"), [430.25, 437.25, 221, 411.5]);
  assert.deepEqual(panelLabels(svg, "plan-C"), [411.5, 221, 437.25, 430.25]);
  assert.deepEqual(panelLabels(svg, "wall-A"), [350, 900, 350]);
  assert.deepEqual(panelLabels(svg, "wall-C"), [423.25, 430.25, 235, 411.5]);
  assert.match(svg, /data-plan-cop="C"/u);
});
