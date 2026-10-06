import { getLanguage, t } from "./i18n.js?v=20260924-sidebar-collapse-1";
import { isPdfFile, openGeneratedFilePreview } from "./file-preview.js?v=20260924-sidebar-collapse-1";
import { evaluateTFlexExpression } from "./safe-expression.js?v=20260828-speed-dependent-oh-pd-1";
import { createSessionRequestGuard } from "./session-requests.js?v=20260720-ui-hardening-1";
import { createNativePreviewController } from "./native-preview.js?v=20261004-native-preview-3";
import { renderLiveSvgPreview, renderLiveSvgPreviewMetrics } from "./live-svg-preview.js?v=20261005-unfolding-panels-1";
import {
  calculateAutomaticStopLevel,
  clampStopCount,
  collectStopParameterValues,
  getAuthoritativeStopLevelValues,
  getMainSelectionMode,
  getStopLevelParameterName,
  getStopNameParameterName,
  getStopRowKey,
  isSignedIntegerDraft,
  isSignedStopIntegerParameterName,
  resolveMainFloor
} from "./stop-state.js?v=20260827-lobby-units-1";
import {
  isBlockingValidationIssue,
  isValidationPassed,
  normalizeValidationSeverity,
  partitionValidationIssues
} from "./validation-state.js?v=20260721-validation-parity-1";
import {
  expandValidationParameterNames,
  FIELD_LABEL_OVERRIDES
} from "./parameter-labels.js?v=20260830-readable-validation-errors-1";

const state = {
  templates: [],
  selectedTemplate: null,
  parameterValues: {},
  validationFieldNames: new Set(),
  activeJobId: null,
  pollTimer: null,
  pollRequestToken: null,
  pollFailureCount: 0,
  pollErrorAnnounced: false,
  lastRenderedJobFingerprint: "",
  latestJob: null,
  jobs: [],
  jobsVisibleCount: 5,
  pendingRenderFrame: null,
  pendingFocusTarget: null,
  activeParameterCategory: null,
  showAllParameters: true,
  currentUser: null,
  projects: [],
  configurations: [],
  editingConfigurationId: null,
  sellerRequestHasDeviations: false,
  engineerRequest: null
};
const sessionRequests = createSessionRequestGuard();
let pageLoadErrorContext = "load";
let sellerRequestCheckTimer = null;
let sellerRequestCheckSequence = 0;

const guestMain = document.querySelector("#guestMain");
const appMain = document.querySelector("#appMain");
const editorHeading = document.querySelector("#editorHeading");
const pageSkeleton = document.querySelector("#pageSkeleton");
const pageLoadError = document.querySelector("#pageLoadError");
const pageLoadErrorTitle = document.querySelector("#pageLoadErrorTitle");
const pageLoadErrorMessage = document.querySelector("#pageLoadErrorMessage");
const retryPageLoadButton = document.querySelector("#retryPageLoadButton");
const loginForm = document.querySelector("#loginForm");
const loginUserName = document.querySelector("#loginUserName");
const loginPassword = document.querySelector("#loginPassword");
const guestLoginPanel = document.querySelector("#guestLoginPanel");
const registerPanel = document.querySelector("#registerPanel");
const guestLoginForm = document.querySelector("#guestLoginForm");
const showRegisterPanelButton = document.querySelector("#showRegisterPanel");
const showLoginPanelButton = document.querySelector("#showLoginPanel");
const registerForm = document.querySelector("#registerForm");
const registerUserName = document.querySelector("#registerUserName");
const registerDisplayName = document.querySelector("#registerDisplayName");
const registerPassword = document.querySelector("#registerPassword");
const registerStatus = document.querySelector("#registerStatus");
const userPanel = document.querySelector("#userPanel");
const currentUserName = document.querySelector("#currentUserName");
const currentUserRoleLabel = document.querySelector("#currentUserRoleLabel");
const roleAccessNote = document.querySelector("#roleAccessNote");
const adminNavLinks = document.querySelectorAll(".admin-only-nav");
const logoutButton = document.querySelector("#logoutButton");
const templateSelect = document.querySelector("#templateSelect");
const formatSelect = document.querySelector("#formatSelect");
const globalSearchInput = document.querySelector(".global-search input");
const parametersForm = document.querySelector("#parametersForm");
const submitButton = document.querySelector("#submitButton");
const requestEngineerButton = document.querySelector("#requestEngineerButton");
const engineerRequestDescriptionField = document.querySelector("#engineerRequestDescriptionField");
const engineerRequestDescription = document.querySelector("#engineerRequestDescription");
const engineerRequestStatus = document.querySelector("#engineerRequestStatus");
const previewResultButton = document.querySelector("#previewResultButton");
const downloadResultButton = document.querySelector("#downloadResultButton");
const statusPanel = document.querySelector("#statusPanel");
const jobsTableBody = document.querySelector("#jobsTableBody");
const showMoreJobsButton = document.querySelector("#showMoreJobsButton");
const validationPanel = document.querySelector("#validationPanel");
const parameterTabs = document.querySelector("#parameterTabs");
const parameterTabsPrevious = document.querySelector("#parameterTabsPrevious");
const parameterTabsNext = document.querySelector("#parameterTabsNext");
const showAllParametersToggle = document.querySelector("#showAllParametersToggle");
const parameterReadyBanner = document.querySelector("#parameterReadyBanner");
const previewPanelTitle = document.querySelector("#previewPanelTitle");
const shaftPreviewSubtitle = document.querySelector("#shaftPreviewSubtitle");
const shaftPreviewUnavailable = document.querySelector("#shaftPreviewUnavailable");
const shaftPreviewContent = document.querySelector("#shaftPreviewContent");
const shaftPreviewCanvas = document.querySelector("#shaftPreviewCanvas");
const shaftCollisionStatus = document.querySelector("#shaftCollisionStatus");
const shaftPreviewMetrics = document.querySelector("#shaftPreviewMetrics");
const projectSelect = document.querySelector("#projectSelect");
const saveConfigurationButton = document.querySelector("#saveConfigurationButton");
const configurationNamePreview = document.querySelector("#configurationNamePreview");
const nativePreviewStatus = document.querySelector("#nativePreviewStatus");
const nativePreviewFrame = document.querySelector("#nativePreviewFrame");
const nativePreviewOpen = document.querySelector("#nativePreviewOpen");
const nativePreviewRetry = document.querySelector("#nativePreviewRetry");
const previewModeSvg = document.querySelector("#previewModeSvg");
const previewModePdf = document.querySelector("#previewModePdf");
const svgPreviewStatus = document.querySelector("#svgPreviewStatus");
const svgPreviewCanvas = document.querySelector("#svgPreviewCanvas");
let previewMode = "pdf";
let svgPreviewSnapshotKey = null;
const nativePreviewController = createNativePreviewController({
  fetch: (url, options) => apiFetch(url, options),
  onStatus: (message, retryable = false) => {
    if (nativePreviewStatus) nativePreviewStatus.textContent = message;
    if (nativePreviewRetry) nativePreviewRetry.hidden = !retryable;
  },
  onPdf: url => {
    if (!nativePreviewFrame || !nativePreviewOpen) return;
    if (!url && nativePreviewRetry) nativePreviewRetry.hidden = true;
    nativePreviewFrame.hidden = !url;
    nativePreviewOpen.hidden = !url;
    if (url) {
      const page = state.selectedTemplate?.id === "razvertki_lehy" ? 1 : 2;
      const pdfUrl = new URL(url, window.location.origin);
      pdfUrl.hash = "";
      pdfUrl.searchParams.set("inline", "true");
      const file = encodeURIComponent(`${pdfUrl.pathname}${pdfUrl.search}`);
      const template = encodeURIComponent(state.selectedTemplate?.id || "");
      nativePreviewFrame.src = `/native-pdf-viewer.html?file=${file}&page=${page}&mode=crop&template=${template}&v=20261005-preview-fixes-3`;
      nativePreviewOpen.href = `/native-pdf-viewer.html?file=${file}&page=${page}&mode=sheet&template=${template}&v=20261005-preview-fixes-3`;
    } else {
      nativePreviewFrame.removeAttribute("src");
      nativePreviewOpen.removeAttribute("href");
    }
  }
});
nativePreviewRetry?.addEventListener("click", () => nativePreviewController.retry());

const STOP_CONTROL_NAMES = new Set(["main", "name", "level", "main_floor"]);
const FRONTEND_HIDDEN_PARAMETER_NAMES = new Set(["$Oboznach", "$ver"]);
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
const ESCALATOR_PREVIEW_SUPPORTED_TEMPLATE_PREFIXES = [
  "k_ii_type"
];
function isAuthenticated() {
  return Boolean(state.currentUser?.isAuthenticated);
}

function canCreateJobs() {
  const roles = state.currentUser?.roles || [];
  return roles.includes("Admin") || roles.includes("Engineer");
}

function isSeller() {
  return (state.currentUser?.roles || []).includes("Seller");
}

