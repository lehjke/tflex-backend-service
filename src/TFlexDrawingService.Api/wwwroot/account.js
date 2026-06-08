const state = {
  currentUser: null,
  projects: [],
  configurationsByProjectId: new Map(),
  templates: [],
  adminUsers: [],
  adminTemplates: []
};

const guestMain = document.querySelector("#guestMain");
const accountMain = document.querySelector("#accountMain");
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
const projectNameInput = document.querySelector("#projectNameInput");
const createProjectButton = document.querySelector("#createProjectButton");
const projectsList = document.querySelector("#projectsList");
const accountStatus = document.querySelector("#accountStatus");
const adminNavLink = document.querySelector("#adminNavLink");
const adminPanel = document.querySelector("#adminPanel");
const adminUsersTableBody = document.querySelector("#adminUsersTableBody");
const adminTemplatesTableBody = document.querySelector("#adminTemplatesTableBody");

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
  return new Intl.DateTimeFormat("ru-RU", {
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

function getTemplate(templateId) {
  return state.templates.find(template => template.id === templateId || template.code === templateId) || null;
}

function getTemplateLabel(templateId) {
  const template = getTemplate(templateId);
  return template ? (template.name || template.code || template.id) : templateId;
}

function getTemplateFormats(configuration) {
  const formats = getTemplate(configuration.templateId)?.outputFormats || [];
  const normalized = [...new Set([
    configuration.outputFormat,
    ...formats
  ].filter(Boolean).map(format => String(format).toLowerCase()))];

  return normalized.length > 0 ? normalized : ["pdf"];
}

function findConfigurationFormatSelect(configurationId) {
  return [...projectsList.querySelectorAll("select[data-format-for]")]
    .find(select => select.dataset.formatFor === configurationId) || null;
}

function updateAuthView() {
  const authenticated = isAuthenticated();
  guestMain.hidden = authenticated;
  loginForm.hidden = authenticated;
  userPanel.hidden = !authenticated;
  accountMain.hidden = !authenticated;
  adminPanel.hidden = !authenticated || !canAdmin();
  adminNavLink.hidden = !authenticated || !canAdmin();

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

async function loadTemplates() {
  const response = await apiFetch("/api/templates");
  if (!response.ok) return;
  state.templates = await response.json();
}

async function loadProjects() {
  const response = await apiFetch("/api/projects");
  if (!response.ok) return;

  state.projects = await response.json();
  state.configurationsByProjectId = new Map();
  await Promise.all(state.projects.map(async project => {
    const configurationsResponse = await apiFetch(`/api/projects/${project.id}/configurations`);
    state.configurationsByProjectId.set(
      project.id,
      configurationsResponse.ok ? await configurationsResponse.json() : []);
  }));

  renderProjects();
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

  for (const project of state.projects) {
    const configurations = state.configurationsByProjectId.get(project.id) || [];
    const details = document.createElement("details");
    details.className = "project-item";
    details.open = configurations.length > 0;

    const summary = document.createElement("summary");
    summary.className = "project-summary";
    summary.innerHTML = `
      <span>${escapeHtml(project.name)}</span>
      <span class="muted">${configurations.length} конф.</span>
    `;
    details.append(summary);

    const body = document.createElement("div");
    body.className = "project-item__body";
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
      <td>${escapeHtml(configuration.name)}</td>
      <td>${escapeHtml(getTemplateLabel(configuration.templateId))}</td>
      <td>
        <select class="format-select" data-format-for="${escapeHtml(configuration.id)}">
          ${formatOptions}
        </select>
      </td>
      <td>${formatDate(configuration.updatedAt)}</td>
      <td>
        <div class="inline-actions">
          <button class="secondary" type="button" data-action="download" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Скачать</button>
          <a class="secondary button-link" href="/?configurationId=${encodeURIComponent(configuration.id)}">Редактировать</a>
          <button class="secondary" type="button" data-action="delete" data-project-id="${escapeHtml(project.id)}" data-id="${escapeHtml(configuration.id)}">Удалить</button>
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
    projectNameInput.setCustomValidity("Укажите название проекта");
    projectNameInput.reportValidity();
    return;
  }

  projectNameInput.setCustomValidity("");
  const response = await apiFetch("/api/projects", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name, description: "" })
  });

  if (!response.ok) {
    showAccountStatus((await readProblem(response, "Не удалось создать проект")).join(" "), "error");
    return;
  }

  projectNameInput.value = "";
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

async function downloadConfiguration(projectId, configurationId, format) {
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

  const job = await createResponse.json();
  await waitForDownload(job.id, format);
}

async function waitForDownload(jobId, format) {
  for (let attempt = 0; attempt < 600; attempt += 1) {
    await new Promise(resolve => setTimeout(resolve, 1000));

    const response = await apiFetch(`/api/jobs/${encodeURIComponent(jobId)}`);
    if (!response.ok) continue;

    const job = await response.json();
    if (job.status === "Completed") {
      const file = (job.resultFiles || []).find(candidate =>
        String(candidate.format).toLowerCase() === String(format).toLowerCase())
        || (job.resultFiles || [])[0];

      if (!file) {
        showAccountStatus("Задание завершено, но файл не найден.", "error");
        return;
      }

      showAccountStatus("Файл готов. Скачивание началось.");
      window.location.href = file.downloadUrl;
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
  state.adminUsers = await response.json();
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
    const actions = [];
    if (!isCurrentUser && normalizedStatus !== "approved") {
      const label = normalizedStatus === "disabled" ? "Включить" : "Подтвердить";
      actions.push(`<button class="secondary" type="button" data-action="approve" data-user="${escapeHtml(user.userName)}">${label}</button>`);
    }
    if (!isCurrentUser && normalizedStatus === "pending") {
      actions.push(`<button class="secondary" type="button" data-action="reject" data-user="${escapeHtml(user.userName)}">Отклонить</button>`);
    }
    if (!isCurrentUser && user.enabled) {
      actions.push(`<button class="secondary" type="button" data-action="disable" data-user="${escapeHtml(user.userName)}">Отключить</button>`);
    }

    row.innerHTML = `
      <td>${escapeHtml(user.displayName || user.userName)}<br><span class="muted">${escapeHtml(user.userName)}</span></td>
      <td><span class="status ${escapeHtml(normalizedStatus)}">${escapeHtml(status)}</span></td>
      <td>${escapeHtml((user.roles || []).join(", "))}</td>
      <td><div class="inline-actions">${actions.join("") || "<span class=\"muted\">Нет действий</span>"}</div></td>
    `;
    adminUsersTableBody.append(row);
  }
}

async function handleAdminUserAction(action, userName) {
  if (!userName) return;

  const user = state.adminUsers.find(item => item.userName === userName);
  let url = `/api/admin/users/${encodeURIComponent(userName)}`;
  let options = { method: "DELETE" };
  if (action === "approve") {
    const currentRoles = user?.roles || [];
    const roles = currentRoles.some(role => role === "Admin" || role === "Operator")
      ? currentRoles
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
  state.adminTemplates = await response.json();
  renderAdminTemplates();
}

function renderAdminTemplates() {
  adminTemplatesTableBody.replaceChildren();

  for (const template of state.adminTemplates) {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td>${escapeHtml(template.name || template.code || template.id)}<br><span class="muted">${escapeHtml(template.code || template.id)}</span></td>
      <td>${escapeHtml((template.outputFormats || []).map(format => format.toUpperCase()).join(", "))}</td>
      <td><input class="mini-toggle" type="checkbox" data-template-id="${escapeHtml(template.id)}" ${template.enabled ? "checked" : ""}></td>
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
  await loadAdminData();
}

async function logout() {
  await apiFetch("/api/auth/logout", { method: "POST" });
  state.currentUser = null;
  state.projects = [];
  state.configurationsByProjectId = new Map();
  state.adminUsers = [];
  state.adminTemplates = [];
  projectsList.replaceChildren();
  adminUsersTableBody.replaceChildren();
  adminTemplatesTableBody.replaceChildren();
  hideAccountStatus();
  updateAuthView();
}

async function boot() {
  const authenticated = await loadCurrentUser();
  if (!authenticated) return;

  await loadTemplates();
  await loadProjects();
  await loadAdminData();
}

registerForm.addEventListener("submit", register);
loginForm.addEventListener("submit", login);
logoutButton.addEventListener("click", logout);
createProjectButton.addEventListener("click", createProject);
projectsList.addEventListener("click", event => {
  const button = event.target.closest("button[data-action]");
  if (!button) return;

  if (button.dataset.action === "delete") {
    deleteConfiguration(button.dataset.projectId, button.dataset.id);
  } else if (button.dataset.action === "download") {
    const select = findConfigurationFormatSelect(button.dataset.id);
    downloadConfiguration(button.dataset.projectId, button.dataset.id, select?.value || "pdf");
  }
});
adminUsersTableBody.addEventListener("click", event => {
  const button = event.target.closest("button[data-action]");
  if (!button) return;
  handleAdminUserAction(button.dataset.action, button.dataset.user);
});
adminTemplatesTableBody.addEventListener("change", event => {
  const input = event.target.closest("input[data-template-id]");
  if (!input) return;
  setTemplateEnabled(input.dataset.templateId, input.checked);
});

await boot();
