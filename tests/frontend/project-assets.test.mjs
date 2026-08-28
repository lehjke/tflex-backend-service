import assert from "node:assert/strict";
import test from "node:test";

import {
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

test("preserves an explicit drawing link even when display names differ", () => {
  const drawing = { id: "drawing-1", name: "L1" };
  const pricing = { id: "price-1", name: "КП для L1", projectConfigurationId: "drawing-1" };

  const groups = groupProjectAssets([drawing], [pricing]);

  assert.equal(groups.length, 1);
  assert.deepEqual(groups[0].pricingSpecifications, [pricing]);
});

test("does not merge unrelated names", () => {
  const groups = groupProjectAssets(
    [{ id: "drawing-1", name: "L1" }],
    [{ id: "price-1", name: "L2" }]);

  assert.equal(groups.length, 2);
});
