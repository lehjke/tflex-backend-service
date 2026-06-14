const state = {
  templates: [],
  selectedTemplate: null,
  parameterValues: {},
  validationFieldNames: new Set(),
  activeJobId: null,
  pollTimer: null,
  pendingRenderFrame: null,
  pendingFocusTarget: null,
  activeParameterCategory: null,
  showAllParameters: true,
  currentUser: null,
  projects: [],
  configurations: [],
  editingConfigurationId: null
};

const guestMain = document.querySelector("#guestMain");
const appMain = document.querySelector("#appMain");
const loginForm = document.querySelector("#loginForm");
const loginUserName = document.querySelector("#loginUserName");
const loginPassword = document.querySelector("#loginPassword");
const registerForm = document.querySelector("#registerForm");
const registerUserName = document.querySelector("#registerUserName");
const registerDisplayName = document.querySelector("#registerDisplayName");
const registerPassword = document.querySelector("#registerPassword");
const registerStatus = document.querySelector("#registerStatus");
const userPanel = document.querySelector("#userPanel");
const currentUserName = document.querySelector("#currentUserName");
const logoutButton = document.querySelector("#logoutButton");
const createTopButton = document.querySelector("#createTopButton");
const templateSelect = document.querySelector("#templateSelect");
const formatSelect = document.querySelector("#formatSelect");
const parametersForm = document.querySelector("#parametersForm");
const submitButton = document.querySelector("#submitButton");
const statusPanel = document.querySelector("#statusPanel");
const jobsTableBody = document.querySelector("#jobsTableBody");
const validationPanel = document.querySelector("#validationPanel");
const parameterTabs = document.querySelector("#parameterTabs");
const showAllParametersToggle = document.querySelector("#showAllParametersToggle");
const parameterReadyBanner = document.querySelector("#parameterReadyBanner");
const shaftPreviewSubtitle = document.querySelector("#shaftPreviewSubtitle");
const shaftPreviewUnavailable = document.querySelector("#shaftPreviewUnavailable");
const shaftPreviewContent = document.querySelector("#shaftPreviewContent");
const shaftPreviewCanvas = document.querySelector("#shaftPreviewCanvas");
const shaftPreviewMetrics = document.querySelector("#shaftPreviewMetrics");
const projectSelect = document.querySelector("#projectSelect");
const saveConfigurationButton = document.querySelector("#saveConfigurationButton");
const configurationNamePreview = document.querySelector("#configurationNamePreview");

const STOP_CONTROL_NAMES = new Set(["main", "name", "level", "main_floor"]);
const FRONTEND_HIDDEN_PARAMETER_NAMES = new Set(["$ver"]);
const CONFIGURATION_NAME_PARAMETER_NAMES = ["$Oboznach"];
const STOP_GROUP_LABEL = "\u041e\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0438";
const STOP_LOBBY_LABEL = "\u041b\u043e\u0431\u0431\u0438";
const STOP_FLOOR_LABEL = "\u042d\u0442\u0430\u0436";
const STOP_LEVEL_LABEL = "\u041e\u0442\u043c.";
const STOP_FRONT_LABEL = "\u041f\u0435\u0440.";
const STOP_REAR_LABEL = "\u0417\u0430\u0434.";
const STOP_AO_LABEL = "AO";
const DEFAULT_PARAMETER_CATEGORY = "\u0420\u0430\u0437\u043d\u043e\u0435";
const CATEGORY_LABEL_OVERRIDES = new Map([
  ["LOP", "Панель вызова"],
  ["LIP", "Этажный указатель"]
]);
const FIELD_LABEL_OVERRIDES = new Map([
  ["AA", "Ширина кабины"],
  ["BB", "Глубина кабины"],
  ["HL", "Высота кабины (в чистоте)"],
  ["JJ", "Ширина дверей (в чистоте)"],
  ["EI", "Огнестойкость дверей"],
  ["A4", "Эксцентриситет дверей"],
  ["HH", "Высота дверей (в чистоте)"],
  ["WW", "Ширина противовеса"],
  ["WG", "Длина противовеса"],
  ["AH", "Ширина шахты"],
  ["BH", "Глубина шахты"],
  ["A3", "Расстояние от оси кабины до стенки без противовеса"],
  ["OH", "Оголовок"],
  ["PD", "Приямок"]
]);
const CATEGORY_DISPLAY_ORDER = [
  "Инфо о проекте",
  "Общие параметры",
  "Характеристики",
  "Параметры эскалатора",
  "Кабина",
  "Двери",
  "Противовес",
  "Шахта",
  "Ферма",
  "Опции",
  "Опоры",
  "Приямок",
  "Этаж",
  "Входные площадки",
  "Рамка",
  "Приямок и оголовок",
  "Основная надпись",
  "Вертикальный разрез",
  STOP_GROUP_LABEL,
  "Крюки",
  "Панель вызова",
  "Этажный указатель",
  "Отделка"
];
const SHAFT_PREVIEW_SUPPORTED_TEMPLATE_PREFIXES = [
  "lehy_l_pro",
  "lehy_pro"
];

function isAuthenticated() {
  return Boolean(state.currentUser?.isAuthenticated);
}

function canCreateJobs() {
  const roles = state.currentUser?.roles || [];
  return roles.includes("Admin") || roles.includes("Operator");
}

function getTemplateLabel(templateId) {
  const template = state.templates.find(item => item.id === templateId || item.code === templateId);
  return template ? (template.name || template.code || template.id) : templateId;
}

function getConfigurationName(parameters) {
  for (const name of CONFIGURATION_NAME_PARAMETER_NAMES) {
    const value = parameters?.[name] ?? getParameterValueByName(name);
    if (hasValue(value) && String(value).trim()) return String(value).trim();
  }

  const titleParameter = state.selectedTemplate?.parameters
    ?.find(parameter => (parameter.displayName || "").includes("№"));
  if (titleParameter) {
    const value = parameters?.[titleParameter.name] ?? getParameterValue(titleParameter);
    if (hasValue(value) && String(value).trim()) return String(value).trim();
  }

  return state.selectedTemplate?.name || state.selectedTemplate?.code || state.selectedTemplate?.id || "Конфигурация";
}

function updateConfigurationNamePreview(parameters = state.parameterValues) {
  if (!configurationNamePreview) return;
  if (!state.selectedTemplate) {
    configurationNamePreview.value = "-";
    return;
  }

  const oboznach = parameters?.$Oboznach ?? getParameterValueByName("$Oboznach");
  if (hasValue(oboznach) && String(oboznach).trim()) {
    configurationNamePreview.value = `$Oboznach: ${String(oboznach).trim()}`;
    return;
  }

  configurationNamePreview.value = getConfigurationName(parameters);
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}

function updateAuthView() {
  const authenticated = isAuthenticated();
  guestMain.hidden = authenticated;
  loginForm.hidden = authenticated;
  userPanel.hidden = !authenticated;
  appMain.hidden = !authenticated;
  createTopButton.hidden = !authenticated || !canCreateJobs();
  submitButton.hidden = authenticated && !canCreateJobs();
  saveConfigurationButton.hidden = !authenticated;

  if (authenticated) {
    currentUserName.textContent = state.currentUser.displayName || state.currentUser.userName;
  } else {
    currentUserName.textContent = "";
  }
}

async function apiFetch(url, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers || {});
  if (method !== "GET" && method !== "HEAD") {
    headers.set("X-TFlex-Requested-With", "fetch");
  }

  const response = await fetch(url, {
    credentials: "same-origin",
    ...options,
    headers
  });

  if (response.status === 401) {
    state.currentUser = null;
    updateAuthView();
  }

  return response;
}

async function readProblem(response, fallback) {
  try {
    const problem = await response.json();
    return problem.errors?.request || [problem.detail || problem.title || fallback];
  } catch {
    return [fallback];
  }
}

function formatDate(value) {
  if (!value) return "";
  return new Intl.DateTimeFormat("ru-RU", {
    dateStyle: "short",
    timeStyle: "medium"
  }).format(new Date(value));
}

function getParameterType(parameter) {
  return (parameter.type || "string").toLowerCase();
}

function hasValue(value) {
  return value !== null && value !== undefined;
}

function getDefaultValue(parameter) {
  if (hasValue(parameter.defaultValue)) return parameter.defaultValue;
  const type = getParameterType(parameter);
  return type === "bool" || type === "boolean" ? false : "";
}

function readInputValue(input, parameter) {
  const type = getParameterType(parameter);
  if (type === "bool" || type === "boolean") return input.checked;
  if (type === "integer") return input.value === "" ? null : Number.parseInt(input.value, 10);
  if (type === "number") return input.value === "" ? null : Number(input.value);
  return input.value;
}

function setInputValue(input, parameter, value) {
  if (input.type === "radio") {
    input.checked = String(value) === input.value;
    return;
  }

  const type = getParameterType(parameter);
  if (type === "bool" || type === "boolean") {
    input.checked = Boolean(value);
    return;
  }

  input.value = hasValue(value) ? value : "";
}

function rememberCurrentValues() {
  if (!state.selectedTemplate) return;

  for (const input of parametersForm.querySelectorAll("input, select, textarea")) {
    if (input.type === "radio" && !input.checked) continue;

    const name = input.dataset.parameterName || input.name;
    const definition = state.selectedTemplate.parameters.find(parameter => parameter.name === name);
    if (!definition) continue;
    state.parameterValues[name] = readInputValue(input, definition);
  }
}

function getParameterValue(parameter) {
  return Object.prototype.hasOwnProperty.call(state.parameterValues, parameter.name)
    ? state.parameterValues[parameter.name]
    : getDefaultValue(parameter);
}

function getParameterDefinition(name) {
  return getTemplateDefinitions().find(parameter => parameter.name === name) || null;
}

function getParameterValueByName(name) {
  const parameter = getParameterDefinition(name);
  return parameter ? getParameterValue(parameter) : undefined;
}

function isLookupMatch(expected, actual) {
  if (typeof expected === "number") return Number(actual) === expected;
  return String(actual ?? "") === String(expected ?? "");
}

function tflexSwitch(selector, ...pairs) {
  for (let index = 0; index < pairs.length - 1; index += 2) {
    if (isLookupMatch(pairs[index], selector)) return pairs[index + 1];
  }

  return pairs.length % 2 === 1 ? pairs[pairs.length - 1] : undefined;
}

function tflexAtof(value) {
  return toNumber(value);
}

function tflexVal(value, lookupValue) {
  return hasValue(lookupValue) ? toNumber(lookupValue) : toNumber(value);
}

function tflexTan(value) {
  return Math.tan(toNumber(value) * Math.PI / 180);
}

