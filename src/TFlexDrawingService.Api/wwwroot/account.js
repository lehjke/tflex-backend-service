import { getLanguage, t } from "./i18n.js?v=20260826-design-fixes-1";
import { openGeneratedFilePreview } from "./file-preview.js?v=20260806-design-fixes-1";
import { createSessionRequestGuard } from "./session-requests.js?v=20260720-ui-hardening-1";
import { groupProjectAssets } from "./project-assets.js?v=20260827-asset-grouping-1";

const state = {
  currentUser: null,
  projects: [],
  configurationsByProjectId: new Map(),
  pricingByProjectId: new Map(),
  templates: [],
  jobs: [],
  adminUsers: [],
  adminTemplates: [],
  templateAnalyses: [],
  activeTemplateAnalysis: null,
  activeGenerationActions: new Map(),
  activeAdminUserActions: new Set()
};
const sessionRequests = createSessionRequestGuard();
let bootPromise = null;
let pageLoadErrorContext = "load";

const guestMain = document.querySelector("#guestMain");
const accountMain = document.querySelector("#accountMain");
const pageSkeleton = document.querySelector("#pageSkeleton");
const pageLoadingElements = document.querySelectorAll("[data-page-loading]");
const pageLoadError = document.querySelector("#pageLoadError");
const pageLoadErrorTitle = document.querySelector("#pageLoadErrorTitle");
const pageLoadErrorMessage = document.querySelector("#pageLoadErrorMessage");
const retryBootButton = document.querySelector("#retryBootButton");
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
const currentUserAccessNote = document.querySelector("#currentUserAccessNote");
const adminNavLinks = document.querySelectorAll(".admin-only-nav");
const logoutButton = document.querySelector("#logoutButton");
const globalSearch = document.querySelector(".global-search");
const globalSearchInput = document.querySelector(".global-search input");
const projectNameInput = document.querySelector("#projectNameInput");
const projectAddressInput = document.querySelector("#projectAddressInput");
const projectFactoryRequestNumberInput = document.querySelector("#projectFactoryRequestNumberInput");
const createProjectButton = document.querySelector("#createProjectButton");
const toggleProjectCreateButton = document.querySelector("#toggleProjectCreateButton");
const projectsList = document.querySelector("#projectsList");
const accountStatus = document.querySelector("#accountStatus");
const projectSearchInput = document.querySelector("#projectSearchInput");
const projectsMetric = document.querySelector("#projectsMetric");
const configurationsMetric = document.querySelector("#configurationsMetric");
const readyFilesMetric = document.querySelector("#readyFilesMetric");
const pendingMetric = document.querySelector("#pendingMetric");
const savedConfigurationsList = document.querySelector("#savedConfigurationsList");
const adminAccessCard = document.querySelector("#adminAccessCard");
const adminPanel = document.querySelector("#adminPanel");
const adminStatus = document.querySelector("#adminStatus");
const adminUsersTableBody = document.querySelector("#adminUsersTableBody");
const adminTemplatesTableBody = document.querySelector("#adminTemplatesTableBody");
const accountCreateSection = document.querySelector(".account-create");
const templateImportForm = document.querySelector("#templateImportForm");
const templateGrbFile = document.querySelector("#templateGrbFile");
const templateFragmentsFile = document.querySelector("#templateFragmentsFile");
const templateImportButton = document.querySelector("#templateImportButton");
const templateImportStatus = document.querySelector("#templateImportStatus");
const templateAnalysesList = document.querySelector("#templateAnalysesList");
const templateAnalysisEditor = document.querySelector("#templateAnalysisEditor");
const closeTemplateAnalysisEditor = document.querySelector("#closeTemplateAnalysisEditor");
const templateAnalysisWarnings = document.querySelector("#templateAnalysisWarnings");
const analysisTemplateName = document.querySelector("#analysisTemplateName");
const analysisTemplateCode = document.querySelector("#analysisTemplateCode");
const analysisTemplateId = document.querySelector("#analysisTemplateId");
const analysisTemplateDescription = document.querySelector("#analysisTemplateDescription");
const templateAnalysisSummary = document.querySelector(".template-analysis-summary");
const templateAnalysisParameters = document.querySelector("#templateAnalysisParameters");
const saveTemplateAnalysisDraft = document.querySelector("#saveTemplateAnalysisDraft");
const publishTemplateAnalysis = document.querySelector("#publishTemplateAnalysis");
const CONFIGURATION_NAME_PARAMETER_NAMES = ["$Oboznach"];
const ADMIN_ROLE_OPTIONS = ["Admin", "Operator", "Viewer"];

function isAuthenticated() {
  return Boolean(state.currentUser?.isAuthenticated);
}

function canCreateJobs() {
  const roles = state.currentUser?.roles || [];
  return roles.includes("Admin") || roles.includes("Operator");
}

function canAdmin() {
  return (state.currentUser?.roles || []).includes("Admin");
}

function localized(ru, en) {
  return getLanguage() === "en" ? en : ru;
}

function getCurrentRole() {
  const roles = state.currentUser?.roles || [];
  return ADMIN_ROLE_OPTIONS.find(role => roles.includes(role)) || "Viewer";
}

function isAdminPanelRoute() {
  return window.location.hash === "#adminPanel";
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}

function formatDate(value) {
  if (!value) return "";
  return new Intl.DateTimeFormat(getLanguage() === "en" ? "en-GB" : "ru-RU", {
    dateStyle: "short",
    timeStyle: "medium"
  }).format(new Date(value));
}

function setLiveRegionUrgency(element, kind) {
  if (!element) return;
  const isError = kind === "error";
  element.setAttribute("role", isError ? "alert" : "status");
  element.setAttribute("aria-live", isError ? "assertive" : "polite");
}

function showAccountStatus(message, kind = "empty") {
  setLiveRegionUrgency(accountStatus, kind);
  accountStatus.hidden = false;
  accountStatus.className = kind;
  accountStatus.textContent = message;
}

function hideAccountStatus() {
  accountStatus.hidden = true;
  accountStatus.textContent = "";
}

function showAdminStatus(message, kind = "empty") {
  if (!adminStatus) return;
  setLiveRegionUrgency(adminStatus, kind);
  adminStatus.hidden = false;
  adminStatus.className = kind;
  adminStatus.textContent = message;
}

function hideAdminStatus() {
  if (!adminStatus) return;
  adminStatus.hidden = true;
  adminStatus.textContent = "";
}

function showRegisterStatus(message, kind = "empty") {
  if (!registerStatus) return;
  setLiveRegionUrgency(registerStatus, kind);
  registerStatus.hidden = false;
  registerStatus.className = kind;
  registerStatus.textContent = message;
}

function clearAccountSessionState() {
  sessionRequests.invalidate();
  state.projects = [];
  state.configurationsByProjectId = new Map();
  state.pricingByProjectId = new Map();
  state.templates = [];
  state.jobs = [];
  state.adminUsers = [];
  state.adminTemplates = [];
  state.templateAnalyses = [];
  state.activeTemplateAnalysis = null;
  state.activeGenerationActions = new Map();
  state.activeAdminUserActions = new Set();

  loginForm?.reset();
  guestLoginForm?.reset();
  registerForm?.reset();
  templateImportForm?.reset();
  loginPassword?.setCustomValidity("");
  guestLoginForm?.querySelector("[name='password']")?.setCustomValidity("");
  if (registerStatus) {
    registerStatus.hidden = true;
    registerStatus.textContent = "";
  }
  if (templateImportStatus) {
    templateImportStatus.hidden = true;
    templateImportStatus.textContent = "";
  }

  projectNameInput.value = "";
  projectAddressInput.value = "";
  projectFactoryRequestNumberInput.value = "";
  if (accountCreateSection) accountCreateSection.hidden = true;
  if (toggleProjectCreateButton) toggleProjectCreateButton.setAttribute("aria-expanded", "false");
  if (projectSearchInput) projectSearchInput.value = "";
  if (globalSearchInput) globalSearchInput.value = "";

  projectsList.replaceChildren();
  savedConfigurationsList?.replaceChildren();
  adminUsersTableBody.replaceChildren();
  adminTemplatesTableBody.replaceChildren();
  hideAccountStatus();
  hideAdminStatus();
  updateMetrics();
}

function getTemplate(templateId) {
  return state.templates.find(template => template.id === templateId || template.code === templateId) || null;
}

function getTemplateLabel(templateId) {
  const template = getTemplate(templateId);
  return template ? (template.name || template.code || template.id) : templateId;
}

