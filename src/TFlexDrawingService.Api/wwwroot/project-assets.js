export function normalizeProjectAssetName(value) {
  return String(value || "")
    .trim()
    .replace(/\s+/gu, " ")
    .toLocaleLowerCase("ru-RU");
}

function parseNumber(value) {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const number = Number(String(value).trim().replace(",", "."));
  return Number.isFinite(number) ? number : null;
}

export function getConfigurationTravelHeightMeters(configuration, template) {
  const parameters = configuration?.parameters || {};
  const templateParameters = template?.parameters || [];
  const travelHeightParameter = templateParameters.find(parameter => {
    const displayName = String(parameter?.displayName || "").toLocaleLowerCase("ru-RU");
    return ["TR", "$R", "HE"].includes(parameter?.name)
      || displayName.includes("высота подъема")
      || displayName.includes("высота подъёма");
  });
  const candidateNames = [...new Set([
    travelHeightParameter?.name,
    "TR",
    "$R",
    "HE"
  ].filter(Boolean))];
  const parameterName = candidateNames.find(name => {
    const value = parameters[name];
    return value !== null && value !== undefined && String(value).trim() !== "";
  });
  if (!parameterName) return null;

  const value = parseNumber(parameters[parameterName]);
  if (value === null) return null;

  const metadata = templateParameters.find(parameter => parameter?.name === parameterName)
    || travelHeightParameter;
  const unit = String(metadata?.unit || "").trim().toLocaleLowerCase("ru-RU");
  if (unit === "мм" || unit === "mm") return value / 1000;
  if (unit === "м" || unit === "m") return value;

  // Older templates did not always declare units. Elevator heights are stored
  // in metres, while escalator HE values are stored in millimetres.
  return Math.abs(value) >= 1000 ? value / 1000 : value;
}

export function formatProjectAssetTitle(name, travelHeightMeters, address) {
  const normalizedName = String(name || "Конфигурация").trim() || "Конфигурация";
  const height = parseNumber(travelHeightMeters);
  const normalizedAddress = String(address || "").trim();
  const heightSuffix = height === null ? "" : ` (${height.toFixed(1)})`;
  const addressSuffix = normalizedAddress ? ` - ${normalizedAddress}` : "";
  return `${normalizedName}${heightSuffix}${addressSuffix}`;
}

function getUpdatedTimestamp(value) {
  const timestamp = new Date(value || 0).getTime();
  return Number.isFinite(timestamp) ? timestamp : 0;
}

export function groupProjectAssets(
  configurations,
  pricingSpecifications,
  getConfigurationName = configuration => configuration?.name || "Конфигурация") {
  const groups = [];
  const drawingGroupsByName = new Map();
  const groupsByConfigurationId = new Map();

  const createGroup = (name, key) => {
    const group = {
      key,
      name: String(name || "Конфигурация").trim() || "Конфигурация",
      configurations: [],
      pricingSpecifications: [],
      updatedAt: null
    };
    groups.push(group);
    return group;
  };

  const touchGroup = (group, updatedAt) => {
    if (getUpdatedTimestamp(updatedAt) >= getUpdatedTimestamp(group.updatedAt)) {
      group.updatedAt = updatedAt || group.updatedAt;
    }
  };

  for (const configuration of configurations || []) {
    const name = getConfigurationName(configuration);
    const normalizedName = normalizeProjectAssetName(name);
    const group = createGroup(name, `drawing:${configuration.id}`);
    if (normalizedName) {
      const sameNameGroups = drawingGroupsByName.get(normalizedName) || [];
      sameNameGroups.push(group);
      drawingGroupsByName.set(normalizedName, sameNameGroups);
    }
    group.configurations.push(configuration);
    groupsByConfigurationId.set(String(configuration.id), group);
    touchGroup(group, configuration.updatedAt);
  }

  for (const specification of pricingSpecifications || []) {
    const normalizedName = normalizeProjectAssetName(specification.name);
    const linkedGroup = specification.projectConfigurationId
      ? groupsByConfigurationId.get(String(specification.projectConfigurationId))
      : null;
    const sameNameDrawingGroups = drawingGroupsByName.get(normalizedName) || [];
    // Name matching is retained for legacy prices only when it is unambiguous.
    // An explicit configuration id always wins; equally named drawings remain
    // separate so each one keeps its own format and action controls.
    let group = linkedGroup
      || (sameNameDrawingGroups.length === 1 ? sameNameDrawingGroups[0] : null);
    if (!group) {
      group = createGroup(specification.name, `pricing:${specification.id}`);
    }
    group.pricingSpecifications.push(specification);
    touchGroup(group, specification.updatedAt);
  }

  return groups;
}