function canAdmin() {
  return (state.currentUser?.roles || []).includes("Admin");
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

function getConfigurationNameParameter() {
  for (const name of CONFIGURATION_NAME_PARAMETER_NAMES) {
    const parameter = getParameterDefinition(name);
    if (parameter) return parameter;
  }

  return null;
}

function rememberConfigurationNameValue() {
  if (!configurationNamePreview || !state.selectedTemplate) return;
  const parameter = getConfigurationNameParameter();
  if (!parameter) return;

  state.parameterValues[parameter.name] = configurationNamePreview.value;
}

function updateConfigurationNamePreview(parameters = state.parameterValues) {
  if (!configurationNamePreview) return;
  if (!state.selectedTemplate) {
    configurationNamePreview.disabled = true;
    configurationNamePreview.value = "-";
    return;
  }

  configurationNamePreview.disabled = false;
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

function hidePageSkeleton() {
  if (pageSkeleton) {
    pageSkeleton.removeAttribute("aria-busy");
    pageSkeleton.hidden = true;
  }
}

function focusBootDestination(authenticated) {
  const target = authenticated
    ? editorHeading
    : guestLoginForm?.querySelector("[name='userName']");
  requestAnimationFrame(() => target?.focus({ preventScroll: true }));
}

function syncPageLoadErrorCopy(context = pageLoadErrorContext) {
  pageLoadErrorContext = context;
  const isLogoutRecovery = context === "logout";
  if (pageLoadErrorTitle) {
    pageLoadErrorTitle.textContent = t(isLogoutRecovery
      ? "Выход не подтвержден"
      : "Не удалось загрузить конфигуратор");
  }
  if (pageLoadErrorMessage) {
    pageLoadErrorMessage.textContent = t(isLogoutRecovery
      ? "Сервер не подтвердил выход. Сессия могла сохраниться. Проверьте состояние еще раз."
      : "Проверьте соединение с API и повторите загрузку.");
  }
  if (retryPageLoadButton) {
    retryPageLoadButton.textContent = t(isLogoutRecovery ? "Проверить снова" : "Повторить");
  }
}

function showPageLoadFailure({ focus = false, context = "load" } = {}) {
  guestMain.hidden = true;
  appMain.hidden = true;
  hidePageSkeleton();
  syncPageLoadErrorCopy(context);
  if (pageLoadError) pageLoadError.hidden = false;
  if (retryPageLoadButton) {
    retryPageLoadButton.disabled = false;
    if (focus) requestAnimationFrame(() => retryPageLoadButton.focus({ preventScroll: true }));
  }
}

function updateAuthView() {
  const authenticated = isAuthenticated();
  const isAdmin = authenticated && canAdmin();
  const roles = state.currentUser?.roles || [];
  const visibleRole = roles.includes("Admin")
    ? "Admin"
    : roles.includes("Engineer")
      ? "Engineer"
      : roles.includes("Seller")
        ? "Seller"
        : "";
  guestMain.hidden = authenticated;
  loginForm.hidden = true;
  userPanel.hidden = !authenticated;
  appMain.hidden = !authenticated;
  const requestMode = Boolean(state.engineerRequest);
  submitButton.hidden = requestMode || authenticated && (!canCreateJobs() || isSeller() && state.sellerRequestHasDeviations);
  if (requestEngineerButton) requestEngineerButton.hidden = requestMode || !authenticated || !isSeller() || !state.sellerRequestHasDeviations;
  saveConfigurationButton.hidden = requestMode ? false : !authenticated || !canCreateJobs();
  saveConfigurationButton.textContent = requestMode ? "Сохранить параметры заявки" : "Сохранить конфигурацию";
  adminNavLinks.forEach(link => {
    link.hidden = !isAdmin;
  });

  if (authenticated) {
    currentUserName.textContent = state.currentUser.displayName || state.currentUser.userName;
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = !visibleRole;
      currentUserRoleLabel.textContent = visibleRole;
    }
    if (roleAccessNote) {
      const readOnly = visibleRole === "Seller";
      roleAccessNote.hidden = !readOnly;
      roleAccessNote.textContent = readOnly
        ? t("Режим продавца: отправьте параметры инженеру на проверку.")
        : "";
    }
  } else {
    currentUserName.textContent = "";
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = true;
      currentUserRoleLabel.textContent = "";
    }
    if (roleAccessNote) {
      roleAccessNote.hidden = true;
      roleAccessNote.textContent = "";
    }
  }
}

function clearEditorSessionState() {
  sessionRequests.invalidate();
  clearTimeout(sellerRequestCheckTimer);
  sellerRequestCheckSequence++;
  state.sellerRequestHasDeviations = false;
  if (state.pendingRenderFrame !== null) {
    cancelAnimationFrame(state.pendingRenderFrame);
  }
  clearTimeout(state.pollTimer);

  state.templates = [];
  state.selectedTemplate = null;
  state.parameterValues = {};
  state.validationFieldNames = new Set();
  state.activeJobId = null;
  state.pollTimer = null;
  state.pollRequestToken = null;
  state.pollFailureCount = 0;
  state.pollErrorAnnounced = false;
  state.lastRenderedJobFingerprint = "";
  state.latestJob = null;
  state.jobs = [];
  state.jobsVisibleCount = 5;
  state.pendingRenderFrame = null;
  state.pendingFocusTarget = null;
  state.activeParameterCategory = null;
  state.projects = [];
  state.configurations = [];
  state.editingConfigurationId = null;
  state.engineerRequest = null;
  document.querySelector("#projectField").hidden = false;
  document.querySelector("#configurationField").hidden = false;
  document.querySelector(".reset-job-button").hidden = false;
  previewResultButton.hidden = false;
  downloadResultButton.hidden = false;
  templateSelect.disabled = false;
  formatSelect.disabled = false;
  editorHeading.textContent = "Конфигуратор чертежей";

  loginForm?.reset();
  guestLoginForm?.reset();
  registerForm?.reset();
  document.querySelector("#jobForm")?.reset();
  loginPassword?.setCustomValidity("");
  guestLoginForm?.querySelector("[name='password']")?.setCustomValidity("");
  if (registerStatus) {
    registerStatus.hidden = true;
    registerStatus.textContent = "";
  }
  if (globalSearchInput) globalSearchInput.value = "";

  jobsTableBody.replaceChildren();
  templateSelect.replaceChildren();
  formatSelect.replaceChildren();
  projectSelect.replaceChildren();
  parametersForm.replaceChildren();
  parameterTabs?.replaceChildren();
  updateValidationPanel();
  updateConfigurationNamePreview();
  updateDownloadResultButton(null);
  if (parameterReadyBanner) parameterReadyBanner.hidden = true;
  statusPanel.className = "empty";
  statusPanel.textContent = "";
  statusPanel.setAttribute("role", "status");
  statusPanel.setAttribute("aria-live", "polite");
  statusPanel.setAttribute("aria-busy", "false");
  updateShaftPreview(null);

  const currentUrl = new URL(window.location.href);
  if (currentUrl.searchParams.has("configurationId")) {
    currentUrl.searchParams.delete("configurationId");
    window.history.replaceState(null, "", currentUrl);
  }
}

function showAuthPanel(panel) {
  const showRegister = panel === "register";
  if (guestLoginPanel) guestLoginPanel.hidden = showRegister;
  if (registerPanel) registerPanel.hidden = !showRegister;

  requestAnimationFrame(() => {
    const target = showRegister ? registerUserName : guestLoginForm?.querySelector("[name='userName']");
    target?.focus({ preventScroll: true });
  });
}

async function apiFetch(url, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers || {});
  if (method !== "GET" && method !== "HEAD") {
    headers.set("X-TFlex-Requested-With", "fetch");
  }

  const response = await sessionRequests.fetch(url, {
    credentials: "same-origin",
    ...options,
    headers
  });

  if (sessionRequests.isCurrent(response) && response.status === 401) {
    clearEditorSessionState();
    state.currentUser = null;
    updateAuthView();
  }

  return response;
}

async function readProblem(response, fallback) {
  if (!sessionRequests.isCurrent(response)) return [];

  try {
    const problem = await sessionRequests.readJson(response);
    if (problem === sessionRequests.stalePayload) return [];
    const errors = Object.values(problem.errors || {}).flatMap(value => Array.isArray(value) ? value : [value]);
    return errors.length ? errors : [problem.detail || problem.message || problem.title || fallback];
  } catch {
    return [fallback];
  }
}

function formatDate(value) {
  if (!value) return "";
  return new Intl.DateTimeFormat(getLanguage() === "en" ? "en-GB" : "ru-RU", {
    dateStyle: "short",
    timeStyle: "medium"
  }).format(new Date(value));
}

function normalizeSearch(value) {
  return String(value || "").trim().toLowerCase();
}

function getResultFileFormat(file) {
  const format = String(file?.format || "").trim().replace(/^\./, "");
  if (format) return format.toUpperCase();

  const extension = String(file?.fileName || "").split(".").pop();
  return extension ? extension.toUpperCase() : "";
}

function getPreferredResultFile(files = []) {
  if (!files.length) return null;

  const currentFormat = String(formatSelect?.value || "").trim().toUpperCase();
  return files.find(file => getResultFileFormat(file) === "PDF")
    || files.find(file => currentFormat && getResultFileFormat(file) === currentFormat)
    || files[0];
}

function updateDownloadResultButton(job = undefined) {
  if (!downloadResultButton) return;

  const sourceJob = job === undefined ? state.latestJob : job;
  const file = getPreferredResultFile(sourceJob?.resultFiles || []);
  if (!file?.downloadUrl) {
    const expectedFormat = String(formatSelect?.value || "pdf").toUpperCase();
    downloadResultButton.disabled = true;
    downloadResultButton.dataset.downloadUrl = "";
    downloadResultButton.textContent = `Скачать ${expectedFormat}`;
    if (previewResultButton) {
      previewResultButton.disabled = true;
      previewResultButton.dataset.downloadUrl = "";
      previewResultButton.dataset.fileName = "";
    }
    return;
  }

  const format = getResultFileFormat(file) || "файл";
  downloadResultButton.disabled = false;
  downloadResultButton.dataset.downloadUrl = file.downloadUrl;
  downloadResultButton.textContent = `Скачать ${format}`;

  if (previewResultButton) {
    const pdf = (sourceJob?.resultFiles || []).find(isPdfFile);
    previewResultButton.disabled = !pdf?.downloadUrl;
    previewResultButton.dataset.downloadUrl = pdf?.downloadUrl || "";
    previewResultButton.dataset.fileName = pdf?.fileName || "";
    previewResultButton.dataset.format = pdf?.format || "pdf";
  }
}