function getConfigurationName(configuration) {
  const parameters = configuration.parameters || {};
  for (const name of CONFIGURATION_NAME_PARAMETER_NAMES) {
    const value = parameters[name];
    if (value !== null && value !== undefined && String(value).trim()) {
      return String(value).trim();
    }
  }

  const template = getTemplate(configuration.templateId);
  const titleParameter = template?.parameters
    ?.find(parameter => (parameter.displayName || "").includes("№"));
  if (titleParameter) {
    const value = parameters[titleParameter.name];
    if (value !== null && value !== undefined && String(value).trim()) {
      return String(value).trim();
    }
  }

  return configuration.name || "Конфигурация";
}

function getTemplateFormats(configuration) {
  const formats = getTemplate(configuration.templateId)?.outputFormats || [];
  const normalized = [...new Set([
    configuration.outputFormat,
    ...formats
  ].filter(Boolean).map(format => String(format).toLowerCase()))];

  return normalized.length > 0 ? normalized : ["pdf"];
}

function findConfigurationFormatSelect(configurationId, scope = document) {
  return [...scope.querySelectorAll("select[data-format-for]")]
    .find(select => select.dataset.formatFor === configurationId) || null;
}

function findConfigurationActionScope(button) {
  return button.closest(".saved-configuration-item, tr") || document;
}

function getGenerationKey(projectId, configurationId) {
  return JSON.stringify([String(projectId || ""), String(configurationId || "")]);
}

function syncGenerationControls() {
  const buttons = document.querySelectorAll(`
    #projectsList button[data-action="preview"],
    #projectsList button[data-action="download"],
    #savedConfigurationsList button[data-action="preview"],
    #savedConfigurationsList button[data-action="download"]
  `);

  for (const button of buttons) {
    const key = getGenerationKey(button.dataset.projectId, button.dataset.id);
    const activeAction = state.activeGenerationActions.get(key);
    if (activeAction) {
      if (!button.dataset.generationIdleLabel) {
        button.dataset.generationIdleLabel = button.textContent;
      }
      button.disabled = true;
      button.setAttribute("aria-disabled", "true");
      button.dataset.generationBusy = "true";
      if (button.dataset.action === activeAction) {
        button.setAttribute("aria-busy", "true");
        button.textContent = localized("Формирование…", "Preparing…");
      } else {
        button.removeAttribute("aria-busy");
      }
      continue;
    }

    if (button.dataset.generationBusy === "true") {
      button.disabled = false;
      button.removeAttribute("aria-disabled");
      button.removeAttribute("aria-busy");
      button.textContent = button.dataset.generationIdleLabel || button.textContent;
      delete button.dataset.generationBusy;
      delete button.dataset.generationIdleLabel;
    }
  }

  const scopes = document.querySelectorAll("#projectsList tr, #savedConfigurationsList .saved-configuration-item");
  for (const scope of scopes) {
    const isBusy = Boolean(scope.querySelector("button[data-generation-busy='true']"));
    if (isBusy) {
      scope.setAttribute("aria-busy", "true");
      scope.dataset.generationBusy = "true";
    } else if (scope.dataset.generationBusy === "true") {
      scope.removeAttribute("aria-busy");
      delete scope.dataset.generationBusy;
    }
  }
}

function getProjectAssetGroups(project) {
  return groupProjectAssets(
    state.configurationsByProjectId.get(project.id) || [],
    state.pricingByProjectId.get(project.id) || [],
    getConfigurationName);
}

function getAllProjectAssetGroups() {
  return state.projects.flatMap(project => getProjectAssetGroups(project)
    .map(group => ({ project, group })));
}

function formatMoney(value, currency = "CNY") {
  const amount = Number(value);
  if (!Number.isFinite(amount)) return "—";
  return new Intl.NumberFormat(getLanguage() === "en" ? "en-GB" : "ru-RU", {
    style: "currency",
    currency: currency || "CNY",
    currencyDisplay: "code",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  }).format(amount);
}

function getPricingAmountLabel(specification) {
  const primary = formatMoney(specification.totalCny, "CNY");
  const targetCurrency = String(specification.targetCurrency || "CNY").toUpperCase();
  if (targetCurrency === "CNY") return primary;
  return `${primary} · ${formatMoney(specification.totalConverted, targetCurrency)}`;
}

function getProjectOwnerName(project) {
  return project?.ownerUserName || project?.OwnerUserName || "";
}

function getProjectAddress(project) {
  return project?.address || project?.Address || "";
}

function getProjectFactoryRequestNumber(project) {
  return project?.factoryRequestNumber || project?.FactoryRequestNumber || "";
}

function shouldShowProjectOwner(project) {
  const ownerUserName = getProjectOwnerName(project);
  return canAdmin() && ownerUserName && ownerUserName !== state.currentUser?.userName;
}

function renderProjectOwnerBadge(project) {
  return shouldShowProjectOwner(project)
    ? `<span class="owner-badge">${escapeHtml(getProjectOwnerName(project))}</span>`
    : "";
}

function getProjectMetaLabel(project) {
  const ownerUserName = getProjectOwnerName(project);
  return shouldShowProjectOwner(project)
    ? `${project.name} · ${ownerUserName}`
    : project.name;
}

function normalizeSearch(value) {
  return String(value || "").trim().toLowerCase();
}

function getAccountSearchQuery() {
  return normalizeSearch(projectSearchInput?.value || globalSearchInput?.value);
}

function syncSearchInputs(value, source) {
  if (globalSearchInput && source !== globalSearchInput) {
    globalSearchInput.value = value;
  }

  if (projectSearchInput && source !== projectSearchInput) {
    projectSearchInput.value = value;
  }
}

function matchesProjectSearch(project, configurations, pricingSpecifications, query) {
  if (!query) return true;
  const values = [
    project.name,
    getProjectAddress(project),
    getProjectFactoryRequestNumber(project),
    project.description,
    getProjectOwnerName(project),
    ...configurations.flatMap(configuration => [
      getConfigurationName(configuration),
      getTemplateLabel(configuration.templateId),
      configuration.ownerUserName,
      configuration.outputFormat,
      Object.values(configuration.parameters || {}).join(" ")
    ]),
    ...pricingSpecifications.flatMap(specification => [
      specification.name,
      specification.supplier,
      specification.series,
      specification.status,
      getPricingAmountLabel(specification),
      formatDate(specification.updatedAt)
    ])
  ];
  return values.some(value => normalizeSearch(value).includes(query));
}

function matchesPricingSearch(project, specification, query) {
  if (!query) return true;
  const values = [
    project.name,
    getProjectAddress(project),
    getProjectFactoryRequestNumber(project),
    project.description,
    getProjectOwnerName(project),
    specification.name,
    specification.supplier,
    specification.series,
    specification.status,
    getPricingAmountLabel(specification),
    formatDate(specification.updatedAt)
  ];
  return values.some(value => normalizeSearch(value).includes(query));
}

function matchesConfigurationSearch(project, configuration, query) {
  if (!query) return true;
  const values = [
    project.name,
    getProjectAddress(project),
    getProjectFactoryRequestNumber(project),
    project.description,
    getProjectOwnerName(project),
    configuration.ownerUserName,
    configuration.name,
    getConfigurationName(configuration),
    getTemplateLabel(configuration.templateId),
    configuration.templateId,
    configuration.outputFormat,
    formatDate(configuration.updatedAt),
    Object.values(configuration.parameters || {}).join(" ")
  ];
  return values.some(value => normalizeSearch(value).includes(query));
}

function matchesProjectAssetGroupSearch(project, group, query) {
  if (!query) return true;
  return group.configurations.some(configuration => matchesConfigurationSearch(project, configuration, query))
    || group.pricingSpecifications.some(specification => matchesPricingSearch(project, specification, query));
}

function applyAccountSearch(source = null) {
  const value = source?.value || "";
  syncSearchInputs(value, source);
  renderProjects();
  renderSavedConfigurations();
  syncGenerationControls();
}

function updateMetrics() {
  const allAssetGroups = getAllProjectAssetGroups();
  const completedJobs = state.jobs.filter(job => String(job.status).toLowerCase() === "completed");
  const pendingJobs = state.jobs.filter(job => {
    const status = String(job.status).toLowerCase();
    return status === "pending" || status === "running";
  });
  const resultFilesCount = completedJobs.reduce((total, job) => total + (job.resultFiles || []).length, 0);

  if (projectsMetric) projectsMetric.textContent = String(state.projects.length);
  if (configurationsMetric) {
    configurationsMetric.textContent = String(allAssetGroups.length);
  }
  if (readyFilesMetric) readyFilesMetric.textContent = String(resultFilesCount);
  if (pendingMetric) pendingMetric.textContent = String(pendingJobs.length);
}

