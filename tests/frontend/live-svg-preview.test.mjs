import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { resolveDrawingConfigurationValues } from "../../src/TFlexDrawingService.Api/wwwroot/drawing-configuration-values.js";
import { renderLiveSvgPreview, renderLiveSvgPreviewMetrics } from "../../src/TFlexDrawingService.Api/wwwroot/live-svg-preview.js";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const templates = JSON.parse(fs.readFileSync(path.join(root, "templates/templates.json"), "utf8")).templates;

test("renders a schematic for all nine supported templates", () => {
  const ids = ["un_victor_mrl", "un_victor_mrl_t", "un_victor_r", "lehy_l_pro_320_1050", "lehy_l_pro_1050_2500", "lehy_pro_side_cwt", "lehy_pro_rear_cwt", "k_ii_type", "razvertki_lehy"];
  for (const id of ids) {
    const template = templates.find(item => item.id === id);
    const context = resolveDrawingConfigurationValues({ templateId: id, parameters: {} }, template);
    const svg = renderLiveSvgPreview(template, context);
    const metrics = renderLiveSvgPreviewMetrics(template, context);
    assert.match(svg, /<svg\b/u, `${id} should produce SVG from its catalog defaults`);
    assert.match(svg, /role="img"/u, `${id} SVG should have an accessible image role`);
    assert.match(metrics, /shaft-preview__metric/u, `${id} should restore its old metrics section`);
    assert.doesNotMatch(`${svg}${metrics}`, /(?:NaN|undefined|Infinity)/u, `${id} preview must contain valid values`);
  }
});

test("cabin unfolding live SVG includes the full walls and measured doorway dimensions", () => {
  const template = templates.find(item => item.id === "razvertki_lehy");
  const context = resolveDrawingConfigurationValues({ templateId: template.id, parameters: {} }, template);
  const svg = renderLiveSvgPreview(template, context);
  assert.match(svg, /data-door-wall="D"/u);
  assert.match(svg, /D · фронт/u);
  assert.match(svg, /A · зад/u);
  assert.match(svg, /AA \d+/u);
  assert.match(svg, /BB \d+/u);
  assert.match(svg, /JJ \d+/u);
  assert.match(svg, /HH \d+/u);
});

test("LEHY plan includes cabin and both landing and car door geometry", () => {
  const template = templates.find(item => item.id === "lehy_l_pro_320_1050");
  const context = resolveDrawingConfigurationValues({ templateId: template.id, parameters: {} }, template);
  const svg = renderLiveSvgPreview(template, context);
  assert.match(svg, /shaft-preview-svg__car-inner/u);
  assert.match(svg, /shaft-preview-svg__landing-door/u);
  assert.match(svg, /shaft-preview-svg__car-door/u);
  const metrics = renderLiveSvgPreviewMetrics(template, context);
  assert.match(metrics, /shaft-preview__metric/u);
  assert.match(metrics, new RegExp(`JJ[\\s\\S]*?${context.JJ}\\s*<span>Ширина дверей`, "u"));
  assert.match(svg, new RegExp(`data-dimension-label="door-clear-width"[^>]*>JJ ${context.JJ}<`, "u"));
});

