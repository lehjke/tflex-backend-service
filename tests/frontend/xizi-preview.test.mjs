import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { resolveDrawingConfigurationValues } from "../../src/TFlexDrawingService.Api/wwwroot/drawing-configuration-values.js";
import { getXiziPreviewGeometry, isXiziPreviewTemplate } from "../../src/TFlexDrawingService.Api/wwwroot/xizi-preview.js";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const templates = JSON.parse(fs.readFileSync(path.join(root, "templates/templates.json"), "utf8")).templates;

test("resolves live XIZI geometry for all three supported templates", () => {
  for (const [id, expected] of [
    ["un_victor_mrl", [2750, 1750, 2100, 1100, 1485, 1835, "13", 1200]],
    ["un_victor_mrl_t", [2750, 1750, 2100, 1100, 1450, 1800, "13", 700]],
    ["un_victor_r", [2650, 1800, 2100, 1100, 1325, 1675, "12", 1200]]
  ]) {
    const template = templates.find(item => item.id === id);
    const values = resolveDrawingConfigurationValues({ templateId: id, parameters: {} }, template);
    assert.equal(isXiziPreviewTemplate(template), true);
    const geometry = getXiziPreviewGeometry(template, values);
    assert.deepEqual([
      geometry.width, geometry.depth, geometry.cabinWidth, geometry.cabinDepth,
      geometry.cabinAxisX, geometry.doorAxisX, geometry.place, geometry.cwtSize
    ], expected);
    assert.equal(geometry.doors.length, 1);
    assert.equal(geometry.doors[0].y + geometry.doors[0].height, geometry.depth);
    assert.ok(geometry.cwtRect.y >= 0);
  }
});

test("resolves LAND depth and preserves edited absolute axes and counterweight placement", () => {
  const template = templates.find(item => item.id === "un_victor_mrl");
  const configuration = { parameters: { $LDMTG: "LAND", WTW: 1800, HL6: 0, DOP: 0, $CWTLOC: "24", NBENT: 2 } };
  const values = resolveDrawingConfigurationValues(configuration, template);
  const geometry = getXiziPreviewGeometry(template, values);
  assert.equal(geometry.depth, values.HD);
  assert.ok(geometry.depth > values.WTW);
  assert.equal(geometry.cabinAxisX, values.HW1);
  assert.equal(geometry.doorAxisX, 0);
  assert.equal(geometry.entrances, 2);
  assert.equal(geometry.place, "24");
});

test("keeps zero axes and rejects invalid dimensions or layout values", () => {
  const template = templates.find(item => item.id === "un_victor_r");
  const context = {
    HW: 2500, HD: 1800, CW: 1400, CD: 1200, HW1: 0, HL6: 0, DOP: 0, OP: 800,
    NBENT: 1, $CWTLOC: "12", $HAND: "RIGHT", CWTDBG: 1000,
    ldt: 120, cdt: 175, tol: 0, df: 475, x_cwt: 802, y_cwt: 200
  };
  const valid = getXiziPreviewGeometry(template, context);
  assert.equal(valid.cabinAxisX, 0);
  assert.equal(valid.doorAxisX, 0);
  assert.equal(getXiziPreviewGeometry(template, { ...context, HW1: -1 }), null);
  assert.equal(getXiziPreviewGeometry(template, { ...context, HL6: -1 }), null);
  for (const name of ["HW", "HD", "CW", "CD", "OP", "CWTDBG"]) {
    for (const value of [undefined, " ", 0, -1, NaN, Infinity]) {
      assert.equal(getXiziPreviewGeometry(template, { ...context, [name]: value }), null, `${name}=${value}`);
    }
  }
  assert.equal(getXiziPreviewGeometry(template, { ...context, $CWTLOC: "" }), null);
  assert.equal(getXiziPreviewGeometry(template, { ...context, $CWTLOC: "99" }), null);
  assert.equal(getXiziPreviewGeometry(template, { ...context, $HAND: "unknown" }), null);
  assert.equal(getXiziPreviewGeometry(template, { ...context, NBENT: 3 }), null);
  assert.equal(getXiziPreviewGeometry(template, { ...context, NBENT: 1.5 }), null);
});

