const state = {
  templates: [],
  selectedTemplate: null,
  activeJobId: null,
  pollTimer: null
};

const templateSelect = document.querySelector("#templateSelect");
const formatSelect = document.querySelector("#formatSelect");
const parametersForm = document.querySelector("#parametersForm");
const submitButton = document.querySelector("#submitButton");
const statusPanel = document.querySelector("#statusPanel");
const jobsTableBody = document.querySelector("#jobsTableBody");

function formatDate(value) {
  if (!value) return "";
  return new Intl.DateTimeFormat("ru-RU", {
    dateStyle: "short",
    timeStyle: "medium"
  }).format(new Date(value));
}

function createParameterInput(parameter) {
  const field = document.createElement("label");
  field.className = "field";

  const label = document.createElement("span");
  label.className = "field__label";
  label.textContent = parameter.unit
    ? `${parameter.displayName || parameter.name}, ${parameter.unit}`
    : parameter.displayName || parameter.name;

  let input;
  if (parameter.allowedValues?.length) {
    input = document.createElement("select");
    for (const value of parameter.allowedValues) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = value;
      input.append(option);
    }
  } else {
    input = document.createElement("input");
    input.type = parameter.type === "number" || parameter.type === "integer" ? "number" : "text";
    if (parameter.type === "integer") input.step = "1";
    if (parameter.type === "number") input.step = "any";
    if (parameter.minValue !== null && parameter.minValue !== undefined) input.min = parameter.minValue;
    if (parameter.maxValue !== null && parameter.maxValue !== undefined) input.max = parameter.maxValue;
  }

  input.name = parameter.name;
  input.required = Boolean(parameter.isRequired);
  if (parameter.defaultValue !== null && parameter.defaultValue !== undefined) {
    input.value = parameter.defaultValue;
  }

  field.append(label, input);
  return field;
}

function renderSelectedTemplate() {
  state.selectedTemplate = state.templates.find(template => template.id === templateSelect.value);
  parametersForm.replaceChildren();
  formatSelect.replaceChildren();

  if (!state.selectedTemplate) return;

  for (const format of state.selectedTemplate.outputFormats) {
    const option = document.createElement("option");
    option.value = format;
    option.textContent = format.toUpperCase();
    formatSelect.append(option);
  }

  for (const parameter of state.selectedTemplate.parameters) {
    parametersForm.append(createParameterInput(parameter));
  }
}

function collectParameters() {
  const parameters = {};
  for (const input of parametersForm.querySelectorAll("input, select")) {
    const definition = state.selectedTemplate.parameters.find(parameter => parameter.name === input.name);
    if (!definition) continue;

    if (definition.type === "number") {
      parameters[input.name] = input.value === "" ? null : Number(input.value);
    } else if (definition.type === "integer") {
      parameters[input.name] = input.value === "" ? null : Number.parseInt(input.value, 10);
    } else {
      parameters[input.name] = input.value;
    }
  }
  return parameters;
}

function renderJob(job) {
  const files = job.resultFiles || [];
  const downloadLinks = files.map(file =>
    `<a href="${file.downloadUrl}">${file.fileName}</a>`
  ).join("");

  statusPanel.innerHTML = `
    <div class="status ${job.status.toLowerCase()}">${job.status}</div>
    <dl>
      <dt>Задание</dt><dd>${job.id}</dd>
      <dt>Шаблон</dt><dd>${job.templateId}</dd>
      <dt>Создано</dt><dd>${formatDate(job.createdAt)}</dd>
      <dt>Завершено</dt><dd>${formatDate(job.finishedAt)}</dd>
      <dt>Ошибка</dt><dd>${job.errorMessage || ""}</dd>
      <dt>Результат</dt><dd>${downloadLinks || ""}</dd>
    </dl>
  `;
}

async function refreshJob(jobId) {
  const response = await fetch(`/api/jobs/${jobId}`);
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
  const response = await fetch("/api/jobs?take=20");
  const jobs = await response.json();
  jobsTableBody.replaceChildren();

  for (const job of jobs) {
    const row = document.createElement("tr");
    const files = job.resultFiles || [];
    row.innerHTML = `
      <td>${job.id.slice(0, 8)}</td>
      <td>${job.templateId}</td>
      <td><span class="status ${job.status.toLowerCase()}">${job.status}</span></td>
      <td>${job.outputFormat.toUpperCase()}</td>
      <td>${formatDate(job.createdAt)}</td>
      <td>${files.map(file => `<a href="${file.downloadUrl}">скачать</a>`).join(" ")}</td>
    `;
    jobsTableBody.append(row);
  }
}

async function submitJob(event) {
  event.preventDefault();
  if (!state.selectedTemplate) return;

  submitButton.disabled = true;
  statusPanel.innerHTML = `<div class="status pending">Pending</div>`;

  const response = await fetch("/api/jobs", {
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
    const problem = await response.json();
    statusPanel.innerHTML = `<div class="error">${(problem.errors?.request || ["Ошибка валидации"]).join("<br>")}</div>`;
    return;
  }

  const job = await response.json();
  state.activeJobId = job.id;
  renderJob(job);
  await refreshJobs();

  clearInterval(state.pollTimer);
  state.pollTimer = setInterval(() => refreshJob(job.id), 1200);
}

async function loadTemplates() {
  const response = await fetch("/api/templates");
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

templateSelect.addEventListener("change", renderSelectedTemplate);
document.querySelector("#jobForm").addEventListener("submit", submitJob);

await loadTemplates();
await refreshJobs();
