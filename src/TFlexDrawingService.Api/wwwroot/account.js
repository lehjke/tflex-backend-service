import { getLanguage, t } from "./i18n.js?v=20260720-ui-hardening-4";
import { openGeneratedFilePreview } from "./file-preview.js?v=20260720-ui-hardening-4";
import { createSessionRequestGuard } from "./session-requests.js?v=20260720-ui-hardening-1";

const state = {
  currentUser: null,
  projects: [],
  configurationsByProjectId: new Map(),
  templates: [],
  jobs: [],
  adminUsers: [],
  adminTemplates: []
};
const sessionRequests = createSessionRequestGuard();

const guestMain = document.querySelector("#guestMain");
const accountMain = document.querySelector("#accountMain");
const pageSkeleton = document.querySelector("#pageSkeleton");
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
const adminNavLinks = document.querySelectorAll(".admin-only-nav");
const logoutButton = document.querySelector("#logoutButton");
const globalSearchInput = document.querySelector(".global-search input");
const projectNameInput = document.querySelector("#projectNameInput");
const projectAddressInput = document.querySelector("#projectAddressInput");
const projectFactoryRequestNumberInput = document.querySelector("#projectFactoryRequestNumberInput");
const createProjectButton = document.querySelector("#createProjectButton");
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
const adminUsersTableBody = document.querySelector("#adminUsersTableBody");
const adminTemplatesTableBody = document.querySelector("#adminTemplatesTableBody");
const accountCreateSection = document.querySelector(".account-create");
const templateImportForm = document.querySelector("#templateImportForm");
const templateManifestFile = document.querySelector("#templateManifestFile");
const templateGrbFile = document.querySelector("#templateGrbFile");
const templateFragmentsFile = document.querySelector("#templateFragmentsFile");
const templateImportButton = document.querySelector("#templateImportButton");
const templateImportStatus = document.querySelector("#templateImportStatus");
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

function showAccountStatus(message, kind = "empty") {
  accountStatus.hidden = false;
  accountStatus.className = kind;
  accountStatus.textContent = message;
}

function hideAccountStatus() {
  accountStatus.hidden = true;
  accountStatus.textContent = "";
}

