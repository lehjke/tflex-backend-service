#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";

const sourceDir = process.argv[2];
const catalogPath = process.argv[3] || path.resolve("src/TFlexDrawingService.Api/Data/pricing-catalog.json");
if (!sourceDir) {
  throw new Error("Usage: sync_xizi_reference.mjs <reference-directory> [catalog-path]");
}

const referenceFiles = [
  "xizi-base-prices.js",
  "xizi-extra-rise.js",
  "xizi-doors-prices.js",
  "xizi-cabin-finish.js",
  "xizi-panels.js",
  "xizi-options.js"
];
const sandbox = { window: {} };
vm.createContext(sandbox);
for (const file of referenceFiles) {
  vm.runInContext(fs.readFileSync(path.join(sourceDir, file), "utf8"), sandbox, { filename: file });
}

const data = sandbox.window;
const catalog = JSON.parse(fs.readFileSync(catalogPath, "utf8"));
const series = Object.keys(data.XIZIPRICEDATA);
const extraRiseSeries = name => name.includes("MRL(T)") ? "UN-Victor MRL" : name;
const basePrices = [];
for (const [seriesName, speeds] of Object.entries(data.XIZIPRICEDATA)) {
  for (const [speedText, stopsMap] of Object.entries(speeds)) {
    for (const [stopsText, capacities] of Object.entries(stopsMap)) {
      for (const [capacityText, price] of Object.entries(capacities)) {
        const extra = data.EXTRARISEDATA?.[extraRiseSeries(seriesName)]?.[speedText]?.[capacityText] ?? null;
        basePrices.push({
          series: seriesName,
          capacity: Number(capacityText),
          speed: Number(speedText),
          stops: Number(stopsText),
          price,
          extraRisePerMeter: extra
        });
      }
    }
  }
}

const normalizeFinish = value => value.toLowerCase().includes("stainless") ? "AISI443" : "Painted steel";
const normalizeDoorType = value => value === "SO" ? "2S" : value;
const capacitiesFor = value => value.includes("~") ? [450, 550, 630] : [Number(value)];
const allCapacities = [...new Set(basePrices.map(item => item.capacity))];
const doors = [];
for (const [manufacturer, manufacturerData] of Object.entries(data.DOORSPRICEDATA_RAW)) {
  for (const [rawDoorType, values] of Object.entries(manufacturerData.car || {})) {
    for (const [key, surcharge] of Object.entries(values)) {
      const parts = key.split("|");
      if (parts.length !== 4) continue;
      const [capacityText, width, fireRating, finish] = parts;
      const base = values[`${width}|${fireRating}|${finish}`] ?? 0;
      for (const capacity of capacitiesFor(capacityText)) {
        doors.push({ manufacturer, part: "Car door", doorType: normalizeDoorType(rawDoorType), fireRating,
          finish: normalizeFinish(finish), capacity, floor: "-", width: Number(width), price: base + surcharge });
      }
    }
  }
  for (const [rawDoorType, values] of Object.entries(manufacturerData.through || {})) {
    for (const [key, price] of Object.entries(values)) {
      const [width, fireRating, finish] = key.split("|");
      for (const capacity of allCapacities) {
        doors.push({ manufacturer, part: "2nd door", doorType: normalizeDoorType(rawDoorType), fireRating,
          finish: normalizeFinish(finish), capacity, floor: "-", width: Number(width), price });
      }
    }
  }
  for (const [rawDoorType, values] of Object.entries(manufacturerData.landing || {})) {
    for (const [key, price] of Object.entries(values)) {
      const [capacityText, width, fireRating, finish, floor] = key.split("|");
      for (const capacity of capacitiesFor(capacityText)) {
        doors.push({ manufacturer, part: "Shaft door", doorType: normalizeDoorType(rawDoorType), fireRating,
          finish: normalizeFinish(finish), capacity, floor: floor === "1st floor" ? "First" : "Other",
          width: Number(width), price });
      }
    }
  }
}