function getEditorSearchQuery() {
  return normalizeSearch(globalSearchInput?.value);
}

function matchesSearchValue(values, query) {
  if (!query) return true;
  return values.some(value => normalizeSearch(value).includes(query));
}

function matchesProjectOption(project, query) {
  return matchesSearchValue([
    project.name,
    project.address,
    project.factoryRequestNumber,
    project.description,
    project.ownerUserName,
    project.id
  ], query);
}

function getProjectOptionLabel(project) {
  const ownerUserName = project.ownerUserName || project.OwnerUserName || "";
  return (canAdmin() || state.currentUser?.roles?.includes("Engineer")) && ownerUserName && ownerUserName !== state.currentUser?.userName
    ? `${project.name} · ${ownerUserName}`
    : project.name;
}

function matchesTemplateOption(template, query) {
  return matchesSearchValue([
    template.name,
    template.code,
    template.id
  ], query);
}

function matchesJobSearch(job, query) {
  return matchesSearchValue([
    job.id,
    job.templateId,
    getTemplateLabel(job.templateId),
    job.status,
    job.outputFormat,
    formatDate(job.createdAt),
    formatDate(job.finishedAt),
    ...(job.resultFiles || []).flatMap(file => [file.fileName, file.format])
  ], query);
}

function applySelectSearch(select, options, matcher, query) {
  if (!select) return;

  for (const option of select.options) {
    if (!option.value) {
      option.hidden = false;
      continue;
    }

    const item = options.find(candidate => candidate.id === option.value || candidate.code === option.value);
    const isCurrent = option.value === select.value;
    option.hidden = Boolean(query) && !isCurrent && !matcher(item || {}, query);
  }
}

function applyEditorSearch() {
  const query = getEditorSearchQuery();
  applySelectSearch(projectSelect, state.projects, matchesProjectOption, query);
  applySelectSearch(templateSelect, state.templates, matchesTemplateOption, query);
  renderJobs();
}

function getParameterType(parameter) {
  return (parameter.type || "string").toLowerCase();
}

function acceptsDecimalInput(parameter) {
  return parameter?.name === "TR";
}