function tflexCos(value) {
  return Math.cos(toNumber(value) * Math.PI / 180);
}

function tflexSin(value) {
  return Math.sin(toNumber(value) * Math.PI / 180);
}

function tflexCeil(value, step = 1) {
  const number = toNumber(value);
  const increment = Math.abs(toNumber(step)) || 1;
  return Math.ceil(number / increment) * increment;
}

function tflexTpart(value, start, length) {
  const text = String(value ?? "");
  const from = Math.max(0, Math.trunc(toNumber(start)) - 1);
  const count = Math.trunc(toNumber(length));
  return (count > 0 ? text.slice(from, from + count) : text.slice(from)).trim();
}

function tflexFtoa(value) {
  return hasValue(value) ? String(value) : "";
}

function tflexLtot(value, step = 0.001, sign = 1, precision = 3) {
  const number = toNumber(value);
  const decimals = Math.max(0, Math.trunc(toNumber(precision)));
  return number.toFixed(decimals);
}

function tflexGet() {
  return 0;
}

function tflexGetv() {
  return 0;
}

function tflexTgetv(part) {
  const now = new Date();
  const key = String(part || "").trim().toUpperCase();
  const values = {
    YEAR: now.getFullYear(),
    MONTH: now.getMonth() + 1,
    DAY: now.getDate(),
    HOUR: now.getHours(),
    MINUTE: now.getMinutes(),
    SECOND: now.getSeconds()
  };

  return String(values[key] ?? "");
}

function tflexSelect(...pairs) {
  for (let index = 0; index < pairs.length - 1; index += 2) {
    if (Boolean(pairs[index])) return pairs[index + 1];
  }

  return pairs.length % 2 === 1 ? pairs[pairs.length - 1] : undefined;
}

function tflexRound(value, step = 1) {
  const number = toNumber(value);
  const increment = Math.abs(toNumber(step)) || 1;
  return Math.round(number / increment) * increment;
}

function tflexError() {
  return 0;
}

function tflexWarn() {
  return 1;
}

function tflexFind(tableName, fieldName, predicate) {
  const rows = state.selectedTemplate?.lookupTables?.[tableName] || [];

  for (const row of rows) {
    try {
      if (predicate(row)) return row[fieldName];
    } catch {
      return undefined;
    }
  }

  return undefined;
}

function splitTopLevelComma(value) {
  let depth = 0;
  let quote = null;

  for (let index = 0; index < value.length; index += 1) {
    const char = value[index];
    const previous = value[index - 1];

    if (quote) {
      if (char === quote && previous !== "\\") quote = null;
      continue;
    }

    if (char === "\"" || char === "'") {
      quote = char;
    } else if (char === "(") {
      depth += 1;
    } else if (char === ")") {
      depth -= 1;
    } else if (char === "," && depth === 0) {
      return index;
    }
  }

  return -1;
}

function translateFindExpressions(expression) {
  let result = "";
  let index = 0;

  while (index < expression.length) {
    const findIndex = expression.indexOf("find(", index);
    if (findIndex < 0) {
      result += expression.slice(index);
      break;
    }

    result += expression.slice(index, findIndex);

    let depth = 1;
    let quote = null;
    let cursor = findIndex + "find(".length;

    for (; cursor < expression.length; cursor += 1) {
      const char = expression[cursor];
      const previous = expression[cursor - 1];

      if (quote) {
        if (char === quote && previous !== "\\") quote = null;
        continue;
      }

      if (char === "\"" || char === "'") {
        quote = char;
      } else if (char === "(") {
        depth += 1;
      } else if (char === ")") {
        depth -= 1;
        if (depth === 0) break;
      }
    }

    if (depth !== 0) {
      result += expression.slice(findIndex);
      break;
    }

    const inner = expression.slice(findIndex + "find(".length, cursor);
    const commaIndex = splitTopLevelComma(inner);
    const target = inner.slice(0, commaIndex).trim();
    const condition = inner.slice(commaIndex + 1).trim();
    const match = /^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(target);

    if (commaIndex < 0 || !match) {
      result += expression.slice(findIndex, cursor + 1);
    } else {
      const [, tableName, fieldName] = match;
      const rowCondition = condition.replace(
        new RegExp(`\\b${tableName}\\.([A-Za-z_][A-Za-z0-9_]*)`, "g"),
        "row.$1");
      result += `tflexFind(${JSON.stringify(tableName)}, ${JSON.stringify(fieldName)}, row => ${rowCondition})`;
    }

    index = cursor + 1;
  }

  return result;
}

function truncateTopLevelSemicolon(expression) {
  const value = String(expression || "");
  let depth = 0;
  let quote = null;

  for (let index = 0; index < value.length; index += 1) {
    const char = value[index];
    const previous = value[index - 1];

    if (quote) {
      if (char === quote && previous !== "\\") quote = null;
      continue;
    }

    if (char === "\"" || char === "'") {
      quote = char;
    } else if (char === "(") {
      depth += 1;
    } else if (char === ")") {
      depth = Math.max(0, depth - 1);
    } else if (char === ";" && depth === 0) {
      return value.slice(0, index).trim();
    }
  }

  return value;
}