const decorations = [];
const addFinishGroup = (category, values) => {
  for (const [code, item] of Object.entries(values || {})) {
    decorations.push({
      category,
      code,
      price: item.base_price ?? item.price ?? item.price_per_door ?? 0,
      overprice: item.price_per_100mm ?? item.price_per_floor_above_4 ?? null,
      height: item.standard_height ?? null,
      multiplier10501600: item.multiplier_1050_1600 ?? null,
      multiplier1600Plus: item.multiplier_1600_plus ?? null,
      description: item.name ?? null
    });
  }
};
addFinishGroup("Car design", data.CABINFINISHDATA_RAW.designs);
for (const [code, item] of Object.entries(data.CABINFINISHDATA_RAW.wall_materials || {})) {
  decorations.push({ category: "Car walls", code, price: item.base_price ?? 0,
    overprice: 352, height: 2400, multiplier10501600: null, multiplier1600Plus: null,
    description: item.name ?? null });
}
addFinishGroup("Ceiling", data.CABINFINISHDATA_RAW.ceilings);
addFinishGroup("Floor", data.CABINFINISHDATA_RAW.floors);
addFinishGroup("Mirror", data.CABINFINISHDATA_RAW.mirrors);
for (const [code, item] of Object.entries(data.CABINFINISHDATA_RAW.handrails || {})) {
  decorations.push({ category: "Handrail", code, price: item.price_small_cabin ?? 0,
    overprice: (item.price_large_cabin ?? 0) - (item.price_small_cabin ?? 0), height: null,
    multiplier10501600: null, multiplier1600Plus: null, description: null });
}
addFinishGroup("COP", data.PANELSDATA_RAW.cop);
addFinishGroup("Button", data.PANELSDATA_RAW.cop_buttons);
addFinishGroup("LOP", data.PANELSDATA_RAW.lop);
addFinishGroup("LIP", data.PANELSDATA_RAW.lip);

const mapOption = ([code, item]) => ({
  category: item.category,
  code,
  price: item.type === "fixed" ? (item.price ?? null) : null,
  description: item.name ?? null,
  type: item.type ?? null,
  formula: item.formula ?? null,
  showInKp: item.showInKP ?? null
});
const optionEntries = Object.entries(data.OPTIONSDATA_RAW);
const options = optionEntries.filter(([, item]) => item.category !== "russia").map(mapOption);
const localRequirements = optionEntries.filter(([, item]) => item.category === "russia").map(mapOption);

const choices = new Map((catalog.xizi.choiceGroups || []).map(group => [group.name, group]));
const setChoices = (name, values) => {
  const group = choices.get(name) || { name, sourceSheet: "Калькулятор_NEW", cells: [], options: [] };
  group.options = values;
  choices.set(name, group);
};
setChoices("Model", ["UN-Victor R", "UN-Victior MRL", "MRL-T", "G3"]);
setChoices("Cabin Design", ["U-CR126-BASE", ...Object.keys(data.CABINFINISHDATA_RAW.designs).filter(code => code !== "U-CR126")]);
setChoices("Car Wall Material", Object.keys(data.CABINFINISHDATA_RAW.wall_materials));
setChoices("Ceiling", Object.keys(data.CABINFINISHDATA_RAW.ceilings));
setChoices("Floor", Object.keys(data.CABINFINISHDATA_RAW.floors));
setChoices("Mirror Height", Object.keys(data.CABINFINISHDATA_RAW.mirrors));
setChoices("Handrail", Object.keys(data.CABINFINISHDATA_RAW.handrails));
setChoices("COP", Object.keys(data.PANELSDATA_RAW.cop));
setChoices("COP Button", Object.keys(data.PANELSDATA_RAW.cop_buttons));
setChoices("LOP", Object.keys(data.PANELSDATA_RAW.lop));
setChoices("LIP", Object.keys(data.PANELSDATA_RAW.lip));

catalog.generatedAt = new Date().toISOString();
catalog.xizi.series = series;
catalog.xizi.basePrices = basePrices;
catalog.xizi.doors = doors;
catalog.xizi.decorations = decorations;
catalog.xizi.options = options;
catalog.xizi.localRequirements = localRequirements;
catalog.xizi.choiceGroups = [...choices.values()];
fs.writeFileSync(catalogPath, `${JSON.stringify(catalog, null, 2)}\n`);
console.log(JSON.stringify({ series: series.length, basePrices: basePrices.length, doors: doors.length,
  decorations: decorations.length, options: options.length, localRequirements: localRequirements.length }));