function parseDecimalValue(value) {
  if (value === "" || value === null || value === undefined) return null;
  const normalized = String(value).trim().replace(",", ".");
  const number = Number(normalized);
  return Number.isFinite(number) ? number : null;
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
  if (acceptsDecimalInput(parameter)) return parseDecimalValue(input.value);
  if (type === "integer") {
    if (input.value === "") return null;
    const value = Number.parseInt(input.value, 10);
    return Number.isFinite(value) ? value : null;
  }
  if (type === "number") return parseDecimalValue(input.value);
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

  rememberConfigurationNameValue();

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

function evaluateFormulaExpression(expression, context) {
  return evaluateTFlexExpression(expression, context, {
    lookupTables: state.selectedTemplate?.lookupTables
  });
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
  if (["un_victor_mrl", "un_victor_mrl_t"].includes(state.selectedTemplate?.id)) {
    const tableByParameter = { $V: "Speed", OP: "Doorwidth", DOP: "Dop", CH: "Carheight", OPH: "Doorheight" };
    const tableName = tableByParameter[parameter.name];
    const rows = state.selectedTemplate.lookupTables?.[tableName];
    if (rows?.length) {
      const conditions = {
        $V: { DL: context?.DL },
        OP: {
          CARTYPE: context?.$CARTYPE ?? String(context?.$CARTYPE_MENU || "").split("/")[0].trim(),
          DOOR: context?.$DOOR,
          NBENT: context?.NBENT
        },
        DOP: { DOOR: context?.$DOOR, CW: context?.CW, OP: context?.OP, NBENT: context?.NBENT },
        CH: { CEIL: context?.$CEIL },
        OPH: { CEIL: context?.$CEIL, CH: context?.CH }
      }[parameter.name];
      const filteredRows = rows.filter(row => Object.entries(conditions).every(([key, value]) =>
        !hasValue(value) || isLookupMatch(row[key], value)));
      const resultName = parameter.name === "$V" ? "V" : parameter.name;
      const values = [...new Set(filteredRows.map(row => String(row[resultName])).filter(value => value !== "undefined"))];
      return values;
    }
  }

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
  // Legacy shaft-door fields must not overwrite the cabin-unfolding A4 formula.
  if (hasValue(context.$door_type)) {
    context.$A4 = context.$door_type === "\u0422\u041e" ? "\u041d\u0435\u0442" : (context.$A4_1 || "\u041d\u0435\u0442");

    if (context.$door_type === "\u0422\u041e") {
      context.A4 = Math.abs(toNumber(context.AA) / 2 - (toNumber(context.JJ) / 2 + 25));
    } else {
      context.A4 = context.$A4_1 === "\u041d\u0435\u0442" ? 0 : toNumber(context.A4_1);
    }

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
    ...state.selectedTemplate.parameters.filter(parameter =>
      parameter.isReadOnly && !calculatedNames.has(parameter.name))
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

  const stopsDefinition = getParameterDefinition("stops");
  if (stopsDefinition) {
    const stops = clampStopCount(getParameterValue(stopsDefinition));
    synchronizeAutomaticStopState(stops);
    synchronizeAutomaticStopLevels(stops);
  }

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

function getPreviewNumber(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  const number = toNumber(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}

function getPreviewOptionalNumber(context, name) {
  const value = context?.[name] ?? getParameterValueByName(name);
  if (!hasValue(value) || String(value).trim() === "") return null;
  const number = Number(String(value).replace(",", "."));
  return Number.isFinite(number) ? number : null;
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
  if (place === "справа" || place === "направо" || place === "right" || place === "1") {
    return { label: place === "направо" ? "Направо" : "Справа", mirrorX: true };
  }

  return { label: place === "налево" ? "Налево" : "Слева", mirrorX: false };
}

function getRearDoorDirection(context) {
  const direction = normalizePreviewToken(getPreviewTextValue(context, "$s_1", "s_1"));
  const opensRight = direction === "направо" || direction === "right" || direction === "1";
  return {
    value: opensRight ? "right" : "left",
    label: opensRight ? "Направо" : "Налево"
  };
}

function getPreviewLayoutType(template = state.selectedTemplate) {
  const marker = [template?.id, template?.code, template?.name]
    .filter(Boolean)
    .map(value => String(value).toLowerCase())
    .join(" ");

  if (marker.includes("rear") || marker.includes("back") || marker.includes("зад")) {
    return "rear";
  }

  return "side";
}

function isLehyProTemplate(template = state.selectedTemplate) {
  return template?.id === "lehy_pro_side_cwt"
    || template?.id === "lehy_pro_rear_cwt";
}

function getPreviewVariantNumber(context, prefix, fallbackNames = []) {
  const visibleVariant = findLevelVariant(prefix, context);
  if (visibleVariant) {
    const value = getPreviewNumber(context, visibleVariant.name);
    if (value) return value;
  }

  for (const name of fallbackNames) {
    const value = getPreviewNumber(context, name);
    if (value) return value;
  }

  return null;
}

function getPreviewKk(context, centerOpeningDoor) {
  const kk = getPreviewNumber(context, "KK");
  if (kk) return kk;

  if (state.selectedTemplate?.id === "lehy_pro_side_cwt") {
    return centerOpeningDoor ? 80 : 45;
  }

  return centerOpeningDoor ? 55 : 45;
}

function clampPreviewNumber(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function formatPreviewNumber(value) {
  if (!Number.isFinite(value)) return "-";
  return String(Math.round(value));
}

function updateShaftPreview(context = null) {
  if (!nativePreviewController) return;
  if (previewPanelTitle) previewPanelTitle.textContent = previewMode === "svg" && state.selectedTemplate?.id === "razvertki_lehy"
    ? "Предпросмотр развёрток" : "Предпросмотр плана";
  const allowed = Boolean(state.selectedTemplate && isAuthenticated() && canCreateJobs() && !state.engineerRequest);
  if (!allowed) {
    nativePreviewController.invalidate();
    if (svgPreviewCanvas) { svgPreviewCanvas.replaceChildren(); svgPreviewCanvas.hidden = true; }
    if (svgPreviewStatus) { svgPreviewStatus.hidden = true; svgPreviewStatus.textContent = ""; }
    if (nativePreviewStatus) nativePreviewStatus.hidden = false;
    if (nativePreviewStatus) nativePreviewStatus.textContent = !state.selectedTemplate
      ? "Выберите шаблон, чтобы увидеть предпросмотр PDF."
      : isAuthenticated()
        ? "Предпросмотр PDF доступен только ролям Admin и Engineer."
        : "Войдите как Admin или Engineer для предпросмотра PDF.";
    return;
  }
  if (previewMode === "svg") {
    const parameters = collectParameters();
    const snapshotKey = JSON.stringify({ templateId: state.selectedTemplate.id, parameters });
    nativePreviewController.invalidate({ preserveReady: svgPreviewSnapshotKey === null || svgPreviewSnapshotKey === snapshotKey });
    svgPreviewSnapshotKey = snapshotKey;
    if (nativePreviewFrame) nativePreviewFrame.hidden = true;
    if (nativePreviewOpen) nativePreviewOpen.hidden = true;
    if (nativePreviewStatus) nativePreviewStatus.hidden = true;
    if (nativePreviewRetry) nativePreviewRetry.hidden = true;
    if (svgPreviewCanvas && svgPreviewStatus) {
      const previewContext = context || buildLevelContext();
      const svg = renderLiveSvgPreview(state.selectedTemplate, previewContext);
      const metrics = renderLiveSvgPreviewMetrics(state.selectedTemplate, previewContext);
      const parametersOpen = svgPreviewCanvas.querySelector(".shaft-preview__parameters")?.open === true;
      svgPreviewCanvas.innerHTML = svg
        ? `<div class="shaft-preview"><div class="shaft-preview__canvas">${svg}</div><details class="shaft-preview__parameters"${parametersOpen ? " open" : ""}><summary>Параметры превью</summary><dl class="shaft-preview__metrics">${metrics}</dl></details></div>`
        : "";
      svgPreviewCanvas.hidden = !svg;
      svgPreviewStatus.hidden = Boolean(svg);
      svgPreviewStatus.textContent = svg ? "" : "Схема недоступна: проверьте параметры геометрии.";
      if (shaftPreviewSubtitle) shaftPreviewSubtitle.textContent = "Схема по текущим параметрам";
    }
    return;
  }
  if (nativePreviewStatus) nativePreviewStatus.hidden = false;
  if (svgPreviewCanvas) svgPreviewCanvas.hidden = true;
  if (svgPreviewStatus) svgPreviewStatus.hidden = true;
  if (shaftPreviewSubtitle) shaftPreviewSubtitle.textContent = "План из оригинального чертежа";
  nativePreviewController.schedule({
    allowed: true,
    templateId: state.selectedTemplate.id,
    parameters: collectParameters()
  });
}

function setPreviewMode(mode) {
  if (mode === "svg" && previewMode !== "svg") svgPreviewSnapshotKey = null;
  previewMode = mode;
  previewModeSvg?.setAttribute("aria-pressed", String(mode === "svg"));
  previewModePdf?.setAttribute("aria-pressed", String(mode === "pdf"));
  updateShaftPreview();
}

previewModeSvg?.addEventListener("click", () => setPreviewMode("svg"));
previewModePdf?.addEventListener("click", () => setPreviewMode("pdf"));

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

function getValidationInput(fieldNames = []) {
  return [...parametersForm.querySelectorAll("input, select, textarea")]
    .find(input => fieldNames.includes(input.dataset.parameterName || input.name));
}

function ensureValidationInputId(input, fieldName = "parameter") {
  if (input.id) return input.id;
  const safeName = String(fieldName).replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "") || "parameter";
  input.id = `parameter-${safeName}`;
  return input.id;
}

function getShaftPreviewGeometryErrors(context) {
  if (!isLehyProTemplate() || getPreviewLayoutType() !== "rear") return [];

  const cc = getPreviewNumber(context, "CC");
  const bb = getPreviewNumber(context, "BB");
  const ww = getPreviewNumber(context, "WW") || getPreviewVariantNumber(context, "WW_", [
    "WW_1",
    "WW_11",
    "WW_12",
    "WW_2",
    "WW_21",
    "WW_22",
    "WW_3",
    "WW_31",
    "WW_32"
  ]);
  const entrances = Math.max(1, Math.round(getPreviewNumber(context, "NE") || 1));
  const rearWall = entrances > 1
    ? (getPreviewNumber(context, "DK") || 141)
    : (getPreviewNumber(context, "bb") || 30);

  if (!cc || !bb || !ww) return [];

  const availableDepth = cc - bb / 2 - rearWall;
  const requiredDepth = ww + 30;
  if (availableDepth >= requiredDepth) return [];

  const deficit = Math.max(1, Math.ceil(requiredDepth - availableDepth));
  return [{
    name: "rear_counterweight_clearance",
    message: `Недостаточно места для заднего противовеса. Требуется еще ${deficit} мм.`,
    fieldNames: ["CC", "BB", "WW", entrances > 1 ? "DK" : "bb", "NE"]
  }];
}

function applyValidationHighlights(errors = []) {
  state.validationFieldNames = collectValidationFieldNames(errors);

  for (const message of parametersForm.querySelectorAll("[data-validation-inline]")) {
    message.remove();
  }

  for (const field of parametersForm.querySelectorAll(".field--invalid")) {
    field.classList.remove("field--invalid");
  }

  for (const input of parametersForm.querySelectorAll("input, select, textarea")) {
    input.classList.remove("is-invalid");
    input.removeAttribute("aria-invalid");
    const describedBy = (input.getAttribute("aria-describedby") || "")
      .split(/\s+/)
      .filter(Boolean)
      .filter(id => id !== "validationPanel" && !id.startsWith("validation-error-"));
    if (describedBy.length) {
      input.setAttribute("aria-describedby", describedBy.join(" "));
    } else {
      input.removeAttribute("aria-describedby");
    }

    const name = input.dataset.parameterName || input.name;
    if (!state.validationFieldNames.has(name)) continue;

    const issue = errors.find(error => (error.fieldNames || []).includes(name));
    const inputId = ensureValidationInputId(input, name);
    const errorId = `validation-error-${inputId}`;

    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
    input.setAttribute("aria-describedby", [...describedBy, errorId, "validationPanel"].join(" "));
    const field = input.closest(".field");
    field?.classList.add("field--invalid");

    const message = document.createElement("span");
    message.id = errorId;
    message.className = "field-error-message";
    message.dataset.validationInline = "true";
    message.textContent = issue?.message || t("Проверьте значение параметра.");
    (field || input.closest("td") || input.parentElement)?.append(message);
  }
}

function getCurrentValidationIssues(context = buildLevelContext()) {
  const rules = state.selectedTemplate?.validationRules || [];
  const issues = [];
  const seenMessages = new Set();

  for (const rule of rules) {
    const result = evaluateFormulaExpression(rule.expression, context);
    if (isValidationPassed(result)) continue;

    const message = expandValidationParameterNames(
      formatValidationMessage(rule.message, context),
      state.selectedTemplate);
    if (seenMessages.has(message)) continue;

    seenMessages.add(message);
    issues.push({
      name: rule.name,
      message,
      fieldNames: getValidationFieldNames(rule),
      severity: normalizeValidationSeverity(rule.severity)
    });
  }

  for (const error of getShaftPreviewGeometryErrors(context)) {
    if (seenMessages.has(error.message)) continue;
    seenMessages.add(error.message);
    issues.push({ ...error, severity: "error" });
  }

  return issues;
}

function appendValidationIssueGroup(titleText, issues, severity) {
  if (issues.length === 0) return;

  const group = document.createElement("section");
  group.className = `validation-panel__group validation-panel__group--${severity}`;

  const title = document.createElement("h3");
  title.className = "validation-panel__title";
  title.textContent = titleText;

  const list = document.createElement("ul");
  list.className = "validation-panel__list";

  for (const issue of issues) {
    const item = document.createElement("li");
    const input = getValidationInput(issue.fieldNames || []);
    if (input) {
      const inputId = ensureValidationInputId(input, issue.fieldNames?.[0]);
      const link = document.createElement("a");
      link.href = `#${inputId}`;
      link.textContent = issue.message;
      link.addEventListener("click", () => requestAnimationFrame(() => input.focus()));
      item.append(link);
    } else {
      item.textContent = issue.message;
    }
    list.append(item);
  }

  group.append(title, list);
  validationPanel.append(group);
}

function updateValidationPanel(issues = [], { announceErrors = false } = {}) {
  if (issues.length === 0) {
    if (!validationPanel.hidden || validationPanel.childElementCount > 0) {
      validationPanel.replaceChildren();
    }
    validationPanel.hidden = true;
    validationPanel.dataset.validationFingerprint = "";
    validationPanel.setAttribute("role", "status");
    validationPanel.setAttribute("aria-live", "polite");
    return;
  }

  const { errors, warnings } = partitionValidationIssues(issues);
  const alertMode = announceErrors && errors.length > 0;
  const fingerprint = JSON.stringify({
    mode: alertMode ? "alert" : "status",
    errors: errors.map(issue => issue.message),
    warnings: warnings.map(issue => issue.message)
  });
  validationPanel.setAttribute("role", alertMode ? "alert" : "status");
  validationPanel.setAttribute("aria-live", alertMode ? "assertive" : "polite");
  validationPanel.classList.toggle("validation-panel--warning-only", errors.length === 0);
  validationPanel.hidden = false;
  if (validationPanel.dataset.validationFingerprint === fingerprint) return;

  validationPanel.replaceChildren();
  appendValidationIssueGroup("Проверьте параметры", errors, "error");
  appendValidationIssueGroup("Предупреждения", warnings, "warning");
  validationPanel.dataset.validationFingerprint = fingerprint;
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

function acceptsSignedStopIntegerInput(parameter) {
  return isSignedStopIntegerParameterName(parameter?.name);
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
  const automaticStart = getAutomaticStopNameStart();

  if (!manualNames) {
    for (let index = 1; index <= stops; index += 1) {
      const parameter = getParameterDefinition(getStopNameParameterName(index, stops));
      if (parameter) {
        state.parameterValues[parameter.name] = getAutomaticStopName(index, automaticStart);
      }
    }
  }

  state.parameterValues.main_floor = resolveMainFloor({
    mainValue: getParameterValueByName("main"),
    selectedMainFloor: getParameterValueByName("main_floor"),
    lobbyStopIndex: findLobbyStopIndex(stops, manualNames, automaticStart),
    stops
  });
}

function getAutomaticStopLevel(index, stops) {
  return calculateAutomaticStopLevel({
    bottomLevel: getParameterValueByName("s01_level_1"),
    travelHeightMeters: getParameterValueByName("TR"),
    index,
    stops
  });
}

function synchronizeAutomaticStopLevels(stops) {
  const synchronizedValues = getAuthoritativeStopLevelValues({
    stops,
    manualLevels: getParameterValueByName("level"),
    values: state.parameterValues,
    bottomLevel: getParameterValueByName("s01_level_1"),
    travelHeightMeters: getParameterValueByName("TR"),
    hasParameter: name => Boolean(getParameterDefinition(name))
  });

  for (const [name, value] of Object.entries(synchronizedValues)) {
    if (hasValue(value)) state.parameterValues[name] = value;
  }
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
    const parameter = getParameterDefinition(parameterName);
    input.setAttribute(
      "aria-label",
      parameter ? getFieldLabelText(parameter) : parameterName);
  }
  return input;
}

function bindInputChange(input, parameter) {
  const handleInputChange = event => {
    const focusTarget = getInputFocusTarget(input);
    if (acceptsSignedStopIntegerInput(parameter) && event?.type === "input") {
      if (isSignedIntegerDraft(input.value)) {
        state.parameterValues[parameter.name] = input.value;
      } else {
        input.value = hasValue(state.parameterValues[parameter.name])
          ? state.parameterValues[parameter.name]
          : "";
      }
      return;
    }

    if (acceptsDecimalInput(parameter) && event?.type === "input") {
      state.parameterValues[parameter.name] = input.value;
      return;
    }

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
  } else if (acceptsSignedStopIntegerInput(parameter)) {
    input.type = "text";
    input.inputMode = "text";
    input.pattern = "-?[0-9]+";
    input.autocomplete = "off";
    input.spellcheck = false;
  } else if (acceptsDecimalInput(parameter)) {
    input.type = "text";
    input.inputMode = "decimal";
  } else {
    input.type = type === "number" || type === "integer" ? "number" : "text";
    if (type === "integer") input.step = "1";
    if (type === "number") input.step = "any";
  }

  input.dataset.parameterName = parameter.name;
  if (input.name === "") input.name = parameter.name;
  input.setAttribute("aria-label", getFieldLabelText(parameter));
  input.disabled = Boolean(parameter.isReadOnly);
  setInputValue(input, parameter, options.radioValue !== undefined ? getParameterValue(parameter) : getParameterValue(parameter));
  input.className = options.className || "stops-table__input";
  if (state.validationFieldNames.has(parameter.name)) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
    input.setAttribute("aria-describedby", "validationPanel");
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
  const input = document.createElement("input");
  input.type = "radio";
  input.name = parameter.name;
  input.value = String(value);
  input.dataset.parameterName = parameter.name;
  input.setAttribute("aria-label", `${STOP_LOBBY_LABEL} ${value}`);
  input.className = "stops-table__radio";
  input.disabled = Boolean(parameter.isReadOnly);
  input.checked = Number(getParameterValue(parameter)) === value;
  if (state.validationFieldNames.has(parameter.name)) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
    input.setAttribute("aria-describedby", "validationPanel");
  }
  input.addEventListener("change", () => {
    if (!input.checked) return;
    const focusTarget = getInputFocusTarget(input);
    state.parameterValues[parameter.name] = value;
    renderParametersAfterInputChange(focusTarget);
  });
  return input;
}

function createDisplayStopRadio(checked, value) {
  const input = document.createElement("input");
  input.type = "radio";
  input.className = "stops-table__radio";
  input.disabled = true;
  input.checked = checked;
  input.setAttribute("aria-label", `${STOP_LOBBY_LABEL} ${value}`);
  return input;
}

function createStopsTable(context, options = {}) {
  const stops = clampStopCount(context.stops);
  synchronizeAutomaticStopState(stops);
  synchronizeAutomaticStopLevels(stops);

  const mainSelectionMode = getMainSelectionMode(getParameterValueByName("main"));
  const manualNames = toFlagNumber(context.name) === 1;
  const manualLevels = toFlagNumber(context.level) === 1;
  const hasRearDoors = toNumber(context.NE) === 2;
  const hasAo = toFlagNumber(context.em) === 1;
  const automaticStart = getAutomaticStopNameStart();
  const lobbyStopIndex = mainSelectionMode.manual
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
    lobbyCell.append(mainSelectionMode.radiosReadOnly
      ? createDisplayStopRadio(index === lobbyStopIndex, index)
      : createStopRadio(index));
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
  requestAnimationFrame(updateParameterTabScrollControls);
}

function updateParameterTabScrollControls() {
  if (!parameterTabs) return;
  const maxScrollLeft = Math.max(0, parameterTabs.scrollWidth - parameterTabs.clientWidth);
  if (parameterTabsPrevious) parameterTabsPrevious.disabled = parameterTabs.scrollLeft <= 1;
  if (parameterTabsNext) parameterTabsNext.disabled = parameterTabs.scrollLeft >= maxScrollLeft - 1;
}

function scrollParameterTabs(direction) {
  if (!parameterTabs) return;
  parameterTabs.scrollBy({
    left: direction * Math.max(160, parameterTabs.clientWidth * 0.75),
    behavior: prefersReducedMotion() ? "auto" : "smooth"
  });
}

function getCurrentParameterInputValue(parameter, context) {
  if (["un_victor_mrl", "un_victor_mrl_t"].includes(state.selectedTemplate?.id)
    && ["$V", "OP", "DOP", "CH", "OPH"].includes(parameter.name)) {
    return getDisplayParameterValue(parameter, context);
  }
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
  let isComposing = false;
  const handleParameterChange = event => {
    const focusTarget = getInputFocusTarget(input);
    if (isComposing || event?.isComposing) {
      state.parameterValues[parameter.name] = input.value;
      return;
    }
    if (acceptsDecimalInput(parameter) && event?.type === "input") {
      state.parameterValues[parameter.name] = input.value;
      return;
    }

    state.parameterValues[parameter.name] = readInputValue(input, parameter);
    renderParametersAfterInputChange(focusTarget);
  };

  if (input.tagName === "INPUT" || input.tagName === "TEXTAREA") {
    input.addEventListener("compositionstart", () => {
      isComposing = true;
    });
    input.addEventListener("compositionend", event => {
      isComposing = false;
      handleParameterChange(event);
    });
    input.addEventListener("input", handleParameterChange);
  }

  input.addEventListener("change", handleParameterChange);
}

function createParameterInput(parameter, context) {
  const field = document.createElement("label");
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
  } else if (allowedValues.length || (["un_victor_mrl", "un_victor_mrl_t"].includes(state.selectedTemplate?.id)
    && ["$V", "OP", "DOP", "CH", "OPH"].includes(parameter.name))) {
    input = document.createElement("select");
    if (!allowedValues.length && !hasValue(currentValue)) {
      const unavailable = document.createElement("option");
      unavailable.value = "";
      unavailable.textContent = "Нет допустимых значений";
      unavailable.disabled = true;
      input.append(unavailable);
    }
    if (hasValue(currentValue) && !allowedValues.includes(String(currentValue))) {
      const unavailable = document.createElement("option");
      unavailable.value = String(currentValue);
      unavailable.textContent = `${currentValue} (недоступно для текущих параметров)`;
      input.append(unavailable);
    }
    for (const value of allowedValues) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = getAllowedValueLabel(parameter, value);
      input.append(option);
    }
  } else if (acceptsDecimalInput(parameter)) {
    input = document.createElement("input");
    input.type = "text";
    input.inputMode = "decimal";
  } else {
    input = document.createElement("input");
    input.type = type === "number" || type === "integer" ? "number" : "text";
    if (type === "integer") input.step = "1";
    if (type === "number") input.step = "any";
    if (parameter.minValue !== null && parameter.minValue !== undefined) input.min = parameter.minValue;
    if (parameter.maxValue !== null && parameter.maxValue !== undefined) input.max = parameter.maxValue;
  }

  wireParameterInput(input, parameter, isDisabled);
  input.id = `parameter-${encodeURIComponent(parameter.name).replaceAll("%", "-")}`;
  field.htmlFor = input.id;
  setInputValue(input, parameter, currentValue);
  if (hasValidationError) {
    input.classList.add("is-invalid");
    input.setAttribute("aria-invalid", "true");
    input.setAttribute("aria-describedby", "validationPanel");
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

  const focusTarget = {
    name,
    type: input.type,
    value: input.type === "radio" ? input.value : null
  };
  try {
    if (typeof input.selectionStart === "number") {
      focusTarget.selectionStart = input.selectionStart;
      focusTarget.selectionEnd = input.selectionEnd;
      focusTarget.selectionDirection = input.selectionDirection;
    }
  } catch {
    // Selection APIs are unavailable for controls such as number inputs.
  }

  return focusTarget;
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
  if (input && typeof focusTarget.selectionStart === "number") {
    try {
      input.setSelectionRange(
        focusTarget.selectionStart,
        focusTarget.selectionEnd,
        focusTarget.selectionDirection || "none");
    } catch {
      // Some input types cannot restore a text selection.
    }
  }
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
  const validationIssues = getCurrentValidationIssues(context);
  const validationErrors = validationIssues.filter(isBlockingValidationIssue);
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
  updateValidationPanel(validationIssues);
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
    scheduleSellerRequestAvailability();
    return;
  }

  for (const format of state.selectedTemplate.outputFormats) {
    const option = document.createElement("option");
    option.value = format;
    option.textContent = format.toUpperCase();
    formatSelect.append(option);
  }

  renderParameters();
  updateDownloadResultButton();
  scheduleSellerRequestAvailability();
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
  if (type === "number" || acceptsDecimalInput(definition)) {
    parameters[definition.name] = parseDecimalValue(value);
  } else if (type === "integer") {
    parameters[definition.name] = value === "" || value === null ? null : Number.parseInt(value, 10);
  } else if (type === "bool" || type === "boolean") {
    parameters[definition.name] = Boolean(value);
  } else {
    parameters[definition.name] = hasValue(value) ? String(value) : "";
  }
}

function appendStopParameters(parameters) {
  if (!state.selectedTemplate || !getParameterDefinition("stops")) return;

  const stops = clampStopCount(parameters.stops ?? getParameterValueByName("stops"));
  synchronizeAutomaticStopState(stops);
  synchronizeAutomaticStopLevels(stops);

  const mainFloor = getParameterDefinition("main_floor");
  if (mainFloor) {
    putCollectedParameter(parameters, mainFloor, state.parameterValues.main_floor);
  }

  const stopValues = collectStopParameterValues({
    stops,
    values: state.parameterValues,
    hasParameter: name => Boolean(getParameterDefinition(name))
  });
  for (const [name, value] of Object.entries(stopValues)) {
    putCollectedParameter(parameters, getParameterDefinition(name), value);
  }
}

function appendFrontendHiddenParameters(parameters) {
  if (!state.selectedTemplate) return;

  for (const definition of state.selectedTemplate.parameters) {
    if (!isFrontendHiddenParameter(definition)) continue;
    putCollectedParameter(parameters, definition, getParameterValue(definition));
  }
}

function renderResultFileActions(files, compact = false) {
  return files.map(file => `
    <span class="result-file-actions">
      <a href="${escapeHtml(file.downloadUrl)}">${escapeHtml(compact ? "скачать" : file.fileName)}</a>
      ${isPdfFile(file) ? `
        <button
          class="result-file-preview"
          type="button"
          data-preview-url="${escapeHtml(file.downloadUrl)}"
          data-preview-name="${escapeHtml(file.fileName || "drawing.pdf")}"
          data-preview-format="${escapeHtml(file.format || "pdf")}">Просмотреть</button>
      ` : ""}
    </span>
  `).join(" ");
}

function openPreviewFromControl(control) {
  if (!control?.dataset.previewUrl) return;
  openGeneratedFilePreview({
    downloadUrl: control.dataset.previewUrl,
    fileName: control.dataset.previewName,
    format: control.dataset.previewFormat
  }, control);
}

function renderJob(job, { force = false } = {}) {
  const fingerprint = JSON.stringify({
    id: job.id,
    status: job.status,
    templateId: job.templateId,
    createdAt: job.createdAt,
    finishedAt: job.finishedAt,
    errorMessage: job.errorMessage,
    resultFiles: (job.resultFiles || []).map(file => ({
      fileName: file.fileName,
      format: file.format,
      downloadUrl: file.downloadUrl
    }))
  });
  state.latestJob = job;
  if (!force && state.lastRenderedJobFingerprint === fingerprint) return false;
  state.lastRenderedJobFingerprint = fingerprint;
  const files = job.resultFiles || [];
  const downloadLinks = renderResultFileActions(files);

  statusPanel.className = "job-status";
  statusPanel.setAttribute("role", "status");
  statusPanel.setAttribute("aria-live", "polite");
  statusPanel.setAttribute("aria-busy", "true");
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
  statusPanel.setAttribute("aria-busy", "false");
  updateDownloadResultButton(job);
  return true;
}

function renderStatusError(messages) {
  state.latestJob = null;
  state.lastRenderedJobFingerprint = "";
  updateDownloadResultButton(null);
  statusPanel.className = "";
  statusPanel.setAttribute("role", "alert");
  statusPanel.setAttribute("aria-live", "assertive");
  statusPanel.setAttribute("aria-busy", "false");
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
  if (state.activeJobId !== jobId) return;
  if (state.pollRequestToken !== null) {
    scheduleJobPoll(jobId);
    return;
  }
  const requestToken = {};
  state.pollRequestToken = requestToken;
  try {
    const response = await apiFetch(`/api/jobs/${jobId}`);
    if (!response.ok) {
      handleJobPollingFailure(jobId);
      return;
    }
    const job = await sessionRequests.readJson(response);
    if (job === sessionRequests.stalePayload || state.activeJobId !== jobId) return;
    state.pollFailureCount = 0;
    state.pollErrorAnnounced = false;
    renderJob(job);

    if (job.status === "Completed" || job.status === "Failed" || job.status === "Cancelled") {
      clearTimeout(state.pollTimer);
      state.pollTimer = null;
      state.activeJobId = null;
      await refreshJobs();
    } else {
      scheduleJobPoll(jobId);
    }
  } catch {
    handleJobPollingFailure(jobId);
  } finally {
    if (state.pollRequestToken === requestToken) {
      state.pollRequestToken = null;
    }
  }
}

function scheduleJobPoll(jobId, delay = 1200) {
  if (state.activeJobId !== jobId) return;
  clearTimeout(state.pollTimer);
  state.pollTimer = setTimeout(() => {
    state.pollTimer = null;
    void refreshJob(jobId);
  }, delay);
}

function handleJobPollingFailure(jobId) {
  if (state.activeJobId !== jobId) return;
  state.pollFailureCount += 1;
  if (!state.pollErrorAnnounced) {
    renderStatusError([t("Не удалось обновить статус задания. Повторная проверка продолжится автоматически.")]);
    state.pollErrorAnnounced = true;
  }
  const retryDelay = Math.min(1200 * (2 ** Math.min(state.pollFailureCount, 4)), 15000);
  scheduleJobPoll(jobId, retryDelay);
}

async function refreshJobs({ required = false } = {}) {
  const response = await apiFetch("/api/jobs?take=200");
  if (!response.ok) {
    if (required && sessionRequests.isCurrent(response)) {
      throw new Error("Jobs could not be loaded");
    }
    return false;
  }
  const jobs = await sessionRequests.readJson(response);
  if (jobs === sessionRequests.stalePayload) return false;
  state.jobs = jobs;
  renderJobs();
  return true;
}

function renderJobs() {
  jobsTableBody.replaceChildren();
  const query = getEditorSearchQuery();
  const jobs = state.jobs.filter(job => matchesJobSearch(job, query));

  if (state.jobs.length > 0 && jobs.length === 0) {
    const row = document.createElement("tr");
    row.innerHTML = `<td colspan="6">По этому запросу задания не найдены.</td>`;
    jobsTableBody.append(row);
    showMoreJobsButton.hidden = true;
    return;
  }

  for (const job of jobs.slice(0, state.jobsVisibleCount)) {
    const row = document.createElement("tr");
    const files = job.resultFiles || [];
    row.innerHTML = `
      <td>${escapeHtml(job.id.slice(0, 8))}</td>
      <td>${escapeHtml(job.templateId)}</td>
      <td><span class="status ${escapeHtml(job.status.toLowerCase())}">${escapeHtml(job.status)}</span></td>
      <td>${escapeHtml(job.outputFormat.toUpperCase())}</td>
      <td>${formatDate(job.createdAt)}</td>
      <td>${renderResultFileActions(files, true)}</td>
    `;
    jobsTableBody.append(row);
  }
  showMoreJobsButton.hidden = jobs.length <= state.jobsVisibleCount;
}

showMoreJobsButton.addEventListener("click", () => {
  state.jobsVisibleCount += 5;
  renderJobs();
});

async function submitJob(event) {
  event.preventDefault();
  if (state.engineerRequest) return;
  if (!state.selectedTemplate) return;
  if (!canCreateJobs()) {
    renderStatusError([t("Недостаточно прав для создания задания.")]);
    return;
  }

  rememberCurrentValues();
  const drawingRequest = {
    templateId: state.selectedTemplate.id,
    outputFormat: formatSelect.value,
    parameters: collectParameters()
  };
  let classification;
  try {
    const classificationResponse = await apiFetch("/api/drawings/classify", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(drawingRequest)
    });
    if (!classificationResponse.ok) {
      renderStatusError(await readProblem(classificationResponse, t("Не удалось проверить параметры.")));
      return;
    }
    classification = await sessionRequests.readJson(classificationResponse);
    if (classification === sessionRequests.stalePayload) return;
  } catch {
    renderStatusError([t("Не удалось проверить параметры. Проверьте соединение с API.")]);
    return;
  }

  const hardErrors = classification.hardErrors || classification.HardErrors || [];
  const deviations = classification.overridableDeviations || classification.OverridableDeviations || [];
  if (hardErrors.length > 0) {
    renderStatusError(hardErrors);
    return;
  }
  const validationIssues = getCurrentValidationIssues();
  const validationErrors = validationIssues.filter(isBlockingValidationIssue);
  applyValidationHighlights(validationErrors);
  updateValidationPanel(validationIssues, { announceErrors: true });
  if (validationErrors.length > 0 && deviations.length === 0) {
    renderStatusError([t("Исправьте параметры перед созданием задания.")]);
    const firstInvalidInput = parametersForm.querySelector('[aria-invalid="true"]');
    const invalidGroup = firstInvalidInput?.closest(".parameter-group");
    if (invalidGroup?.dataset.category) {
      state.activeParameterCategory = invalidGroup.dataset.category;
      applyParameterTabVisibility();
    }
    requestAnimationFrame(() => {
      const focusTarget = validationErrors.length > 1 ? validationPanel : firstInvalidInput || validationPanel;
      focusTarget.focus({ preventScroll: true });
      focusTarget.scrollIntoView({ behavior: "auto", block: "center" });
    });
    return;
  }

  const confirmationFingerprint = classification.fingerprint || classification.Fingerprint;
  if (deviations.length > 0 && (!confirmationFingerprint || !window.confirm(
    `${t("Параметры выходят за типовой диапазон:")}\n\n${deviations.map(item => `• ${item}`).join("\n")}\n\n${t("Подтвердить создание чертежа?")}`))) return;

  setJobSubmitDisabled(true);
  state.latestJob = null;
  state.lastRenderedJobFingerprint = "";
  updateDownloadResultButton(null);
  statusPanel.className = "job-status";
  statusPanel.setAttribute("role", "status");
  statusPanel.setAttribute("aria-live", "polite");
  statusPanel.setAttribute("aria-busy", "true");
  statusPanel.innerHTML = `<div class="status pending">Pending</div>`;
  statusPanel.setAttribute("aria-busy", "false");

  try {
    const response = await apiFetch("/api/jobs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        ...drawingRequest,
        ...(deviations.length > 0 ? { confirmationFingerprint } : {})
      })
    });

    if (!response.ok) {
      renderStatusError(await readProblem(response, "Ошибка создания задания"));
      return;
    }

    const job = await sessionRequests.readJson(response);
    if (job === sessionRequests.stalePayload) return;
    state.activeJobId = job.id;
    renderJob(job);
    await refreshJobs();
    if (!sessionRequests.isCurrent(response)) return;

    clearTimeout(state.pollTimer);
    state.pollFailureCount = 0;
    state.pollErrorAnnounced = false;
    scheduleJobPoll(job.id);
  } catch {
    renderStatusError([t("Не удалось создать задание. Проверьте соединение с API.")]);
  } finally {
    setJobSubmitDisabled(false);
  }
}