function updatePageLoadErrorCopy(context = pageLoadErrorContext) {
  pageLoadErrorContext = context;
  const isLogoutRecovery = context === "logout";
  if (pageLoadErrorTitle) {
    pageLoadErrorTitle.textContent = localized(
      isLogoutRecovery ? "Выход не подтвержден" : "Не удалось загрузить личный кабинет",
      isLogoutRecovery ? "Sign-out not confirmed" : "The account could not be loaded");
  }
  if (pageLoadErrorMessage) {
    pageLoadErrorMessage.textContent = localized(
      isLogoutRecovery
        ? "Сервер не подтвердил выход. Сессия могла сохраниться. Проверьте состояние еще раз."
        : "Проверьте сетевое соединение и повторите загрузку.",
      isLogoutRecovery
        ? "The server did not confirm sign-out. The session may still be active. Check the state again."
        : "Check your network connection and try loading the account again.");
  }
  if (retryBootButton) {
    retryBootButton.textContent = localized(
      isLogoutRecovery ? "Проверить снова" : "Повторить",
      isLogoutRecovery ? "Check again" : "Try again");
  }
}

function showPageLoading(context = "load") {
  if (!pageSkeleton) return;
  updatePageLoadErrorCopy(context);
  pageSkeleton.hidden = false;
  pageSkeleton.setAttribute("aria-label", localized("Загрузка страницы", "Loading page"));
  pageSkeleton.setAttribute("aria-busy", "true");
  pageLoadingElements.forEach(element => {
    element.hidden = false;
  });
  if (pageLoadError) pageLoadError.hidden = true;
  if (retryBootButton) {
    retryBootButton.disabled = true;
    retryBootButton.setAttribute("aria-busy", "true");
  }
}

function showPageLoadError({ context = "load" } = {}) {
  if (!pageSkeleton || !pageLoadError) return;
  updatePageLoadErrorCopy(context);
  guestMain.hidden = true;
  accountMain.hidden = true;
  pageLoadingElements.forEach(element => {
    element.hidden = true;
  });
  pageSkeleton.hidden = false;
  pageSkeleton.setAttribute("aria-label", localized("Ошибка загрузки", "Loading error"));
  pageSkeleton.removeAttribute("aria-busy");
  pageLoadError.hidden = false;
  retryBootButton?.removeAttribute("aria-busy");
  if (retryBootButton) retryBootButton.disabled = false;
  requestAnimationFrame(() => retryBootButton?.focus({ preventScroll: true }));
}

function hidePageSkeleton() {
  if (pageSkeleton) {
    pageSkeleton.removeAttribute("aria-busy");
    pageSkeleton.hidden = true;
  }
}

function updateAuthView() {
  const authenticated = isAuthenticated();
  const isAdmin = authenticated && canAdmin();
  guestMain.hidden = authenticated;
  loginForm.hidden = true;
  userPanel.hidden = !authenticated;
  accountMain.hidden = !authenticated;
  const showAdminPanel = isAdmin && isAdminPanelRoute();
  if (globalSearch) globalSearch.hidden = !authenticated || showAdminPanel;
  accountMain.classList.toggle("is-admin-route", showAdminPanel);
  adminPanel.hidden = !showAdminPanel;
  adminPanel.classList.toggle("is-open", showAdminPanel);
  adminNavLinks.forEach(link => {
    link.hidden = !isAdmin;
  });
  if (adminAccessCard) adminAccessCard.hidden = !isAdmin;
  if (toggleProjectCreateButton) toggleProjectCreateButton.hidden = !canCreateJobs();
  if (!canCreateJobs() && accountCreateSection) {
    accountCreateSection.hidden = true;
    toggleProjectCreateButton?.setAttribute("aria-expanded", "false");
  }

  if (showAdminPanel) {
    requestAnimationFrame(() => {
      adminPanel.scrollIntoView({ block: "start", inline: "nearest", behavior: "auto" });
    });
  }

  if (authenticated) {
    const currentRole = getCurrentRole();
    currentUserName.textContent = state.currentUser.displayName || state.currentUser.userName;
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = false;
      currentUserRoleLabel.textContent = currentRole;
    }
    if (currentUserAccessNote) {
      const isViewer = currentRole === "Viewer";
      currentUserAccessNote.hidden = !isViewer;
      currentUserAccessNote.textContent = isViewer
        ? localized(
          "Роль Viewer: режим только для просмотра. Создание проектов и выпуск файлов недоступны.",
          "Viewer role: read-only access. Creating projects and generating files are unavailable.")
        : "";
    }
  } else {
    currentUserName.textContent = "";
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = true;
      currentUserRoleLabel.textContent = "";
    }
    if (currentUserAccessNote) {
      currentUserAccessNote.hidden = true;
      currentUserAccessNote.textContent = "";
    }
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
    clearAccountSessionState();
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
    const validationMessages = Object.values(problem.errors || {})
      .flatMap(value => Array.isArray(value) ? value : [value])
      .filter(Boolean);
    return validationMessages.length > 0
      ? validationMessages
      : [problem.detail || problem.title || fallback];
  } catch {
    return [fallback];
  }
}

function createAccountRequestAbortError(resource) {
  const error = new Error(`The ${resource} request no longer belongs to the active session`);
  error.name = "AbortError";
  return error;
}

function requireSuccessfulLoadResponse(response, resource) {
  if (!sessionRequests.isCurrent(response)) {
    throw createAccountRequestAbortError(resource);
  }
  if (response.ok) return;

  const error = new Error(`The ${resource} request failed with status ${response.status}`);
  error.name = "AccountLoadError";
  error.status = response.status;
  throw error;
}

function requireCurrentLoadPayload(payload, resource) {
  if (payload === sessionRequests.stalePayload) {
    throw createAccountRequestAbortError(resource);
  }
  return payload;
}

async function loadCurrentUser() {
  const response = await apiFetch("/api/auth/me");
  if (!response.ok) {
    if (response.status === 401 || response.status === 403) {
      state.currentUser = null;
      updateAuthView();
      return false;
    }
    requireSuccessfulLoadResponse(response, "authentication");
  }

  const currentUser = requireCurrentLoadPayload(
    await sessionRequests.readJson(response),
    "authentication");
  state.currentUser = currentUser;
  updateAuthView();
  return isAuthenticated();
}

async function loadTemplates() {
  const response = await apiFetch("/api/templates");
  requireSuccessfulLoadResponse(response, "templates");
  const templates = requireCurrentLoadPayload(
    await sessionRequests.readJson(response),
    "templates");
  state.templates = templates;
}

async function loadProjects() {
  const response = await apiFetch("/api/projects");
  requireSuccessfulLoadResponse(response, "projects");

  const projects = requireCurrentLoadPayload(
    await sessionRequests.readJson(response),
    "projects");

  const projectEntries = await Promise.all(projects.map(async project => {
    const [configurationsResponse, pricingResponse] = await Promise.all([
      apiFetch(`/api/projects/${project.id}/configurations`),
      apiFetch(`/api/projects/${project.id}/pricing-specifications`)
    ]);
    requireSuccessfulLoadResponse(
      configurationsResponse,
      `configurations for project ${project.id}`);
    requireSuccessfulLoadResponse(
      pricingResponse,
      `pricing specifications for project ${project.id}`);
    const configurations = requireCurrentLoadPayload(
      await sessionRequests.readJson(configurationsResponse),
      `configurations for project ${project.id}`);
    const pricingSpecifications = requireCurrentLoadPayload(
      await sessionRequests.readJson(pricingResponse),
      `pricing specifications for project ${project.id}`);
    return [project.id, configurations, pricingSpecifications];
  }));
  requireSuccessfulLoadResponse(response, "projects");

  state.projects = projects;
  state.configurationsByProjectId = new Map(
    projectEntries.map(([projectId, configurations]) => [projectId, configurations]));
  state.pricingByProjectId = new Map(
    projectEntries.map(([projectId, , pricingSpecifications]) => [projectId, pricingSpecifications]));
  renderAccountData();
}

async function loadAccountJobs() {
  const response = await apiFetch("/api/jobs?take=100");
  requireSuccessfulLoadResponse(response, "jobs");
  const jobs = requireCurrentLoadPayload(
    await sessionRequests.readJson(response),
    "jobs");
  state.jobs = jobs;
  updateMetrics();
}

function renderAccountData() {
  renderProjects();
  renderSavedConfigurations();
  updateMetrics();
  syncGenerationControls();
}

