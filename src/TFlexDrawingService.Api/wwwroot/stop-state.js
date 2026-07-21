function toFiniteNumber(value) {
  if (value === true) return 1;
  if (value === false || value === null || value === undefined || value === "") return 0;
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function toFlag(value) {
  if (typeof value === "boolean") return value;
  const text = String(value ?? "").trim().toLowerCase();
  return text === "1" || text === "true" || text === "да";
}

export function clampStopCount(value) {
  return Math.min(48, Math.max(2, Math.trunc(toFiniteNumber(value)) || 2));
}

export function getStopRowKey(index, stops) {
  return index === stops ? "s_top" : `s${String(index).padStart(2, "0")}`;
}

export function getStopNameParameterName(index, stops) {
  return `${getStopRowKey(index, stops)}_name_1`;
}

export function getStopLevelParameterName(index, stops) {
  return `${getStopRowKey(index, stops)}_level_1`;
}

export function getMainSelectionMode(mainValue) {
  const automatic = toFlag(mainValue);
  return {
    automatic,
    manual: !automatic,
    radiosReadOnly: automatic
  };
}

export function resolveMainFloor({ mainValue, selectedMainFloor, lobbyStopIndex, stops }) {
  const mode = getMainSelectionMode(mainValue);
  const candidate = mode.automatic ? lobbyStopIndex : selectedMainFloor;
  const normalized = Math.trunc(toFiniteNumber(candidate)) || 1;
  return Math.min(clampStopCount(stops), Math.max(1, normalized));
}

export function calculateAutomaticStopLevel({ bottomLevel, travelHeightMeters, index, stops }) {
  const normalizedStops = clampStopCount(stops);
  const travelHeight = toFiniteNumber(travelHeightMeters) * 1000;
  const total = travelHeight > 0 ? travelHeight : (normalizedStops - 1) * 6000;
  return toFiniteNumber(bottomLevel)
    + Math.round((total * (index - 1)) / Math.max(1, normalizedStops - 1));
}

export function getAuthoritativeStopLevelValues({
  stops,
  manualLevels,
  values,
  bottomLevel,
  travelHeightMeters,
  hasParameter = () => true
}) {
  const normalizedStops = clampStopCount(stops);
  const result = {};

  for (let index = 1; index <= normalizedStops; index += 1) {
    const name = getStopLevelParameterName(index, normalizedStops);
    if (!hasParameter(name)) continue;

    if (toFlag(manualLevels) && index !== normalizedStops) {
      result[name] = values?.[name];
      continue;
    }

    result[name] = calculateAutomaticStopLevel({
      bottomLevel,
      travelHeightMeters,
      index,
      stops: normalizedStops
    });
  }

  return result;
}

export function collectStopParameterValues({ stops, values, hasParameter = () => true }) {
  const normalizedStops = clampStopCount(stops);
  const result = {};

  for (let index = 1; index <= normalizedStops; index += 1) {
    for (const name of [
      getStopNameParameterName(index, normalizedStops),
      getStopLevelParameterName(index, normalizedStops)
    ]) {
      if (hasParameter(name)) result[name] = values?.[name];
    }
  }

  return result;
}