async function submitEngineerRequest() {
  if (state.engineerRequest) return;
  if (!isSeller() || !state.sellerRequestHasDeviations || !state.selectedTemplate || !projectSelect.value) return;
  if (engineerRequestDescriptionField.hidden) {
    engineerRequestDescriptionField.hidden = false;
    engineerRequestDescription.focus();
    return;
  }
  const description = engineerRequestDescription.value.trim();
  if (!description) {
    engineerRequestDescription.setCustomValidity(t("Опишите задачу для инженера."));
    engineerRequestDescription.reportValidity();
    engineerRequestDescription.setCustomValidity("");
    return;
  }
  requestEngineerButton.disabled = true;
  engineerRequestStatus.hidden = false;
  engineerRequestStatus.className = "empty";
  engineerRequestStatus.textContent = t("Отправляем запрос…");
  try {
    const response = await apiFetch("/api/engineer-requests", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        projectId: projectSelect.value,
        description,
        templateId: state.selectedTemplate.id,
        outputFormat: formatSelect.value,
        parameters: collectParameters()
      })
    });
    if (!response.ok) {
      engineerRequestStatus.className = "error";
      engineerRequestStatus.textContent = (await readProblem(response, t("Не удалось отправить запрос."))).join(" ");
      return;
    }
    engineerRequestDescription.value = "";
    engineerRequestDescriptionField.hidden = true;
    engineerRequestStatus.className = "success";
    engineerRequestStatus.textContent = t("Запрос отправлен инженеру. Статус можно отслеживать в личном кабинете.");
  } catch {
    engineerRequestStatus.className = "error";
    engineerRequestStatus.textContent = t("Не удалось отправить запрос. Проверьте соединение с API.");
  } finally {
    requestEngineerButton.disabled = false;
  }
}

