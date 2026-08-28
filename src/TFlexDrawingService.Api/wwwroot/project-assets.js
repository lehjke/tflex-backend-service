export function normalizeProjectAssetName(value) {
  return String(value || "")
    .trim()
    .replace(/\s+/gu, " ")
    .toLocaleLowerCase("ru-RU");
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
  const groupsByName = new Map();
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
    const key = normalizedName || `drawing:${configuration.id}`;
    let group = groupsByName.get(normalizedName);
    if (!group || !normalizedName) {
      group = createGroup(name, key);
      if (normalizedName) groupsByName.set(normalizedName, group);
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
    let group = linkedGroup || groupsByName.get(normalizedName);
    if (!group) {
      const key = normalizedName || `pricing:${specification.id}`;
      group = createGroup(specification.name, key);
      if (normalizedName) groupsByName.set(normalizedName, group);
    }
    group.pricingSpecifications.push(specification);
    touchGroup(group, specification.updatedAt);
  }

  return groups;
}
