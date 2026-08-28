import { evaluateTFlexExpression } from "./safe-expression.js?v=20260721-validation-parity-1";

function hasValue(value) {
  return value !== null && value !== undefined && String(value).trim() !== "";
}

function toNumber(value) {
  if (value === true) return 1;
  if (value === false || !hasValue(value)) return 0;
  const number = Number(String(value).replace(",", "."));
  return Number.isFinite(number) ? number : 0;
}

function toFlagNumber(value) {
  if (typeof value === "boolean") return value ? 1 : 0;
  return ["1", "true", "да", "yes"].includes(String(value ?? "").trim().toLowerCase()) ? 1 : 0;
}

function putContextValue(context, definition, value) {
  const type = String(definition?.type || "string").toLowerCase();
  if (type === "number" || type === "integer") {
    context[definition.name] = toNumber(value);
  } else if (type === "bool" || type === "boolean") {
    context[definition.name] = toFlagNumber(value);
  } else {
    context[definition.name] = hasValue(value) ? String(value) : "";
  }
}

function isLookupMatch(expected, actual) {
  return typeof expected === "number"
    ? Number(actual) === expected
    : String(actual ?? "") === String(expected ?? "");
}

function getLookupValue(definition, context) {
  for (const row of definition?.lookupValues || []) {
    if (Object.entries(row).every(([key, expected]) =>
      key === "value" || isLookupMatch(expected, context[key]))) {
      return row.value;
    }
  }
  return undefined;
}

function applyKnownDerivedValues(context) {
  context.cwt_sg = toFlagNumber(context.$cwt_sg);
  context.dim = toFlagNumber(context.dim);
  context.load_type = context.$load_type === "Крюки" ? 1 : 2;
  context.load_mount = context.$load_type === "Крюки" && context.$load_mount === "Да" ? 1 : 0;
  context.$lip_type = context.$lop_type === "Да" ? "Нет" : (context.$lip_type_1 || "Нет");
  context.lip_type = context.$lip_type === "Да" ? 1 : 0;
  context.$A4 = context.$door_type === "ТО" ? "Нет" : (context.$A4_1 || "Нет");
  context.A4 = context.$door_type === "ТО"
    ? Math.abs(toNumber(context.AA) / 2 - (toNumber(context.JJ) / 2 + 25))
    : (context.$A4_1 === "Нет" ? 0 : toNumber(context.A4_1));
  context.$fire_rating = context.$PPP === "Да"
    ? "EI60"
    : (context.$fire_rating_1 === "Нет" ? "Без огнестойкости" : context.$fire_rating_1);
  context.$roller = toNumber(context.speed) === 3 ? "Да" : (context.$roller_1 || context.$roller || "Нет");
  context.roller = context.$roller === "Да" ? 1 : 0;
}

function findNumericVariant(parameters, prefix, numericValue) {
  return parameters.find(definition =>
    definition.name.startsWith(prefix)
    && Number(definition.name.slice(prefix.length)) === Number(numericValue));
}

export function resolveDrawingConfigurationValues(configuration, template, overrides = {}) {
  const parameters = template?.parameters || [];
  const calculatedVariables = template?.calculatedVariables || [];
  const definitions = [...parameters, ...calculatedVariables];
  const storedValues = { ...(configuration?.parameters || {}), ...overrides };
  const overriddenNames = new Set(Object.keys(overrides));
  const context = {
    Electric: { Heat: 0, Heat_Rel: 0, Regen: 0 },
    name: 0,
    level: 0,
    main: 0,
    em: 0
  };

  for (const definition of definitions) {
    const value = Object.prototype.hasOwnProperty.call(storedValues, definition.name)
      ? storedValues[definition.name]
      : definition.defaultValue;
    putContextValue(context, definition, value);
  }

  for (const [name, value] of Object.entries(storedValues)) {
    if (!definitions.some(definition => definition.name === name)) context[name] = value;
  }

  const capacity = toNumber(context.cap);
  const carTypeVariant = findNumericVariant(parameters, "$car_type_", capacity);
  if (carTypeVariant && !overriddenNames.has("$car_type")) context.$car_type = context[carTypeVariant.name];
  const speedVariant = findNumericVariant(parameters, "$speed_", capacity);
  if (speedVariant && !overriddenNames.has("speed")) context.speed = toNumber(context[speedVariant.name]);

  applyKnownDerivedValues(context);
  for (const definition of definitions) {
    if (overriddenNames.has(definition.name)) continue;
    const lookupValue = getLookupValue(definition, context);
    if (hasValue(lookupValue)) putContextValue(context, definition, lookupValue);
  }
  applyKnownDerivedValues(context);

  const expressionDefinitions = [
    ...calculatedVariables,
    ...parameters.filter(definition => definition.isReadOnly)
  ];
  for (let pass = 0; pass < 8; pass += 1) {
    for (const definition of expressionDefinitions) {
      if (overriddenNames.has(definition.name)) continue;
      const lookupValue = getLookupValue(definition, context);
      if (hasValue(lookupValue)) {
        putContextValue(context, definition, lookupValue);
        continue;
      }
      if (!definition.expression) continue;
      const expressionValue = evaluateTFlexExpression(definition.expression, context, {
        lookupTables: template.lookupTables
      });
      if (hasValue(expressionValue)) putContextValue(context, definition, expressionValue);
    }
    applyKnownDerivedValues(context);
  }

  return context;
}