function scheduleSellerRequestAvailability() {
  if (state.engineerRequest) {
    clearTimeout(sellerRequestCheckTimer);
    state.sellerRequestHasDeviations = false;
    if (requestEngineerButton) requestEngineerButton.hidden = true;
    if (engineerRequestDescriptionField) engineerRequestDescriptionField.hidden = true;
    if (submitButton) submitButton.hidden = true;
    return;
  }
  if (!isSeller() || !state.selectedTemplate) {
    state.sellerRequestHasDeviations = false;
    if (requestEngineerButton) requestEngineerButton.hidden = true;
    if (submitButton) submitButton.hidden = isAuthenticated() && !canCreateJobs();
    if (engineerRequestDescriptionField) engineerRequestDescriptionField.hidden = true;
    return;
  }
  state.sellerRequestHasDeviations = false;
  requestEngineerButton.hidden = true;
  submitButton.hidden = false;
  engineerRequestDescriptionField.hidden = true;
  const sequence = ++sellerRequestCheckSequence;
  clearTimeout(sellerRequestCheckTimer);
  sellerRequestCheckTimer = setTimeout(async () => {
    try {
      const response = await apiFetch("/api/drawings/classify", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          templateId: state.selectedTemplate.id,
          outputFormat: formatSelect.value,
          parameters: collectParameters()
        })
      });
      if (!response.ok || sequence !== sellerRequestCheckSequence || !isSeller()) return;
      const result = await sessionRequests.readJson(response);
      if (result === sessionRequests.stalePayload || sequence !== sellerRequestCheckSequence) return;
      const deviations = result.overridableDeviations || result.OverridableDeviations || [];
      state.sellerRequestHasDeviations = deviations.length > 0;
      requestEngineerButton.hidden = !state.sellerRequestHasDeviations;
      submitButton.hidden = state.sellerRequestHasDeviations;
    } catch {
      state.sellerRequestHasDeviations = false;
    }
  }, 350);
}

