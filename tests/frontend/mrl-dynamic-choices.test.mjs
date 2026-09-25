import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const app = fs.readFileSync(path.join(root, "src/TFlexDrawingService.Api/wwwroot/app.js"), "utf8");
const catalog = JSON.parse(fs.readFileSync(path.join(root, "templates/templates.json"), "utf8"));
const functionSource = app.slice(app.indexOf("function getAllowedValues("), app.indexOf("function getAllowedValueLabel("));
const runtimeState = { selectedTemplate: null };
const getAllowedValues = new Function("state", "hasValue", "isLookupMatch", `${functionSource}; return getAllowedValues;`) (
  runtimeState,
  value => value !== null && value !== undefined,
  (expected, actual) => typeof expected === "number" ? Number(actual) === expected : String(actual ?? "") === String(expected ?? "")
);

test("MRL choice lists are filtered from the template lookup tables", () => {
  for (const id of ["un_victor_mrl", "un_victor_mrl_t"]) {
    const template = catalog.templates.find(item => item.id === id);
    runtimeState.selectedTemplate = template;
    const values = (name, context) => getAllowedValues({ name, allowedValues: [] }, context);
    const tableValues = (table, key, matches) => [...new Set(template.lookupTables[table]
      .filter(matches).map(row => String(row[key])))];

    assert.deepEqual(values("$V", { DL: 1000 }), tableValues("Speed", "V", row => row.DL === 1000));
    const doorwidthContext = { $CARTYPE_MENU: "13D / 1000 / 1100×2100", $DOOR: "TLD", NBENT: 1 };
    assert.deepEqual(values("OP", doorwidthContext), tableValues("Doorwidth", "OP",
      row => row.CARTYPE === "13D" && row.DOOR === "TLD" && row.NBENT === 1));
    const dopContext = { $DOOR: "TLD", CW: 950, OP: 700, NBENT: 1 };
    assert.deepEqual(values("DOP", dopContext), tableValues("Dop", "DOP",
      row => row.DOOR === "TLD" && row.CW === 950 && row.OP === 700 && row.NBENT === 1));
    assert.deepEqual(values("CH", { $CEIL: "INTEGR" }), tableValues("Carheight", "CH", row => row.CEIL === "INTEGR"));
    const height = template.lookupTables.Carheight.find(row => row.CEIL === "INTEGR").CH;
    assert.deepEqual(values("OPH", { $CEIL: "INTEGR", CH: height }), [...new Set(template.lookupTables.Doorheight.filter(row => row.CEIL === "INTEGR" && row.CH === height).map(row => String(row.OPH)))]);
  }
});

test("MRL edits keep an invalid current choice visible instead of normalizing it", () => {
  assert.match(app, /return getDisplayParameterValue\(parameter, context\);[\s\S]*?function wireParameterInput/u);
  assert.match(app, /недоступно для текущих параметров/u);
});