function renderProjects() {
  projectsList.replaceChildren();

  if (state.projects.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "Пока нет проектов.";
    projectsList.append(empty);
    return;
  }

  const query = getAccountSearchQuery();
  const filteredProjects = state.projects.filter(project => matchesProjectSearch(
    project,
    state.configurationsByProjectId.get(project.id) || [],
    state.pricingByProjectId.get(project.id) || [],
    query));

  if (filteredProjects.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "По этому запросу проекты не найдены.";
    projectsList.append(empty);
    return;
  }

  for (const project of filteredProjects) {
    const configurations = state.configurationsByProjectId.get(project.id) || [];
    const pricingSpecifications = state.pricingByProjectId.get(project.id) || [];
    const assetGroups = groupProjectAssets(configurations, pricingSpecifications, getConfigurationName);
    const details = document.createElement("details");
    details.className = "project-item";
    const summary = document.createElement("summary");
    summary.className = "project-summary";
    summary.innerHTML = `
      <span class="project-summary__name"><span class="project-summary__title">${escapeHtml(project.name)}</span>${renderProjectOwnerBadge(project)}</span>
      <span class="project-summary__counts">
        <span>${assetGroups.length} конф.</span>
      </span>
    `;
    details.append(summary);

    const body = document.createElement("div");
    body.className = "project-item__body";
    body.append(createProjectEditForm(project));

    if (configurations.length === 0 && pricingSpecifications.length === 0) {
      const empty = document.createElement("div");
      empty.className = "empty";
      empty.textContent = "В проекте пока нет сохраненных конфигураций.";
      body.append(empty);
    } else {
      body.append(createConfigurationsTable(project, assetGroups));
    }

    details.append(body);
    projectsList.append(details);
  }
}

function createProjectEditForm(project) {
  const form = document.createElement("div");
  form.className = "project-edit-form";
  form.dataset.projectId = project.id;
  form.innerHTML = `
    <label class="field">
      <span class="field__label">Название проекта</span>
      <input data-project-field="name" value="${escapeHtml(project.name || "")}">
    </label>
    <label class="field">
      <span class="field__label">Адрес проекта</span>
      <input data-project-field="address" value="${escapeHtml(getProjectAddress(project))}">
    </label>
    <label class="field">
      <span class="field__label">Номер запроса на завод</span>
      <input data-project-field="factoryRequestNumber" value="${escapeHtml(getProjectFactoryRequestNumber(project))}">
    </label>
    ${canCreateJobs() ? `
      <div class="project-edit-form__actions">
        <button class="secondary" type="button" data-action="update-project" data-project-id="${escapeHtml(project.id)}">Сохранить проект</button>
        <button class="secondary secondary--danger" type="button" data-action="delete-project" data-project-id="${escapeHtml(project.id)}">Удалить проект</button>
      </div>
    ` : ""}
  `;
  return form;
}

function renderSavedConfigurations() {
  if (!savedConfigurationsList) return;

  savedConfigurationsList.replaceChildren();
  const query = getAccountSearchQuery();
  const entries = getAllProjectAssetGroups()
    .filter(({ project, group }) => matchesProjectAssetGroupSearch(project, group, query))
    .sort((left, right) => new Date(right.group.updatedAt || 0) - new Date(left.group.updatedAt || 0));

  if (entries.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = query
      ? "По этому запросу конфигурации не найдены."
      : "Пока нет сохраненных конфигураций.";
    savedConfigurationsList.append(empty);
    return;
  }

  for (const entry of entries) {
    const { project, group } = entry;
    const item = document.createElement("article");
    const isCombined = group.configurations.length > 0 && group.pricingSpecifications.length > 0;
    item.className = `saved-configuration-item${isCombined ? " saved-configuration-item--combined" : ""}`;
    item.innerHTML = `
      <div class="saved-configuration-item__info">
        ${renderProjectAssetKinds(group)}
        <strong>${escapeHtml(group.name)}</strong>
        <span>${renderProjectAssetModelLabels(group)}</span>
        <small>${escapeHtml(getProjectMetaLabel(project))} · ${formatDate(group.updatedAt)}</small>
      </div>
      <div class="saved-configuration-item__assets">
        ${renderProjectAssetFormats(group)}
        ${renderProjectAssetPrices(group)}
      </div>
      <div class="inline-actions">
        ${renderProjectAssetActions(project, group)}
      </div>
    `;
    savedConfigurationsList.append(item);
  }
}

function renderProjectAssetKinds(group) {
  const kinds = [];
  if (group.configurations.length > 0) {
    kinds.push('<span class="configuration-kind">Чертёж</span>');
  }
  if (group.pricingSpecifications.length > 0) {
    kinds.push('<span class="configuration-kind configuration-kind--pricing">Цена</span>');
  }
  return `<span class="configuration-kind-stack">${kinds.join("")}</span>`;
}

function renderProjectAssetModelLabels(group) {
  const labels = [
    ...group.configurations.map(configuration => getTemplateLabel(configuration.templateId)),
    ...group.pricingSpecifications.map(specification => `${specification.supplier} · ${specification.series}`)
  ].filter(Boolean);
  return [...new Set(labels)].map(escapeHtml).join("<br>") || '<span class="muted">—</span>';
}

function renderProjectAssetFormats(group) {
  const controls = group.configurations.map(configuration => {
    const formats = getTemplateFormats(configuration);
    const options = formats
      .map(format => `<option value="${escapeHtml(format)}" ${format === String(configuration.outputFormat).toLowerCase() ? "selected" : ""}>${escapeHtml(format.toUpperCase())}</option>`)
      .join("");
    return `
      <select class="format-select" data-format-for="${escapeHtml(configuration.id)}" aria-label="Формат чертежа ${escapeHtml(group.name)}">
        ${options}
      </select>`;
  });
  if (group.pricingSpecifications.length > 0) {
    controls.push('<span class="configuration-format-label">ТКП</span>');
  }
  return `<span class="configuration-format-stack">${controls.join("")}</span>`;
}

function renderProjectAssetPrices(group) {
  if (group.pricingSpecifications.length === 0) return '<span class="muted">—</span>';
  return `<span class="configuration-price-stack">${group.pricingSpecifications
    .map(specification => `<strong class="configuration-price">${escapeHtml(getPricingAmountLabel(specification))}</strong>`)
    .join("")}</span>`;
}

function renderProjectAssetActions(project, group) {
  const actions = [];
  for (const configuration of group.configurations) {
    const formats = getTemplateFormats(configuration);
    actions.push(`<a class="secondary button-link" href="/drawings?configurationId=${encodeURIComponent(configuration.id)}">Редактировать чертёж</a>`);
    if (formats.includes("pdf") && canCreateJobs()) {
      actions.push(`<button class="secondary" type="button" data-action="preview" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Просмотреть</button>`);
    }
    if (canCreateJobs()) {
      actions.push(`<button class="secondary" type="button" data-action="download" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Скачать чертёж</button>`);
      actions.push(`<button class="secondary secondary--danger" type="button" data-action="delete" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Удалить чертёж</button>`);
    }
  }
  for (const specification of group.pricingSpecifications) {
    actions.push(`<a class="secondary button-link" href="/pricing?specificationId=${encodeURIComponent(specification.id)}">Редактировать цену</a>`);
    actions.push(`<a class="primary primary--compact button-link" href="/api/pricing-specifications/${encodeURIComponent(specification.id)}/tkp">Скачать ТКП</a>`);
    if (canCreateJobs()) {
      actions.push(`<button class="secondary secondary--danger" type="button" data-action="delete-pricing" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(specification.id)}">Удалить цену</button>`);
    }
  }
  return actions.join("");
}

function createConfigurationsTable(project, assetGroups) {
  const wrap = document.createElement("div");
  wrap.className = "table-wrap table-wrap--compact project-assets-table";

  const table = document.createElement("table");
  table.innerHTML = `
    <thead>
      <tr>
        <th>Тип</th>
        <th>Конфигурация</th>
        <th>Шаблон / модель</th>
        <th>Формат</th>
        <th>Цена</th>
        <th>Обновлено</th>
        <th>Действия</th>
      </tr>
    </thead>
    <tbody></tbody>
  `;

  const tbody = table.querySelector("tbody");
  for (const group of assetGroups) {
    const row = document.createElement("tr");
    const isCombined = group.configurations.length > 0 && group.pricingSpecifications.length > 0;
    const isPricingOnly = group.configurations.length === 0 && group.pricingSpecifications.length > 0;
    row.className = `configuration-row${isCombined ? " configuration-row--combined" : ""}${isPricingOnly ? " configuration-row--pricing" : ""}`;
    row.innerHTML = `
      <td>${renderProjectAssetKinds(group)}</td>
      <td><strong>${escapeHtml(group.name)}</strong></td>
      <td>${renderProjectAssetModelLabels(group)}</td>
      <td>${renderProjectAssetFormats(group)}</td>
      <td>${renderProjectAssetPrices(group)}</td>
      <td>${formatDate(group.updatedAt)}</td>
      <td>
        <div class="inline-actions">
          ${renderProjectAssetActions(project, group)}
        </div>
      </td>
    `;
    tbody.append(row);
  }

  wrap.append(table);
  return wrap;
}

async function reloadProjectsAfterMutation(successMessage) {
  try {
    await loadProjects();
    showAccountStatus(successMessage);
  } catch (error) {
    if (error?.name !== "AbortError") {
      showPageLoadError();
    }
  }
}