function setJobSubmitDisabled(disabled) {
  if (submitButton) submitButton.disabled = disabled;
}

function resetJobForm(event) {
  event.preventDefault();
  if (!window.confirm(t("Сбросить все параметры и вернуть значения по умолчанию?"))) return;
  state.editingConfigurationId = null;
  initializeParameterValues();
  renderParameters();
}

async function loadTemplates({ required = false } = {}) {
  const response = await apiFetch("/api/templates");
  if (!response.ok) {
    if (required && sessionRequests.isCurrent(response)) {
      throw new Error("Templates could not be loaded");
    }
    return false;
  }
  const templates = await sessionRequests.readJson(response);
  if (templates === sessionRequests.stalePayload) return false;
  state.templates = templates;

  templateSelect.replaceChildren();
  for (const template of state.templates) {
    const option = document.createElement("option");
    option.value = template.id;
    option.textContent = template.name || template.code;
    templateSelect.append(option);
  }

  renderSelectedTemplate();
  applyEditorSearch();
  return true;
}

async function loadProjects(selectedProjectId = null, { required = false } = {}) {
  const response = await apiFetch("/api/projects");
  if (!response.ok) {
    if (required && sessionRequests.isCurrent(response)) {
      throw new Error("Projects could not be loaded");
    }
    return false;
  }

  const previousValue = selectedProjectId || projectSelect.value;
  const projects = await sessionRequests.readJson(response);
  if (projects === sessionRequests.stalePayload) return false;
  state.projects = projects;
  projectSelect.replaceChildren();

  if (state.projects.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = "Создайте проект в ЛК";
    projectSelect.append(option);
    projectSelect.disabled = true;
    saveConfigurationButton.disabled = true;
    applyEditorSearch();
    return true;
  }

  for (const project of state.projects) {
    const option = document.createElement("option");
    option.value = project.id;
    option.textContent = getProjectOptionLabel(project);
    projectSelect.append(option);
  }

  if (previousValue && state.projects.some(project => project.id === previousValue)) {
    projectSelect.value = previousValue;
  }

  projectSelect.disabled = false;
  saveConfigurationButton.disabled = false;
  applyEditorSearch();
  return true;
}

async function saveCurrentConfiguration() {
  if (state.engineerRequest) return saveEngineerRequestParameters();
  if (!state.selectedTemplate) return;
  if (!projectSelect.value) {
    renderStatusError([t("Сначала создайте проект в личном кабинете.")]);
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

  const savedConfiguration = await sessionRequests.readJson(response);
  if (savedConfiguration === sessionRequests.stalePayload) return;
  state.editingConfigurationId = savedConfiguration.id || editingConfigurationId;
  updateConfigurationNamePreview(parameters);
  statusPanel.className = "empty";
  statusPanel.setAttribute("role", "status");
  statusPanel.setAttribute("aria-live", "polite");
  statusPanel.setAttribute("aria-busy", "false");
  statusPanel.textContent = editingConfigurationId
    ? "Конфигурация обновлена"
    : "Конфигурация сохранена в проект";
  updateDownloadResultButton(null);
}

async function saveEngineerRequestParameters() {
  const request = state.engineerRequest;
  if (!request || !state.selectedTemplate) return;
  saveConfigurationButton.disabled = true;
  try {
    const response = await apiFetch(`/api/engineer-requests/${encodeURIComponent(request.id)}/parameters`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ parameters: collectParameters() })
    });
    if (!response.ok) {
      renderStatusError(await readProblem(response, "Не удалось сохранить параметры заявки."));
      return;
    }
    if (!sessionRequests.isCurrent(response)) return;
    statusPanel.className = "empty";
    statusPanel.setAttribute("role", "status");
    statusPanel.setAttribute("aria-live", "polite");
    statusPanel.replaceChildren(document.createTextNode(`Параметры заявки сохранены. Статус: ${request.status}. `));
    const link = document.createElement("a");
    link.href = `/account?requestId=${encodeURIComponent(request.id)}`;
    link.textContent = "Вернуться к заявке";
    statusPanel.append(link);
  } catch {
    renderStatusError(["Не удалось сохранить параметры заявки. Проверьте соединение с API."]);
  } finally {
    saveConfigurationButton.disabled = false;
  }
}

