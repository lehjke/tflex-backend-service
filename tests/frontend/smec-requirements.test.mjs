import test from "node:test";
import assert from "node:assert/strict";
import { normalizeSmecRequirements } from "../../src/TFlexDrawingService.Api/wwwroot/smec-requirements.js";

test("SMEC legacy notes migrate beyond six lines, deduplicate functions and preserve unknown text", () => {
  const request = normalizeSmecRequirements({
    supplier: "SMEC", options: ["CWT Safety Gear", "Roller guide shoe"],
    specificationFields: { "Other Requirements": "1 EI60;2 CWT;3 Roller;4 UV;5 Kickplate ZDT-001;6 Handrail ZDT-501;7 Button ZDT-500;8 OH/PD;9 COP Faceplate ZDT-007 SUS-M;10 LOP Faceplate SUS-H;11 EN81;Confirm factory colour" }
  });
  assert.equal(request.specificationFields["Fire Rating"], "EI60");
  assert.equal(request.specificationFields["Kickplate Finish"], "ZDT-001");
  assert.equal(request.specificationFields["Handrail Finish"], "ZDT-501");
  assert.equal(request.specificationFields["Button Finish"], "ZDT-500");
  assert.equal(request.specificationFields["COP Faceplate"], "ZDT-001 SUS-M");
  assert.equal(request.specificationFields["Main LOP Faceplate"], "SUS-H");
  assert.equal(request.specificationFields["Other LOP Faceplate"], "SUS-H");
  assert.equal(request.specificationFields["Other Requirements"], "Confirm factory colour");
  assert.deepEqual(request.options, ["CWT Safety Gear", "Roller guide shoe", "UV", "Reduced OH/PD", "EN81"]);
  assert.deepEqual(normalizeSmecRequirements(request), request);
});

test("Clearing or changing a migrated choice stays authoritative on subsequent saves", () => {
  const request = { supplier: "SMEC", options: [], specificationFields: {
    "Fire Rating": "", "COP Faceplate": "SUS-M", "Main LOP Faceplate": "",
    "Other Requirements": "EI60; COP Faceplate ZDT-001 SUS-H; LOP Faceplate ZDT-501 SUS-H"
  } };
  const normalized = normalizeSmecRequirements(request);
  assert.equal(normalized.specificationFields["Fire Rating"], "");
  assert.equal(normalized.specificationFields["COP Faceplate"], "SUS-M");
  assert.equal(normalized.specificationFields["Main LOP Faceplate"], "");
  assert.equal(normalized.specificationFields["Other LOP Faceplate"], "ZDT-501 SUS-H");
  normalized.options = [];
  assert.deepEqual(normalizeSmecRequirements(normalized).options, []);
  assert.equal(request.specificationFields["Other Requirements"], "EI60; COP Faceplate ZDT-001 SUS-H; LOP Faceplate ZDT-501 SUS-H");
});

test("XIZI remains untouched and legacy field casing does not leave a stale duplicate", () => {
  const xizi = { supplier: "XIZI", specificationFields: { "Other Requirements": "EI60;UV" } };
  assert.equal(normalizeSmecRequirements(xizi), xizi);
  const smec = normalizeSmecRequirements({ supplier: "SMEC", specificationFields: { "other requirements": "ZPKG-050A;UV" } });
  assert.equal(smec.specificationFields["Glass Door"], "ZPKG-050");
  assert.equal(smec.specificationFields["other requirements"], undefined);
  assert.equal(smec.specificationFields["Other Requirements"], "");
});
