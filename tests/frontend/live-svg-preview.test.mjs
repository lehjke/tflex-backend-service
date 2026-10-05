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
    assert.doesNotMatch(svg, /(?:NaN|undefined|Infinity)/u);
  }
});
