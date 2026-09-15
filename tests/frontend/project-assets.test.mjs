import assert from "node:assert/strict";
import test from "node:test";

import {
  formatProjectAssetTitle,
  getConfigurationTravelHeightMeters,
  groupProjectAssets,
  normalizeProjectAssetName
} from "../../src/TFlexDrawingService.Api/wwwroot/project-assets.js";

test("normalizes project asset names across case and whitespace", () => {
  assert.equal(normalizeProjectAssetName("  Лифт   L1 "), "лифт l1");
});

test("groups drawing and pricing records with the same name", () => {
  const drawing = { id: "drawing-1", name: "Лифт L1", updatedAt: "2026-08-20T10:00:00Z" };
  const pricing = { id: "price-1", name: " лифт   l1 ", updatedAt: "2026-08-21T10:00:00Z" };

  const groups = groupProjectAssets([drawing], [pricing]);

  assert.equal(groups.length, 1);
  assert.deepEqual(groups[0].configurations, [drawing]);
  assert.deepEqual(groups[0].pricingSpecifications, [pricing]);
  assert.equal(groups[0].updatedAt, pricing.updatedAt);
});

test("keeps equally named assets in different project calls independent", () => {
  const firstProject = groupProjectAssets(
    [{ id: "drawing-1", name: "L1" }],
    [{ id: "price-1", name: "L1" }]);
  const secondProject = groupProjectAssets(
    [{ id: "drawing-2", name: "L1" }],
    []);

  assert.equal(firstProject.length, 1);
  assert.equal(firstProject[0].pricingSpecifications.length, 1);
  assert.equal(secondProject.length, 1);
  assert.equal(secondProject[0].pricingSpecifications.length, 0);
});

test("keeps equally named drawings in the same project separate", () => {
  const firstDrawing = { id: "drawing-1", name: "L1" };
  const secondDrawing = { id: "drawing-2", name: "L1" };

  const groups = groupProjectAssets([firstDrawing, secondDrawing], []);

  assert.equal(groups.length, 2);
  assert.deepEqual(groups[0].configurations, [firstDrawing]);
  assert.deepEqual(groups[1].configurations, [secondDrawing]);
});

test("preserves an explicit drawing link even when display names differ", () => {
  const drawing = { id: "drawing-1", name: "L1" };
  const pricing = { id: "price-1", name: "КП для L1", projectConfigurationId: "drawing-1" };

  const groups = groupProjectAssets([drawing], [pricing]);

  assert.equal(groups.length, 1);
  assert.deepEqual(groups[0].pricingSpecifications, [pricing]);
});

test("links a price to the requested drawing when names are duplicated", () => {
  const firstDrawing = { id: "drawing-1", name: "L1" };
  const secondDrawing = { id: "drawing-2", name: "L1" };
  const pricing = { id: "price-1", name: "L1", projectConfigurationId: "drawing-2" };

  const groups = groupProjectAssets([firstDrawing, secondDrawing], [pricing]);

  assert.equal(groups.length, 2);
  assert.equal(groups[0].pricingSpecifications.length, 0);
  assert.deepEqual(groups[1].pricingSpecifications, [pricing]);
});

test("does not attach an unlinked legacy price to ambiguous drawings", () => {
  const groups = groupProjectAssets(
    [{ id: "drawing-1", name: "L1" }, { id: "drawing-2", name: "L1" }],
    [{ id: "price-1", name: "L1" }]);

  assert.equal(groups.length, 3);
  assert.equal(groups[2].configurations.length, 0);
  assert.equal(groups[2].pricingSpecifications.length, 1);
});

test("does not merge unrelated names", () => {
  const groups = groupProjectAssets(
    [{ id: "drawing-1", name: "L1" }],
    [{ id: "price-1", name: "L2" }]);

  assert.equal(groups.length, 2);
});

test("formats drawing titles with one-decimal travel height and address", () => {
  assert.equal(
    formatProjectAssetTitle("L1", 20, "г. Москва, Пшеничная улица д.1"),
    "L1 (20.0) - г. Москва, Пшеничная улица д.1");
});

test("reads metre and millimetre travel heights from template metadata", () => {
  const metresTemplate = { parameters: [{ name: "TR", unit: "м" }] };
  const millimetresTemplate = { parameters: [{ name: "HE", unit: "мм" }] };
  const legacyMillimetresTemplate = {
    parameters: [{ name: "HE", displayName: "Высота подъема" }]
  };

  assert.equal(getConfigurationTravelHeightMeters({ parameters: { TR: "20,5" } }, metresTemplate), 20.5);
  assert.equal(getConfigurationTravelHeightMeters({ parameters: { HE: 3900 } }, millimetresTemplate), 3.9);
  assert.equal(getConfigurationTravelHeightMeters({ parameters: { HE: 3900 } }, legacyMillimetresTemplate), 3.9);
});