function clearAccountSessionState() {
  sessionRequests.invalidate();
  state.projects = [];
  state.configurationsByProjectId = new Map();
  state.templates = [];
  state.jobs = [];
  state.adminUsers = [];
  state.adminTemplates = [];

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
  if (projectSearchInput) projectSearchInput.value = "";
  if (globalSearchInput) globalSearchInput.value = "";

  projectsList.replaceChildren();
  savedConfigurationsList?.replaceChildren();
  adminUsersTableBody.replaceChildren();
  adminTemplatesTableBody.replaceChildren();
  hideAccountStatus();
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

function getAllConfigurations() {
  return state.projects.flatMap(project => {
    const configurations = state.configurationsByProjectId.get(project.id) || [];
    return configurations.map(configuration => ({ project, configuration }));
  });
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

function matchesProjectSearch(project, configurations, query) {
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
    ])
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

function applyAccountSearch(source = null) {
  const value = source?.value || "";
  syncSearchInputs(value, source);
  renderProjects();
  renderSavedConfigurations();
}

function updateMetrics() {
  const allConfigurations = getAllConfigurations();
  const completedJobs = state.jobs.filter(job => String(job.status).toLowerCase() === "completed");
  const pendingJobs = state.jobs.filter(job => {
    const status = String(job.status).toLowerCase();
    return status === "pending" || status === "running";
  });
  const resultFilesCount = completedJobs.reduce((total, job) => total + (job.resultFiles || []).length, 0);

  if (projectsMetric) projectsMetric.textContent = String(state.projects.length);
  if (configurationsMetric) configurationsMetric.textContent = String(allConfigurations.length);
  if (readyFilesMetric) readyFilesMetric.textContent = String(resultFilesCount);
  if (pendingMetric) pendingMetric.textContent = String(pendingJobs.length);
}

function hidePageSkeleton() {
  if (pageSkeleton) {
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
  accountMain.classList.toggle("is-admin-route", showAdminPanel);
  adminPanel.hidden = !showAdminPanel;
  adminPanel.classList.toggle("is-open", showAdminPanel);
  adminNavLinks.forEach(link => {
    link.hidden = !isAdmin;
  });
  if (adminAccessCard) adminAccessCard.hidden = !isAdmin;
  if (accountCreateSection) accountCreateSection.hidden = !canCreateJobs();

  if (showAdminPanel) {
    requestAnimationFrame(() => {
      const desktopOffset = window.matchMedia("(min-width: 901px)").matches ? 42 : 0;
      window.scrollTo({ top: desktopOffset, left: 0, behavior: "auto" });
    });
  }

  if (authenticated) {
    currentUserName.textContent = state.currentUser.displayName || state.currentUser.userName;
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = !isAdmin;
      currentUserRoleLabel.textContent = isAdmin ? "Admin" : "";
    }
  } else {
    currentUserName.textContent = "";
    if (currentUserRoleLabel) {
      currentUserRoleLabel.hidden = true;
      currentUserRoleLabel.textContent = "";
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

async function loadCurrentUser() {
  const response = await apiFetch("/api/auth/me");
  if (!response.ok) {
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

async function loadTemplates() {
  const response = await apiFetch("/api/templates");
  if (!response.ok) return;
  const templates = await sessionRequests.readJson(response);
  if (templates === sessionRequests.stalePayload) return;
  state.templates = templates;
}

async function loadProjects() {
  const response = await apiFetch("/api/projects");
  if (!response.ok) return;

  const projects = await sessionRequests.readJson(response);
  if (projects === sessionRequests.stalePayload) return;

  const configurationEntries = await Promise.all(projects.map(async project => {
    const configurationsResponse = await apiFetch(`/api/projects/${project.id}/configurations`);
    if (!configurationsResponse.ok) return [project.id, []];
    const configurations = await sessionRequests.readJson(configurationsResponse);
    return configurations === sessionRequests.stalePayload
      ? sessionRequests.stalePayload
      : [project.id, configurations];
  }));
  if (!sessionRequests.isCurrent(response)
      || configurationEntries.includes(sessionRequests.stalePayload)) {
    return;
  }

  state.projects = projects;
  state.configurationsByProjectId = new Map(configurationEntries);
  renderAccountData();
}

async function loadAccountJobs() {
  const response = await apiFetch("/api/jobs?take=100");
  if (!response.ok) return;
  const jobs = await sessionRequests.readJson(response);
  if (jobs === sessionRequests.stalePayload) return;
  state.jobs = jobs;
  updateMetrics();
}

function renderAccountData() {
  renderProjects();
  renderSavedConfigurations();
  updateMetrics();
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
  const filteredProjects = state.projects.filter(project =>
    matchesProjectSearch(project, state.configurationsByProjectId.get(project.id) || [], query));

  if (filteredProjects.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "По этому запросу проекты не найдены.";
    projectsList.append(empty);
    return;
  }

  for (const project of filteredProjects) {
    const configurations = state.configurationsByProjectId.get(project.id) || [];
    const details = document.createElement("details");
    details.className = "project-item";
    details.open = configurations.length > 0;

    const summary = document.createElement("summary");
    summary.className = "project-summary";
    summary.innerHTML = `
      <span class="project-summary__name"><span class="project-summary__title">${escapeHtml(project.name)}</span>${renderProjectOwnerBadge(project)}</span>
      <span class="muted">${configurations.length} конф.</span>
    `;
    details.append(summary);

    const body = document.createElement("div");
    body.className = "project-item__body";
    body.append(createProjectEditForm(project));

    if (configurations.length === 0) {
      const empty = document.createElement("div");
      empty.className = "empty";
      empty.textContent = "В проекте пока нет сохраненных конфигураций.";
      body.append(empty);
    } else {
      body.append(createConfigurationsTable(project, configurations));
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
  const entries = getAllConfigurations()
    .filter(({ project, configuration }) => matchesConfigurationSearch(project, configuration, query))
    .sort((left, right) => new Date(right.configuration.updatedAt || 0) - new Date(left.configuration.updatedAt || 0));

  if (entries.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = query
      ? "По этому запросу конфигурации не найдены."
      : "Пока нет сохраненных конфигураций.";
    savedConfigurationsList.append(empty);
    return;
  }

  for (const { project, configuration } of entries) {
    const formats = getTemplateFormats(configuration);
    const formatOptions = formats
      .map(format => `<option value="${escapeHtml(format)}" ${format === String(configuration.outputFormat).toLowerCase() ? "selected" : ""}>${escapeHtml(format.toUpperCase())}</option>`)
      .join("");
    const item = document.createElement("article");
    item.className = "saved-configuration-item";
    item.innerHTML = `
      <div>
        <strong>${escapeHtml(getConfigurationName(configuration))}</strong>
        <span>${escapeHtml(getTemplateLabel(configuration.templateId))}</span>
        <small>${escapeHtml(getProjectMetaLabel(project))} · ${formatDate(configuration.updatedAt)}</small>
      </div>
      <select class="format-select" data-format-for="${escapeHtml(configuration.id)}">
        ${formatOptions}
      </select>
      <div class="inline-actions">
        <a class="secondary button-link" href="/drawings?configurationId=${encodeURIComponent(configuration.id)}">Edit</a>
        ${formats.includes("pdf") && canCreateJobs() ? `<button class="secondary" type="button" data-action="preview" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Просмотреть</button>` : ""}
        ${canCreateJobs() ? `<button class="primary primary--compact" type="button" data-action="download" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Скачать</button>` : ""}
      </div>
    `;
    savedConfigurationsList.append(item);
  }
}

function createConfigurationsTable(project, configurations) {
  const wrap = document.createElement("div");
  wrap.className = "table-wrap table-wrap--compact";

  const table = document.createElement("table");
  table.innerHTML = `
    <thead>
      <tr>
        <th>Конфигурация</th>
        <th>Шаблон</th>
        <th>Формат</th>
        <th>Обновлено</th>
        <th>Действия</th>
      </tr>
    </thead>
    <tbody></tbody>
  `;

  const tbody = table.querySelector("tbody");
  for (const configuration of configurations) {
    const row = document.createElement("tr");
    const formats = getTemplateFormats(configuration);
    const formatOptions = formats
      .map(format => `<option value="${escapeHtml(format)}" ${format === String(configuration.outputFormat).toLowerCase() ? "selected" : ""}>${escapeHtml(format.toUpperCase())}</option>`)
      .join("");
    row.innerHTML = `
      <td>${escapeHtml(getConfigurationName(configuration))}</td>
      <td>${escapeHtml(getTemplateLabel(configuration.templateId))}</td>
      <td>
        <select class="format-select" data-format-for="${escapeHtml(configuration.id)}">
          ${formatOptions}
        </select>
      </td>
      <td>${formatDate(configuration.updatedAt)}</td>
      <td>
        <div class="inline-actions">
          ${formats.includes("pdf") && canCreateJobs() ? `<button class="secondary" type="button" data-action="preview" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Просмотреть</button>` : ""}
          ${canCreateJobs() ? `<button class="secondary" type="button" data-action="download" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Скачать</button>` : ""}
          <a class="secondary button-link" href="/drawings?configurationId=${encodeURIComponent(configuration.id)}">Редактировать</a>
          ${canCreateJobs() ? `<button class="secondary" type="button" data-action="delete" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Удалить</button>` : ""}
        </div>
      </td>
    `;
    tbody.append(row);
  }

  wrap.append(table);
  return wrap;
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
  hideAccountStatus();
  await loadProjects();
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

  hideAccountStatus();
  await loadProjects();
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

  hideAccountStatus();
  await loadProjects();
}

async function deleteConfiguration(projectId, configurationId) {
  const response = await apiFetch(`/api/project-configurations/${encodeURIComponent(configurationId)}`, {
    method: "DELETE"
  });

  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось удалить конфигурацию")).join(" "), "error");
    return;
  }

  hideAccountStatus();
  await loadProjects();
}

async function downloadConfiguration(projectId, configurationId, format, options = {}) {
  if (!canCreateJobs()) {
    showAccountStatus("Недостаточно прав для генерации файла.", "error");
    return;
  }

  const configuration = (state.configurationsByProjectId.get(projectId) || [])
    .find(item => item.id === configurationId);
  if (!configuration) return;

  showAccountStatus(`Генерация ${format.toUpperCase()} запущена...`);
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
  await Promise.all([loadAdminUsers(), loadAdminTemplates()]);
}

async function loadAdminUsers() {
  const response = await apiFetch("/api/admin/users");
  if (!response.ok) return;
  const users = await sessionRequests.readJson(response);
  if (users === sessionRequests.stalePayload) return;
  state.adminUsers = users;
  renderAdminUsers();
}

function renderAdminUsers() {
  adminUsersTableBody.replaceChildren();

  for (const user of state.adminUsers) {
    const row = document.createElement("tr");
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
  if (!userName) return;

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

  const response = await apiFetch(url, options);
  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось обновить пользователя")).join(" "), "error");
    return;
  }

  await loadAdminUsers();
}

async function loadAdminTemplates() {
  const response = await apiFetch("/api/admin/templates");
  if (!response.ok) return;
  const templates = await sessionRequests.readJson(response);
  if (templates === sessionRequests.stalePayload) return;
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

async function setTemplateEnabled(templateId, enabled) {
  const response = await apiFetch(`/api/admin/templates/${encodeURIComponent(templateId)}/enabled`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ enabled })
  });

  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось обновить шаблон")).join(" "), "error");
    await loadAdminTemplates();
    return;
  }

  await loadTemplates();
  await loadAdminTemplates();
}

function showTemplateImportStatus(message, kind = "empty") {
  if (!templateImportStatus) return;
  templateImportStatus.hidden = false;
  templateImportStatus.className = `template-import__status ${kind}`;
  templateImportStatus.textContent = message;
}

async function importTemplate(event) {
  event.preventDefault();
  const manifest = templateManifestFile?.files?.[0];
  const template = templateGrbFile?.files?.[0];
  const fragments = templateFragmentsFile?.files?.[0];

  if (!manifest || !template) {
    showTemplateImportStatus(t("Выберите манифест и файл шаблона."), "error");
    return;
  }

  const formData = new FormData();
  formData.append("manifest", manifest);
  formData.append("template", template);
  if (fragments) formData.append("fragments", fragments);

  templateImportButton.disabled = true;
  showTemplateImportStatus(t("Импорт выполняется..."));
  try {
    const response = await apiFetch("/api/admin/templates/import", {
      method: "POST",
      body: formData
    });

    if (!response.ok) {
      const messages = await readProblem(response, t("Не удалось импортировать шаблон"));
      showTemplateImportStatus(messages.join(" "), "error");
      return;
    }

    const importedTemplate = await sessionRequests.readJson(response);
    if (importedTemplate === sessionRequests.stalePayload) return;
    templateImportForm.reset();
    showTemplateImportStatus(
      `${t("Шаблон импортирован.")} ${importedTemplate.name || importedTemplate.code || importedTemplate.id || ""}`.trim(),
      "success");
    await Promise.all([loadTemplates(), loadAdminTemplates()]);
  } catch {
    showTemplateImportStatus(t("Не удалось импортировать шаблон"), "error");
  } finally {
    templateImportButton.disabled = false;
  }
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
    const messages = await readProblem(response, t("Не удалось отправить заявку"));
    registerStatus.hidden = false;
    registerStatus.className = "error";
    registerStatus.textContent = messages.join(" ");
    return;
  }

  registerForm.reset();
  registerStatus.hidden = false;
  registerStatus.className = "empty";
  registerStatus.textContent = t("Заявка отправлена. Доступ появится после подтверждения администратором.");
}

async function login(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const userNameInput = form.querySelector("[name='userName']") || loginUserName;
  const passwordInput = form.querySelector("[name='password']") || loginPassword;
  passwordInput.setCustomValidity("");

  const response = await apiFetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      userName: userNameInput.value,
      password: passwordInput.value
    })
  });

  if (!response.ok) {
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
}