test("shaft dimensions and labels stay outside the concrete contour for all shaft templates", () => {
  const ids = ["un_victor_mrl", "un_victor_mrl_t", "un_victor_r", "lehy_l_pro_320_1050", "lehy_l_pro_1050_2500", "lehy_pro_side_cwt", "lehy_pro_rear_cwt"];
  for (const id of ids) {
    const template = templates.find(item => item.id === id);
    const entrances = id.startsWith("un_") ? { NBENT: 2 } : { NE: 2 };
    const context = resolveDrawingConfigurationValues({ templateId: id, parameters: entrances }, template);
    if (id.startsWith("lehy_") && id !== "lehy_pro_rear_cwt") context.$s = "right";
    const svg = renderLiveSvgPreview(template, context);
    const [, concretePath] = svg.match(/class="shaft-preview-svg__shaft-concrete" d="([^"]+)"/u) ?? [];
    assert.ok(concretePath, `${id}: expected concrete contour`);
    const outerPoints = [...concretePath.matchAll(/[ML] ([\d.]+) ([\d.]+)/gu)].slice(0, 4).map(([, x, y]) => [Number(x), Number(y)]);
    assert.equal(outerPoints.length, 4, `${id}: expected outer concrete rectangle`);
    const bounds = {
      left: Math.min(...outerPoints.map(([x]) => x)), right: Math.max(...outerPoints.map(([x]) => x)),
      top: Math.min(...outerPoints.map(([, y]) => y)), bottom: Math.max(...outerPoints.map(([, y]) => y))
    };
    const lines = Object.fromEntries([...svg.matchAll(/<line data-dimension="([^"]+)" x1="([\d.]+)" y1="([\d.]+)" x2="([\d.]+)" y2="([\d.]+)"\/>/gu)].map(([, name, x1, y1, x2, y2]) => [name, { x1: Number(x1), y1: Number(y1), x2: Number(x2), y2: Number(y2) }]));
    assert.ok(lines["cabin-width"].y1 < bounds.top, `${id}: cabin width must be above concrete`);
    assert.ok(lines["cabin-depth"].x1 > bounds.right, `${id}: cabin depth must be right of concrete`);
    assert.ok(lines["door-clear-width"].y1 > bounds.bottom, `${id}: clear opening must be below concrete`);
    assert.ok(lines["shaft-width"].y1 > lines["door-clear-width"].y1, `${id}: shaft width must use the lower tier`);
    assert.ok(lines["shaft-depth"].x1 < bounds.left, `${id}: shaft depth must be left of concrete`);
    const [, dimensionMarkup] = svg.match(/<g fill="none" stroke="#64748b" stroke-width="1">([\s\S]*?)<\/g>/u);
    for (const [, x1, y1, x2, y2] of dimensionMarkup.matchAll(/<line[^>]* x1="([\d.]+)" y1="([\d.]+)" x2="([\d.]+)" y2="([\d.]+)"/gu)) {
      assert.ok(Math.max(Number(x1), Number(x2)) <= bounds.left + 0.1 || Math.min(Number(x1), Number(x2)) >= bounds.right - 0.1
        || Math.max(Number(y1), Number(y2)) <= bounds.top + 0.1 || Math.min(Number(y1), Number(y2)) >= bounds.bottom - 0.1,
      `${id}: dimension witness lines must not cross the shaft interior`);
    }
    const labels = Object.fromEntries([...svg.matchAll(/<text data-dimension-label="([^"]+)" x="([\d.]+)" y="([\d.]+)"/gu)].map(([, name, x, y]) => [name, { x: Number(x), y: Number(y) }]));
    assert.ok(labels["cabin-width"].y < bounds.top, `${id}: cabin width label must be above concrete`);
    assert.ok(labels["cabin-depth"].x > bounds.right, `${id}: cabin depth label must be right of concrete`);
    assert.ok(labels["door-clear-width"].y > bounds.bottom, `${id}: clear-width label must be below concrete`);
    assert.ok(labels["shaft-width"].y > lines["shaft-width"].y1, `${id}: shaft width label must sit outside below its line`);
    assert.ok(labels["shaft-depth"].x < bounds.left, `${id}: shaft depth label must be left of concrete`);
    assert.ok(Object.values(labels).every(({ x, y }) => x >= 0 && x <= 380 && y >= 0 && y <= 360), `${id}: dimension labels must stay inside the SVG`);
    assert.match(svg, /data-dimension-label="cabin-width"[^>]*>\w+ \d+</u);
    assert.match(svg, /data-dimension-label="cabin-depth"[^>]*>\w+ \d+</u);
    assert.match(svg, /data-dimension-label="door-clear-width"[^>]*>(?:JJ|OP) \d+/u);
    if (id.startsWith("un_")) assert.match(svg, /HL6 \d+/u);
    assert.doesNotMatch(svg, /CW \d+ × CD \d+/u, `${id}: cabin dimensions must be outside the cabin`);
    assert.doesNotMatch(svg, /(?:NaN|undefined|Infinity)/u);
  }
});

test("escalator SVG overlays numeric rise, run, and landing dimensions", () => {
  const template = templates.find(item => item.id === "k_ii_type");
  const context = resolveDrawingConfigurationValues({ templateId: template.id, parameters: {} }, template);
  const svg = renderLiveSvgPreview(template, context);
  for (const name of ["HE", "TG", "TK", "TJ"]) assert.match(svg, new RegExp(`${name} ${context[name]}`, "u"));
  assert.match(svg, /aria-label="Размеры эскалатора"/u);
  const [, heLabelX] = svg.match(/<text data-dimension="HE" x="([\d.]+)"/u) ?? [];
  assert.ok(Number(heLabelX) >= 18, "rotated HE label should stay inside the left SVG padding");
  assert.doesNotMatch(svg, /(?:NaN|undefined|Infinity)/u);
});

test("legacy shaft render restores concrete walls, axes, openings, counterweight and XIZI car doors", () => {
  for (const id of ["un_victor_mrl", "lehy_l_pro_320_1050"]) {
    const template = templates.find(item => item.id === id);
    const context = resolveDrawingConfigurationValues({ templateId: id, parameters: { NBENT: 2 } }, template);
    const svg = renderLiveSvgPreview(template, context);
    assert.match(svg, /shaft-preview-svg__shaft-concrete/u);
    assert.match(svg, /shaft-preview-svg__axis/u);
    assert.match(svg, /shaft-preview-svg__door-opening-marker/u);
    assert.match(svg, /shaft-preview-svg__counterweight/u);
    assert.match(svg, /shaft-preview-svg__car-door/u);
    assert.match(svg, /stroke="#64748b" stroke-width="1"/u);
    assert.doesNotMatch(svg, /(?:NaN|undefined|Infinity)/u);
  }
});