async function createProject() {
  const name = projectNameInput.value.trim();
  if (!name) {
    projectNameInput.setCustomValidity(t("Укажите название проекта"));
    projectNameInput.reportValidity();
    return;
  }

  projectNameInput.setCustomValidity("");
  const response = await apiFetch("/api/projects", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      name,
      address: projectAddressInput?.value.trim() || "",
      factoryRequestNumber: projectFactoryRequestNumberInput?.value.trim() || "",
      description: ""
    })
  });

  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось создать проект")).join(" "), "error");
    return;
  }

  projectNameInput.value = "";
  if (projectAddressInput) projectAddressInput.value = "";
  if (projectFactoryRequestNumberInput) projectFactoryRequestNumberInput.value = "";
  if (accountCreateSection) accountCreateSection.hidden = true;
  toggleProjectCreateButton?.setAttribute("aria-expanded", "false");
  await reloadProjectsAfterMutation(localized("Проект создан.", "Project created."));
}

function getProjectFormValues(button) {
  const form = button.closest(".project-edit-form");
  const nameInput = form?.querySelector("[data-project-field='name']");
  return {
    name: nameInput?.value.trim() || "",
    address: form?.querySelector("[data-project-field='address']")?.value.trim() || "",
    factoryRequestNumber: form?.querySelector("[data-project-field='factoryRequestNumber']")?.value.trim() || "",
    nameInput
  };
}

async function updateProject(projectId, button) {
  const values = getProjectFormValues(button);
  if (!values.name) {
    values.nameInput?.setCustomValidity(t("Укажите название проекта"));
    values.nameInput?.reportValidity();
    return;
  }

  values.nameInput?.setCustomValidity("");
  const response = await apiFetch(`/api/projects/${encodeURIComponent(projectId)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      name: values.name,
      address: values.address,
      factoryRequestNumber: values.factoryRequestNumber,
      description: ""
    })
  });

  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось сохранить проект")).join(" "), "error");
    return;
  }

  await reloadProjectsAfterMutation(localized("Проект сохранен.", "Project saved."));
}

async function deleteProject(projectId) {
  const project = state.projects.find(item => item.id === projectId);
  const label = project?.name ? ` "${project.name}"` : "";
  const confirmation = getLanguage() === "en"
    ? `Delete project${label} and every saved configuration inside it? This action cannot be undone.`
    : `Удалить проект${label} и все сохраненные конфигурации внутри него? Это действие нельзя отменить.`;
  if (!confirm(confirmation)) {
    return;
  }

  const response = await apiFetch(`/api/projects/${encodeURIComponent(projectId)}`, {
    method: "DELETE"
  });

  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось удалить проект")).join(" "), "error");
    return;
  }

  await reloadProjectsAfterMutation(localized("Проект удален.", "Project deleted."));
}

async function deleteConfiguration(projectId, configurationId) {
  const project = state.projects.find(item => item.id === projectId);
  const configuration = (state.configurationsByProjectId.get(projectId) || [])
    .find(item => item.id === configurationId);
  const configurationLabel = configuration ? ` "${getConfigurationName(configuration)}"` : "";
  const projectLabel = project?.name ? ` из проекта "${project.name}"` : "";
  const confirmation = localized(
    `Удалить сохраненную конфигурацию${configurationLabel}${projectLabel}? Это действие нельзя отменить.`,
    `Delete saved configuration${configurationLabel}${project?.name ? ` from project "${project.name}"` : ""}? This action cannot be undone.`);
  if (!confirm(confirmation)) {
    return;
  }

  const response = await apiFetch(`/api/project-configurations/${encodeURIComponent(configurationId)}`, {
    method: "DELETE"
  });

  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось удалить конфигурацию")).join(" "), "error");
    return;
  }

  await reloadProjectsAfterMutation(localized("Конфигурация удалена.", "Configuration deleted."));
}

async function deletePricingSpecification(projectId, specificationId) {
  const project = state.projects.find(item => item.id === projectId);
  const specification = (state.pricingByProjectId.get(projectId) || [])
    .find(item => item.id === specificationId);
  const specificationLabel = specification ? ` "${specification.name}"` : "";
  const projectLabel = project?.name ? ` из проекта "${project.name}"` : "";
  const confirmation = localized(
    `Удалить конфигурацию цены${specificationLabel}${projectLabel}? Это действие нельзя отменить.`,
    `Delete pricing configuration${specificationLabel}${project?.name ? ` from project "${project.name}"` : ""}? This action cannot be undone.`);
  if (!confirm(confirmation)) return;

  const response = await apiFetch(`/api/pricing-specifications/${encodeURIComponent(specificationId)}`, {
    method: "DELETE"
  });
  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось удалить конфигурацию цены")).join(" "), "error");
    return;
  }

  await reloadProjectsAfterMutation(localized(
    "Конфигурация цены удалена.",
    "Pricing configuration deleted."));
}

async function downloadConfiguration(projectId, configurationId, format, options = {}) {
  if (!canCreateJobs()) {
    showAccountStatus("Недостаточно прав для генерации файла.", "error");
    return;
  }

  const configuration = (state.configurationsByProjectId.get(projectId) || [])
    .find(item => item.id === configurationId);
  if (!configuration) return;

  const generationKey = getGenerationKey(projectId, configurationId);
  if (state.activeGenerationActions.has(generationKey)) {
    showAccountStatus(localized(
      "Файл для этой конфигурации уже формируется.",
      "A file for this configuration is already being prepared."));
    return;
  }

  state.activeGenerationActions.set(generationKey, options.preview ? "preview" : "download");
  syncGenerationControls();

  try {
    showAccountStatus(`Генерация ${String(format).toUpperCase()} запущена...`);
    const createResponse = await apiFetch("/api/jobs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        templateId: configuration.templateId,
        outputFormat: format,
        parameters: configuration.parameters || {}
      })
    });

    if (!createResponse.ok) {
      showAccountStatus((await readProblem(createResponse, "Не удалось создать задание")).join(" "), "error");
      return;
    }

    const job = await sessionRequests.readJson(createResponse);
    if (job === sessionRequests.stalePayload) return;
    await waitForDownload(job.id, format, options);
  } catch (error) {
    if (error?.name !== "AbortError") {
      showAccountStatus(localized(
        "Не удалось связаться с сервисом генерации. Повторите позже.",
        "The generation service could not be reached. Try again later."), "error");
    }
  } finally {
    state.activeGenerationActions.delete(generationKey);
    syncGenerationControls();
  }
}

async function waitForDownload(jobId, format, options = {}) {
  for (let attempt = 0; attempt < 600; attempt += 1) {
    await new Promise(resolve => setTimeout(resolve, 1000));

    const response = await apiFetch(`/api/jobs/${encodeURIComponent(jobId)}`);
    if (!sessionRequests.isCurrent(response)) return;
    if (!response.ok) continue;

    const job = await sessionRequests.readJson(response);
    if (job === sessionRequests.stalePayload) return;
    if (job.status === "Completed") {
      const file = (job.resultFiles || []).find(candidate =>
        String(candidate.format).toLowerCase() === String(format).toLowerCase())
        || (job.resultFiles || [])[0];

      if (!file) {
        showAccountStatus("Задание завершено, но файл не найден.", "error");
        return;
      }

      if (options.preview && openGeneratedFilePreview(file, options.trigger)) {
        showAccountStatus(getLanguage() === "en" ? "PDF is ready for preview." : "PDF готов к просмотру.");
      } else {
        showAccountStatus(t("Файл готов. Скачивание началось."));
        window.location.href = file.downloadUrl;
      }
      return;
    }

    if (job.status === "Failed" || job.status === "Cancelled") {
      showAccountStatus(job.errorMessage || "Генерация завершилась ошибкой.", "error");
      return;
    }

    showAccountStatus(`Генерация ${String(format).toUpperCase()}: ${job.status}`);
  }

  showAccountStatus("Генерация идет дольше ожидаемого. Проверьте историю заданий в редакторе.", "error");
}

async function loadAdminData() {
  if (!canAdmin()) return;
  await Promise.all([loadAdminUsers(), loadAdminTemplates(), loadTemplateAnalyses()]);
}

async function loadAdminUsers() {
  const response = await apiFetch("/api/admin/users");
  requireSuccessfulLoadResponse(response, "admin users");
  const users = requireCurrentLoadPayload(
    await sessionRequests.readJson(response),
    "admin users");
  state.adminUsers = users;
  renderAdminUsers();
}

function setAdminUserRowBusy(row, busy) {
  if (!row) return;

  if (busy) {
    row.setAttribute("aria-busy", "true");
    row.dataset.adminUserBusy = "true";
  } else {
    row.removeAttribute("aria-busy");
    delete row.dataset.adminUserBusy;
  }

  row.querySelectorAll("button, input").forEach(control => {
    if (busy) {
      if (control.dataset.adminRowWasDisabled === undefined) {
        control.dataset.adminRowWasDisabled = String(control.disabled);
      }
      control.disabled = true;
      return;
    }

    if (control.dataset.adminRowWasDisabled !== undefined) {
      control.disabled = control.dataset.adminRowWasDisabled === "true";
      delete control.dataset.adminRowWasDisabled;
    }
  });
}

function syncAdminUserActionRows() {
  for (const row of adminUsersTableBody.querySelectorAll("tr[data-admin-user]")) {
    setAdminUserRowBusy(row, state.activeAdminUserActions.has(row.dataset.adminUser));
  }
}

function renderAdminUsers() {
  adminUsersTableBody.replaceChildren();

  for (const user of state.adminUsers) {
    const row = document.createElement("tr");
    row.dataset.adminUser = user.userName;
    const status = !user.enabled && (user.approvalStatus || "Approved") === "Approved"
      ? "Disabled"
      : (user.approvalStatus || (user.enabled ? "Approved" : "Disabled"));
    const normalizedStatus = status.toLowerCase();
    const isCurrentUser = user.userName === state.currentUser?.userName;
    const isAdminUser = (user.roles || []).includes("Admin");
    const actions = [];
    actions.push(`<button class="secondary" type="button" data-action="save-roles" data-user="${escapeHtml(user.userName)}">Сохранить права</button>`);
    if (!isCurrentUser && normalizedStatus !== "approved") {
      const label = normalizedStatus === "disabled" ? "Включить" : "Подтвердить";
      actions.push(`<button class="secondary" type="button" data-action="approve" data-user="${escapeHtml(user.userName)}">${label}</button>`);
    }
    if (!isCurrentUser && normalizedStatus === "pending") {
      actions.push(`<button class="secondary" type="button" data-action="reject" data-user="${escapeHtml(user.userName)}">Отклонить</button>`);
    }
    if (!isCurrentUser && !isAdminUser) {
      actions.push(`<button class="secondary secondary--danger" type="button" data-action="delete" data-user="${escapeHtml(user.userName)}">Удалить</button>`);
    } else if (!isCurrentUser && isAdminUser) {
      actions.push("<span class=\"muted admin-action-note\">Админ защищен</span>");
    }

    row.innerHTML = `
      <td>${escapeHtml(user.displayName || user.userName)}<br><span class="muted">${escapeHtml(user.userName)}</span></td>
      <td><span class="status ${escapeHtml(normalizedStatus)}">${escapeHtml(status)}</span></td>
      <td>${renderAdminRoleControls(user, isCurrentUser)}</td>
      <td><div class="inline-actions">${actions.join("") || "<span class=\"muted\">Нет действий</span>"}</div></td>
    `;
    adminUsersTableBody.append(row);
  }

  syncAdminUserActionRows();
}

function renderAdminRoleControls(user, isCurrentUser) {
  const roles = new Set(user.roles || []);
  return `
    <div class="role-controls">
      ${ADMIN_ROLE_OPTIONS.map(role => {
        const checked = roles.has(role) ? " checked" : "";
        const disabled = isCurrentUser && role === "Admin" ? " disabled" : "";
        return `
          <label class="role-control">
            <input type="checkbox" data-role="${role}"${checked}${disabled}>
            <span>${role}</span>
          </label>
        `;
      }).join("")}
    </div>
  `;
}

function getSelectedAdminRoles(button) {
  const row = button.closest("tr");
  const roles = Array.from(row?.querySelectorAll("input[data-role]:checked") || [])
    .map(input => input.dataset.role)
    .filter(Boolean);
  return roles.length > 0 ? roles : ["Viewer"];
}

async function handleAdminUserAction(action, userName, button) {
  if (!userName || state.activeAdminUserActions.has(userName)) return;

  let url = `/api/admin/users/${encodeURIComponent(userName)}`;
  let options = { method: "DELETE" };
  if (action === "save-roles") {
    options = {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ roles: getSelectedAdminRoles(button) })
    };
  } else if (action === "delete") {
    if (!confirm(`Удалить аккаунт ${userName}? Это действие нельзя отменить.`)) {
      return;
    }
  } else if (action === "approve") {
    const selectedRoles = getSelectedAdminRoles(button);
    const roles = selectedRoles.some(role => role === "Admin" || role === "Operator")
      ? selectedRoles
      : ["Operator", "Viewer"];
    url += "/approve";
    options = {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ roles })
    };
  } else if (action === "reject") {
    url += "/reject";
    options = { method: "POST" };
  }

  state.activeAdminUserActions.add(userName);
  syncAdminUserActionRows();
  showAdminStatus(localized("Обновление пользователя…", "Updating user…"));
  try {
    const response = await apiFetch(url, options);
    if (!response.ok) {
      showAdminStatus((await readProblem(response, "Не удалось обновить пользователя")).join(" "), "error");
      return;
    }

    await loadAdminUsers();
    showAdminStatus(localized("Данные пользователя обновлены.", "User updated."));
  } catch (error) {
    if (error?.name !== "AbortError") {
      showAdminStatus(localized(
        "Не удалось связаться с сервисом пользователей.",
        "The user service could not be reached."), "error");
    }
  } finally {
    state.activeAdminUserActions.delete(userName);
    syncAdminUserActionRows();
  }
}

async function loadAdminTemplates() {
  const response = await apiFetch("/api/admin/templates");
  requireSuccessfulLoadResponse(response, "admin templates");
  const templates = requireCurrentLoadPayload(
    await sessionRequests.readJson(response),
    "admin templates");
  state.adminTemplates = templates;
  renderAdminTemplates();
}

function renderAdminTemplates() {
  adminTemplatesTableBody.replaceChildren();

  for (const template of state.adminTemplates) {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td>${escapeHtml(template.name || template.code || template.id)}<br><span class="muted">${escapeHtml(template.code || template.id)}</span></td>
      <td>${escapeHtml((template.outputFormats || []).map(format => format.toUpperCase()).join(", "))}</td>
      <td>
        <label class="mini-toggle-hit">
          <input class="mini-toggle" type="checkbox" data-template-id="${escapeHtml(template.id)}" ${template.enabled ? "checked" : ""}>
          <span class="sr-only">${escapeHtml(template.name || template.code || template.id)}</span>
        </label>
      </td>
    `;
    adminTemplatesTableBody.append(row);
  }
}

