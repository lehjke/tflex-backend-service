import assert from "node:assert/strict";
import test from "node:test";

import {
  collectStopParameterValues,
  getAuthoritativeStopLevelValues,
  getMainSelectionMode,
  resolveMainFloor
} from "../../src/TFlexDrawingService.Api/wwwroot/stop-state.js";

test("automatic levels are authoritative and every active level reaches the payload", () => {
  const existing = {
    s01_name_1: 1,
    s02_name_1: 2,
    s_top_name_1: 3,
    s01_level_1: 0,
    s02_level_1: 3000,
    s_top_level_1: 20000
  };
  const levels = getAuthoritativeStopLevelValues({
    stops: 3,
    manualLevels: false,
    values: existing,
    bottomLevel: 0,
    travelHeightMeters: 20
  });

  assert.deepEqual(levels, {
    s01_level_1: 0,
    s02_level_1: 10000,
    s_top_level_1: 20000
  });

  const payload = collectStopParameterValues({
    stops: 3,
    values: { ...existing, ...levels }
  });
  assert.deepEqual(payload, {
    s01_name_1: 1,
    s01_level_1: 0,
    s02_name_1: 2,
    s02_level_1: 10000,
    s_top_name_1: 3,
    s_top_level_1: 20000
  });
});

test("manual intermediate levels are preserved while the top remains derived", () => {
  const levels = getAuthoritativeStopLevelValues({
    stops: 3,
    manualLevels: true,
    values: {
      s01_level_1: -500,
      s02_level_1: 8500,
      s_top_level_1: 999
    },
    bottomLevel: -500,
    travelHeightMeters: 20
  });

  assert.deepEqual(levels, {
    s01_level_1: -500,
    s02_level_1: 8500,
    s_top_level_1: 19500
  });
});

test("main=true makes lobby radios read-only and main=false enables explicit selection", () => {
  assert.deepEqual(getMainSelectionMode(true), {
    automatic: true,
    manual: false,
    radiosReadOnly: true
  });
  assert.equal(resolveMainFloor({
    mainValue: true,
    selectedMainFloor: 3,
    lobbyStopIndex: 1,
    stops: 3
  }), 1);

  assert.deepEqual(getMainSelectionMode(false), {
    automatic: false,
    manual: true,
    radiosReadOnly: false
  });
  assert.equal(resolveMainFloor({
    mainValue: false,
    selectedMainFloor: 3,
    lobbyStopIndex: 1,
    stops: 3
  }), 3);
});