function translateTFlexExpression(expression) {
  return translateFindExpressions(truncateTopLevelSemicolon(expression))
    .replace(/\b(?:ERROR|error)\s*\([^)]*\)/g, "tflexError()")
    .replace(/\b(?:WARN|warn)\s*\([^)]*\)/g, "tflexWarn()")
    .replace(/\*\*/g, "*")
    .replace(/\^/g, "**")
    .replace(/\b(?:switch|SWITCH)\s*\(/g, "tflexSwitch(")
    .replace(/\b(?:select|SELECT)\s*\(/g, "tflexSelect(")
    .replace(/\b(?:atof|ATOF)\s*\(/g, "tflexAtof(")
    .replace(/\b(?:val|VAL)\s*\(/g, "tflexVal(")
    .replace(/\b(?:TPART|tpart)\s*\(/g, "tflexTpart(")
    .replace(/\b(?:FTOA2|FTOA|ftoa2|ftoa)\s*\(/g, "tflexFtoa(")
    .replace(/\b(?:ltot|LTOT)\s*\(/g, "tflexLtot(")
    .replace(/\b(?:tgetv|TGETV)\s*\(/g, "tflexTgetv(")
    .replace(/\b(?:getv|GETV)\s*\(/g, "tflexGetv(")
    .replace(/\b(?:GET|get)\s*\(/g, "tflexGet(")
    .replace(/\b(?:max|MAX)\s*\(/g, "Math.max(")
    .replace(/\b(?:min|MIN)\s*\(/g, "Math.min(")
    .replace(/\b(?:abs|ABS)\s*\(/g, "Math.abs(")
    .replace(/\b(?:floor|FLOOR)\s*\(/g, "Math.floor(")
    .replace(/\b(?:ceil|CEIL)\s*\(/g, "tflexCeil(")
    .replace(/\b(?:tan|TAN)\s*\(/g, "tflexTan(")
    .replace(/\b(?:cos|COS)\s*\(/g, "tflexCos(")
    .replace(/\b(?:sin|SIN)\s*\(/g, "tflexSin(")
    .replace(/\b(?:round|ROUND)\s*\(/g, "tflexRound(");
}

function evaluateFormulaExpression(expression, context) {
  if (!expression) return undefined;
  const translated = translateTFlexExpression(expression);

  try {
    return Function(
      "context",
      "tflexSwitch",
      "tflexSelect",
      "tflexAtof",
      "tflexVal",
      "tflexRound",
      "tflexCeil",
      "tflexTan",
      "tflexCos",
      "tflexSin",
      "tflexTpart",
      "tflexFtoa",
      "tflexLtot",
      "tflexGet",
      "tflexGetv",
      "tflexTgetv",
      "tflexError",
      "tflexWarn",
      "tflexFind",
      `with (context) { return (${translated}); }`
    )(
      context,
      tflexSwitch,
      tflexSelect,
      tflexAtof,
      tflexVal,
      tflexRound,
      tflexCeil,
      tflexTan,
      tflexCos,
      tflexSin,
      tflexTpart,
      tflexFtoa,
      tflexLtot,
      tflexGet,
      tflexGetv,
      tflexTgetv,
      tflexError,
      tflexWarn,
      tflexFind);
  } catch {
    return undefined;
  }
}

function getLookupValue(parameter, context) {
  if (!parameter.lookupValues?.length) return undefined;

  for (const row of parameter.lookupValues) {
    let matches = true;
    for (const [key, expected] of Object.entries(row)) {
      if (key === "value") continue;

      if (!isLookupMatch(expected, context[key])) {
        matches = false;
        break;
      }
    }

    if (matches) return row.value;
  }

  return undefined;
}

function getDisplayParameterValue(parameter, context) {
  const lookupValue = context ? getLookupValue(parameter, context) : undefined;
  if (hasValue(lookupValue)) return lookupValue;

  if (context && parameter.isReadOnly && parameter.expression) {
    const expressionValue = evaluateFormulaExpression(parameter.expression, context);
    if (hasValue(expressionValue)) return expressionValue;
  }

  return getParameterValue(parameter);
}

function getAllowedValues(parameter, context) {
  if (parameter.name.startsWith("$car_type_") && context) {
    const aa = getParameterDefinition("AA");
    const values = aa?.lookupValues
      ?.filter(row => isLookupMatch(row.cap, context.cap))
      .map(row => row.$car_type)
      .filter(hasValue) || [];

    if (values.length > 0) {
      if (parameter.allowedValues?.includes("PXX")) values.push("PXX");
      return [...new Set(values.map(value => String(value)))];
    }
  }

  return parameter.allowedValues || [];
}

function getAllowedValueLabel(parameter, value) {
  const key = String(value);
  return parameter.allowedValueLabels?.[key] || key;
}

function normalizeValueForAllowedList(parameter, value, context) {
  const allowedValues = getAllowedValues(parameter, context);
  if (allowedValues.length === 0 || !hasValue(value)) return value;
  return allowedValues.includes(String(value)) ? value : allowedValues[0];
}

function initializeParameterValues() {
  state.parameterValues = {};
  if (!state.selectedTemplate) return;

  for (const parameter of state.selectedTemplate.parameters) {
    state.parameterValues[parameter.name] = getDefaultValue(parameter);
  }
}

function getTemplateDefinitions() {
  if (!state.selectedTemplate) return [];
  return [
    ...(state.selectedTemplate.parameters || []),
    ...(state.selectedTemplate.calculatedVariables || [])
  ];
}

function toNumber(value) {
  if (value === true) return 1;
  if (value === false || value === null || value === undefined || value === "") return 0;
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function toFlagNumber(value) {
  if (typeof value === "boolean") return value ? 1 : 0;
  const text = String(value ?? "").trim().toLowerCase();
  return text === "1" || text === "true" || text === "\u0434\u0430" ? 1 : 0;
}

function findNumericVariant(prefix, numericValue) {
  if (!state.selectedTemplate) return null;

  return state.selectedTemplate.parameters.find(parameter => {
    if (!parameter.name.startsWith(prefix)) return false;
    return Number(parameter.name.slice(prefix.length)) === Number(numericValue);
  });
}

function findLevelVariant(prefix, context) {
  if (!state.selectedTemplate) return null;

  return state.selectedTemplate.parameters.find(parameter =>
    parameter.name.startsWith(prefix) && isParameterVisible(parameter, context));
}

function putContextValue(context, parameter, value) {
  const type = getParameterType(parameter);
  if (type === "number" || type === "integer") {
    context[parameter.name] = toNumber(value);
  } else if (type === "bool" || type === "boolean") {
    context[parameter.name] = toFlagNumber(value);
  } else {
    context[parameter.name] = hasValue(value) ? String(value) : "";
  }
}

function applyKnownDerivedValues(context) {
  context.cwt_sg = toFlagNumber(context.$cwt_sg);
  context.dim = toFlagNumber(context.dim);
  context.load_type = context.$load_type === "\u041a\u0440\u044e\u043a\u0438" ? 1 : 2;
  context.load_mount = context.$load_type === "\u041a\u0440\u044e\u043a\u0438" && context.$load_mount === "\u0414\u0430" ? 1 : 0;
  context.$lip_type = context.$lop_type === "\u0414\u0430" ? "\u041d\u0435\u0442" : (context.$lip_type_1 || "\u041d\u0435\u0442");
  context.lip_type = context.$lip_type === "\u0414\u0430" ? 1 : 0;
  context.$A4 = context.$door_type === "\u0422\u041e" ? "\u041d\u0435\u0442" : (context.$A4_1 || "\u041d\u0435\u0442");

  if (context.$door_type === "\u0422\u041e") {
    context.A4 = Math.abs(toNumber(context.AA) / 2 - (toNumber(context.JJ) / 2 + 25));
  } else {
    context.A4 = context.$A4_1 === "\u041d\u0435\u0442" ? 0 : toNumber(context.A4_1);
  }

  context.$fire_rating = context.$PPP === "\u0414\u0430"
    ? "EI60"
    : (context.$fire_rating_1 === "\u041d\u0435\u0442" ? "\u0411\u0435\u0437 \u043e\u0433\u043d\u0435\u0441\u0442\u043e\u0439\u043a\u043e\u0441\u0442\u0438" : context.$fire_rating_1);
  context.$roller = toNumber(context.speed) === 3 ? "\u0414\u0430" : (context.$roller_1 || context.$roller || "\u041d\u0435\u0442");
  context.roller = context.$roller === "\u0414\u0430" ? 1 : 0;
}

function applyReadOnlyExpressions(context) {
  if (!state.selectedTemplate) return;

  const calculatedVariables = state.selectedTemplate.calculatedVariables || [];
  const calculatedNames = new Set(calculatedVariables.map(parameter => parameter.name));
  const definitions = [
    ...calculatedVariables,
    ...state.selectedTemplate.parameters.filter(parameter => parameter.isReadOnly)
  ];

  for (let pass = 0; pass < 8; pass += 1) {
    for (const parameter of definitions) {
      if (!parameter.expression) continue;
      const value = getLookupValue(parameter, context);
      if (hasValue(value)) {
        putContextValue(context, parameter, value);
        continue;
      }

      const expressionValue = evaluateFormulaExpression(parameter.expression, context);
      if (hasValue(expressionValue)) putContextValue(context, parameter, expressionValue);
    }

    applyKnownDerivedValues(context);
  }

  for (const parameter of state.selectedTemplate.parameters) {
    if (!parameter.isReadOnly) continue;
    if (Object.prototype.hasOwnProperty.call(context, parameter.name)) {
      state.parameterValues[parameter.name] = context[parameter.name];
    }
  }

  for (const name of calculatedNames) {
    delete state.parameterValues[name];
  }
}

function buildLevelContext() {
  const context = {
    Electric: {
      Heat: 0,
      Heat_Rel: 0,
      Regen: 0
    },
    name: 0,
    level: 0,
    main: 0,
    em: 0
  };

  if (!state.selectedTemplate) return context;

  for (const parameter of getTemplateDefinitions()) {
    const value = getParameterValue(parameter);
    putContextValue(context, parameter, value);
  }

  const cap = toNumber(context.cap);
  const carTypeVariant = findLevelVariant("$car_type_", context) || findNumericVariant("$car_type_", cap);
  if (carTypeVariant) {
    const carTypeValue = normalizeValueForAllowedList(
      carTypeVariant,
      context[carTypeVariant.name] || getDefaultValue(carTypeVariant),
      context);
    context[carTypeVariant.name] = carTypeValue;
    context.$car_type = carTypeValue;
  }

  const speedVariant = findLevelVariant("$speed_", context) || findNumericVariant("$speed_", cap);
  if (speedVariant) context.speed = toNumber(context[speedVariant.name] || getDefaultValue(speedVariant));

  applyKnownDerivedValues(context);

  for (const parameter of getTemplateDefinitions()) {
    const lookupValue = getLookupValue(parameter, context);
    if (!hasValue(lookupValue)) continue;

    putContextValue(context, parameter, lookupValue);
  }

  applyKnownDerivedValues(context);
  applyReadOnlyExpressions(context);

  return context;
}

function isShaftPreviewSupportedTemplate(template = state.selectedTemplate) {
  if (!template) return false;
  const markers = [
    template.id,
    template.code,
    template.name
  ].filter(Boolean).map(value => String(value)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "_")
    .replace(/^_+|_+$/g, ""));

  return markers.some(marker =>
    SHAFT_PREVIEW_SUPPORTED_TEMPLATE_PREFIXES.some(prefix =>
      marker === prefix || marker.startsWith(`${prefix}_`)));
}

function getPreviewNumber(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  const number = toNumber(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}

function getPreviewSignedNumber(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function getPreviewFlag(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  return toFlagNumber(value);
}

function getPreviewRawValue(context, ...names) {
  for (const name of names) {
    const value = context?.[name] ?? getParameterValueByName(name);
    if (hasValue(value) && String(value).trim() !== "") return value;
  }

  return undefined;
}

function getPreviewTextValue(context, ...names) {
  const value = getPreviewRawValue(context, ...names);
  return hasValue(value) ? String(value).trim() : "";
}

function normalizePreviewToken(value) {
  return String(value || "")
    .trim()
    .toLowerCase()
    .replaceAll(" ", "")
    .replaceAll("-", "");
}

function isCenterOpeningDoor(context) {
  const opening = normalizePreviewToken(getPreviewTextValue(
    context,
    "$Opening",
    "Opening",
    "$door_type",
    "door_type",
    "$DOOR",
    "DOOR"));

  return opening === "co"
    || opening === "2co"
    || opening === "цо"
    || opening === "cld";
}

function getCounterweightPlace(context) {
  const place = normalizePreviewToken(getPreviewTextValue(context, "$s", "s", "$HAND", "HAND"));
  if (place === "справа" || place === "right" || place === "1") {
    return { label: "Справа", mirrorX: true };
  }

  return { label: "Слева", mirrorX: false };
}

function clampPreviewNumber(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function formatPreviewNumber(value) {
  if (!Number.isFinite(value)) return "-";
  return String(Math.round(value));
}

function formatPreviewMetricValue(value) {
  if (typeof value === "number") return formatPreviewNumber(value);
  return escapeHtml(value);
}

function showShaftPreviewUnavailable(message) {
  if (shaftPreviewSubtitle) shaftPreviewSubtitle.textContent = "Preview недоступен";
  if (shaftPreviewUnavailable) {
    shaftPreviewUnavailable.hidden = false;
    shaftPreviewUnavailable.textContent = message;
  }
  if (shaftPreviewContent) shaftPreviewContent.hidden = true;
}

function updateShaftPreview(context = null) {
  if (!shaftPreviewContent || !shaftPreviewCanvas || !shaftPreviewMetrics) return;

  if (!state.selectedTemplate) {
    showShaftPreviewUnavailable("Выберите шаблон, чтобы увидеть план шахты.");
    return;
  }

  if (!isShaftPreviewSupportedTemplate()) {
    showShaftPreviewUnavailable("Предпросмотр плана шахты доступен для шаблонов LEHY-L-PRO и LEHY-PRO.");
    return;
  }

  const previewContext = context || buildLevelContext();
  const ah = getPreviewNumber(previewContext, "AH");
  const bh = getPreviewNumber(previewContext, "BH");
  const aa = getPreviewNumber(previewContext, "AA");
  const bb = getPreviewNumber(previewContext, "BB");
  const jj = getPreviewNumber(previewContext, "JJ");
  const ee = getPreviewNumber(previewContext, "EE");
  const ww = getPreviewNumber(previewContext, "WW") || getPreviewNumber(previewContext, "WW_1");
  const wg = getPreviewNumber(previewContext, "WG") || getPreviewNumber(previewContext, "WG_1");
  const a1 = getPreviewNumber(previewContext, "A1");
  const a2 = getPreviewNumber(previewContext, "A2");
  const bw = getPreviewNumber(previewContext, "BW");
  const ca = getPreviewNumber(previewContext, "CA");
  const cb = getPreviewNumber(previewContext, "CB");
  const as = getPreviewNumber(previewContext, "AS") || (aa ? aa + 62 : null);
  const a6 = getPreviewNumber(previewContext, "A6");
  const b1 = getPreviewNumber(previewContext, "B1");
  const bs = getPreviewNumber(previewContext, "BS");
  const dk = getPreviewNumber(previewContext, "DK");
  const bottomGap = getPreviewNumber(previewContext, "bb");
  const cwtPlace = getCounterweightPlace(previewContext);
  const centerOpeningDoor = isCenterOpeningDoor(previewContext);
  const doorWidth = jj && centerOpeningDoor ? jj * 2 : jj;
  const lehyProSideCwt = state.selectedTemplate?.id === "lehy_pro_side_cwt";
  const carCenterX = a1 && a2
    ? a1 + a2
    : (lehyProSideCwt && cb ? cb : (ca || (ah ? ah / 2 : null)));
  const rearCwt = lehyProSideCwt
    && getPreviewFlag(previewContext, "CWT_SG") === 1;
  const sideCwtCenterX = a1
    ?? (lehyProSideCwt && ca && carCenterX ? carCenterX + ca : null)
    ?? (bw && ah ? ah - bw : null);
  const sideCwtX = sideCwtCenterX && ww ? sideCwtCenterX - ww / 2 : null;
  const rearCwtX = carCenterX && wg ? carCenterX - wg / 2 : null;
  const cwtX = rearCwt ? rearCwtX : sideCwtX;
  const cwtMetricName = rearCwt
    ? "WG"
    : (a1 ? "A1" : (lehyProSideCwt && ca ? "CA" : (bw ? "AH-BW" : "CWT X")));
  const cwtMetricValue = rearCwt
    ? wg
    : (a1 ?? (lehyProSideCwt && ca ? ca : sideCwtCenterX));
  const cwtDepth = ee
    ? ee + 150
    : (rearCwt
      ? (b1 ? Math.max(0, b1 - 15) : Math.max(0, (a6 || 138) - 33))
      : (bh && wg ? Math.max(0, (bh - wg) / 2) : null));
  const cwtY = ee && bh && wg && !rearCwt
    ? bh - cwtDepth - wg
    : cwtDepth;
  const dimensions = {
    ah,
    bh,
    aa,
    bb,
    as,
    jj,
    doorWidth,
    centerOpeningDoor,
    cwtPlaceLabel: cwtPlace.label,
    mirrorX: cwtPlace.mirrorX,
    a4: getPreviewSignedNumber(previewContext, "A4"),
    a1,
    a2,
    a6,
    bw,
    ca,
    cb,
    carCenterX,
    ee,
    cwtX,
    cwtMetricName,
    cwtMetricValue,
    cwtDepth,
    cwtY,
    cwtDepthLabel: rearCwt ? "CWD" : (ee ? "EE+150" : "CWT Y"),
    rearCwt,
    bs,
    dk,
    bottomGap,
    ww,
    wg
  };

  if (!dimensions.ah || !dimensions.bh || !dimensions.aa || !dimensions.bb) {
    showShaftPreviewUnavailable("Недостаточно размеров AH, BH, AA и BB для построения плана.");
    return;
  }

  if (shaftPreviewSubtitle) shaftPreviewSubtitle.textContent = "Live preview по текущим параметрам";
  if (shaftPreviewUnavailable) shaftPreviewUnavailable.hidden = true;
  shaftPreviewContent.hidden = false;

  shaftPreviewCanvas.innerHTML = renderShaftPreviewSvg(dimensions);
  shaftPreviewMetrics.innerHTML = renderShaftPreviewMetrics(dimensions);
}

function renderShaftPreviewSvg(dimensions) {
  const svgWidth = 380;
  const svgHeight = 286;
  const paddingX = 34;
  const paddingY = 30;
  const drawingWidth = svgWidth - paddingX * 2;
  const drawingHeight = svgHeight - paddingY * 2;

  const shaftRect = { x: 0, y: 0, width: dimensions.ah, height: dimensions.bh };
  const cabinX = dimensions.carCenterX
    ? dimensions.carCenterX - dimensions.aa / 2
    : (dimensions.ah - dimensions.aa) / 2;
  const cabinBottomClearance = dimensions.bs
    ? Math.max(0, dimensions.bs - dimensions.bb)
    : ((dimensions.dk || 0) + (dimensions.bottomGap || 0));
  const cabinY = dimensions.rearCwt && dimensions.ww && Number.isFinite(dimensions.cwtDepth)
    ? clampPreviewNumber(dimensions.cwtDepth + dimensions.ww + 30, 0, Math.max(0, dimensions.bh - dimensions.bb))
    : (dimensions.bh - dimensions.bb - cabinBottomClearance);
  const baseCabinRect = {
    x: cabinX,
    y: cabinY,
    width: dimensions.aa,
    height: dimensions.bb
  };
  const doorWidthMm = dimensions.doorWidth || dimensions.aa * 0.55;
  const baseDoorRect = {
    x: baseCabinRect.x + baseCabinRect.width / 2 + dimensions.a4 - doorWidthMm / 2,
    y: baseCabinRect.y + baseCabinRect.height - 70,
    width: doorWidthMm,
    height: 50
  };
  const baseCwtRect = dimensions.ww
    && dimensions.wg
    && Number.isFinite(dimensions.cwtX)
    && Number.isFinite(dimensions.cwtY)
    ? {
        x: dimensions.cwtX,
        y: dimensions.cwtY,
        width: dimensions.rearCwt ? dimensions.wg : dimensions.ww,
        height: dimensions.rearCwt ? dimensions.ww : dimensions.wg
      }
    : null;
  const railInset = 85;
  const baseRailLeft = {
    x1: baseCabinRect.x + railInset,
    y1: baseCabinRect.y - 80,
    x2: baseCabinRect.x + railInset,
    y2: baseCabinRect.y + baseCabinRect.height + 80
  };
  const baseRailRight = {
    x1: baseCabinRect.x + baseCabinRect.width - railInset,
    y1: baseCabinRect.y - 80,
    x2: baseCabinRect.x + baseCabinRect.width - railInset,
    y2: baseCabinRect.y + baseCabinRect.height + 80
  };
  const mirrorRect = rect => dimensions.mirrorX
    ? { ...rect, x: dimensions.ah - rect.x - rect.width }
    : rect;
  const mirrorLine = line => dimensions.mirrorX
    ? { ...line, x1: dimensions.ah - line.x1, x2: dimensions.ah - line.x2 }
    : line;
  const cabinRect = mirrorRect(baseCabinRect);
  const doorRect = mirrorRect(baseDoorRect);
  const cwtRect = baseCwtRect ? mirrorRect(baseCwtRect) : null;
  const railLeft = mirrorLine(baseRailLeft);
  const railRight = mirrorLine(baseRailRight);
  const rects = [shaftRect, cabinRect, doorRect, ...(cwtRect ? [cwtRect] : [])];
  const margin = 140;
  const bounds = rects.reduce((acc, rect) => ({
    minX: Math.min(acc.minX, rect.x),
    minY: Math.min(acc.minY, rect.y),
    maxX: Math.max(acc.maxX, rect.x + rect.width),
    maxY: Math.max(acc.maxY, rect.y + rect.height)
  }), { minX: 0, minY: 0, maxX: dimensions.ah, maxY: dimensions.bh });
  bounds.minX = Math.min(bounds.minX, railLeft.x1, railRight.x1) - margin;
  bounds.minY = Math.min(bounds.minY, railLeft.y1, railRight.y1) - margin;
  bounds.maxX = Math.max(bounds.maxX, railLeft.x2, railRight.x2) + margin;
  bounds.maxY = Math.max(bounds.maxY, railLeft.y2, railRight.y2) + margin;

  const scale = Math.min(drawingWidth / (bounds.maxX - bounds.minX), drawingHeight / (bounds.maxY - bounds.minY));
  const mapX = value => paddingX + (value - bounds.minX) * scale;
  const mapY = value => paddingY + (value - bounds.minY) * scale;
  const mapSize = value => value * scale;
  const rectAttrs = rect =>
    `x="${mapX(rect.x).toFixed(1)}" y="${mapY(rect.y).toFixed(1)}" width="${mapSize(rect.width).toFixed(1)}" height="${mapSize(rect.height).toFixed(1)}"`;
  const lineAttrs = line =>
    `x1="${mapX(line.x1).toFixed(1)}" y1="${mapY(line.y1).toFixed(1)}" x2="${mapX(line.x2).toFixed(1)}" y2="${mapY(line.y2).toFixed(1)}"`;
  const isInside = (inner, outer) =>
    inner.x >= outer.x
    && inner.y >= outer.y
    && inner.x + inner.width <= outer.x + outer.width
    && inner.y + inner.height <= outer.y + outer.height;
  const intersects = (first, second) =>
    first.x < second.x + second.width
    && first.x + first.width > second.x
    && first.y < second.y + second.height
    && first.y + first.height > second.y;
  const cabinCollision = !isInside(cabinRect, shaftRect) || (cwtRect && intersects(cabinRect, cwtRect));
  const cwtCollision = cwtRect && (!isInside(cwtRect, shaftRect) || intersects(cabinRect, cwtRect));
  const doorCollision = !isInside(doorRect, shaftRect) || !isInside(doorRect, cabinRect);
  const cwtMarkup = cwtRect
    ? `<rect class="shaft-preview-svg__counterweight ${cwtCollision ? "shaft-preview-svg__counterweight--collision" : ""}" ${rectAttrs(cwtRect)} rx="3" />`
    : "";

  return `
    <svg class="shaft-preview-svg" viewBox="0 0 ${svgWidth} ${svgHeight}" role="img" aria-label="План шахты">
      <rect class="shaft-preview-svg__shaft" ${rectAttrs(shaftRect)} rx="4" />
      <line class="shaft-preview-svg__axis" x1="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y1="${mapY(shaftRect.y).toFixed(1)}" x2="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y2="${mapY(shaftRect.y + shaftRect.height).toFixed(1)}" />
      <line class="shaft-preview-svg__axis" x1="${mapX(shaftRect.x).toFixed(1)}" y1="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}" x2="${mapX(shaftRect.x + shaftRect.width).toFixed(1)}" y2="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}" />
      ${cwtMarkup}
      <rect class="shaft-preview-svg__car ${cabinCollision ? "shaft-preview-svg__car--collision" : ""}" ${rectAttrs(cabinRect)} rx="3" />
      <line class="shaft-preview-svg__rail" ${lineAttrs(railLeft)} />
      <line class="shaft-preview-svg__rail" ${lineAttrs(railRight)} />
      <rect class="shaft-preview-svg__door ${doorCollision ? "shaft-preview-svg__door--collision" : ""}" ${rectAttrs(doorRect)} rx="1" />
      <text class="shaft-preview-svg__label" x="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y="${mapY(shaftRect.y + shaftRect.height + 110).toFixed(1)}">AH ${formatPreviewNumber(dimensions.ah)}</text>
      <text class="shaft-preview-svg__label shaft-preview-svg__label--vertical" x="${mapX(shaftRect.x - 110).toFixed(1)}" y="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}">BH ${formatPreviewNumber(dimensions.bh)}</text>
    </svg>`;
}

function renderShaftPreviewMetrics(dimensions) {
  const metrics = [
    ["AH", dimensions.ah, "Ширина шахты"],
    ["BH", dimensions.bh, "Глубина шахты"],
    ["AA", dimensions.aa, "Ширина кабины"],
    ["BB", dimensions.bb, "Глубина кабины"],
    ["JJ", dimensions.jj, "Ширина дверей"],
    ...(dimensions.centerOpeningDoor && dimensions.doorWidth !== dimensions.jj
      ? [["2xJJ", dimensions.doorWidth, "Расчетная ширина CO"]]
      : []),
    ["A4", dimensions.a4, "Эксцентриситет"],
    ["Место", dimensions.cwtPlaceLabel, "Положение противовеса"],
    [dimensions.cwtMetricName, dimensions.cwtMetricValue, "Противовес по ширине"],
    [dimensions.cwtDepthLabel, dimensions.cwtDepth, "Противовес по глубине"]
  ];

  return metrics
    .filter(([, value]) => value !== null && value !== undefined && value !== "")
    .map(([name, value, label]) => `
      <div class="shaft-preview__metric">
        <dt>${escapeHtml(name)}</dt>
        <dd>${formatPreviewMetricValue(value)}<span>${escapeHtml(label)}</span></dd>
      </div>`)
    .join("");
}

function evaluateLevelExpression(expression, context) {
  if (!expression) return 1;

  const result = evaluateFormulaExpression(expression, context);
  const numericResult = Number(result);
  return Number.isFinite(numericResult) ? numericResult : -1;
}

function formatValidationValue(value) {
  if (typeof value === "number" && Number.isFinite(value)) {
    const rounded = Math.round(value * 1000) / 1000;
    return String(rounded);
  }

  return hasValue(value) ? String(value) : "";
}

function formatValidationMessage(message, context) {
  return String(message || "Параметры не проходят проверку T-FLEX.")
    .replace(/\{([^{}]+)\}/g, (_, expression) => {
      const value = evaluateFormulaExpression(expression, context);
      return hasValue(value) ? formatValidationValue(value) : `{${expression}}`;
    });
}

function isValidationPassed(value) {
  if (!hasValue(value)) return true;
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return Number.isFinite(value) && value !== 0;
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    return normalized !== "" && normalized !== "0" && normalized !== "false" && normalized !== "нет";
  }

  return Boolean(value);
}

function stripStringLiterals(value) {
  return String(value || "").replace(/(["'])(?:\\.|(?!\1).)*\1/g, "");
}

function extractIdentifierTokens(value) {
  const tokens = new Set();
  const text = stripStringLiterals(value);
  for (const match of text.matchAll(/[$A-Za-z_][A-Za-z0-9_$]*/g)) {
    tokens.add(match[0]);
  }

  return [...tokens];
}

function extractMessageExpressionTokens(message) {
  const tokens = new Set();
  for (const match of String(message || "").matchAll(/\{([^{}]+)\}/g)) {
    for (const token of extractIdentifierTokens(match[1])) {
      tokens.add(token);
    }
  }

  return [...tokens];
}

function getTemplateDefinitionMap() {
  return new Map(getTemplateDefinitions().map(parameter => [parameter.name, parameter]));
}

function getParameterNameSet() {
  return new Set((state.selectedTemplate?.parameters || []).map(parameter => parameter.name));
}

function getDefinitionDependencyTokens(definition) {
  return [
    ...extractIdentifierTokens(definition.expression),
    ...extractIdentifierTokens(definition.levelExpression)
  ];
}

function resolveParameterDependencies(token, definitionMap, parameterNames, visited = new Set()) {
  if (parameterNames.has(token)) return [token];
  if (visited.has(token)) return [];
  visited.add(token);

  const definition = definitionMap.get(token);
  if (!definition) return [];

  const dependencies = new Set();
  for (const dependencyToken of getDefinitionDependencyTokens(definition)) {
    for (const dependency of resolveParameterDependencies(dependencyToken, definitionMap, parameterNames, visited)) {
      dependencies.add(dependency);
    }
  }

  return [...dependencies];
}

function getValidationFieldNames(rule) {
  if (Array.isArray(rule.fieldNames) && rule.fieldNames.length > 0) {
    return rule.fieldNames;
  }

  const parameterNames = getParameterNameSet();
  const definitionMap = getTemplateDefinitionMap();
  const messageFields = extractMessageExpressionTokens(rule.message)
    .filter(token => parameterNames.has(token));

  if (messageFields.length > 0) return [...new Set(messageFields)];

  const fields = new Set();
  const baseName = String(rule.name || "").replace(/^r_/, "");
  if (parameterNames.has(baseName)) fields.add(baseName);

  for (const token of extractIdentifierTokens(rule.expression)) {
    if (parameterNames.has(token)) {
      fields.add(token);
    }
  }

  if (fields.size > 0) return [...fields];

  for (const token of extractIdentifierTokens(rule.expression)) {
    for (const dependency of resolveParameterDependencies(token, definitionMap, parameterNames)) {
      fields.add(dependency);
    }
  }

  return [...fields];
}

function collectValidationFieldNames(errors = []) {
  return new Set(errors.flatMap(error => error.fieldNames || []));
}

function applyValidationHighlights(errors = []) {
  state.validationFieldNames = collectValidationFieldNames(errors);

  for (const field of parametersForm.querySelectorAll(".field--invalid")) {
    field.classList.remove("field--invalid");
  }

  for (const input of parametersForm.querySelectorAll("input, select, textarea")) {
    input.classList.remove("is-invalid");
    input.removeAttribute("aria-invalid");

    const name = input.dataset.parameterName || input.name;
    if (!state.validationFieldNames.has(name)) continue;

    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
    input.closest(".field")?.classList.add("field--invalid");
  }
}

function getCurrentValidationErrors(context = buildLevelContext()) {
  const rules = state.selectedTemplate?.validationRules || [];
  const errors = [];
  const seenMessages = new Set();

  for (const rule of rules) {
    const result = evaluateFormulaExpression(rule.expression, context);
    if (isValidationPassed(result)) continue;

    const message = formatValidationMessage(rule.message, context);
    if (seenMessages.has(message)) continue;

    seenMessages.add(message);
    errors.push({ name: rule.name, message, fieldNames: getValidationFieldNames(rule) });
  }

  return errors;
}

function updateValidationPanel(errors = []) {
  validationPanel.replaceChildren();

  if (errors.length === 0) {
    validationPanel.hidden = true;
    return;
  }

  const title = document.createElement("h3");
  title.className = "validation-panel__title";
  title.textContent = "Проверьте параметры";

  const list = document.createElement("ul");
  list.className = "validation-panel__list";

  for (const error of errors) {
    const item = document.createElement("li");
    item.textContent = error.message;
    list.append(item);
  }

  validationPanel.append(title, list);
  validationPanel.hidden = false;
}

function isParameterVisible(parameter, context) {
  if (!parameter.levelExpression) return true;
  return evaluateLevelExpression(parameter.levelExpression, context) >= 0;
}

function isStopParameter(parameter) {
  const name = parameter.name;
  return STOP_CONTROL_NAMES.has(name)
    || /^s\d{2}_(name_1|level_1|front_1|rear_1|em_1)$/.test(name)
    || /^s_top_(name_1|level_1|rear_1)$/.test(name);
}

function isFrontendHiddenParameter(parameter) {
  return FRONTEND_HIDDEN_PARAMETER_NAMES.has(parameter.name);
}

function clampStopCount(value) {
  const number = Math.trunc(toNumber(value));
  if (!Number.isFinite(number)) return 2;
  return Math.min(48, Math.max(2, number));
}

function getStopRowKey(index, stops) {
  return index === stops ? "s_top" : `s${String(index).padStart(2, "0")}`;
}

function getStopNameParameterName(index, stops) {
  return `${getStopRowKey(index, stops)}_name_1`;
}

function getStopLevelParameterName(index, stops) {
  return `${getStopRowKey(index, stops)}_level_1`;
}

function normalizeStopFloorName(value) {
  const number = Math.trunc(toNumber(value));
  return number === 0 ? 1 : number;
}

function getNextStopFloorName(value) {
  const next = value + 1;
  return next === 0 ? 1 : next;
}

function getAutomaticStopNameStart() {
  const firstStopName = getParameterDefinition("s01_name_1");
  if (!firstStopName) return 1;
  return normalizeStopFloorName(getParameterValue(firstStopName));
}

function getAutomaticStopName(index, start = 1) {
  let floor = normalizeStopFloorName(start);
  for (let position = 1; position < index; position += 1) {
    floor = getNextStopFloorName(floor);
  }

  return floor;
}

function getStopNameValue(index, stops, manualNames, automaticStart) {
  const parameterName = getStopNameParameterName(index, stops);
  if (manualNames) {
    const parameter = getParameterDefinition(parameterName);
    if (parameter) return normalizeStopFloorName(getParameterValue(parameter));
  }

  return getAutomaticStopName(index, automaticStart);
}

function findLobbyStopIndex(stops, manualNames, automaticStart) {
  for (let index = 1; index <= stops; index += 1) {
    if (getStopNameValue(index, stops, manualNames, automaticStart) === 1) {
      return index;
    }
  }

  return 1;
}

function synchronizeAutomaticStopState(stops) {
  const manualNames = toFlagNumber(getParameterValueByName("name")) === 1;
  const manualMain = toFlagNumber(getParameterValueByName("main")) === 1;
  const automaticStart = getAutomaticStopNameStart();

  if (!manualNames) {
    for (let index = 1; index <= stops; index += 1) {
      const parameter = getParameterDefinition(getStopNameParameterName(index, stops));
      if (parameter) {
        state.parameterValues[parameter.name] = getAutomaticStopName(index, automaticStart);
      }
    }
  }

  if (!manualMain) {
    state.parameterValues.main_floor = findLobbyStopIndex(stops, manualNames, automaticStart);
  }
}

function getAutomaticStopLevel(index, stops) {
  const bottomLevel = toNumber(getParameterValueByName("s01_level_1"));
  const travelHeight = toNumber(getParameterValueByName("TR")) * 1000;
  const total = travelHeight > 0 ? travelHeight : (stops - 1) * 6000;
  return bottomLevel + Math.round((total * (index - 1)) / Math.max(1, stops - 1));
}

function createDisplayInput(value, parameterName = null) {
  const input = document.createElement("input");
  input.type = "number";
  input.value = value;
  input.disabled = true;
  input.className = "stops-table__input";
  if (parameterName) {
    input.dataset.parameterName = parameterName;
    input.name = parameterName;
  }
  return input;
}

function bindInputChange(input, parameter) {
  const handleInputChange = () => {
    const focusTarget = getInputFocusTarget(input);
    state.parameterValues[parameter.name] = readInputValue(input, parameter);
    renderParametersAfterInputChange(focusTarget);
  };

  if (input.type !== "checkbox" && input.type !== "radio") {
    input.addEventListener("input", handleInputChange);
  }

  input.addEventListener("change", handleInputChange);
}

function createCompactInput(parameter, options = {}) {
  const input = document.createElement("input");
  const type = getParameterType(parameter);

  if (options.radioValue !== undefined) {
    input.type = "radio";
    input.name = parameter.name;
    input.value = String(options.radioValue);
  } else if (type === "bool" || type === "boolean") {
    input.type = "checkbox";
  } else {
    input.type = type === "number" || type === "integer" ? "number" : "text";
    if (type === "integer") input.step = "1";
    if (type === "number") input.step = "any";
  }

  input.dataset.parameterName = parameter.name;
  if (input.name === "") input.name = parameter.name;
  input.disabled = Boolean(parameter.isReadOnly);
  setInputValue(input, parameter, options.radioValue !== undefined ? getParameterValue(parameter) : getParameterValue(parameter));
  input.className = options.className || "stops-table__input";
  if (state.validationFieldNames.has(parameter.name)) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
  }
  bindInputChange(input, parameter);
  return input;
}

function createStopModeHeader(name, labelText) {
  const label = document.createElement("label");
  label.className = "stops-table__mode";

  const parameter = getParameterDefinition(name);
  if (parameter) {
    label.append(createCompactInput(parameter, { className: "stops-table__mode-input" }));
  }

  const text = document.createElement("span");
  text.textContent = labelText;
  label.append(text);
  return label;
}

function createStopCellControl(name, fallback = "") {
  const parameter = getParameterDefinition(name);
  if (!parameter) {
    const span = document.createElement("span");
    span.className = "stops-table__empty";
    span.textContent = fallback;
    return span;
  }

  return createCompactInput(parameter);
}

function createStopRadio(value) {
  const parameter = getParameterDefinition("main_floor");
  if (!parameter) return document.createTextNode("");
  return createCompactInput(parameter, { radioValue: value, className: "stops-table__radio" });
}

function createDisplayStopRadio(checked) {
  const input = document.createElement("input");
  input.type = "radio";
  input.className = "stops-table__radio";
  input.disabled = true;
  input.checked = checked;
  return input;
}

function createStopsTable(context, options = {}) {
  const stops = clampStopCount(context.stops);
  synchronizeAutomaticStopState(stops);

  const manualMain = toFlagNumber(getParameterValueByName("main")) === 1;
  const manualNames = toFlagNumber(context.name) === 1;
  const manualLevels = toFlagNumber(context.level) === 1;
  const hasRearDoors = toNumber(context.NE) === 2;
  const hasAo = toFlagNumber(context.em) === 1;
  const automaticStart = getAutomaticStopNameStart();
  const lobbyStopIndex = manualMain
    ? toNumber(getParameterValueByName("main_floor"))
    : findLobbyStopIndex(stops, manualNames, automaticStart);

  const panel = document.createElement("section");
  panel.className = "stops-panel";

  if (!options.hideTitle) {
    const title = document.createElement("h3");
    title.textContent = STOP_GROUP_LABEL;
    panel.append(title);
  }

  const wrapper = document.createElement("div");
  wrapper.className = "stops-table-wrap";

  const table = document.createElement("table");
  table.className = "stops-table";

  const thead = document.createElement("thead");
  const headRow = document.createElement("tr");
  const headers = [
    createStopModeHeader("main", STOP_LOBBY_LABEL),
    createStopModeHeader("name", STOP_FLOOR_LABEL),
    createStopModeHeader("level", STOP_LEVEL_LABEL)
  ];

  if (hasRearDoors) {
    headers.push(document.createTextNode(STOP_FRONT_LABEL));
    headers.push(document.createTextNode(STOP_REAR_LABEL));
  }

  if (hasAo) {
    headers.push(document.createTextNode(STOP_AO_LABEL));
  }

  for (const header of headers) {
    const th = document.createElement("th");
    th.append(header);
    headRow.append(th);
  }

  thead.append(headRow);
  table.append(thead);

  const tbody = document.createElement("tbody");
  for (let index = 1; index <= stops; index += 1) {
    const rowKey = getStopRowKey(index, stops);
    const row = document.createElement("tr");

    const lobbyCell = document.createElement("td");
    lobbyCell.append(manualMain
      ? createStopRadio(index)
      : createDisplayStopRadio(index === lobbyStopIndex));
    row.append(lobbyCell);

    const stopNameParameterName = getStopNameParameterName(index, stops);
    const nameCell = document.createElement("td");
    nameCell.append(manualNames
      ? createStopCellControl(stopNameParameterName)
      : createDisplayInput(getAutomaticStopName(index, automaticStart), stopNameParameterName));
    row.append(nameCell);

    const stopLevelParameterName = getStopLevelParameterName(index, stops);
    const levelCell = document.createElement("td");
    levelCell.append(manualLevels && rowKey !== "s_top"
      ? createStopCellControl(stopLevelParameterName)
      : createDisplayInput(getAutomaticStopLevel(index, stops), stopLevelParameterName));
    row.append(levelCell);

    if (hasRearDoors) {
      const frontCell = document.createElement("td");
      if (rowKey !== "s_top") frontCell.append(createStopCellControl(`${rowKey}_front_1`));
      row.append(frontCell);

      const rearCell = document.createElement("td");
      rearCell.append(createStopCellControl(rowKey === "s_top" ? "s_top_rear_1" : `${rowKey}_rear_1`));
      row.append(rearCell);
    }

    if (hasAo) {
      const aoCell = document.createElement("td");
      if (rowKey !== "s_top" && index > 1) aoCell.append(createStopCellControl(`${rowKey}_em_1`));
      row.append(aoCell);
    }

    tbody.append(row);
  }

  table.append(tbody);
  wrapper.append(table);
  panel.append(wrapper);
  return panel;
}

function getParameterDisplayParts(parameter) {
  const displayName = normalizeParameterDisplayText(parameter.displayName || parameter.name);
  const explicitCategory = normalizeParameterCategory(
    parameter.category || parameter.groupName || parameter.group || "");
  const separatorIndex = displayName.indexOf("/");

  if (separatorIndex < 0) {
    return {
      category: explicitCategory || DEFAULT_PARAMETER_CATEGORY,
      label: normalizeParameterLabel(displayName, parameter.name)
    };
  }

  const category = displayName.slice(0, separatorIndex);
  const label = displayName.slice(separatorIndex + 1);

  return {
    category: normalizeParameterCategory(category) || explicitCategory || DEFAULT_PARAMETER_CATEGORY,
    label: normalizeParameterLabel(label, parameter.name)
  };
}

function normalizeParameterDisplayText(value) {
  return String(value || "")
    .replace(/\s+/g, " ")
    .trim();
}

function isBrokenParameterText(value) {
  const text = normalizeParameterDisplayText(value);
  return !text || /^[?\-_\s]+$/.test(text);
}

function normalizeParameterCategory(value) {
  const category = normalizeParameterDisplayText(value);
  if (isBrokenParameterText(category)) return "";
  return CATEGORY_LABEL_OVERRIDES.get(category) || category;
}

function normalizeParameterLabel(value, fallbackName) {
  const label = normalizeParameterDisplayText(value);
  if (!isBrokenParameterText(label)) {
    return FIELD_LABEL_OVERRIDES.get(label) || label;
  }

  return FIELD_LABEL_OVERRIDES.get(fallbackName) || fallbackName;
}

function getFieldLabelText(parameter) {
  const parts = getParameterDisplayParts(parameter);
  return parts.label;
}

function createParameterGroup(category) {
  const group = document.createElement("fieldset");
  group.className = "parameter-group";
  group.dataset.category = category;

  if (category === STOP_GROUP_LABEL) {
    group.classList.add("parameter-group--stops");
  }

  const legend = document.createElement("legend");
  legend.className = "parameter-group__title";
  legend.textContent = category;

  const fields = document.createElement("div");
  fields.className = "parameter-group__fields";

  group.append(legend, fields);
  return group;
}

function appendToParameterGroup(groups, category, node) {
  let group = groups.get(category);
  if (!group) {
    group = createParameterGroup(category);
    groups.set(category, group);
    parametersForm.append(group);
  }

  group.querySelector(".parameter-group__fields").append(node);
}

function getCategoryDisplayOrder(category, fallbackIndex) {
  const index = CATEGORY_DISPLAY_ORDER.indexOf(category);
  return index >= 0 ? index : CATEGORY_DISPLAY_ORDER.length + fallbackIndex;
}

function reorderParameterGroups(groups) {
  [...groups.entries()]
    .map(([category, group], index) => ({
      category,
      group,
      index,
      order: getCategoryDisplayOrder(category, index)
    }))
    .sort((left, right) => left.order - right.order || left.index - right.index)
    .forEach(({ group }) => parametersForm.append(group));
}

function getRenderedParameterCategories() {
  return [...parametersForm.querySelectorAll(".parameter-group")]
    .map(group => group.dataset.category)
    .filter(Boolean);
}

function getParameterGroupByCategory(category) {
  return [...parametersForm.querySelectorAll(".parameter-group")]
    .find(group => group.dataset.category === category);
}

function prefersReducedMotion() {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches || false;
}

function scrollToParameterCategory(category) {
  const group = getParameterGroupByCategory(category);
  if (!group) return;

  group.scrollIntoView({
    behavior: prefersReducedMotion() ? "auto" : "smooth",
    block: "start",
    inline: "nearest"
  });
}

function applyParameterTabVisibility() {
  const categories = getRenderedParameterCategories();
  if (!categories.length) {
    if (parameterTabs) parameterTabs.replaceChildren();
    state.activeParameterCategory = null;
    return;
  }

  if (!state.activeParameterCategory || !categories.includes(state.activeParameterCategory)) {
    state.activeParameterCategory = categories[0];
  }

  for (const group of parametersForm.querySelectorAll(".parameter-group")) {
    group.hidden = !state.showAllParameters && group.dataset.category !== state.activeParameterCategory;
  }

  for (const button of parameterTabs?.querySelectorAll("button[data-category]") || []) {
    const active = button.dataset.category === state.activeParameterCategory;
    button.classList.toggle("is-active", active);
    button.setAttribute("aria-pressed", active ? "true" : "false");
  }

  if (showAllParametersToggle) {
    showAllParametersToggle.checked = state.showAllParameters;
  }
}

function renderParameterTabs() {
  if (!parameterTabs) return;

  const categories = getRenderedParameterCategories();
  const visibleCategories = categories;

  if (state.activeParameterCategory && !visibleCategories.includes(state.activeParameterCategory)) {
    state.activeParameterCategory = visibleCategories[0] || categories[0] || null;
  }

  parameterTabs.replaceChildren();
  for (const category of visibleCategories) {
    const button = document.createElement("button");
    button.type = "button";
    button.dataset.category = category;
    button.textContent = category;
    button.addEventListener("click", () => {
      state.activeParameterCategory = category;
      applyParameterTabVisibility();
      if (state.showAllParameters) {
        requestAnimationFrame(() => scrollToParameterCategory(category));
      }
    });
    parameterTabs.append(button);
  }

  applyParameterTabVisibility();
}

function getCurrentParameterInputValue(parameter, context) {
  return normalizeValueForAllowedList(
    parameter,
    getDisplayParameterValue(parameter, context),
    context);
}

function wireParameterInput(input, parameter, isDisabled) {
  input.name = parameter.name;
  input.dataset.parameterName = parameter.name;
  input.disabled = isDisabled;
  input.required = Boolean(parameter.isRequired) && !input.disabled;
  const handleParameterChange = () => {
    const focusTarget = getInputFocusTarget(input);
    state.parameterValues[parameter.name] = readInputValue(input, parameter);
    renderParametersAfterInputChange(focusTarget);
  };

  if (input.tagName === "INPUT" || input.tagName === "TEXTAREA") {
    input.addEventListener("input", handleParameterChange);
  }

  input.addEventListener("change", handleParameterChange);
}

function createParameterInput(parameter, context) {
  const field = document.createElement("div");
  field.className = "field";
  const hasValidationError = state.validationFieldNames.has(parameter.name);
  if (hasValidationError) {
    field.classList.add("field--invalid");
  }

  const label = document.createElement("span");
  label.className = "field__label";
  label.textContent = getFieldLabelText(parameter);

  let input;
  const type = getParameterType(parameter);
  const isDisabled = Boolean(parameter.isReadOnly);
  const allowedValues = getAllowedValues(parameter, context);
  const currentValue = getCurrentParameterInputValue(parameter, context);
  const displayName = parameter.displayName || parameter.name;
  const isAddressMultiline = displayName.toLowerCase().includes("\u0430\u0434\u0440\u0435\u0441");
  const isMultiline = parameter.multiline || parameter.name === "$Address" || displayName.toLowerCase().includes("адрес");

  if (type === "bool" || type === "boolean") {
    field.classList.add("field--checkbox");
    input = document.createElement("input");
    input.type = "checkbox";
  } else if (isMultiline || isAddressMultiline) {
    input = document.createElement("textarea");
    input.rows = parameter.rows || 3;
  } else if (allowedValues.length) {
    input = document.createElement("select");
    for (const value of allowedValues) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = getAllowedValueLabel(parameter, value);
      input.append(option);
    }
  } else {
    input = document.createElement("input");
    input.type = type === "number" || type === "integer" ? "number" : "text";
    if (type === "integer") input.step = "1";
    if (type === "number") input.step = "any";
    if (parameter.minValue !== null && parameter.minValue !== undefined) input.min = parameter.minValue;
    if (parameter.maxValue !== null && parameter.maxValue !== undefined) input.max = parameter.maxValue;
  }

  wireParameterInput(input, parameter, isDisabled);
  setInputValue(input, parameter, currentValue);
  if (hasValidationError) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
  }

  const shouldShowUnit = parameter.unit && input.tagName !== "SELECT" && input.type !== "checkbox" && input.type !== "radio";
  if (shouldShowUnit) {
    const control = document.createElement("div");
    control.className = "field__control";
    const suffix = document.createElement("span");
    suffix.className = "field__suffix";
    suffix.textContent = parameter.unit;
    control.append(input, suffix);
    field.append(label, control);
  } else {
    field.append(label, input);
  }

  if (parameter.multiline || parameter.name === "$Address" || input.tagName === "TEXTAREA") {
    field.classList.add("field--wide");
  }

  return field;
}

function getInputFocusTarget(input) {
  const name = input?.dataset?.parameterName || input?.name || null;
  if (!name) return null;

  return {
    name,
    type: input.type,
    value: input.type === "radio" ? input.value : null
  };
}

function getFocusedParameterTarget() {
  const activeElement = document.activeElement;
  if (!activeElement || !parametersForm.contains(activeElement)) return null;
  return getInputFocusTarget(activeElement);
}

function focusParameterInput(focusTarget) {
  if (!focusTarget?.name) return;

  const inputs = [...parametersForm.querySelectorAll("input, select, textarea")]
    .filter(element => (element.dataset.parameterName || element.name) === focusTarget.name && !element.disabled);
  const input = inputs.find(element => focusTarget.type !== "radio" || element.value === focusTarget.value)
    || inputs[0];

  input?.focus({ preventScroll: true });
}

function getViewportScrollPosition() {
  const scrollingElement = document.scrollingElement || document.documentElement;
  return {
    x: window.scrollX,
    y: window.scrollY,
    elementLeft: scrollingElement.scrollLeft,
    elementTop: scrollingElement.scrollTop
  };
}

function applyViewportScrollPosition(scrollPosition) {
  if (!scrollPosition) return;

  const scrollingElement = document.scrollingElement || document.documentElement;
  scrollingElement.scrollLeft = scrollPosition.elementLeft;
  scrollingElement.scrollTop = scrollPosition.elementTop;
  window.scrollTo({
    left: scrollPosition.x,
    top: scrollPosition.y,
    behavior: "auto"
  });
}

function restoreViewport(options, scrollPosition, focusTarget) {
  if (!options.preserveScroll && !options.preserveFocus) return;

  let focusRestored = false;
  const restore = () => {
    if (options.preserveFocus && !focusRestored) {
      focusParameterInput(focusTarget);
      focusRestored = true;
    }

    if (options.preserveScroll) {
      applyViewportScrollPosition(scrollPosition);
    }
  };

  restore();
  requestAnimationFrame(() => {
    restore();
    requestAnimationFrame(restore);
  });
}

function renderParametersAfterInputChange(focusTarget) {
  state.pendingFocusTarget = focusTarget || state.pendingFocusTarget;
  if (state.pendingRenderFrame !== null) {
    cancelAnimationFrame(state.pendingRenderFrame);
  }

  state.pendingRenderFrame = requestAnimationFrame(() => {
    const nextFocusTarget = state.pendingFocusTarget;
    state.pendingRenderFrame = null;
    state.pendingFocusTarget = null;
    renderParameters({ preserveScroll: true, preserveFocus: true, focusTarget: nextFocusTarget });
  });
}

function renderParameters(options = {}) {
  const scrollPosition = options.preserveScroll
    ? getViewportScrollPosition()
    : null;
  const focusTarget = options.preserveFocus ? (options.focusTarget || getFocusedParameterTarget()) : null;

  rememberCurrentValues();
  parametersForm.replaceChildren();
  if (!state.selectedTemplate) {
    state.validationFieldNames = new Set();
    updateValidationPanel();
    if (parameterReadyBanner) parameterReadyBanner.hidden = true;
    updateShaftPreview(null);
    restoreViewport(options, scrollPosition, focusTarget);
    return;
  }

  const context = buildLevelContext();
  const validationErrors = getCurrentValidationErrors(context);
  state.validationFieldNames = collectValidationFieldNames(validationErrors);
  let stopsTablePending = false;
  const groups = new Map();
  for (const parameter of state.selectedTemplate.parameters) {
    if (isFrontendHiddenParameter(parameter)) continue;
    if (isStopParameter(parameter)) continue;

    if (isParameterVisible(parameter, context)) {
      const parts = getParameterDisplayParts(parameter);
      appendToParameterGroup(groups, parts.category, createParameterInput(parameter, context));
      if (parameter.name === "stops") {
        stopsTablePending = true;
      } else if (parameter.name === "em" && stopsTablePending) {
        appendToParameterGroup(groups, STOP_GROUP_LABEL, createStopsTable(context, { hideTitle: true }));
        stopsTablePending = false;
      }
    }
  }

  if (stopsTablePending) {
    appendToParameterGroup(groups, STOP_GROUP_LABEL, createStopsTable(context, { hideTitle: true }));
  }

  reorderParameterGroups(groups);
  renderParameterTabs();
  updateValidationPanel(validationErrors);
  if (parameterReadyBanner) {
    parameterReadyBanner.hidden = validationErrors.length > 0;
  }
  updateConfigurationNamePreview();
  updateShaftPreview(context);
  restoreViewport(options, scrollPosition, focusTarget);
}

function renderSelectedTemplate() {
  state.selectedTemplate = state.templates.find(template => template.id === templateSelect.value);
  formatSelect.replaceChildren();
  parametersForm.replaceChildren();
  state.validationFieldNames = new Set();
  state.activeParameterCategory = null;
  updateValidationPanel();
  updateConfigurationNamePreview();
  initializeParameterValues();

  if (!state.selectedTemplate) {
    updateShaftPreview(null);
    return;
  }

  for (const format of state.selectedTemplate.outputFormats) {
    const option = document.createElement("option");
    option.value = format;
    option.textContent = format.toUpperCase();
    formatSelect.append(option);
  }

  renderParameters();
}

function collectParameters() {
  rememberCurrentValues();
  const parameters = {};
  for (const input of parametersForm.querySelectorAll("input, select, textarea")) {
    if (input.type === "radio" && !input.checked) continue;

    const name = input.dataset.parameterName || input.name;
    const definition = state.selectedTemplate.parameters.find(parameter => parameter.name === name);
    if (!definition) continue;
    if (input.disabled && !definition.submitWhenDisabled) continue;

    putCollectedParameter(parameters, definition, readInputValue(input, definition));
  }

  appendStopParameters(parameters);
  appendFrontendHiddenParameters(parameters);
  return parameters;
}

function putCollectedParameter(parameters, definition, value) {
  const type = getParameterType(definition);
  if (type === "number") {
    parameters[definition.name] = value === "" || value === null ? null : Number(value);
  } else if (type === "integer") {
    parameters[definition.name] = value === "" || value === null ? null : Number.parseInt(value, 10);
  } else if (type === "bool" || type === "boolean") {
    parameters[definition.name] = Boolean(value);
  } else {
    parameters[definition.name] = hasValue(value) ? String(value) : "";
  }
}

function appendStopParameters(parameters) {
  if (!state.selectedTemplate) return;

  const stops = clampStopCount(parameters.stops ?? getParameterValueByName("stops"));
  synchronizeAutomaticStopState(stops);

  const mainFloor = getParameterDefinition("main_floor");
  if (mainFloor) {
    putCollectedParameter(parameters, mainFloor, state.parameterValues.main_floor);
  }

  for (let index = 1; index <= stops; index += 1) {
    const stopName = getParameterDefinition(getStopNameParameterName(index, stops));
    if (stopName) {
      putCollectedParameter(parameters, stopName, state.parameterValues[stopName.name]);
    }
  }
}

function appendFrontendHiddenParameters(parameters) {
  if (!state.selectedTemplate) return;

  for (const definition of state.selectedTemplate.parameters) {
    if (!isFrontendHiddenParameter(definition)) continue;
    putCollectedParameter(parameters, definition, getParameterValue(definition));
  }
}

function renderJob(job) {
  const files = job.resultFiles || [];
  const downloadLinks = files.map(file =>
    `<a href="${escapeHtml(file.downloadUrl)}">${escapeHtml(file.fileName)}</a>`
  ).join("");

  statusPanel.className = "job-status";
  statusPanel.innerHTML = `
    <div class="status ${escapeHtml(job.status.toLowerCase())}">${escapeHtml(job.status)}</div>
    <dl>
      <dt>Задание</dt><dd>${escapeHtml(job.id)}</dd>
      <dt>Шаблон</dt><dd>${escapeHtml(job.templateId)}</dd>
      <dt>Создано</dt><dd>${formatDate(job.createdAt)}</dd>
      <dt>Завершено</dt><dd>${formatDate(job.finishedAt)}</dd>
      <dt>Ошибка</dt><dd>${escapeHtml(job.errorMessage || "")}</dd>
      <dt>Результат</dt><dd>${downloadLinks || ""}</dd>
    </dl>
  `;
}

function renderStatusError(messages) {
  statusPanel.className = "";
  const error = document.createElement("div");
  error.className = "error";

  for (const message of messages) {
    const line = document.createElement("div");
    line.textContent = message;
    error.append(line);
  }

  statusPanel.replaceChildren(error);
}

async function refreshJob(jobId) {
  const response = await apiFetch(`/api/jobs/${jobId}`);
  if (!response.ok) return;
  const job = await response.json();
  renderJob(job);

  if (job.status === "Completed" || job.status === "Failed" || job.status === "Cancelled") {
    clearInterval(state.pollTimer);
    state.pollTimer = null;
    await refreshJobs();
  }
}

async function refreshJobs() {
  const response = await apiFetch("/api/jobs?take=20");
  if (!response.ok) return;
  const jobs = await response.json();
  jobsTableBody.replaceChildren();

  for (const job of jobs) {
    const row = document.createElement("tr");
    const files = job.resultFiles || [];
    row.innerHTML = `
      <td>${escapeHtml(job.id.slice(0, 8))}</td>
      <td>${escapeHtml(job.templateId)}</td>
      <td><span class="status ${escapeHtml(job.status.toLowerCase())}">${escapeHtml(job.status)}</span></td>
      <td>${escapeHtml(job.outputFormat.toUpperCase())}</td>
      <td>${formatDate(job.createdAt)}</td>
      <td>${files.map(file => `<a href="${escapeHtml(file.downloadUrl)}">скачать</a>`).join(" ")}</td>
    `;
    jobsTableBody.append(row);
  }
}

async function submitJob(event) {
  event.preventDefault();
  if (!state.selectedTemplate) return;
  if (!canCreateJobs()) {
    renderStatusError(["Недостаточно прав для создания задания."]);
    return;
  }

  rememberCurrentValues();
  const validationErrors = getCurrentValidationErrors();
  updateValidationPanel(validationErrors);
  applyValidationHighlights(validationErrors);
  if (validationErrors.length > 0) {
    renderStatusError(["Исправьте параметры перед созданием задания."]);
    validationPanel.scrollIntoView({ behavior: "auto", block: "nearest" });
    return;
  }

  submitButton.disabled = true;
  statusPanel.className = "job-status";
  statusPanel.innerHTML = `<div class="status pending">Pending</div>`;

  const response = await apiFetch("/api/jobs", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      templateId: state.selectedTemplate.id,
      outputFormat: formatSelect.value,
      parameters: collectParameters()
    })
  });

  submitButton.disabled = false;

  if (!response.ok) {
    renderStatusError(await readProblem(response, "Ошибка создания задания"));
    return;
  }

  const job = await response.json();
  state.activeJobId = job.id;
  renderJob(job);
  await refreshJobs();

  clearInterval(state.pollTimer);
  state.pollTimer = setInterval(() => refreshJob(job.id), 1200);
}

function resetJobForm(event) {
  event.preventDefault();
  state.editingConfigurationId = null;
  initializeParameterValues();
  renderParameters();
}

async function loadTemplates() {
  const response = await apiFetch("/api/templates");
  if (!response.ok) return;
  state.templates = await response.json();

  templateSelect.replaceChildren();
  for (const template of state.templates) {
    const option = document.createElement("option");
    option.value = template.id;
    option.textContent = template.name || template.code;
    templateSelect.append(option);
  }

  renderSelectedTemplate();
}

async function loadProjects(selectedProjectId = null) {
  const response = await apiFetch("/api/projects");
  if (!response.ok) return;

  const previousValue = selectedProjectId || projectSelect.value;
  state.projects = await response.json();
  projectSelect.replaceChildren();

  if (state.projects.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = "Создайте проект в ЛК";
    projectSelect.append(option);
    projectSelect.disabled = true;
    saveConfigurationButton.disabled = true;
    return;
  }

  for (const project of state.projects) {
    const option = document.createElement("option");
    option.value = project.id;
    option.textContent = project.name;
    projectSelect.append(option);
  }

  if (previousValue && state.projects.some(project => project.id === previousValue)) {
    projectSelect.value = previousValue;
  }

  projectSelect.disabled = false;
  saveConfigurationButton.disabled = false;
}

async function saveCurrentConfiguration() {
  if (!state.selectedTemplate) return;
  if (!projectSelect.value) {
    renderStatusError(["Сначала создайте проект в личном кабинете."]);
    return;
  }

  const parameters = collectParameters();
  const name = getConfigurationName(parameters);
  const editingConfigurationId = state.editingConfigurationId;
  const url = editingConfigurationId
    ? `/api/project-configurations/${encodeURIComponent(editingConfigurationId)}`
    : `/api/projects/${projectSelect.value}/configurations`;
  const response = await apiFetch(url, {
    method: editingConfigurationId ? "PUT" : "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      name,
      templateId: state.selectedTemplate.id,
      outputFormat: formatSelect.value,
      parameters
    })
  });

  if (!response.ok) {
    renderStatusError(await readProblem(response, "Не удалось сохранить конфигурацию"));
    return;
  }

  const savedConfiguration = await response.json();
  state.editingConfigurationId = savedConfiguration.id || editingConfigurationId;
  updateConfigurationNamePreview(parameters);
  statusPanel.className = "empty";
  statusPanel.textContent = editingConfigurationId
    ? "Конфигурация обновлена"
    : "Конфигурация сохранена в проект";
}

function applyConfiguration(configuration) {
  const template = state.templates.find(item => item.id === configuration.templateId);
  if (!template) {
    renderStatusError(["Шаблон этой конфигурации сейчас недоступен."]);
    return;
  }

  state.editingConfigurationId = configuration.id;
  templateSelect.value = template.id;
  renderSelectedTemplate();
  state.parameterValues = {
    ...state.parameterValues,
    ...(configuration.parameters || {})
  };
  if ([...formatSelect.options].some(option => option.value === configuration.outputFormat)) {
    formatSelect.value = configuration.outputFormat;
  }
  renderParameters();
  statusPanel.className = "empty";
  statusPanel.textContent = "Конфигурация загружена";
}

async function loadConfigurationFromUrl() {
  const configurationId = new URLSearchParams(window.location.search).get("configurationId");
  if (!configurationId) return;

  const response = await apiFetch(`/api/project-configurations/${encodeURIComponent(configurationId)}`);
  if (!response.ok) {
    renderStatusError(await readProblem(response, "Не удалось открыть конфигурацию"));
    return;
  }

  const configuration = await response.json();
  await loadProjects(configuration.projectId);
  applyConfiguration(configuration);
}

async function loadCurrentUser() {
  const response = await apiFetch("/api/auth/me");
  if (!response.ok) {
    state.currentUser = null;
    updateAuthView();
    return false;
  }

  state.currentUser = await response.json();
  updateAuthView();
  return isAuthenticated();
}

async function register(event) {
  event.preventDefault();
  registerStatus.hidden = true;
  registerStatus.textContent = "";

  const response = await apiFetch("/api/auth/register", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      userName: registerUserName.value,
      displayName: registerDisplayName.value,
      password: registerPassword.value
    })
  });

  if (!response.ok) {
    const messages = await readProblem(response, "Не удалось отправить заявку");
    registerStatus.hidden = false;
    registerStatus.className = "error";
    registerStatus.textContent = messages.join(" ");
    return;
  }

  registerForm.reset();
  registerStatus.hidden = false;
  registerStatus.className = "empty";
  registerStatus.textContent = "Заявка отправлена. Доступ появится после подтверждения администратором.";
}

async function login(event) {
  event.preventDefault();
  loginPassword.setCustomValidity("");

  const response = await apiFetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      userName: loginUserName.value,
      password: loginPassword.value
    })
  });

  if (!response.ok) {
    loginPassword.setCustomValidity("Неверный логин или пароль");
    loginPassword.reportValidity();
    return;
  }

  state.currentUser = await response.json();
  loginPassword.value = "";
  updateAuthView();
  await loadTemplates();
  await loadProjects();
  await refreshJobs();
  await loadConfigurationFromUrl();
}

async function logout() {
  await apiFetch("/api/auth/logout", { method: "POST" });
  state.currentUser = null;
  state.templates = [];
  state.projects = [];
  state.configurations = [];
  state.selectedTemplate = null;
  state.parameterValues = {};
  jobsTableBody.replaceChildren();
  parametersForm.replaceChildren();
  projectSelect.replaceChildren();
  updateConfigurationNamePreview();
  clearInterval(state.pollTimer);
  state.pollTimer = null;
  updateAuthView();
}

async function boot() {
  const authenticated = await loadCurrentUser();
  if (!authenticated) return;

  await loadTemplates();
  await loadProjects();
  await refreshJobs();
  await loadConfigurationFromUrl();
}

templateSelect.addEventListener("change", renderSelectedTemplate);
document.querySelector("#jobForm").addEventListener("submit", submitJob);
document.querySelector("#jobForm").addEventListener("reset", resetJobForm);
registerForm.addEventListener("submit", register);
loginForm.addEventListener("submit", login);
logoutButton.addEventListener("click", logout);
saveConfigurationButton.addEventListener("click", saveCurrentConfiguration);
showAllParametersToggle?.addEventListener("change", event => {
  state.showAllParameters = event.currentTarget.checked;
  applyParameterTabVisibility();
});

await boot();