async function setTemplateEnabled(templateId, enabled, input) {
  input.disabled = true;
  input.setAttribute("aria-busy", "true");
  showAdminStatus(localized("Обновление шаблона…", "Updating template…"));
  try {
    const response = await apiFetch(`/api/admin/templates/${encodeURIComponent(templateId)}/enabled`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ enabled })
    });

    if (!response.ok) {
      showAdminStatus((await readProblem(response, "Не удалось обновить шаблон")).join(" "), "error");
      await loadAdminTemplates();
      return;
    }

    await Promise.all([loadTemplates(), loadAdminTemplates()]);
    showAdminStatus(localized("Доступность шаблона обновлена.", "Template availability updated."));
  } catch (error) {
    if (error?.name !== "AbortError") {
      showAdminStatus(localized(
        "Не удалось связаться с сервисом шаблонов.",
        "The template service could not be reached."), "error");
      input.checked = !enabled;
    }
  } finally {
    input.disabled = false;
    input.removeAttribute("aria-busy");
  }
}

function showTemplateImportStatus(message, kind = "empty") {
  if (!templateImportStatus) return;
  setLiveRegionUrgency(templateImportStatus, kind);
  templateImportStatus.hidden = false;
  templateImportStatus.className = `template-import__status ${kind}`;
  templateImportStatus.textContent = message;
}

async function importTemplate(event) {
  event.preventDefault();
  const template = templateGrbFile?.files?.[0];
  const components = templateFragmentsFile?.files?.[0];

  if (!template) {
    showTemplateImportStatus(localized("Выберите основной файл GRB.", "Select the main GRB file."), "error");
    return;
  }

  const formData = new FormData();
  formData.append("template", template);
  if (components) formData.append("components", components);

  templateImportButton.disabled = true;
  templateImportButton.setAttribute("aria-busy", "true");
  templateImportForm.setAttribute("aria-busy", "true");
  showTemplateImportStatus(localized(
    "Файлы загружаются. Анализ выполнит Windows Worker…",
    "Uploading files. The Windows Worker will run the analysis…"));
  try {
    const response = await apiFetch("/api/admin/template-analyses", {
      method: "POST",
      body: formData
    });

    if (!response.ok) {
      const messages = await readProblem(response, localized(
        "Не удалось запустить анализ",
        "Could not start template analysis"));
      showTemplateImportStatus(messages.join(" "), "error");
      return;
    }

    const analysis = await sessionRequests.readJson(response);
    if (analysis === sessionRequests.stalePayload) return;
    templateImportForm.reset();
    showTemplateImportStatus(
      localized(
        "Задание создано. После анализа проверьте черновик формы.",
        "Analysis queued. Review the generated form draft when it completes."),
      "success");
    await loadTemplateAnalyses();
  } catch {
    showTemplateImportStatus(localized(
      "Не удалось запустить анализ",
      "Could not start template analysis"), "error");
  } finally {
    templateImportButton.disabled = false;
    templateImportButton.removeAttribute("aria-busy");
    templateImportForm.removeAttribute("aria-busy");
  }
}