async function loadEngineerRequestFromUrl() {
  const requestId = new URLSearchParams(window.location.search).get("engineerRequestId");
  if (!requestId) return false;
  const response = await apiFetch(`/api/engineer-requests/${encodeURIComponent(requestId)}`);
  if (!response.ok) {
    renderStatusError(await readProblem(response, "Нет доступа к заявке или она не найдена."));
    return false;
  }
  const request = await sessionRequests.readJson(response);
  if (request === sessionRequests.stalePayload) return false;
  const isAssignedEngineer = (state.currentUser?.roles || []).includes("Engineer")
    && request.engineerUserName?.toLowerCase() === state.currentUser.userName?.toLowerCase();
  if (!canAdmin() && !isAssignedEngineer || !["InProgress", "NeedsClarification"].includes(request.status)) {
    renderStatusError(["Нет доступа к редактированию этой заявки."]);
    return false;
  }
  const template = state.templates.find(item => item.id === request.templateId);
  if (!template || !template.outputFormats.includes(request.outputFormat)) {
    renderStatusError(["Шаблон или формат заявки недоступен."]);
    return false;
  }
  state.engineerRequest = { ...request, id: request.id || requestId };
  updateAuthView();
  document.querySelector("#projectField").hidden = true;
  document.querySelector("#configurationField").hidden = true;
  document.querySelector(".reset-job-button").hidden = true;
  previewResultButton.hidden = true;
  downloadResultButton.hidden = true;
  templateSelect.disabled = true;
  formatSelect.disabled = true;
  applyConfiguration({ ...request, id: null });
  statusPanel.textContent = `Заявка ${request.id || requestId}. Статус: ${request.status}. Инженер: ${request.engineerUserName}.`;
  saveConfigurationButton.disabled = false;
  editorHeading.textContent = "Редактор заявки инженеру";
  return true;
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
  parametersForm.replaceChildren();
  updateConfigurationNamePreview(state.parameterValues);
  if ([...formatSelect.options].some(option => option.value === configuration.outputFormat)) {
    formatSelect.value = configuration.outputFormat;
  }
  renderParameters();
  statusPanel.className = "empty";
  statusPanel.setAttribute("role", "status");
  statusPanel.setAttribute("aria-live", "polite");
  statusPanel.setAttribute("aria-busy", "false");
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

  const configuration = await sessionRequests.readJson(response);
  if (configuration === sessionRequests.stalePayload) return;
  await loadProjects(configuration.projectId);
  if (!sessionRequests.isCurrent(response)) return;
  applyConfiguration(configuration);
}

async function loadCurrentUser({ required = false } = {}) {
  const response = await apiFetch("/api/auth/me");
  if (!response.ok) {
    if (response.status !== 401 && response.status !== 403
      && required && sessionRequests.isCurrent(response)) {
      throw new Error("Current user could not be loaded");
    }
    state.currentUser = null;
    updateAuthView();
    return false;
  }

  const currentUser = await sessionRequests.readJson(response);
  if (currentUser === sessionRequests.stalePayload) return false;
  state.currentUser = currentUser;
  updateAuthView();
  return isAuthenticated();
}

async function register(event) {
  event.preventDefault();
  registerStatus.hidden = true;
  registerStatus.textContent = "";

  try {
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
      const messages = await readProblem(response, t("Не удалось отправить заявку"));
      registerStatus.hidden = false;
      registerStatus.className = "error";
      registerStatus.setAttribute("role", "alert");
      registerStatus.textContent = messages.join(" ");
      return;
    }

    registerForm.reset();
    registerStatus.hidden = false;
    registerStatus.className = "empty";
    registerStatus.setAttribute("role", "status");
    registerStatus.textContent = t("Заявка отправлена. Доступ появится после подтверждения администратором.");
  } catch (error) {
    if (error?.name === "AbortError") return;
    registerStatus.hidden = false;
    registerStatus.className = "error";
    registerStatus.setAttribute("role", "alert");
    registerStatus.textContent = t("Не удалось отправить заявку. Проверьте соединение с API.");
  }
}

async function login(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const userNameInput = form.querySelector("[name='userName']") || loginUserName;
  const passwordInput = form.querySelector("[name='password']") || loginPassword;
  passwordInput.setCustomValidity("");

  try {
    const response = await apiFetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        userName: userNameInput.value,
        password: passwordInput.value
      })
    });

    if (!response.ok) {
      if (response.status >= 500) {
        const recovered = await boot({ focusOnSuccess: true, focusOnError: true });
        if (recovered === false) {
          passwordInput.setCustomValidity(t("Не удалось проверить вход. Повторите попытку."));
          passwordInput.reportValidity();
        }
        return;
      }
      passwordInput.setCustomValidity(t("Неверный логин или пароль"));
      passwordInput.reportValidity();
      return;
    }

    const currentUser = await sessionRequests.readJson(response);
    if (currentUser === sessionRequests.stalePayload) return;
    clearEditorSessionState();
    state.currentUser = currentUser;
    passwordInput.value = "";
    updateAuthView();
    await boot({ focusOnSuccess: true, focusOnError: true });
  } catch (error) {
    if (error?.name === "AbortError") return;
    const recovered = await boot({ focusOnSuccess: true, focusOnError: true });
    if (recovered === false) {
      passwordInput.setCustomValidity(t("Не удалось проверить вход. Повторите попытку."));
      passwordInput.reportValidity();
    }
  }
}

async function logout() {
  clearEditorSessionState();
  state.currentUser = null;
  updateAuthView();
  try {
    const response = await apiFetch("/api/auth/logout", { method: "POST" });
    if (!response.ok && response.status !== 401 && response.status !== 403) {
      showPageLoadFailure({ focus: true, context: "logout" });
      return;
    }
    focusBootDestination(false);
  } catch {
    showPageLoadFailure({ focus: true, context: "logout" });
  }
}

async function boot({ focusOnSuccess = false, focusOnError = false, context = "load" } = {}) {
  let errorContext = context;
  syncPageLoadErrorCopy(context);
  if (pageSkeleton) {
    pageSkeleton.hidden = false;
    pageSkeleton.setAttribute("aria-busy", "true");
  }
  if (pageLoadError) pageLoadError.hidden = true;
  if (retryPageLoadButton) retryPageLoadButton.disabled = true;
  try {
    const authenticated = await loadCurrentUser({ required: true });
    errorContext = "load";
    syncPageLoadErrorCopy("load");
    if (!authenticated) {
      if (focusOnSuccess) focusBootDestination(false);
      return false;
    }

    await loadTemplates({ required: true });
    const requestMode = new URLSearchParams(window.location.search).has("engineerRequestId");
    if (!requestMode) await loadProjects(null, { required: true });
    await refreshJobs({ required: true });
    if (requestMode && !await loadEngineerRequestFromUrl()) await loadProjects(null, { required: true });
    else await loadConfigurationFromUrl();
    if (focusOnSuccess) focusBootDestination(true);
    return true;
  } catch {
    showPageLoadFailure({ focus: focusOnError, context: errorContext });
    return null;
  } finally {
    if (retryPageLoadButton) retryPageLoadButton.disabled = false;
    hidePageSkeleton();
  }
}

templateSelect.addEventListener("change", renderSelectedTemplate);
formatSelect.addEventListener("change", () => {
  updateDownloadResultButton();
  scheduleSellerRequestAvailability();
});
parametersForm.addEventListener("input", () => { updateShaftPreview(); scheduleSellerRequestAvailability(); });
parametersForm.addEventListener("change", () => { updateShaftPreview(); scheduleSellerRequestAvailability(); });
globalSearchInput?.addEventListener("input", applyEditorSearch);
globalSearchInput?.addEventListener("keydown", event => {
  if (event.key !== "Escape") return;
  globalSearchInput.value = "";
  applyEditorSearch();
});
document.querySelector("#jobForm").addEventListener("submit", submitJob);
requestEngineerButton?.addEventListener("click", submitEngineerRequest);
document.querySelector("#jobForm").addEventListener("reset", resetJobForm);
parameterTabsPrevious?.addEventListener("click", () => scrollParameterTabs(-1));
parameterTabsNext?.addEventListener("click", () => scrollParameterTabs(1));
parameterTabs?.addEventListener("scroll", updateParameterTabScrollControls, { passive: true });
window.addEventListener("resize", updateParameterTabScrollControls, { passive: true });
const parameterTabsResizeObserver = typeof ResizeObserver === "function" && parameterTabs
  ? new ResizeObserver(updateParameterTabScrollControls)
  : null;
parameterTabsResizeObserver?.observe(parameterTabs);
registerForm.addEventListener("submit", register);
loginForm.addEventListener("submit", login);
guestLoginForm?.addEventListener("submit", login);
for (const form of [loginForm, guestLoginForm]) {
  form?.addEventListener("input", () => form.querySelector("[name='password']")?.setCustomValidity(""));
}
showRegisterPanelButton?.addEventListener("click", () => showAuthPanel("register"));
showLoginPanelButton?.addEventListener("click", () => showAuthPanel("login"));
logoutButton.addEventListener("click", logout);
downloadResultButton?.addEventListener("click", () => {
  const url = downloadResultButton.dataset.downloadUrl;
  if (!url) return;
  window.location.href = url;
});
previewResultButton?.addEventListener("click", () => {
  openGeneratedFilePreview({
    downloadUrl: previewResultButton.dataset.downloadUrl,
    fileName: previewResultButton.dataset.fileName,
    format: previewResultButton.dataset.format
  }, previewResultButton);
});
for (const container of [statusPanel, jobsTableBody]) {
  container?.addEventListener("click", event => {
    const control = event.target.closest("[data-preview-url]");
    if (control) openPreviewFromControl(control);
  });
}
saveConfigurationButton.addEventListener("click", saveCurrentConfiguration);
showAllParametersToggle?.addEventListener("change", event => {
  state.showAllParameters = event.currentTarget.checked;
  applyParameterTabVisibility();
});
retryPageLoadButton?.addEventListener("click", () => {
  const context = pageLoadErrorContext;
  sessionRequests.invalidate();
  void boot({ focusOnSuccess: true, focusOnError: true, context });
});
window.addEventListener("tflex:languagechange", () => {
  if (pageLoadError && !pageLoadError.hidden) syncPageLoadErrorCopy();
  if (state.latestJob) renderJob(state.latestJob, { force: true });
  renderJobs();
  updateDownloadResultButton();
});

await boot();