function isValidationPassed(value) {
  if (!hasValue(value)) return false;
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return Number.isFinite(value) && value !== 0;
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    return normalized !== "" && normalized !== "0" && normalized !== "false" && normalized !== "нет";
  }
  return Boolean(value);
}

function formatValidationValue(value) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(Math.round(value * 1000) / 1000);
  }
  return hasValue(value) ? String(value) : "";
}

export function evaluateDrawingConfigurationValidation(
  configuration,
  template,
  overrides = {},
  ruleFieldNames = {}) {
  if (!template) return [];
  const context = resolveDrawingConfigurationValues(configuration, template, overrides);
  const issues = [];
  const seen = new Set();

  for (const rule of template.validationRules || []) {
    const fieldNames = ruleFieldNames[rule.name];
    if (!fieldNames) continue;
    const result = evaluateTFlexExpression(rule.expression, context, {
      lookupTables: template.lookupTables
    });
    if (isValidationPassed(result)) continue;
    const message = String(rule.message || "Параметры не проходят проверку T-FLEX.")
      .replace(/\{([^{}]+)\}/g, (_, expression) => {
        const value = evaluateTFlexExpression(expression, context, {
          lookupTables: template.lookupTables
        });
        return hasValue(value) ? formatValidationValue(value) : `{${expression}}`;
      });
    if (seen.has(message)) continue;
    seen.add(message);
    issues.push({
      name: rule.name,
      message,
      fieldNames,
      severity: String(rule.severity || "error").trim().toLowerCase() === "warning"
        ? "warning"
        : "error"
    });
  }

  return issues;
}

export function toTravelHeightMillimeters(value) {
  if (!hasValue(value)) return null;
  const numericValue = Number(String(value ?? "").replace(",", "."));
  if (!Number.isFinite(numericValue)) return null;
  return numericValue > 0 && numericValue < 1000 ? numericValue * 1000 : numericValue;
}

function findCaseInsensitiveValue(source, names) {
  if (!source) return undefined;
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(source, name)) return source[name];
    const key = Object.keys(source).find(candidate => candidate.toLowerCase() === name.toLowerCase());
    if (key) return source[key];
  }
  return undefined;
}

export function resolveDrawingDoorCount(configuration, template, resolvedValues = null) {
  const storedValues = configuration?.parameters || {};
  const values = resolvedValues || resolveDrawingConfigurationValues(configuration, template);
  const explicitDoors = findCaseInsensitiveValue(storedValues, ["Doors", "Двери", "doorCount"]);
  if (hasValue(explicitDoors)) return Math.max(0, Math.round(toNumber(explicitDoors)));

  const stops = Math.min(48, Math.max(1, Math.round(toNumber(
    findCaseInsensitiveValue(values, ["stops", "NBLD", "Stops", "Остановки"])))));
  const entrances = Math.max(1, Math.round(toNumber(
    findCaseInsensitiveValue(values, ["NE", "Entrances", "Входы"]))) || 1);
  if (entrances === 1) return stops;

  const definitions = template?.parameters || [];
  const definitionsByName = new Map(definitions.map(definition => [definition.name, definition]));
  const hasConfigurableStopDoors = definitions.some(definition =>
    /^s(?:\d{2}|_top)_(?:front|rear)_1$/i.test(definition.name));
  if (!hasConfigurableStopDoors || entrances !== 2) return stops * entrances;

  const readDoorFlag = name => {
    const stored = findCaseInsensitiveValue(storedValues, [name]);
    if (hasValue(stored)) return toFlagNumber(stored);
    return toFlagNumber(definitionsByName.get(name)?.defaultValue);
  };

  // The top front entrance is fixed in the drawing editor; only its rear
  // entrance has a toggle. All preceding stops expose both sides.
  let doors = 1 + readDoorFlag("s_top_rear_1");
  for (let index = 1; index < stops; index += 1) {
    const rowKey = `s${String(index).padStart(2, "0")}`;
    doors += readDoorFlag(`${rowKey}_front_1`);
    doors += readDoorFlag(`${rowKey}_rear_1`);
  }
  return doors;
}
