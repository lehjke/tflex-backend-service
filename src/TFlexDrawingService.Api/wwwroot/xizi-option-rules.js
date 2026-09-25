export function xiziArdCode(capacity, speed) {
  if (Number(capacity) <= 1050) return Number(speed) <= 1.75 ? "ARD_15" : Number(speed) <= 2 ? "ARD_22" : "ARD_37";
  return Number(speed) <= 1.75 ? "ARD_22_2" : "ARD_37_2";
}

export const XIZI_TEMPLATE_BY_SERIES = Object.freeze({
  "UN-Victor MRL": "un_victor_mrl",
  "UN-Victor MRL(T)": "un_victor_mrl_t",
  "UN-Victor R": "un_victor_r"
});

export function xiziTemplateForSeries(series, templatesById) {
  const templateId = XIZI_TEMPLATE_BY_SERIES[series];
  return templateId ? templatesById?.get(templateId) || null : null;
}

export function xiziModelForTemplateId(templateId) {
  return ({ un_victor_mrl: "UN-Victor MRL", un_victor_mrl_t: "MRL-T", un_victor_r: "UN-Victor R" })[templateId] || null;
}

export function xiziValidationRuleFields(template) {
  const mapping = { K: "OH", S: "PD", HW: "AH", WTW: "BH", HD: "BH", $R: "TR", $N: "stops", CH: "HL", OP: "JJ", OPH: "HH", $OPH_CH: "HH", $CARTYPE_MENU: ["AA", "BB"], DOP: "Door Offset", $CWTLOC_MENU: "Counterweight Location", $CWTLOC: "Counterweight Location", $CWT_MENU: "CWT Safety Gear", $HAND: "Door Opening", NBENT_MENU: "Doors", V_MENU: "Speed" };
  return Object.fromEntries((template?.validationRules || [])
    .map(rule => [rule.name, (rule.fieldNames || []).flatMap(field => mapping[field] || field)]));
}

export function xiziTemplateRestrictionFields(template) {
  const parameters = template?.parameters || [];
  const findParameter = (...names) => names.map(name => parameters.find(parameter => parameter.name === name)).find(Boolean);
  const doorAxis = findParameter("HL6");
  const doorOffset = findParameter("DOP");
  const counterweight = findParameter("$CWTLOC_MENU", "$CWTLOC");
  return {
    doorAxisDefault: doorAxis?.defaultValue ?? "",
    doorOffsetDefault: doorOffset?.defaultValue ?? 350,
    doorOffsetOptions: [],
    counterweightName: counterweight?.name || "$CWTLOC",
    counterweightDefault: counterweight?.defaultValue ?? "",
    counterweightOptions: counterweight?.allowedValues?.length ? counterweight.allowedValues : [counterweight?.defaultValue].filter(value => value !== undefined),
    counterweightLabels: counterweight?.allowedValueLabels || {},
    counterweightIsMenu: Boolean(counterweight?.allowedValues?.length),
    doorOffsetIsNumeric: Boolean(doorOffset),
  };
}

export function isXiziManualOption(code, series, capacity, speed) {
  if (/^(?:CONTAINER_|40HQ$|20GP$|AC(?:COLD|HEAT)|AC$|RCC$)/i.test(code)) return false;
  if (code === "CWT_SIDE" && series !== "UN-Victor R") return false;
  return !code.startsWith("ARD_") || code === xiziArdCode(capacity, speed);
}

export function xiziConfigurationInput(template, series, capacity, speed, values, through, doorType, cwtSafety, restrictionValues = {}) {
  const parameters = template?.parameters || [];
  const get = name => parameters.find(parameter => parameter.name === name);
  const location = String(restrictionValues.$CWTLOC ?? values.$CWTLOC ?? get("$CWTLOC_MENU")?.defaultValue ?? get("$CWTLOC")?.defaultValue ?? "");
  const car = parameters.find(p => p.name === "$CARTYPE_MENU")?.allowedValues?.find(label => {
    const match = label.match(/\/\s*(\d+)\s*\/\s*(\d+)×(\d+)(?:\s*\/\s*(Сзади|Сбоку))?\s*$/);
    if (!match || Number(match[1]) !== Number(capacity)) return false;
    const dimensionsMatch = Number(match[2]) === values.AA && Number(match[3]) === values.BB;
    const locationMatch = !match[4] || (match[4] === "Сзади" ? location === "12" : ["13", "24"].includes(location));
    return dimensionsMatch && locationMatch;
  });
  if (!car) return null;
  const input = {
    $CARTYPE_MENU: car, $V: Number(speed).toFixed(Number(speed) === 1.75 ? 2 : 1),
    $N: String(values.stops), $R: String(values.TR / 1000),
    CH: values.HL, K: values.OH, S: values.PD, HW: values.AH, WTW: values.BH, HD: values.BH, OP: values.JJ, OPH: values.HH,
    NBENT_MENU: through ? 2 : 1, $DOOR_MENU: doorType === "CO" ? "CLD" : "TLD"
  };
  for (const name of ["$CWT", "$CWT_MENU"]) if (get(name)) input[name] = cwtSafety ? "WSAFE" : "WOSAF";
  if (get("V_MENU") && (!get("V_MENU").allowedValues?.length
    || get("V_MENU").allowedValues.some(value => Number(value) === Number(speed)))) input.V_MENU = Number(speed);
  if (get("$OPH_CH")) {
    const choices = get("$OPH_CH").allowedValues || [];
    const height = Number(values.HH);
    input.$OPH_CH = choices.find(option => String(option).split("/").map(Number).includes(height)) || choices[0];
  }
  if (get("$HAND")) input.$HAND = doorType === "CO" ? "CENTR" : (location === "13" ? "RIGHT" : "LEFT");
  for (const name of ["HL6", "DOP"]) {
    const value = restrictionValues[name] ?? values[name];
    if (parameters.some(parameter => parameter.name === name)
      && value !== undefined && value !== null && String(value).trim() !== ""
      && Number.isFinite(Number(value))) {
      input[name] = Number(value);
    }
  }
  if (get("DOP") && input.DOP === undefined) input.DOP = Number(restrictionValues.DOP ?? 350);
  const counterweight = get("$CWTLOC_MENU") || get("$CWTLOC");
  if (counterweight && location !== undefined && location !== null && String(location).trim() !== "") {
    input[counterweight.name] = String(location);
  }
  return input;
}
