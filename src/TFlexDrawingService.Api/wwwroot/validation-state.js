function hasValue(value) {
  return value !== null && value !== undefined;
}

export function isValidationPassed(value) {
  if (!hasValue(value)) return false;
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return Number.isFinite(value) && value !== 0;
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    return normalized !== "" && normalized !== "0" && normalized !== "false" && normalized !== "нет";
  }

  return Boolean(value);
}

export function normalizeValidationSeverity(value) {
  return String(value || "error").trim().toLowerCase() === "warning" ? "warning" : "error";
}

export function isBlockingValidationIssue(issue) {
  return normalizeValidationSeverity(issue?.severity) !== "warning";
}

export function partitionValidationIssues(issues = []) {
  return {
    errors: issues.filter(isBlockingValidationIssue),
    warnings: issues.filter(issue => !isBlockingValidationIssue(issue))
  };
}