async function loadTemplateAnalyses() {
  const response = await apiFetch("/api/admin/template-analyses");
  requireSuccessfulLoadResponse(response, "template analyses");
  const analyses = requireCurrentLoadPayload(
    await sessionRequests.readJson(response),
    "template analyses");
  state.templateAnalyses = analyses;
  renderTemplateAnalyses();
  scheduleTemplateAnalysisRefresh();
}

function scheduleTemplateAnalysisRefresh() {
  window.clearTimeout(scheduleTemplateAnalysisRefresh.timer);
  if (!state.templateAnalyses.some(item => item.status === "pending" || item.status === "processing")) return;
  scheduleTemplateAnalysisRefresh.timer = window.setTimeout(async () => {
    if (!canAdmin()) return;
    try {
      await loadTemplateAnalyses();
    } catch {
      scheduleTemplateAnalysisRefresh();
    }
  }, 2500);
}

function getTemplateAnalysisStatusLabel(status) {
  return ({
    pending: localized("В очереди", "Queued"),
    processing: localized("Анализ T-FLEX", "T-FLEX analysis"),
    completed: localized("Готов к проверке", "Ready for review"),
    failed: localized("Ошибка", "Failed"),
    published: localized("Опубликован", "Published")
  })[status] || status;
}

function renderTemplateAnalyses() {
  if (!templateAnalysesList) return;
  templateAnalysesList.replaceChildren();
  for (const analysis of state.templateAnalyses) {
    const card = document.createElement("article");
    card.className = `template-analysis-card template-analysis-card--${analysis.status}`;
    const warningCount = analysis.warningCount ?? analysis.warnings?.length ?? 0;
    const parameterCount = analysis.parameterCount ?? analysis.draft?.parameters?.length ?? 0;
    const action = analysis.status === "completed"
      ? `<button class="secondary secondary--compact" type="button" data-analysis-action="review" data-analysis-id="${escapeHtml(analysis.id)}">Проверить форму</button>`
      : "";
    card.innerHTML = `
      <div class="template-analysis-card__body">
        <div>
          <strong>${escapeHtml(analysis.originalTemplateFileName)}</strong>
          <span class="status ${escapeHtml(analysis.status)}">${escapeHtml(getTemplateAnalysisStatusLabel(analysis.status))}</span>
        </div>
        <p>${analysis.status === "failed"
          ? escapeHtml(analysis.errorMessage || localized("Анализ завершился ошибкой.", "Analysis failed."))
          : escapeHtml(localized(
              `${parameterCount} полей для пользователя · ${warningCount} предупреждений`,
              `${parameterCount} user fields · ${warningCount} warnings`))}</p>
      </div>
      ${action}
    `;
    templateAnalysesList.append(card);
  }
}

async function openTemplateAnalysisEditor(analysisId) {
  const summary = state.templateAnalyses.find(item => item.id === analysisId);
  if (summary?.status !== "completed") return;
  try {
    const response = await apiFetch(`/api/admin/template-analyses/${encodeURIComponent(analysisId)}`);
    if (!response.ok) {
      showTemplateImportStatus(localized("Не удалось загрузить черновик.", "Could not load the draft."), "error");
      return;
    }
    const analysis = await sessionRequests.readJson(response);
    if (analysis === sessionRequests.stalePayload || !analysis?.draft) return;
    state.activeTemplateAnalysis = structuredClone(analysis);
    renderTemplateAnalysisEditor(state.activeTemplateAnalysis);
    templateAnalysisEditor.hidden = false;
    templateAnalysisEditor.scrollIntoView({ behavior: "smooth", block: "start" });
  } catch {
    showTemplateImportStatus(localized("Не удалось загрузить черновик.", "Could not load the draft."), "error");
  }
}

function renderTemplateAnalysisEditor(analysis) {
  const draft = analysis.draft;
  analysisTemplateName.value = draft.name || "";
  analysisTemplateCode.value = draft.code || "";
  analysisTemplateId.value = draft.id || "";
  analysisTemplateDescription.value = draft.description || "";
  const warnings = analysis.warnings || [];
  templateAnalysisWarnings.hidden = warnings.length === 0;
  templateAnalysisWarnings.innerHTML = warnings.length === 0
    ? ""
    : `<strong>${escapeHtml(localized("Публикация разрешена, но проверьте:", "Publishing is allowed, but review:"))}</strong>
       <ul>${warnings.map(item => `<li>${escapeHtml(item)}</li>`).join("")}</ul>`;

  const parameters = draft.parameters || [];
  const calculated = draft.calculatedVariables || [];
  templateAnalysisSummary.textContent = localized(
    `Внешние: ${parameters.length} · вычисляемые: ${calculated.length} · правила: ${(draft.validationRules || []).length}`,
    `External: ${parameters.length} · calculated: ${calculated.length} · rules: ${(draft.validationRules || []).length}`);
  templateAnalysisParameters.replaceChildren();
  for (const [kind, definitions] of [["parameters", parameters], ["calculatedVariables", calculated]]) {
    definitions.forEach((definition, index) => {
      const row = document.createElement("tr");
      row.dataset.definitionKind = kind;
      row.dataset.definitionIndex = String(index);
      row.classList.toggle("is-calculated", kind === "calculatedVariables");
      row.innerHTML = `
        <td><input data-definition-field="displayName" value="${escapeHtml(definition.displayName || definition.name)}" aria-label="Название поля"></td>
        <td><code>${escapeHtml(definition.name)}</code>${kind === "calculatedVariables" ? "<small>авто</small>" : ""}</td>
        <td><select data-definition-field="type" aria-label="Тип поля">${["integer", "number", "string", "enum", "bool"].map(type => `<option value="${type}"${definition.type === type ? " selected" : ""}>${type}</option>`).join("")}</select></td>
        <td><input data-definition-field="unit" value="${escapeHtml(definition.unit || "")}" aria-label="Единица измерения"></td>
        <td><input data-definition-field="minValue" type="number" step="any" value="${escapeHtml(definition.minValue ?? "")}" aria-label="Минимум"></td>
        <td><input data-definition-field="maxValue" type="number" step="any" value="${escapeHtml(definition.maxValue ?? "")}" aria-label="Максимум"></td>
        <td><input data-definition-field="isRequired" type="checkbox" ${definition.isRequired ? "checked" : ""} ${kind === "calculatedVariables" ? "disabled" : ""} aria-label="Обязательное поле"></td>
      `;
      templateAnalysisParameters.append(row);
    });
  }
}

function collectTemplateAnalysisDraft() {
  const analysis = state.activeTemplateAnalysis;
  if (!analysis?.draft) return null;
  const draft = structuredClone(analysis.draft);
  draft.name = analysisTemplateName.value.trim();
  draft.code = analysisTemplateCode.value.trim();
  draft.id = analysisTemplateId.value.trim();
  draft.description = analysisTemplateDescription.value.trim();
  for (const row of templateAnalysisParameters.querySelectorAll("tr[data-definition-kind]")) {
    const definition = draft[row.dataset.definitionKind]?.[Number(row.dataset.definitionIndex)];
    if (!definition) continue;
    for (const input of row.querySelectorAll("[data-definition-field]")) {
      const field = input.dataset.definitionField;
      if (field === "isRequired") definition[field] = input.checked;
      else if (field === "minValue" || field === "maxValue") definition[field] = input.value === "" ? null : Number(input.value);
      else definition[field] = input.value.trim();
    }
  }
  return draft;
}

async function saveActiveTemplateAnalysisDraft({ quiet = false } = {}) {
  const analysis = state.activeTemplateAnalysis;
  const draft = collectTemplateAnalysisDraft();
  if (!analysis || !draft) return false;
  if (!analysisTemplateName.reportValidity() || !analysisTemplateCode.reportValidity() || !analysisTemplateId.reportValidity()) return false;
  saveTemplateAnalysisDraft.disabled = true;
  publishTemplateAnalysis.disabled = true;
  try {
    const response = await apiFetch(`/api/admin/template-analyses/${encodeURIComponent(analysis.id)}/draft`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(draft)
    });
    if (!response.ok) {
      showTemplateImportStatus((await readProblem(response, localized("Не удалось сохранить черновик", "Could not save draft"))).join(" "), "error");
      return false;
    }
    const updated = await sessionRequests.readJson(response);
    if (updated === sessionRequests.stalePayload) return false;
    state.activeTemplateAnalysis = updated;
    const index = state.templateAnalyses.findIndex(item => item.id === updated.id);
    if (index >= 0) state.templateAnalyses[index] = updated;
    if (!quiet) showTemplateImportStatus(localized("Черновик сохранен.", "Draft saved."), "success");
    return true;
  } finally {
    saveTemplateAnalysisDraft.disabled = false;
    publishTemplateAnalysis.disabled = false;
  }
}

