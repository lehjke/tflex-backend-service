export const FIELD_LABEL_OVERRIDES = new Map([
  ["AA", "Ширина кабины"],
  ["BB", "Глубина кабины"],
  ["HL", "Высота кабины (в чистоте)"],
  ["JJ", "Ширина дверей (в чистоте)"],
  ["EI", "Огнестойкость дверей"],
  ["A4", "Эксцентриситет дверей"],
  ["A1", "Противовес по ширине"],
  ["HH", "Высота дверей (в чистоте)"],
  ["BW", "Ширина противовеса"],
  ["WW", "Ширина противовеса"],
  ["WG", "Длина противовеса"],
  ["AH", "Ширина шахты"],
  ["BH", "Глубина шахты"],
  ["CB", "Расстояние от оси кабины до правой стены шахты"],
  ["CJ", "Эксцентриситет дверей"],
  ["NE", "Количество входов"],
  ["TR", "Высота подъема"],
  ["A3", "Расстояние от оси кабины до стенки без противовеса"],
  ["OH", "Высота оголовка"],
  ["PD", "Глубина приямка"],
  ["TJ", "Длина верхней входной площадки"],
  ["TK", "Длина нижней входной площадки"]
]);

const VALIDATION_EXPRESSION_LABELS = new Map([
  ["AA-JJ", "Разница между шириной кабины и шириной дверей (в чистоте)"],
  ["HL-HH", "Разница между высотой кабины и высотой дверей (в чистоте)"],
  ["AA*BB", "Площадь пола кабины"]
]);

function normalizeDisplayText(value) {
  return String(value || "").replace(/\s+/g, " ").trim();
}

function getDefinitionLabel(definition) {
  const name = String(definition?.name || "").trim();
  const displayName = normalizeDisplayText(definition?.displayName);
  const separatorIndex = displayName.indexOf("/");
  const displayLabel = normalizeDisplayText(
    separatorIndex >= 0 ? displayName.slice(separatorIndex + 1) : displayName);

  return FIELD_LABEL_OVERRIDES.get(displayLabel)
    || FIELD_LABEL_OVERRIDES.get(name)
    || displayLabel
    || name;
}

function escapeRegularExpression(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function replaceParameterName(message, parameterName, label) {
  if (!parameterName || !label || parameterName === label) return message;
  const escapedName = escapeRegularExpression(parameterName);
  const expression = new RegExp(
    `(?<![\\p{L}\\p{N}_$])${escapedName}(?![\\p{L}\\p{N}_$])`,
    "gu");
  return message.replace(expression, label);
}

export function expandValidationParameterNames(message, template) {
  const replacements = new Map([
    ...VALIDATION_EXPRESSION_LABELS,
    ...FIELD_LABEL_OVERRIDES
  ]);
  const definitions = [
    ...(template?.parameters || []),
    ...(template?.calculatedVariables || [])
  ];

  for (const definition of definitions) {
    const name = String(definition?.name || "").trim();
    const label = getDefinitionLabel(definition);
    if (!name || name.startsWith("$") || !label || label === name) continue;
    replacements.set(name, label);
  }

  let expandedMessage = String(message || "");
  const sortedReplacements = [...replacements.entries()]
    .sort(([left], [right]) => right.length - left.length);
  for (const [parameterName, label] of sortedReplacements) {
    expandedMessage = replaceParameterName(expandedMessage, parameterName, label);
  }

  return expandedMessage;
}
