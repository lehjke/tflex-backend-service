import assert from "node:assert/strict";
import test from "node:test";
import { getEscalatorPreviewGeometry, renderEscalatorPreviewSvg } from "../../src/TFlexDrawingService.Api/wwwroot/escalator-preview.js";

const source35 = {
  TK: 2253, TJ: 2625, HE: 3900, alpha: 35,
  TG: Math.round(3900 / Math.tan(35 * Math.PI / 180) / 5) * 5 + 2253 + 2625,
  LL: 11873, FC: 938, FJ: 1170, FK: 1020, HH: 1062, CH: 1000,
  Lpit: 4300, Dpit: 1250, PKD: 1110, Wpit: 3062, N: 2,
  s1: 0, SUP1: 7000, HSUP1: 1200, HM1: 180,
  s2: -1, SUP2: 0, HSUP2: -1, HM2: 342
};

test("preserves source anchors and inclination while building normal-depth truss", () => {
  const g = getEscalatorPreviewGeometry(source35);
  assert.ok(g);
  assert.deepEqual(g.lower, { x: source35.TK, y: 0 });
  assert.deepEqual(g.upper, { x: source35.TG - source35.TJ, y: source35.HE });
  assert.equal(g.run, source35.TG - source35.TK - source35.TJ);
  assert.ok(Math.abs(Math.atan(source35.HE / g.run) * 180 / Math.PI - 35) < 0.03);
  assert.ok(Math.abs(Math.hypot(g.truss[0].x-g.truss[3].x, g.truss[0].y-g.truss[3].y) - source35.FC) < 1e-8);
});

test("changes landing positions independently and keeps pit/support visibility parametric", () => {
  const changed = { ...source35, TK: 2600, TJ: 3000, TG: source35.TG + 722 };
  const g = getEscalatorPreviewGeometry(changed);
  assert.equal(g.lower.x, 2600);
  assert.equal(g.upper.x, changed.TG - 3000);
  assert.equal(g.hasPit, true);
  assert.deepEqual(g.supports.map(s => s.code), ["SUP1"]);
  const svg = renderEscalatorPreviewSvg(g);
  assert.match(svg, /viewBox="0 0 420 300"/u);
  assert.match(svg, /role="img"/u);
  assert.match(svg, /Приямок 4300×1250/u);
  const opening = getEscalatorPreviewGeometry({ ...changed, "$pit_v": "Отверстие в плите" });
  assert.equal(opening.hasPit, false);
  assert.match(renderEscalatorPreviewSvg(opening), /opening-edge/u);
});

test("bounds detail and rejects incomplete or impossible source geometry", () => {
  const huge = getEscalatorPreviewGeometry({ ...source35, HE: 1_000_000, TG: 1_010_000 });
  assert.ok(huge.steps.length <= 81);
  assert.equal(getEscalatorPreviewGeometry({ TK: 100, TJ: 100, HE: 500, alpha: 35, TG: 150 }), null);
  assert.equal(renderEscalatorPreviewSvg(null), "");
});


test("support remains visible with zero mounting elevation and the pit is not shortened", () => {
  const geometry = getEscalatorPreviewGeometry({...source35,HM1:0,Lpit:20000});
  const svg = renderEscalatorPreviewSvg(geometry);
  const support = svg.match(/<line data-support="SUP1"[^>]+>/u)[0];
  const y1 = support.match(/y1="([^"]+)"/u)[1];
  const y2 = support.match(/y2="([^"]+)"/u)[1];
  assert.notEqual(y1,y2);
  assert.match(svg,/fill="none" class="escalator-preview-svg__glass-line"/u);
  assert.equal(geometry.Lpit,20000);
});


test("does not draw reversed supports from unavailable source values", () => {
  const geometry = getEscalatorPreviewGeometry({...source35, s2:0,SUP2:0,HSUP2:-2726});
  assert.deepEqual(geometry.unavailableSupports,["SUP2"]);
  assert.deepEqual(geometry.supports.map(support=>support.code),["SUP1"]);
  assert.doesNotMatch(renderEscalatorPreviewSvg(geometry),/data-support="SUP2"/u);
});

test("omits unavailable Wpit=-1 from the schematic dimensions", () => {
  const svg = renderEscalatorPreviewSvg(getEscalatorPreviewGeometry({ ...source35, Wpit: -1 }));
  assert.doesNotMatch(svg, /Wpit\s*-1/u);
  const available = renderEscalatorPreviewSvg(getEscalatorPreviewGeometry({ ...source35, Wpit: 3062 }));
  assert.match(available, /Wpit 3062/u);
});