async function logout() {
  clearAccountSessionState();
  state.currentUser = null;
  updateAuthView();
  await apiFetch("/api/auth/logout", { method: "POST" });
}

async function boot() {
  try {
    const authenticated = await loadCurrentUser();
    if (!authenticated) return;

    await loadTemplates();
    await loadProjects();
    await loadAccountJobs();
    await loadAdminData();
    updateAuthView();
  } finally {
    hidePageSkeleton();
  }
}

registerForm.addEventListener("submit", register);
loginForm.addEventListener("submit", login);
guestLoginForm?.addEventListener("submit", login);
showRegisterPanelButton?.addEventListener("click", () => showAuthPanel("register"));
showLoginPanelButton?.addEventListener("click", () => showAuthPanel("login"));
logoutButton.addEventListener("click", logout);
createProjectButton.addEventListener("click", createProject);
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

  if (button.dataset.action === "delete") {
    deleteConfiguration(button.dataset.projectId, button.dataset.id);
  } else if (button.dataset.action === "download") {
    const select = findConfigurationFormatSelect(button.dataset.id, findConfigurationActionScope(button));
    downloadConfiguration(button.dataset.projectId, button.dataset.id, select?.value || "pdf");
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

  if (button.dataset.action === "download") {
    const select = findConfigurationFormatSelect(button.dataset.id, findConfigurationActionScope(button));
    downloadConfiguration(button.dataset.projectId, button.dataset.id, select?.value || "pdf");
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
  setTemplateEnabled(input.dataset.templateId, input.checked);
});
templateImportForm?.addEventListener("submit", importTemplate);
window.addEventListener("tflex:languagechange", () => {
  renderAccountData();
  if (canAdmin()) {
    renderAdminUsers();
    renderAdminTemplates();
  }
});

await boot();