test("recalculates cabin size and door axes from native XIZI edits", () => {
  const template = templates.find(item => item.id === "un_victor_mrl");
  const values = resolveDrawingConfigurationValues({ parameters: {
    $CARTYPE_MENU: "13W / 1000 / 1600×1400", HL6: 1500, DOP: 100,
    $CWTLOC: "24", NBENT: 2
  } }, template);
  const geometry = getXiziPreviewGeometry(template, values);
  assert.deepEqual([geometry.cabinWidth, geometry.cabinDepth], [1600, 1400]);
  assert.equal(geometry.cabinAxisX, 1600);
  assert.equal(geometry.doorAxisX, 1500);
  assert.equal(geometry.hand, "LEFT");
  assert.equal(geometry.doors.length, 2);
  assert.equal(geometry.doors[1].y, values.tol);
  assert.equal(geometry.cwtAxisX, values.HW1 + values.y_cwt);
  assert.equal(geometry.cwtAxisY, values.HD - values.l_hd1 - values.x_cwt);

  const oversize = getXiziPreviewGeometry(template, { ...values, cwt_dbg: values.HD + 100 });
  assert.ok(oversize.cwtRect.y < 0, "retain out-of-shaft geometry for collision rendering");
});

test("does not classify unrelated templates", () => {
  assert.equal(isXiziPreviewTemplate({ id: "lehy_pro" }), false);
  assert.equal(getXiziPreviewGeometry({ id: "unknown" }, {}), null);
});


test("uses native W_CWT widths and main-document counterweight axes", () => {
  for (const [id, expectedThickness] of [["un_victor_r",150], ["un_victor_mrl",152], ["un_victor_mrl_t",152]]) {
    const template = templates.find(item => item.id === id);
    const values = resolveDrawingConfigurationValues({parameters:{}}, template);
    const geometry = getXiziPreviewGeometry(template, values);
    assert.equal(geometry.cwtThickness, expectedThickness);
    assert.equal(geometry.nativeThickness, expectedThickness);
    const fromFront = id === "un_victor_r" ? values.tol+values.ldt+30+values.cdt+values.df : values.l_hd1;
    assert.equal(geometry.cwtAxisY, values.HD-fromFront-values.x_cwt);
    assert.equal(geometry.carDoors[0].height, values.cdt);
    assert.equal(geometry.doors[0].height, values.ldt);
    const shifted = getXiziPreviewGeometry(template, {...values, x_cwt:values.x_cwt+80, y_cwt:values.y_cwt+50});
    assert.equal(shifted.cwtAxisY, geometry.cwtAxisY-80);
    assert.notEqual(shifted.cwtAxisX, geometry.cwtAxisX);
  }
  const template = templates.find(item => item.id === "un_victor_r");
  const values = resolveDrawingConfigurationValues({parameters:{}}, template);
  assert.equal(getXiziPreviewGeometry(template, {...values,$CARTYPE:"05WS"}).cwtThickness,110);
  assert.equal(getXiziPreviewGeometry(template, {...values,$CWT:"WSAFE"}).cwtThickness,156);
  assert.equal(getXiziPreviewGeometry(template, {...values,x_cwt:undefined}),null);
  assert.equal(getXiziPreviewGeometry(template, {...values,df:undefined}),null);
});


test("rejects unavailable counterweight distances without rejecting zero axes or signed DOP", () => {
  const template = templates.find(item => item.id === "un_victor_r");
  const values = resolveDrawingConfigurationValues({parameters:{}},template);
  for (const name of ["x_cwt","y_cwt","df"]) assert.equal(getXiziPreviewGeometry(template,{...values,[name]:-1}),null);
  assert.ok(getXiziPreviewGeometry(template,{...values,x_cwt:0,y_cwt:0,DOP:-1,HW1:0,HL6:0}));
});
