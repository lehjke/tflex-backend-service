import assert from "node:assert/strict";
import test from "node:test";

import {
  collectStopParameterValues,
  getAuthoritativeStopLevelValues,
  getMainSelectionMode,
  isSignedIntegerDraft,
  isSignedStopIntegerParameterName,
  resolveMainFloor
} from "../../src/TFlexDrawingService.Api/wwwroot/stop-state.js";

test("floor names and levels accept a leading minus while typing", () => {
  for (const name of [
    "s01_name_1",
    "s_top_name_1",
    "s01_level_1",
    "s_top_level_1"
  ]) {
    assert.equal(isSignedStopIntegerParameterName(name), true, name);
  }

  for (const name of ["main_floor", "s01_front_1", "s01_rear_1"]) {
    assert.equal(isSignedStopIntegerParameterName(name), false, name);
  }

  for (const value of ["", "-", "-1", "0", "12"]) {
    assert.equal(isSignedIntegerDraft(value), true, value);
  }

  for (const value of ["--1", "1-", "1.5", "A1"]) {
    assert.equal(isSignedIntegerDraft(value), false, value);
  }
});

test("negative floor names reach the stop payload unchanged", () => {
  const payload = collectStopParameterValues({
    stops: 2,
    values: {
      s01_name_1: -1,
      s01_level_1: 0,
      s_top_name_1: 1,
      s_top_level_1: 3000
    }
  });

  assert.deepEqual(payload, {
    s01_name_1: -1,
    s01_level_1: 0,
    s_top_name_1: 1,
    s_top_level_1: 3000
  });
});

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

test("main=false keeps first floor automatic and main=true enables explicit lobby selection", () => {
  assert.deepEqual(getMainSelectionMode(true), {
    automatic: false,
    manual: true,
    radiosReadOnly: false
  });
  assert.equal(resolveMainFloor({
    mainValue: true,
    selectedMainFloor: 3,
    lobbyStopIndex: 1,
    stops: 3
  }), 3);

  assert.deepEqual(getMainSelectionMode(false), {
    automatic: true,
    manual: false,
    radiosReadOnly: true
  });
  assert.equal(resolveMainFloor({
    mainValue: false,
    selectedMainFloor: 3,
    lobbyStopIndex: 1,
    stops: 3
  }), 1);
});