async function publishActiveTemplateAnalysis() {
  if (!await saveActiveTemplateAnalysisDraft({ quiet: true })) return;
  const analysis = state.activeTemplateAnalysis;
  publishTemplateAnalysis.disabled = true;
  showTemplateImportStatus(localized("Публикация шаблона…", "Publishing template…"));
  try {
    const response = await apiFetch(`/api/admin/template-analyses/${encodeURIComponent(analysis.id)}/publish`, { method: "POST" });
    if (!response.ok) {
      showTemplateImportStatus((await readProblem(response, localized("Не удалось опубликовать шаблон", "Could not publish template"))).join(" "), "error");
      return;
    }
    templateAnalysisEditor.hidden = true;
    state.activeTemplateAnalysis = null;
    showTemplateImportStatus(localized("Шаблон опубликован и доступен в редакторе.", "Template published and available in the editor."), "success");
    await Promise.all([loadTemplates(), loadAdminTemplates(), loadTemplateAnalyses()]);
  } finally {
    publishTemplateAnalysis.disabled = false;
  }
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
      showRegisterStatus(messages.join(" "), "error");
      return;
    }

    registerForm.reset();
    showRegisterStatus(t("Заявка отправлена. Доступ появится после подтверждения администратором."));
  } catch (error) {
    if (error?.name !== "AbortError") {
      showRegisterStatus(localized(
        "Не удалось отправить заявку: нет связи с сервисом.",
        "The request could not be submitted because the service could not be reached."), "error");
    }
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
        showPageLoadError();
        return;
      }
      passwordInput.setCustomValidity(t("Неверный логин или пароль"));
      passwordInput.reportValidity();
      return;
    }

    const currentUser = await sessionRequests.readJson(response);
    if (currentUser === sessionRequests.stalePayload) return;
    clearAccountSessionState();
    state.currentUser = currentUser;
    passwordInput.value = "";
    updateAuthView();
    await loadTemplates();
    await loadProjects();
    await loadAccountJobs();
    await loadAdminData();
    updateAuthView();
  } catch (error) {
    if (error?.name !== "AbortError") {
      showPageLoadError();
    }
  }
}

async function logout() {
  clearAccountSessionState();
  state.currentUser = null;
  updateAuthView();
  try {
    const response = await apiFetch("/api/auth/logout", { method: "POST" });
    if (!response.ok && response.status !== 401 && response.status !== 403) {
      showPageLoadError({ context: "logout" });
      return;
    }
    requestAnimationFrame(() => {
      guestLoginForm?.querySelector("[name='userName']")?.focus({ preventScroll: true });
    });
  } catch (error) {
    if (error?.name !== "AbortError") showPageLoadError({ context: "logout" });
  }
}

async function runBoot({ context = "load" } = {}) {
  let errorContext = context;
  showPageLoading(context);
  sessionRequests.invalidate();
  try {
    const authenticated = await loadCurrentUser();
    errorContext = "load";
    updatePageLoadErrorCopy("load");
    if (!authenticated) {
      hidePageSkeleton();
      return;
    }

    await loadTemplates();
    await loadProjects();
    await loadAccountJobs();
    await loadAdminData();
    updateAuthView();
    hidePageSkeleton();
  } catch (error) {
    if (error?.name === "AbortError") {
      hidePageSkeleton();
      return;
    }
    showPageLoadError({ context: errorContext });
  }
}

function boot({ context = "load" } = {}) {
  if (bootPromise) return bootPromise;
  bootPromise = runBoot({ context }).finally(() => {
    bootPromise = null;
  });
  return bootPromise;
}

registerForm.addEventListener("submit", register);
loginForm.addEventListener("submit", login);
guestLoginForm?.addEventListener("submit", login);
showRegisterPanelButton?.addEventListener("click", () => showAuthPanel("register"));
showLoginPanelButton?.addEventListener("click", () => showAuthPanel("login"));
logoutButton.addEventListener("click", logout);
retryBootButton?.addEventListener("click", () => {
  const context = pageLoadErrorContext;
  void boot({ context });
});
createProjectButton.addEventListener("click", createProject);
toggleProjectCreateButton?.addEventListener("click", () => {
  const willOpen = accountCreateSection.hidden;
  accountCreateSection.hidden = !willOpen;
  toggleProjectCreateButton.setAttribute("aria-expanded", String(willOpen));
  if (willOpen) requestAnimationFrame(() => projectNameInput?.focus({ preventScroll: true }));
});
globalSearchInput?.addEventListener("input", event => applyAccountSearch(event.currentTarget));
projectSearchInput?.addEventListener("input", event => applyAccountSearch(event.currentTarget));
for (const searchInput of [globalSearchInput, projectSearchInput]) {
  searchInput?.addEventListener("keydown", event => {
    if (event.key !== "Escape") return;
    event.currentTarget.value = "";
    applyAccountSearch(event.currentTarget);
  });
}
window.addEventListener("hashchange", updateAuthView);
projectsList.addEventListener("click", event => {
  const button = event.target.closest("button[data-action]");
  if (!button) return;

  if (button.dataset.action === "delete-pricing") {
    deletePricingSpecification(button.dataset.projectId, button.dataset.id);
  } else if (button.dataset.action === "delete") {
    deleteConfiguration(button.dataset.projectId, button.dataset.id);
  } else if (button.dataset.action === "download") {
    const select = findConfigurationFormatSelect(button.dataset.id, findConfigurationActionScope(button));
    downloadConfiguration(button.dataset.projectId, button.dataset.id, select?.value || "pdf", {
      trigger: button
    });
  } else if (button.dataset.action === "preview") {
    downloadConfiguration(button.dataset.projectId, button.dataset.id, "pdf", {
      preview: true,
      trigger: button
    });
  } else if (button.dataset.action === "update-project") {
    updateProject(button.dataset.projectId, button);
  } else if (button.dataset.action === "delete-project") {
    deleteProject(button.dataset.projectId);
  }
});
savedConfigurationsList?.addEventListener("click", event => {
  const button = event.target.closest("button[data-action]");
  if (!button) return;

  if (button.dataset.action === "delete-pricing") {
    deletePricingSpecification(button.dataset.projectId, button.dataset.id);
  } else if (button.dataset.action === "download") {
    const select = findConfigurationFormatSelect(button.dataset.id, findConfigurationActionScope(button));
    downloadConfiguration(button.dataset.projectId, button.dataset.id, select?.value || "pdf", {
      trigger: button
    });
  } else if (button.dataset.action === "preview") {
    downloadConfiguration(button.dataset.projectId, button.dataset.id, "pdf", {
      preview: true,
      trigger: button
    });
  }
});
adminUsersTableBody.addEventListener("click", event => {
  const button = event.target.closest("button[data-action]");
  if (!button) return;
  handleAdminUserAction(button.dataset.action, button.dataset.user, button);
});
adminTemplatesTableBody.addEventListener("change", event => {
  const input = event.target.closest("input[data-template-id]");
  if (!input) return;
  setTemplateEnabled(input.dataset.templateId, input.checked, input);
});
templateImportForm?.addEventListener("submit", importTemplate);
templateAnalysesList?.addEventListener("click", event => {
  const button = event.target.closest("button[data-analysis-action='review']");
  if (button) openTemplateAnalysisEditor(button.dataset.analysisId);
});
closeTemplateAnalysisEditor?.addEventListener("click", () => {
  templateAnalysisEditor.hidden = true;
  state.activeTemplateAnalysis = null;
});
saveTemplateAnalysisDraft?.addEventListener("click", () => saveActiveTemplateAnalysisDraft());
publishTemplateAnalysis?.addEventListener("click", publishActiveTemplateAnalysis);
window.addEventListener("tflex:languagechange", () => {
  updateAuthView();
  if (pageLoadError && !pageLoadError.hidden) {
    updatePageLoadErrorCopy();
  }
  renderAccountData();
  if (canAdmin()) {
    renderAdminUsers();
    renderAdminTemplates();
    renderTemplateAnalyses();
    if (state.activeTemplateAnalysis) renderTemplateAnalysisEditor(state.activeTemplateAnalysis);
  }
});

await boot();
