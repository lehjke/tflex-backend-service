export const smecRequirementControls = [
  ["Fire Rating", "smecFireRatingSelect"],
  ["Glass Door", "smecGlassDoorSelect"],
  ["Kickplate Finish", "smecKickplateFinishSelect"],
  ["Handrail Finish", "smecHandrailFinishSelect"],
  ["Button Finish", "smecButtonFinishSelect"],
  ["COP Faceplate", "smecCopFaceplateSelect"],
  ["Main LOP Faceplate", "smecMainLopFaceplateSelect"],
  ["Other LOP Faceplate", "smecOtherLopFaceplateSelect"]
];

export const smecRequirementFunctions = [
  { code: "UV", description: "Ультрафиолетовая дезинфекция: 8 ламп." },
  { code: "Reduced OH/PD", description: "Исполнение с уменьшенным оголовком / приямком." },
  { code: "EN81", description: "Дополнительное требование EN81. Стоимость требует подтверждения SMEC." }
];

// Keep old projects editable. Recognized text becomes structured selections;
// unknown notes stay in the saved request and are surfaced by calculation warnings.
export function normalizeSmecRequirements(request) {
  if (String(request.supplier).toUpperCase() !== "SMEC") return request;
  const fields = { ...request.specificationFields };
  const originalKeys = new Set(Object.keys(fields).map(key => key.toLowerCase()));
  const options = [...new Set(request.options || [])];
  const get = key => fields[Object.keys(fields).find(candidate => candidate.toLowerCase() === key.toLowerCase())] || "";
  const set = (key, value) => { if (!originalKeys.has(key.toLowerCase())) fields[key] = value; };
  const option = code => { if (!options.some(value => value.toLowerCase() === code.toLowerCase())) options.push(code); };
  const remaining = [];
  for (const line of get("Other Requirements").split(/[\r\n;]/).map(value => value.trim()).filter(Boolean)) {
    const value = line.replace(/^\s*\d+[.)]?\s+/, "");
    const fire = value.match(/\b(EI?(?:30|60|120))\b/i);
    const glass = value.match(/\bZPKG-(050|150|200)A?\b/i);
    let finish = value.match(/ZDT-\d{3}/i)?.[0].toUpperCase() || "";
    const material = /SUS-M/i.test(value) ? "SUS-M" : "SUS-H";
    if (fire) set("Fire Rating", fire[0].toUpperCase());
    else if (glass) set("Glass Door", `ZPKG-${glass[1]}`);
    else if (/CWT/i.test(value)) option("CWT Safety Gear");
    else if (/oller/i.test(value)) option("Roller guide shoe");
    else if (/UV/i.test(value)) option("UV");
    else if (/ickplate/i.test(value)) set("Kickplate Finish", finish || material);
    else if (/andrail/i.test(value)) set("Handrail Finish", finish || material);
    else if (/utton/i.test(value)) set("Button Finish", finish || material);
    else if (/OH\/PD|PD\/OH/i.test(value)) option("Reduced OH/PD");
    else if (/aceplate/i.test(value)) {
      if (finish === "ZDT-007") finish = "ZDT-001";
      const code = finish ? `${finish} ${material}` : material;
      if (/COP/i.test(value)) set("COP Faceplate", code);
      else {
        set("Main LOP Faceplate", code);
        set("Other LOP Faceplate", code);
      }
    } else if (/EN81/i.test(value)) option("EN81");
    else remaining.push(line);
  }
  const legacyKey = Object.keys(fields).find(key => key.toLowerCase() === "other requirements");
  if (legacyKey) delete fields[legacyKey];
  fields["Other Requirements"] = remaining.join("\n");
  return { ...request, specificationFields: fields, options };
}
