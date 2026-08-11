import { getLanguage, t } from "./i18n.js?v=20260806-design-fixes-1";
import { createSessionRequestGuard } from "./session-requests.js?v=20260720-ui-hardening-1";

const state = {
  currentUser: null,
  catalog: null,
  projects: [],
  configurationsByProjectId: new Map(),
  pricingByProjectId: new Map(),
  lastCalculation: null,
  lastRequest: null,
  xiziInitialized: false
};
const sessionRequests = createSessionRequestGuard();

const LIVE_CALCULATION_DELAY_MS = 450;
const VISUAL_SELECT_TYPEAHEAD_TIMEOUT_MS = 700;
let liveCalculationTimer = 0;
let calculationRequestId = 0;
const visualSelectTypeahead = new WeakMap();

const guestMain = document.querySelector("#guestMain");
const pricingMain = document.querySelector("#pricingMain");
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
const currentUserRole = document.querySelector("#currentUserRole");
const pricingAccessNote = document.querySelector("#pricingAccessNote");
const logoutButton = document.querySelector("#logoutButton");
const globalSearchInput = document.querySelector(".global-search input");
const pricingForm = document.querySelector("#pricingForm");
const pricingProjectSelect = document.querySelector("#pricingProjectSelect");
const drawingConfigurationSelect = document.querySelector("#drawingConfigurationSelect");
const pricingNameInput = document.querySelector("#pricingNameInput");
const targetCurrencySelect = document.querySelector("#targetCurrencySelect");
const supplierSelect = document.querySelector("#supplierSelect");
const seriesSelect = document.querySelector("#seriesSelect");
const smecEleSeriesInput = document.querySelector("#smecEleSeriesInput");
const capacitySelect = document.querySelector("#capacitySelect");
const speedSelect = document.querySelector("#speedSelect");
const stopsInput = document.querySelector("#stopsInput");
const manufacturingStandardSelect = document.querySelector("#manufacturingStandardSelect");
const projectTypeSelect = document.querySelector("#projectTypeSelect");
const operationSelect = document.querySelector("#operationSelect");
const floorsInput = document.querySelector("#floorsInput");
const smecQuantityInput = document.querySelector("#smecQuantityInput");
const smecDoorCountInput = document.querySelector("#smecDoorCountInput");
const smecDecorationWeightInput = document.querySelector("#smecDecorationWeightInput");
const smecMainFloorInput = document.querySelector("#smecMainFloorInput");
const smecOtherFloorsInput = document.querySelector("#smecOtherFloorsInput");
const smecPowerSupplyInput = document.querySelector("#smecPowerSupplyInput");
const smecLightingSupplyInput = document.querySelector("#smecLightingSupplyInput");
const xiziProjectNameInput = document.querySelector("#xiziProjectNameInput");
const xiziAddressInput = document.querySelector("#xiziAddressInput");
const xiziContractInput = document.querySelector("#xiziContractInput");
const xiziUnitInput = document.querySelector("#xiziUnitInput");
const xiziElevatorTypeSelect = document.querySelector("#xiziElevatorTypeSelect");
const xiziModelSelect = document.querySelector("#xiziModelSelect");
const xiziLiftNumberInput = document.querySelector("#xiziLiftNumberInput");
const xiziQuantityInput = document.querySelector("#xiziQuantityInput");
const xiziControlSystemSelect = document.querySelector("#xiziControlSystemSelect");
const xiziDecorationWeightInput = document.querySelector("#xiziDecorationWeightInput");
const xiziMainFloorInput = document.querySelector("#xiziMainFloorInput");
const xiziOtherFloorsInput = document.querySelector("#xiziOtherFloorsInput");
const xiziShaftWidthInput = document.querySelector("#xiziShaftWidthInput");
const xiziShaftDepthInput = document.querySelector("#xiziShaftDepthInput");
const xiziTravelHeightInput = document.querySelector("#xiziTravelHeightInput");
const xiziShaftTypeSelect = document.querySelector("#xiziShaftTypeSelect");
const xiziOverheadInput = document.querySelector("#xiziOverheadInput");
const xiziPitInput = document.querySelector("#xiziPitInput");
const xiziCarWidthInput = document.querySelector("#xiziCarWidthInput");
const xiziCarDepthInput = document.querySelector("#xiziCarDepthInput");
const xiziCarHeightSelect = document.querySelector("#xiziCarHeightSelect");
const xiziCarTypeSelect = document.querySelector("#xiziCarTypeSelect");
const xiziDoorHeightSelect = document.querySelector("#xiziDoorHeightSelect");
const xiziFireRatingSelect = document.querySelector("#xiziFireRatingSelect");
const xiziDoorOpeningSelect = document.querySelector("#xiziDoorOpeningSelect");
const xiziCabinDesignSelect = document.querySelector("#xiziCabinDesignSelect");
const xiziCarWallMaterialSelect = document.querySelector("#xiziCarWallMaterialSelect");
const xiziCarDoorMaterialSelect = document.querySelector("#xiziCarDoorMaterialSelect");
const xiziCeilingSelect = document.querySelector("#xiziCeilingSelect");
const xiziFloorSelect = document.querySelector("#xiziFloorSelect");
const xiziMirrorWallSelect = document.querySelector("#xiziMirrorWallSelect");
const xiziMirrorHeightSelect = document.querySelector("#xiziMirrorHeightSelect");
const xiziHandrailPositionSelect = document.querySelector("#xiziHandrailPositionSelect");
const xiziHandrailSelect = document.querySelector("#xiziHandrailSelect");
const xiziCopSelect = document.querySelector("#xiziCopSelect");
const xiziCopButtonSelect = document.querySelector("#xiziCopButtonSelect");
const xiziMainShaftDoorSelect = document.querySelector("#xiziMainShaftDoorSelect");
const xiziOtherShaftDoorSelect = document.querySelector("#xiziOtherShaftDoorSelect");
const xiziMainLopSelect = document.querySelector("#xiziMainLopSelect");
const xiziOtherLopSelect = document.querySelector("#xiziOtherLopSelect");
const xiziMainLipSelect = document.querySelector("#xiziMainLipSelect");
const xiziOtherLipSelect = document.querySelector("#xiziOtherLipSelect");
const xiziAirConditionerSelect = document.querySelector("#xiziAirConditionerSelect");
const xiziRccSelect = document.querySelector("#xiziRccSelect");
const shaftWidthInput = document.querySelector("#shaftWidthInput");
const shaftDepthInput = document.querySelector("#shaftDepthInput");
const shaftDoorTypeInput = document.querySelector("#shaftDoorTypeInput");
const trInput = document.querySelector("#trInput");
const ohInput = document.querySelector("#ohInput");
const pdInput = document.querySelector("#pdInput");
const carWidthInput = document.querySelector("#carWidthInput");
const carDepthInput = document.querySelector("#carDepthInput");
const carHeightInput = document.querySelector("#carHeightInput");
const doorModeSelect = document.querySelector("#doorModeSelect");
const smecDoorWidthInput = document.querySelector("#smecDoorWidthInput");
const doorHeightInput = document.querySelector("#doorHeightInput");
const doorManufacturerSelect = document.querySelector("#doorManufacturerSelect");
const doorTypeSelect = document.querySelector("#doorTypeSelect");
const doorWidthSelect = document.querySelector("#doorWidthSelect");
const doorCountInput = document.querySelector("#doorCountInput");
const decorationSelect = document.querySelector("#decorationSelect");
const decorationPreview = document.querySelector("#decorationPreview");
const carDesignPicker = document.querySelector("#carDesignPicker");
const ceilingSelect = document.querySelector("#ceilingSelect");
const floorTypeSelect = document.querySelector("#floorTypeSelect");
const floorPatternSelect = document.querySelector("#floorPatternSelect");
const wallMaterialSelect = document.querySelector("#wallMaterialSelect");
const carDoorMaterialSelect = document.querySelector("#carDoorMaterialSelect");
const mirrorSelect = document.querySelector("#mirrorSelect");
const mirrorPositionSelect = document.querySelector("#mirrorPositionSelect");
const handrailSelect = document.querySelector("#handrailSelect");
const copSelect = document.querySelector("#copSelect");
const cop2Select = document.querySelector("#cop2Select");
const copButtonSelect = document.querySelector("#copButtonSelect");
const wheelchairCopSelect = document.querySelector("#wheelchairCopSelect");
const wheelchairCop2Select = document.querySelector("#wheelchairCop2Select");
const wheelchairButtonSelect = document.querySelector("#wheelchairButtonSelect");
const mainJambSelect = document.querySelector("#mainJambSelect");
const mainLandingMaterialSelect = document.querySelector("#mainLandingMaterialSelect");
const mainSillBracketSelect = document.querySelector("#mainSillBracketSelect");
const mainLandingDoorSelect = document.querySelector("#mainLandingDoorSelect");
const otherJambSelect = document.querySelector("#otherJambSelect");
const otherLandingMaterialSelect = document.querySelector("#otherLandingMaterialSelect");
const otherSillBracketSelect = document.querySelector("#otherSillBracketSelect");
const otherLandingDoorSelect = document.querySelector("#otherLandingDoorSelect");
const mainLopSelect = document.querySelector("#mainLopSelect");
const otherLopSelect = document.querySelector("#otherLopSelect");
const lopButtonSelect = document.querySelector("#lopButtonSelect");
const otherLopButtonSelect = document.querySelector("#otherLopButtonSelect");
const mainAuxiliaryLopSelect = document.querySelector("#mainAuxiliaryLopSelect");
const otherAuxiliaryLopSelect = document.querySelector("#otherAuxiliaryLopSelect");
const auxiliaryLopButtonSelect = document.querySelector("#auxiliaryLopButtonSelect");
const otherAuxiliaryLopButtonSelect = document.querySelector("#otherAuxiliaryLopButtonSelect");
const hallIndicatorSelect = document.querySelector("#hallIndicatorSelect");
const hallLanternSelect = document.querySelector("#hallLanternSelect");
const optionsList = document.querySelector("#optionsList");
const efsToggle = document.querySelector("#efsToggle");
const e312Toggle = document.querySelector("#e312Toggle");
const smecOtherRequirementsInput = document.querySelector("#smecOtherRequirementsInput");
const savePricingButton = document.querySelector("#savePricingButton");
const downloadTkpButton = document.querySelector("#downloadTkpButton");
const pricingStatus = document.querySelector("#pricingStatus");
const totalCny = document.querySelector("#totalCny");
const totalConverted = document.querySelector("#totalConverted");
const rateInfo = document.querySelector("#rateInfo");
const pricingLines = document.querySelector("#pricingLines");
const pricingWarnings = document.querySelector("#pricingWarnings");
const savedPricingList = document.querySelector("#savedPricingList");
const mobilePricingStatus = document.querySelector("#mobilePricingStatus");
const mobileTotalCny = document.querySelector("#mobileTotalCny");
const mobileTotalConverted = document.querySelector("#mobileTotalConverted");

function isAuthenticated() {
  return Boolean(state.currentUser?.isAuthenticated);
}

function canSavePricing() {
  const roles = state.currentUser?.roles || [];
  return roles.includes("Admin") || roles.includes("Operator");
}

function getRoleLabel() {
  const roles = state.currentUser?.roles || [];
  if (roles.includes("Admin")) return "Admin";
  if (roles.includes("Operator")) return "Operator";
  if (roles.includes("Viewer")) return "Viewer";
  return "";
}

function updateAuthView() {
  const authenticated = isAuthenticated();
  if (guestMain) guestMain.hidden = authenticated;
  if (loginForm) loginForm.hidden = authenticated;
  if (userPanel) userPanel.hidden = !authenticated;
  if (pricingMain) pricingMain.hidden = !authenticated;
  if (savePricingButton) savePricingButton.hidden = !authenticated || !canSavePricing();
  if (downloadTkpButton) downloadTkpButton.hidden = !authenticated || !canSavePricing();

  if (authenticated) {
    currentUserName.textContent = state.currentUser.displayName || state.currentUser.userName;
    const role = getRoleLabel();
    if (currentUserRole) {
      currentUserRole.hidden = !role;
      currentUserRole.textContent = role;
    }
    if (pricingAccessNote) {
      const readOnly = role === "Viewer";
      pricingAccessNote.hidden = !readOnly;
      pricingAccessNote.textContent = readOnly
        ? (getLanguage() === "en"
          ? "Viewer role: calculations are available, but saving specifications and exporting quotations require the Operator or Admin role."
          : "Роль Viewer: расчет доступен, но сохранение спецификаций и выгрузка ТКП требуют роль Operator или Admin.")
        : "";
    }
  } else {
    currentUserName.textContent = "";
    if (currentUserRole) {
      currentUserRole.hidden = true;
      currentUserRole.textContent = "";
    }
    if (pricingAccessNote) {
      pricingAccessNote.hidden = true;
      pricingAccessNote.textContent = "";
    }
  }
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
    clearPricingSessionState();
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
    return problem.errors?.request || problem.errors?.name || [problem.detail || problem.title || fallback];
  } catch {
    return [fallback];
  }
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}

function money(value, currency = "CNY") {
  return `${new Intl.NumberFormat(getLanguage() === "en" ? "en-GB" : "ru-RU", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  }).format(Number(value || 0))} ${currency}`;
}

function syncMobilePricingSummary() {
  if (mobilePricingStatus) {
    mobilePricingStatus.textContent = pricingStatus?.textContent || t("Не рассчитано");
    mobilePricingStatus.className = pricingStatus?.className || "pricing-status";
  }
  if (mobileTotalCny) mobileTotalCny.textContent = totalCny?.textContent || "0 CNY";
  if (mobileTotalConverted) mobileTotalConverted.textContent = totalConverted?.textContent || "0 RUB";
}

function clearPricingSessionState() {
  sessionRequests.invalidate();
  window.clearTimeout(liveCalculationTimer);
  liveCalculationTimer = 0;
  calculationRequestId += 1;

  state.catalog = null;
  state.projects = [];
  state.configurationsByProjectId = new Map();
  state.pricingByProjectId = new Map();
  state.lastCalculation = null;
  state.lastRequest = null;
  state.xiziInitialized = false;

  loginForm?.reset();
  registerForm?.reset();
  pricingForm?.reset();
  loginPassword?.setCustomValidity("");
  if (registerStatus) {
    registerStatus.hidden = true;
    registerStatus.textContent = "";
  }
  if (globalSearchInput) globalSearchInput.value = "";

  pricingProjectSelect?.replaceChildren();
  drawingConfigurationSelect?.replaceChildren();
  savedPricingList?.replaceChildren();
  pricingLines?.replaceChildren();
  optionsList?.querySelectorAll("input").forEach(input => {
    input.checked = false;
  });
  if (decorationPreview) {
    decorationPreview.hidden = true;
    decorationPreview.replaceChildren();
  }
  if (pricingWarnings) {
    pricingWarnings.hidden = true;
    pricingWarnings.className = "pricing-warnings";
    pricingWarnings.replaceChildren();
  }
  if (pricingStatus) {
    pricingStatus.textContent = t("Не рассчитано");
    pricingStatus.className = "pricing-status";
  }
  if (totalCny) totalCny.textContent = "0 CNY";
  if (totalConverted) totalConverted.textContent = "0 RUB";
  if (rateInfo) rateInfo.textContent = "";
  if (savePricingButton) savePricingButton.disabled = true;
  if (downloadTkpButton) downloadTkpButton.disabled = true;

  syncAllVisualSelects();
  syncMobilePricingSummary();
}

function setupPricingSearch() {
  if (!globalSearchInput || !pricingForm) return;

  const status = document.createElement("span");
  status.id = "pricingSearchStatus";
  status.className = "global-search__status";
  status.setAttribute("role", "status");
  status.setAttribute("aria-live", "polite");
  status.setAttribute("aria-atomic", "true");
  globalSearchInput.setAttribute("aria-describedby", status.id);
  globalSearchInput.closest(".global-search")?.append(status);
  let matches = [];
  let matchIndex = -1;

  const applySearch = () => {
    const query = globalSearchInput.value.trim().toLocaleLowerCase();
    const candidates = [...pricingForm.querySelectorAll(".field, .pricing-option, .pricing-section summary h2")];
    candidates.forEach(candidate => candidate.classList.remove("is-search-match"));
    matches = query
      ? candidates.filter(candidate =>
        !candidate.closest("[hidden]")
        && candidate.textContent.trim().toLocaleLowerCase().includes(query))
      : [];
    matches.forEach(candidate => candidate.classList.add("is-search-match"));
    matchIndex = -1;
    status.textContent = query
      ? (matches.length ? `${t("Найдено:")} ${matches.length}` : t("Ничего не найдено"))
      : "";
  };

  globalSearchInput.addEventListener("input", applySearch);
  globalSearchInput.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      globalSearchInput.value = "";
      applySearch();
      return;
    }
    if (event.key !== "Enter" || matches.length === 0) return;

    event.preventDefault();
    matchIndex = (matchIndex + 1) % matches.length;
    const target = matches[matchIndex];
    openParentDisclosures(target);
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    target.scrollIntoView({ behavior: reducedMotion ? "auto" : "smooth", block: "center" });
    const summary = target.closest("summary");
    const control = summary || (target.matches("input:not([aria-hidden='true']), select:not([aria-hidden='true']), textarea, button")
      ? target
      : target.querySelector("input:not([aria-hidden='true']), select:not([aria-hidden='true']), textarea, button"));
    control?.focus({ preventScroll: true });
  });

  window.addEventListener("tflex:languagechange", applySearch);
}

function openParentDisclosures(target) {
  const disclosures = [];
  let disclosure = target?.closest?.("details");
  while (disclosure) {
    disclosures.push(disclosure);
    disclosure = disclosure.parentElement?.closest("details") || null;
  }
  disclosures.reverse().forEach(item => {
    item.open = true;
  });
}

function syncPricingAccessibleCopy() {
  const english = getLanguage() === "en";
  const searchText = english ? "Search price parameters" : "Поиск по параметрам цены";
  const searchLabel = globalSearchInput?.closest(".global-search")?.querySelector(".sr-only");
  if (searchLabel) searchLabel.textContent = searchText;
  globalSearchInput?.setAttribute("aria-label", searchText);
  globalSearchInput?.setAttribute("placeholder", searchText);
  pricingMain?.querySelector(".pricing-result")?.setAttribute(
    "aria-label",
    english ? "Calculation result" : "Результат расчета"
  );
}

function numberValue(input, fallback = 0) {
  const value = Number(String(input?.value || "").replace(",", "."));
  return Number.isFinite(value) ? value : fallback;
}

function fillSelect(select, values, selectedValue = null) {
  if (!select) return;
  select.replaceChildren();
  for (const value of values) {
    const option = document.createElement("option");
    option.value = String(value);
    option.textContent = String(value);
    select.append(option);
  }

  if (selectedValue !== null && values.map(String).includes(String(selectedValue))) {
    select.value = String(selectedValue);
  }

  syncVisualSelect(select);
}

function normalizeCode(value) {
  return String(value || "").replaceAll("■", "");
}

function codeMatches(left, right) {
  return normalizeCode(left).toLowerCase() === normalizeCode(right).toLowerCase();
}

function getSmecVisualItem(code) {
  return (state.catalog?.smecVisualItems || []).find(item => codeMatches(item.code, code)) || null;
}

function getSmecImage(code) {
  return getSmecVisualItem(code)?.imageUrl || null;
}

function getSmecChoices(name, fallback = []) {
  const group = (state.catalog?.smecChoiceGroups || []).find(item => item.name === name);
  return group?.options?.length ? group.options : fallback;
}

function getSmecModelOptions(eleSeries) {
  return getSmecChoices(`Ele Type: ${eleSeries}`, getSmecChoices("Ele Type", state.catalog?.smecSeries || []));
}

function updateSmecModels(selectedValue = null) {
  const eleSeries = smecEleSeriesInput?.value || "LEHY Series";
  const models = getSmecModelOptions(eleSeries);
  const preferred = selectedValue && models.includes(selectedValue) ? selectedValue : models[0] || "";
  fillSelect(seriesSelect, models, preferred);
  updateSmecPower();
}

function numericChoicesFromLabels(values, fallback = []) {
  const source = values?.length ? values : fallback;
  return [...new Set(source
    .map(value => String(value).replace(",", ".").match(/-?\d+(?:\.\d+)?/)?.[0])
    .map(value => Number(value))
    .filter(value => Number.isFinite(value) && value > 0))]
    .sort((left, right) => left - right);
}

function getSmecCarDesignOptions() {
  const designs = state.catalog?.smecCarDesigns || [];
  const choices = getSmecChoices("Car Design", designs.map(design => design.code));
  const designMap = new Map(designs.map(design => [normalizeCode(design.code).toLowerCase(), design]));
  const ordered = [];

  for (const code of choices) {
    const key = normalizeCode(code).toLowerCase();
    const design = designMap.get(key);
    if (design) {
      ordered.push(design);
      designMap.delete(key);
      continue;
    }

    ordered.push({
      code,
      wallDescription: codeMatches(code, "Customized") ? "Индивидуальный дизайн кабины" : "Car design",
      doorDescription: "",
      imageUrl: ""
    });
  }

  ordered.push(...designMap.values());
  return ordered;
}

function getXiziChoices(name, fallback = []) {
  const group = (state.catalog?.xiziChoiceGroups || []).find(item => item.name === name);
  return group?.options?.length ? group.options : fallback;
}

function mapXiziModelToSeries(model) {
  const normalized = String(model || "").toLowerCase();
  if (normalized.includes("g3")) return "G3";
  if (normalized.includes("mrl")) return "UN-Victor MRL";
  return "UN-Victor R";
}

function syncXiziPricingFields() {
  if (supplierSelect.value !== "XIZI") return;
  const mappedSeries = mapXiziModelToSeries(xiziModelSelect?.value);
  if ([...seriesSelect.options].some(option => option.value === mappedSeries)) {
    seriesSelect.value = mappedSeries;
  }

  const opening = String(xiziDoorOpeningSelect?.value || "").toLowerCase();
  const mappedDoorType = opening.includes("централь") ? "CO" : opening.includes("телескоп") ? "2S" : "";
  if (mappedDoorType && [...doorTypeSelect.options].some(option => option.value === mappedDoorType)) {
    doorTypeSelect.value = mappedDoorType;
  }

  const shaftDepth = numberValue(xiziShaftDepthInput);
  const carDepth = numberValue(xiziCarDepthInput);
  const carType = String(xiziCarTypeSelect?.value || "").trim().toLowerCase();
  const isThrough = carType === "проходная" || carType === "through";
  const isSideOpening = mappedDoorType === "2S";
  const threshold = isThrough
    ? (isSideOpening ? 670 : 570)
    : (isSideOpening ? 400 : 350);
  const manufacturer = shaftDepth > 0 && carDepth > 0 && shaftDepth - carDepth >= threshold
    ? "OPTIMAX"
    : "FERMATOR";
  if ([...doorManufacturerSelect.options].some(option => option.value === manufacturer)) {
    doorManufacturerSelect.value = manufacturer;
  }
  doorManufacturerSelect.disabled = true;
}

function byCodePrefix(prefixes) {
  const normalized = Array.isArray(prefixes) ? prefixes : [prefixes];
  return (state.catalog?.smecVisualItems || [])
    .filter(item => normalized.some(prefix => item.code.startsWith(prefix)))
    .map(item => item.code)
    .sort((a, b) => a.localeCompare(b, "ru"));
}

function fillVisualSelect(select, codes, fallback = [], selectedValue = null) {
  if (!select) return;
  const values = codes.length ? codes : fallback;
  select.replaceChildren();
  const empty = document.createElement("option");
  empty.value = "";
  empty.textContent = t("Не выбрано");
  select.append(empty);
  for (const code of values) {
    const option = document.createElement("option");
    option.value = code;
    option.textContent = code;
    select.append(option);
  }
  if (selectedValue !== null) {
    const match = [...select.options].find(option => codeMatches(option.value, selectedValue));
    if (match) select.value = match.value;
  }
  syncVisualSelect(select);
}

function getVisualOptionMeta(code, select) {
  if (select?.id?.startsWith("xizi")) {
    const item = (state.catalog?.xiziVisualItems || []).find(candidate => codeMatches(candidate.code, code))
      || (state.catalog?.xiziDecorations || []).find(candidate => codeMatches(candidate.code, code))
      || (state.catalog?.xiziOptions || []).find(candidate => codeMatches(candidate.code, code));
    return {
      code,
      imageUrl: item?.imageUrl || "",
      description: item?.description || item?.category || select.dataset?.description || ""
    };
  }

  const item = getSmecVisualItem(code);
  return {
    code,
    imageUrl: item?.imageUrl || "",
    description: item?.description || select?.dataset?.description || select?.closest(".field")?.querySelector(".field__label")?.textContent || ""
  };
}

function normalizeVisualSelectField(select, triggerId, labelId) {
  let field = select?.closest(".field");
  if (!field) return;

  if (field.tagName === "LABEL") {
    const replacement = document.createElement("div");
    for (const attribute of field.attributes) {
      if (attribute.name !== "for") replacement.setAttribute(attribute.name, attribute.value);
    }
    while (field.firstChild) replacement.append(field.firstChild);
    field.replaceWith(replacement);
    field = replacement;
  }

  let label = field.querySelector(":scope > .field__label");
  if (!label) return;
  if (label.tagName !== "LABEL") {
    const replacement = document.createElement("label");
    for (const attribute of label.attributes) {
      replacement.setAttribute(attribute.name, attribute.value);
    }
    while (label.firstChild) replacement.append(label.firstChild);
    label.replaceWith(replacement);
    label = replacement;
  }
  label.id = labelId;
  label.htmlFor = triggerId;
}

function ensureVisualSelect(select) {
  if (!select?.matches("[data-visual-select]")) return null;
  const controlId = select.id || `visualSelect${[...document.querySelectorAll("[data-visual-select]")].indexOf(select) + 1}`;
  if (!select.id) select.id = controlId;
  const triggerId = `${controlId}VisualTrigger`;
  const labelId = `${controlId}VisualLabel`;
  normalizeVisualSelectField(select, triggerId, labelId);
  let picker = select.nextElementSibling;
  if (!picker?.classList.contains("visual-select")) {
    const menuId = `${controlId}VisualMenu`;
    picker = document.createElement("div");
    picker.className = "visual-select";
    picker.innerHTML = `
      <button id="${triggerId}" class="visual-select__button" type="button"
        aria-haspopup="listbox" aria-expanded="false" aria-controls="${menuId}"></button>
      <div id="${menuId}" class="visual-select__menu" role="listbox" hidden></div>
    `;
    select.after(picker);
    const button = picker.querySelector(".visual-select__button");
    const menu = picker.querySelector(".visual-select__menu");
    button.addEventListener("click", () => {
      if (!menu.hidden) {
        setVisualSelectOpen(picker, false);
        return;
      }
      closeVisualSelects({ except: picker });
      setVisualSelectOpen(picker, true);
    });
    button.addEventListener("keydown", event => handleVisualSelectTriggerKeydown(event, picker));
    menu.addEventListener("keydown", event => handleVisualSelectMenuKeydown(event, picker));
  }

  select.classList.add("native-select-hidden");
  select.tabIndex = -1;
  select.setAttribute("aria-hidden", "true");
  picker.querySelector(".visual-select__menu").setAttribute("aria-labelledby", labelId);
  return picker;
}

function getVisualSelectControl(picker) {
  const select = picker?.previousElementSibling;
  return select?.matches("[data-visual-select]") ? select : null;
}

function closeVisualSelects({ except = null } = {}) {
  document.querySelectorAll(".visual-select").forEach(picker => {
    if (picker !== except) setVisualSelectOpen(picker, false);
  });
}

function clearVisualSelectTypeahead(picker) {
  const state = visualSelectTypeahead.get(picker);
  if (state?.timer) window.clearTimeout(state.timer);
  visualSelectTypeahead.delete(picker);
}

function setVisualSelectOpen(picker, open, { focus = "selected", restoreFocus = false } = {}) {
  const button = picker.querySelector(".visual-select__button");
  const menu = picker.querySelector(".visual-select__menu");
  const select = getVisualSelectControl(picker);
  if (!button || !menu || !select) return;

  if (open && select.disabled) return;
  button.setAttribute("aria-expanded", String(open));
  menu.hidden = !open;

  if (!open) {
    clearVisualSelectTypeahead(picker);
    menu.replaceChildren();
    if (restoreFocus) button.focus();
    return;
  }

  buildVisualSelectMenu(select, picker);
  const options = getEnabledVisualOptions(picker);
  if (options.length === 0) return;
  const selectedIndex = options.findIndex(option => option.getAttribute("aria-selected") === "true");
  const focusIndex = focus === "first"
    ? 0
    : focus === "last"
      ? options.length - 1
      : Math.max(0, selectedIndex);
  focusVisualOption(picker, focusIndex);
}

function renderVisualSelectOption(meta, selected = false) {
  return `
    ${meta.imageUrl ? `<img src="${escapeHtml(meta.imageUrl)}" alt="">` : `<span class="visual-select__fallback" aria-hidden="true">${escapeHtml(String(meta.code).slice(0, 2))}</span>`}
    <span>
      <strong>${escapeHtml(meta.code || t("Не выбрано"))}</strong>
      <small>${escapeHtml(meta.description ? t(meta.description) : t("Без описания"))}</small>
    </span>
    ${selected ? `<b aria-hidden="true">✓</b>` : ""}
  `;
}

function syncVisualSelect(select) {
  const picker = ensureVisualSelect(select);
  if (!picker) return;

  const button = picker.querySelector(".visual-select__button");
  const menu = picker.querySelector(".visual-select__menu");
  const selectedOption = select.options[select.selectedIndex] || select.options[0];
  const selectedCode = selectedOption?.value || "";
  button.innerHTML = renderVisualSelectOption(getVisualOptionMeta(selectedCode, select), true);
  button.disabled = select.disabled;
  const labelId = `${select.id || "visualSelect"}VisualLabel`;
  const value = button.querySelector("strong");
  const description = button.querySelector("small");
  if (value) value.id = `${button.id}Value`;
  if (description) description.id = `${button.id}Description`;
  button.removeAttribute("aria-label");
  button.setAttribute("aria-labelledby", [labelId, value?.id].filter(Boolean).join(" "));
  if (description?.textContent.trim()) {
    button.setAttribute("aria-describedby", description.id);
  } else {
    button.removeAttribute("aria-describedby");
  }
  menu.removeAttribute("aria-label");
  menu.setAttribute("aria-labelledby", labelId);
  if (!menu.hidden || select.disabled) setVisualSelectOpen(picker, false);
}

function buildVisualSelectMenu(select, picker) {
  const menu = picker.querySelector(".visual-select__menu");
  menu.replaceChildren();
  [...select.options].forEach((option, index) => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = "visual-select__option";
    item.id = `${menu.id}Option${index}`;
    item.setAttribute("role", "option");
    item.setAttribute("aria-selected", String(index === select.selectedIndex));
    item.tabIndex = index === select.selectedIndex ? 0 : -1;
    item.disabled = option.disabled;
    item.dataset.optionIndex = String(index);
    const meta = getVisualOptionMeta(option.value, select);
    item.dataset.search = `${option.textContent || ""} ${meta.description || ""}`.trim().toLocaleLowerCase();
    item.innerHTML = renderVisualSelectOption(meta, index === select.selectedIndex);
    item.addEventListener("click", () => selectVisualOption(picker, item));
    menu.append(item);
  });

  if (!menu.querySelector(".visual-select__option[tabindex='0']:not(:disabled)")) {
    const firstEnabled = menu.querySelector(".visual-select__option:not(:disabled)");
    if (firstEnabled) firstEnabled.tabIndex = 0;
  }
}

function getEnabledVisualOptions(picker) {
  return [...picker.querySelectorAll(".visual-select__option:not(:disabled)")];
}

function focusVisualOption(picker, index) {
  const options = getEnabledVisualOptions(picker);
  if (options.length === 0) return;
  const normalizedIndex = (index + options.length) % options.length;
  options.forEach((option, optionIndex) => {
    option.tabIndex = optionIndex === normalizedIndex ? 0 : -1;
  });
  options[normalizedIndex].focus();
}

function selectVisualOption(picker, item) {
  const select = getVisualSelectControl(picker);
  const index = Number(item?.dataset.optionIndex);
  const option = select?.options[index];
  if (!select || !option || option.disabled) return;
  select.selectedIndex = index;
  select.dispatchEvent(new Event("change", { bubbles: true }));
  setVisualSelectOpen(picker, false, { restoreFocus: true });
}

function isVisualSelectTypeaheadKey(event) {
  return event.key.length === 1
    && event.key !== " "
    && !event.altKey
    && !event.ctrlKey
    && !event.metaKey;
}

function handleVisualSelectTypeahead(picker, key) {
  const options = getEnabledVisualOptions(picker);
  if (options.length === 0) return;

  const previous = visualSelectTypeahead.get(picker);
  if (previous?.timer) window.clearTimeout(previous.timer);
  const normalizedKey = key.toLocaleLowerCase();
  let query = `${previous?.query || ""}${normalizedKey}`;
  if (query.length > 1 && [...query].every(character => character === normalizedKey)) {
    query = normalizedKey;
  }
  const timer = window.setTimeout(
    () => visualSelectTypeahead.delete(picker),
    VISUAL_SELECT_TYPEAHEAD_TIMEOUT_MS
  );
  visualSelectTypeahead.set(picker, { query, timer });

  const activeIndex = Math.max(0, options.indexOf(document.activeElement));
  const findMatch = candidateQuery => {
    for (let offset = 1; offset <= options.length; offset += 1) {
      const index = (activeIndex + offset) % options.length;
      if (options[index].dataset.search.startsWith(candidateQuery)) return index;
    }
    return -1;
  };
  let matchIndex = findMatch(query);
  if (matchIndex < 0 && query.length > 1) {
    query = normalizedKey;
    matchIndex = findMatch(query);
    visualSelectTypeahead.set(picker, { query, timer });
  }
  if (matchIndex >= 0) focusVisualOption(picker, matchIndex);
}

function handleVisualSelectTriggerKeydown(event, picker) {
  const menu = picker.querySelector(".visual-select__menu");
  if (event.key === "Escape") {
    if (!menu.hidden) {
      event.preventDefault();
      setVisualSelectOpen(picker, false, { restoreFocus: true });
    }
    return;
  }

  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    if (menu.hidden) {
      closeVisualSelects({ except: picker });
      setVisualSelectOpen(picker, true);
    }
    return;
  }

  if (event.key === "ArrowDown" || event.key === "ArrowUp" || event.key === "Home" || event.key === "End") {
    event.preventDefault();
    closeVisualSelects({ except: picker });
    const focus = event.key === "Home" ? "first" : event.key === "End" ? "last" : "selected";
    setVisualSelectOpen(picker, true, { focus });
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      const options = getEnabledVisualOptions(picker);
      const selectedIndex = Math.max(0, options.findIndex(option => option.getAttribute("aria-selected") === "true"));
      focusVisualOption(picker, selectedIndex + (event.key === "ArrowDown" ? 1 : -1));
    }
    return;
  }

  if (isVisualSelectTypeaheadKey(event)) {
    event.preventDefault();
    closeVisualSelects({ except: picker });
    if (menu.hidden) setVisualSelectOpen(picker, true);
    handleVisualSelectTypeahead(picker, event.key);
  }
}

function handleVisualSelectMenuKeydown(event, picker) {
  const options = getEnabledVisualOptions(picker);
  const activeIndex = Math.max(0, options.indexOf(document.activeElement));

  if (event.key === "Tab") {
    window.setTimeout(() => setVisualSelectOpen(picker, false), 0);
    return;
  }
  if (event.key === "Escape") {
    event.preventDefault();
    setVisualSelectOpen(picker, false, { restoreFocus: true });
    return;
  }
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    selectVisualOption(picker, document.activeElement);
    return;
  }
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    focusVisualOption(picker, activeIndex + (event.key === "ArrowDown" ? 1 : -1));
    return;
  }
  if (event.key === "Home" || event.key === "End") {
    event.preventDefault();
    focusVisualOption(picker, event.key === "Home" ? 0 : options.length - 1);
    return;
  }
  if (isVisualSelectTypeaheadKey(event)) {
    event.preventDefault();
    handleVisualSelectTypeahead(picker, event.key);
  }
}

function syncAllVisualSelects() {
  document.querySelectorAll("[data-visual-select]").forEach(select => syncVisualSelect(select));
}

function getSelectedCarDesign() {
  const input = carDesignPicker?.querySelector("input:checked");
  return input ? input.value : "";
}

function renderSmecCarDesigns() {
  if (!carDesignPicker) return;
  const designs = getSmecCarDesignOptions();
  carDesignPicker.replaceChildren();
  for (const design of designs) {
    const label = document.createElement("label");
    label.className = "smec-design-card";
    label.innerHTML = `
      <input type="radio" name="smecCarDesign" value="${escapeHtml(design.code)}">
      ${design.imageUrl ? `<img src="${escapeHtml(design.imageUrl)}" alt="${escapeHtml(design.code)}">` : `<span class="smec-design-card__fallback">${escapeHtml(String(design.code).slice(0, 2))}</span>`}
      <span>
        <strong>${escapeHtml(design.code)}</strong>
        <small>${escapeHtml(design.wallDescription || design.doorDescription || "Car design")}</small>
      </span>
    `;
    carDesignPicker.append(label);
  }
  const first = carDesignPicker.querySelector("input");
  if (first) first.checked = true;
}

function getSmecFloorPatternOptions(floorType) {
  const group = (state.catalog?.smecFloorPatterns || []).find(item => item.floorType === floorType);
  return group?.options || [];
}

function updateSmecFloorPatterns(selectedValue = null) {
  const floorType = floorTypeSelect?.value || "concave-down";
  const options = getSmecFloorPatternOptions(floorType);
  const preferred = selectedValue || (floorType === "concave-down" ? "depth 25mm" : options[0] || "");
  fillVisualSelect(floorPatternSelect, options, [], preferred);
  if (floorPatternSelect) {
    floorPatternSelect.disabled = options.length === 0;
    syncVisualSelect(floorPatternSelect);
  }
}

function updateSmecPower() {
  // The current SMEC Excel form shows fixed power supply text, not a motor power input.
}

function renderSmecControls() {
  const materialCodes = byCodePrefix(["SUS-", "ZDT-"]);
  const landingMaterialCodes = materialCodes.length ? materialCodes : ["SUS-H", "SUS-M"];

  const allowedEleSeries = getSmecChoices("Ele Series", ["LEHY Series", "ELENESSA", "Panoramic"])
    .map(value => String(value).trim())
    .filter(value => ["LEHY Series", "ELENESSA", "Panoramic"].includes(value));
  fillSelect(smecEleSeriesInput, allowedEleSeries, "LEHY Series");
  updateSmecModels();
  fillSelect(manufacturingStandardSelect, getSmecChoices("Manufacturing Standard"), "EN81-20/50:2014");
  fillSelect(projectTypeSelect, getSmecChoices("Project Type"), "Residence");
  fillSelect(operationSelect, getSmecChoices("Operation", ["1C-2BC"]), "1C-2BC");
  fillSelect(shaftDoorTypeInput, getSmecChoices("Shaft Door Type", ["1D1G", "1D2G", "2D2G"]), "1D2G");
  fillSelect(doorModeSelect, getSmecChoices("Door Mode", ["Central opening", "Side opening"]), "Central opening");
  fillVisualSelect(ceilingSelect, getSmecChoices("Ceiling", byCodePrefix("ZCL")));
  fillVisualSelect(floorTypeSelect, getSmecChoices("Floor Type"), [], "concave-down");
  updateSmecFloorPatterns("depth 25mm");
  fillVisualSelect(wallMaterialSelect, materialCodes, ["SUS-H", "SUS-M"], "SUS-H");
  fillVisualSelect(carDoorMaterialSelect, materialCodes, ["SUS-H", "SUS-M"], "SUS-H");
  fillSelect(mirrorSelect, getSmecChoices("Mirror", ["None", "Half mirror", "Whole mirror"]), "None");
  fillSelect(mirrorPositionSelect, getSmecChoices("Mirror Position", ["rear wall"]), "rear wall");
  fillVisualSelect(handrailSelect, getSmecChoices("Handrail", byCodePrefix("ZYH")), ["ZYH-RH06"]);
  fillVisualSelect(copSelect, getSmecChoices("COP", byCodePrefix("ZCB")), ["ZCB■-ND10"], "ZCB-ND10");
  fillVisualSelect(cop2Select, getSmecChoices("COP 2", byCodePrefix("ZCB")));
  fillVisualSelect(copButtonSelect, getSmecChoices("COP Button"), ["A14"], "A14");
  fillVisualSelect(wheelchairCopSelect, getSmecChoices("Wheelchair COP"));
  fillVisualSelect(wheelchairCop2Select, getSmecChoices("Wheelchair COP 2"));
  fillVisualSelect(wheelchairButtonSelect, getSmecChoices("Wheelchair COP Button"), ["A14"], "A14");
  fillSelect(mainJambSelect, getSmecChoices("Jamb", ["E-102", "E-302", "E-312", "E-322"]), "E-102");
  fillSelect(otherJambSelect, getSmecChoices("Jamb", ["E-102", "E-302", "E-312", "E-322"]), "E-102");
  fillVisualSelect(mainLandingMaterialSelect, landingMaterialCodes, ["SUS-H", "SUS-M"], "SUS-H");
  fillVisualSelect(otherLandingMaterialSelect, landingMaterialCodes, ["SUS-H", "SUS-M"], "SUS-H");
  fillSelect(mainSillBracketSelect, getSmecChoices("Sill Bracket", ["Steel sill bracket by seller"]), "Steel sill bracket by seller");
  fillSelect(otherSillBracketSelect, getSmecChoices("Sill Bracket", ["Steel sill bracket by seller"]), "Steel sill bracket by seller");
  fillVisualSelect(mainLandingDoorSelect, landingMaterialCodes, ["SUS-H", "SUS-M"], "SUS-H");
  fillVisualSelect(otherLandingDoorSelect, landingMaterialCodes, ["SUS-H", "SUS-M"], "SUS-H");
  fillVisualSelect(mainLopSelect, getSmecChoices("LOP"), ["ZPI■-GD10"], "ZPI-GD10");
  fillVisualSelect(otherLopSelect, getSmecChoices("LOP"), ["ZPI■-GD10"], "ZPI-GD10");
  fillVisualSelect(lopButtonSelect, getSmecChoices("LOP Button"), ["A14"], "A14");
  fillVisualSelect(otherLopButtonSelect, getSmecChoices("LOP Button"), ["A14"], "A14");
  fillVisualSelect(mainAuxiliaryLopSelect, getSmecChoices("Auxiliary LOP"));
  fillVisualSelect(otherAuxiliaryLopSelect, getSmecChoices("Auxiliary LOP"));
  fillVisualSelect(auxiliaryLopButtonSelect, getSmecChoices("Auxiliary LOP Button"), ["A14"], "A14");
  fillVisualSelect(otherAuxiliaryLopButtonSelect, getSmecChoices("Auxiliary LOP Button"), ["A14"], "A14");
  fillVisualSelect(hallIndicatorSelect, getSmecChoices("Hall Indicator"));
  fillVisualSelect(hallLanternSelect, getSmecChoices("Hall Lantern"));
  renderSmecCarDesigns();
  updateSmecPower();
}

function renderXiziControls() {
  fillSelect(xiziElevatorTypeSelect, getXiziChoices("Elevator Type", ["С МП", "Без МП"]), "Без МП");
  fillSelect(xiziModelSelect, getXiziChoices("Model", state.catalog?.xiziSeries || []), "MRL-T");
  fillSelect(xiziControlSystemSelect, getXiziChoices("Control System", ["Одиночная", "Групповая"]), "Одиночная");
  fillSelect(xiziShaftTypeSelect, getXiziChoices("Shaft Type", ["Железобетон", "Металлокаркас"]), "Железобетон");
  fillSelect(xiziCarHeightSelect, getXiziChoices("Car Height", [2200, 2300, 2400]), 2400);
  fillSelect(xiziCarTypeSelect, getXiziChoices("Car Type", ["Проходная", "Непроходная"]), "Непроходная");
  fillSelect(xiziDoorHeightSelect, getXiziChoices("Door Height", [2000, 2100, 2200, 2300]), 2300);
  fillSelect(xiziFireRatingSelect, getXiziChoices("Fire Rating", ["E30", "EI60"]), "EI60");
  fillSelect(xiziDoorOpeningSelect, getXiziChoices("Door Opening Type", ["Телескопического открывания", "Центрального открывания"]), "Центрального открывания");
  fillSelect(xiziCabinDesignSelect, getXiziChoices("Cabin Design"), "U-CR126");
  fillSelect(xiziCarWallMaterialSelect, getXiziChoices("Car Wall Material", ["Нерж. сталь AISI443"]), "Нерж. сталь AISI443");
  fillSelect(xiziCarDoorMaterialSelect, getXiziChoices("Car Door Material", getXiziChoices("Shaft Door Material", ["Нерж. сталь AISI443"])), "Нерж. сталь AISI443");
  fillSelect(xiziCeilingSelect, getXiziChoices("Ceiling"), "U-CL029");
  fillSelect(xiziFloorSelect, getXiziChoices("Floor"), "U-FL033");
  fillSelect(xiziMirrorWallSelect, ["Нет", ...getXiziChoices("Mirror Wall", ["Задняя стена"])], "Нет");
  fillSelect(xiziMirrorHeightSelect, getXiziChoices("Mirror Height", ["Половина высоты", "Во всю высоту"]), "Половина высоты");
  fillSelect(xiziHandrailPositionSelect, ["Нет", ...getXiziChoices("Handrail Position", ["1 х Задняя стена"])], "Нет");
  fillSelect(xiziHandrailSelect, getXiziChoices("Handrail"), "U-HR001");
  fillSelect(xiziCopSelect, getXiziChoices("COP"), "U-CY100");
  fillSelect(xiziCopButtonSelect, getXiziChoices("COP Button"), "iBR34M(BL)");
  fillSelect(xiziMainShaftDoorSelect, getXiziChoices("Shaft Door Material", ["Нерж. сталь AISI443"]), "Нерж. сталь AISI443");
  fillSelect(xiziOtherShaftDoorSelect, getXiziChoices("Shaft Door Material", ["Нерж. сталь AISI443"]), "Нерж. сталь AISI443");
  fillSelect(xiziMainLopSelect, getXiziChoices("LOP"), "U-ZW1600");
  fillSelect(xiziOtherLopSelect, getXiziChoices("LOP"), "U-ZW1600");
  fillSelect(xiziMainLipSelect, ["Нет", ...getXiziChoices("LIP")], "Нет");
  fillSelect(xiziOtherLipSelect, ["Нет", ...getXiziChoices("LIP")], "Нет");
  fillSelect(xiziAirConditionerSelect, getXiziChoices("Air Conditioner", ["Нет", "Охлаждение", "Охлаждение и нагрев"]), "Нет");
  fillSelect(xiziRccSelect, getXiziChoices("RCC", ["Нет"]), "Нет");
  syncAllVisualSelects();
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
  return isAuthenticated();
}

async function loadCatalog() {
  const response = await apiFetch("/api/pricing/catalog");
  if (!response.ok) return;
  const catalog = await sessionRequests.readJson(response);
  if (catalog === sessionRequests.stalePayload) return;
  state.catalog = catalog;
  renderCatalogControls();
}

async function loadProjects() {
  const response = await apiFetch("/api/projects");
  if (!response.ok) return;
  const projects = await sessionRequests.readJson(response);
  if (projects === sessionRequests.stalePayload) return;

  const projectEntries = await Promise.all(projects.map(async project => {
    const [configurationsResponse, pricingResponse] = await Promise.all([
      apiFetch(`/api/projects/${project.id}/configurations`),
      apiFetch(`/api/projects/${project.id}/pricing-specifications`)
    ]);
    const configurations = configurationsResponse.ok
      ? await sessionRequests.readJson(configurationsResponse)
      : [];
    const pricing = pricingResponse.ok
      ? await sessionRequests.readJson(pricingResponse)
      : [];
    if (configurations === sessionRequests.stalePayload
        || pricing === sessionRequests.stalePayload) {
      return sessionRequests.stalePayload;
    }

    return [project.id, configurations, pricing];
  }));
  if (!sessionRequests.isCurrent(response)
      || projectEntries.includes(sessionRequests.stalePayload)) {
    return;
  }

  state.projects = projects;
  state.configurationsByProjectId = new Map(
    projectEntries.map(([projectId, configurations]) => [projectId, configurations]));
  state.pricingByProjectId = new Map(
    projectEntries.map(([projectId, , pricing]) => [projectId, pricing]));
  renderProjects();
}

function renderProjects() {
  pricingProjectSelect.replaceChildren();
  for (const project of state.projects) {
    const option = document.createElement("option");
    option.value = project.id;
    option.textContent = project.name;
    pricingProjectSelect.append(option);
  }

  if (state.projects.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = "Создайте проект в личном кабинете";
    pricingProjectSelect.append(option);
  }

  renderProjectConfigurations();
  renderSavedPricing();
}

function renderProjectConfigurations() {
  const projectId = pricingProjectSelect.value;
  const configurations = state.configurationsByProjectId.get(projectId) || [];
  drawingConfigurationSelect.replaceChildren();
  const empty = document.createElement("option");
  empty.value = "";
  empty.textContent = "Ввести вручную";
  drawingConfigurationSelect.append(empty);

  for (const configuration of configurations) {
    const option = document.createElement("option");
    option.value = configuration.id;
    option.textContent = `${configuration.name} · ${configuration.templateId}`;
    drawingConfigurationSelect.append(option);
  }
}

function renderSavedPricing() {
  const projectId = pricingProjectSelect.value;
  const items = state.pricingByProjectId.get(projectId) || [];
  savedPricingList.replaceChildren();
  if (items.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "Пока нет расчетов в проекте.";
    savedPricingList.append(empty);
    return;
  }

  for (const item of items) {
    const row = document.createElement("div");
    row.className = "saved-pricing-item";
    row.innerHTML = `
      <div>
        <strong>${escapeHtml(item.name)}</strong>
        <span>${escapeHtml(item.supplier)} · ${escapeHtml(item.series)} · ${money(item.totalCny, "CNY")}</span>
      </div>
      <a class="secondary secondary--compact" href="/api/pricing-specifications/${encodeURIComponent(item.id)}/tkp">ТКП Word</a>
    `;
    savedPricingList.append(row);
  }
}

function renderCatalogControls() {
  if (!state.catalog) return;
  const isXizi = supplierSelect.value === "XIZI";
  const smecCapacities = numericChoicesFromLabels(getSmecChoices("Capacity"), state.catalog.smecCapacities);
  const smecSpeeds = numericChoicesFromLabels(getSmecChoices("Speed"), state.catalog.smecSpeeds);
  if (isXizi) {
    fillSelect(seriesSelect, state.catalog.xiziSeries);
  }
  fillSelect(capacitySelect, isXizi ? state.catalog.xiziCapacities : smecCapacities, isXizi ? 1000 : 1050);
  fillSelect(speedSelect, isXizi ? state.catalog.xiziSpeeds : smecSpeeds, 1);
  fillSelect(doorWidthSelect, state.catalog.doorWidths.length ? state.catalog.doorWidths : [700, 800, 900, 1000, 1100], isXizi ? 900 : null);
  fillSelect(doorManufacturerSelect, state.catalog.doorManufacturers.length ? state.catalog.doorManufacturers : ["FERMATOR"]);
  fillSelect(doorTypeSelect, state.catalog.doorTypes.length ? state.catalog.doorTypes : ["2S", "CO"]);
  renderXiziControls();

  decorationSelect.replaceChildren();
  const noDecoration = document.createElement("option");
  noDecoration.value = "";
  noDecoration.textContent = "Не выбрана";
  decorationSelect.append(noDecoration);
  for (const item of state.catalog.xiziDecorations || []) {
    const option = document.createElement("option");
    option.value = item.code;
    option.textContent = `${item.code} · ${item.category || "Decoration"}`;
    decorationSelect.append(option);
  }

  toggleSupplierFields("[data-xizi-only]", isXizi);
  toggleSupplierFields("[data-smec-only]", !isXizi);
  const smecFlags = document.querySelector(".smec-flags");
  if (smecFlags) smecFlags.hidden = isXizi;
  if (!isXizi) {
    renderSmecControls();
  } else {
    if (!state.xiziInitialized) {
      stopsInput.value = "10";
      doorCountInput.value = "10";
      xiziShaftWidthInput.value = "1800";
      xiziShaftDepthInput.value = "2700";
      xiziTravelHeightInput.value = "27900";
      xiziOverheadInput.value = "5300";
      xiziPitInput.value = "1900";
      xiziCarWidthInput.value = "1100";
      xiziCarDepthInput.value = "2100";
      state.xiziInitialized = true;
    }
    syncXiziPricingFields();
  }
  renderOptions();
  renderDecorationPreview();
}

function toggleSupplierFields(selector, visible) {
  document.querySelectorAll(selector).forEach(element => {
    element.hidden = !visible;
    element.querySelectorAll("input, select, textarea, button").forEach(control => {
      control.disabled = !visible;
    });
  });
}

function renderOptions() {
  const isXizi = supplierSelect.value === "XIZI";
  const source = isXizi ? state.catalog?.xiziOptions : getManualSmecFunctions();
  optionsList.replaceChildren();
  for (const item of (source || []).filter(item =>
    !isXizi || !String(item.code).toLowerCase().startsWith("ac "))) {
    const isForced = isXizi && item.code === "40HQ";
    const isDisplayOption = isXizi && item.code.startsWith("ILED ");
    const label = document.createElement("label");
    label.className = `${item.description || item.imageUrl ? "pricing-option pricing-option--rich" : "pricing-option"}${isForced ? " is-locked" : ""}`;
    label.innerHTML = `
      <input type="${isDisplayOption ? "radio" : "checkbox"}" ${isDisplayOption ? `name="xizi-iled"` : ""} value="${escapeHtml(item.code)}" ${isForced ? "checked disabled" : ""}>
      ${item.imageUrl ? `<img src="${escapeHtml(item.imageUrl)}" alt="${escapeHtml(item.code)}">` : ""}
      <span>
        <strong>${escapeHtml(item.code)}</strong>
        ${item.description ? `<small>${escapeHtml(item.description)}</small>` : ""}
      </span>
    `;
    optionsList.append(label);
  }
}

function getManualSmecFunctions() {
  const automaticCodes = new Set([
    "2S door opening",
    "Decoration Weight",
    "Car door lock",
    "Steel nosing by seller",
    "(Sill Support)",
    "1D2G, 2D2G",
    "Through type door opening",
    "HL ＞2400mm",
    "Special type of traction machine",
    "(ELENESSA, ≤1050kg &≤1.75m/s)",
    "HH ＞2100mm",
    "Emergency exit at ceiling",
    "ELENESSA",
    "LEHY-III/LEHY-MRL-II"
  ]);
  const result = [];
  let hasMeld = false;

  for (const item of state.catalog?.smecFunctions || []) {
    const code = String(item.code || "").trim();
    const normalizedCode = code.replace(/\s+/g, " ").trim();
    const capacityHelper = /kg/i.test(normalizedCode)
      && /^[\d\s,~～-]+$/.test(normalizedCode.replace(/kg/gi, ""));
    const formulaHelper = normalizedCode.includes("Sill Support")
      || normalizedCode.includes("Through type door opening")
      || normalizedCode.startsWith("Special type of traction machine")
      || /^HL\s*[＞>]/i.test(normalizedCode)
      || /^HH\s*[＞>]/i.test(normalizedCode);
    if (!code || automaticCodes.has(code) || automaticCodes.has(normalizedCode) || capacityHelper || formulaHelper) continue;
    if (code === "MELD(ELENESSA)" || code === "ELD(LEHY)") {
      if (!hasMeld) {
        result.push({
          code: "MELD",
          description: "Аварийное устройство эвакуации. Модификация и цена определяются по выбранной серии."
        });
        hasMeld = true;
      }
      continue;
    }
    result.push(item);
  }
  return result;
}

function renderDecorationPreview() {
  const code = decorationSelect.value;
  if (!code) {
    decorationPreview.hidden = true;
    decorationPreview.replaceChildren();
    return;
  }

  const item = state.catalog?.xiziDecorations?.find(candidate => candidate.code === code);
  decorationPreview.hidden = false;
  decorationPreview.innerHTML = `
    <div class="finish-preview__image">${escapeHtml(code.slice(0, 2))}</div>
    <div>
      <strong>${escapeHtml(code)}</strong>
      <span>${escapeHtml(item?.category || "Отделка кабины")}</span>
      <small>Цена из прайса: ${money(readJsonValue(item?.price), "CNY")}</small>
    </div>
  `;
}

function readJsonValue(value) {
  if (value === null || value === undefined) return 0;
  return Number(value) || 0;
}

function applyConfiguration(configuration) {
  const parameters = configuration?.parameters || {};
  const get = (...names) => {
    for (const name of names) {
      const direct = parameters[name];
      if (direct !== undefined && direct !== null && String(direct).trim() !== "") return direct;
      const foundKey = Object.keys(parameters).find(key => key.toLowerCase() === name.toLowerCase());
      if (foundKey) return parameters[foundKey];
    }
    return null;
  };

  const capacity = get("DLOAD", "Q", "CAP", "Груз.");
  const speed = get("SPEED", "V", "Скорость");
  const stops = get("NBLD", "Stops", "Остановки");
  const doors = get("Doors", "Двери");
  const doorWidth = get("JJ");
  const travelHeight = get("TR");
  const shaftWidth = get("AH");
  const shaftDepth = get("BH");
  const carWidth = get("AA");
  const carDepth = get("BB");
  const carHeight = get("HL");
  const doorHeight = get("HH");
  const name = get("$Oboznach", "Oboznach");

  if (capacity) capacitySelect.value = String(Number(capacity));
  if (speed) speedSelect.value = String(Number(String(speed).replace(",", ".")));
  if (stops) stopsInput.value = String(Number(stops));
  if (stops && floorsInput) floorsInput.value = String(Number(stops));
  if (doors && smecDoorCountInput) smecDoorCountInput.value = String(Number(doors));
  if (doors && doorCountInput) doorCountInput.value = String(Number(doors));
  if (doorWidth) doorWidthSelect.value = String(Number(doorWidth));
  if (doorWidth && smecDoorWidthInput) smecDoorWidthInput.value = String(Number(doorWidth));
  if (doorWidth && xiziShaftWidthInput && !xiziShaftWidthInput.value) xiziShaftWidthInput.value = "";
  if (shaftWidth && xiziShaftWidthInput) xiziShaftWidthInput.value = String(Number(shaftWidth));
  if (shaftDepth && xiziShaftDepthInput) xiziShaftDepthInput.value = String(Number(shaftDepth));
  if (travelHeight && xiziTravelHeightInput) xiziTravelHeightInput.value = String(Number(travelHeight));
  if (carWidth && xiziCarWidthInput) xiziCarWidthInput.value = String(Number(carWidth));
  if (carDepth && xiziCarDepthInput) xiziCarDepthInput.value = String(Number(carDepth));
  if (doorHeight && xiziDoorHeightSelect) xiziDoorHeightSelect.value = String(Number(doorHeight));
  if (shaftWidth && shaftWidthInput) shaftWidthInput.value = String(Number(shaftWidth));
  if (shaftDepth && shaftDepthInput) shaftDepthInput.value = String(Number(shaftDepth));
  if (carWidth && carWidthInput) carWidthInput.value = String(Number(carWidth));
  if (carDepth && carDepthInput) carDepthInput.value = String(Number(carDepth));
  if (carHeight && carHeightInput) carHeightInput.value = String(Number(carHeight));
  if (doorHeight && doorHeightInput) doorHeightInput.value = String(Number(doorHeight));
  if (name) pricingNameInput.value = String(name);
  updateSmecPower();
}

function collectSpecificationFields() {
  if (supplierSelect.value === "XIZI") {
    return {
      "Project Name": xiziProjectNameInput?.value || "",
      "Address": xiziAddressInput?.value || "",
      "Contract No": xiziContractInput?.value || "",
      "Unit No": xiziUnitInput?.value || "",
      "Elevator Type": xiziElevatorTypeSelect?.value || "",
      "Model": xiziModelSelect?.value || "",
      "Lift No": xiziLiftNumberInput?.value || "",
      "Quantity": xiziQuantityInput?.value || "",
      "Speed": speedSelect?.value || "",
      "Capacity": capacitySelect?.value || "",
      "Stops": stopsInput?.value || "",
      "Doors": doorCountInput?.value || "",
      "Control System": xiziControlSystemSelect?.value || "",
      "Decoration Weight": xiziDecorationWeightInput?.value || "",
      "Main Floor": xiziMainFloorInput?.value || "",
      "Other Floors": xiziOtherFloorsInput?.value || "",
      "Shaft Width": xiziShaftWidthInput?.value || "",
      "Shaft Depth": xiziShaftDepthInput?.value || "",
      "Travel Height": xiziTravelHeightInput?.value || "",
      "Shaft Type": xiziShaftTypeSelect?.value || "",
      "Overhead": xiziOverheadInput?.value || "",
      "Pit": xiziPitInput?.value || "",
      "Car Width": xiziCarWidthInput?.value || "",
      "Car Depth": xiziCarDepthInput?.value || "",
      "Car Height": xiziCarHeightSelect?.value || "",
      "Car Type": xiziCarTypeSelect?.value || "",
      "Door Width": doorWidthSelect?.value || "",
      "Door Height": xiziDoorHeightSelect?.value || "",
      "Fire Rating": xiziFireRatingSelect?.value || "",
      "Door Opening": xiziDoorOpeningSelect?.value || "",
      "Cabin Design": xiziCabinDesignSelect?.value || "",
      "Car Wall Material": xiziCarWallMaterialSelect?.value || "",
      "Car Door Material": xiziCarDoorMaterialSelect?.value || "",
      "Ceiling": xiziCeilingSelect?.value || "",
      "Floor": xiziFloorSelect?.value || "",
      "Mirror Wall": xiziMirrorWallSelect?.value || "",
      "Mirror Height": xiziMirrorHeightSelect?.value || "",
      "Handrail Position": xiziHandrailPositionSelect?.value || "",
      "Handrail": xiziHandrailSelect?.value || "",
      "COP": xiziCopSelect?.value || "",
      "COP Button": xiziCopButtonSelect?.value || "",
      "Main Shaft Door": xiziMainShaftDoorSelect?.value || "",
      "Other Shaft Door": xiziOtherShaftDoorSelect?.value || "",
      "Main LOP": xiziMainLopSelect?.value || "",
      "Other LOP": xiziOtherLopSelect?.value || "",
      "Main LIP": xiziMainLipSelect?.value || "",
      "Other LIP": xiziOtherLipSelect?.value || "",
      "AC": xiziAirConditionerSelect?.value || "",
      "RCC": xiziRccSelect?.value || "",
      "Decoration": decorationSelect?.value || ""
    };
  }

  if (supplierSelect.value !== "SMEC") return {};
  const selectedDesign = getSmecCarDesignOptions().find(item => item.code === getSelectedCarDesign());
  return {
    "Ele Series": smecEleSeriesInput?.value || "",
    "Project Type": projectTypeSelect?.value || "",
    "Manufacturing Standard": manufacturingStandardSelect?.value || "",
    "Quantity": smecQuantityInput?.value || "",
    "Operation": operationSelect?.value || "",
    "Decoration Weight": smecDecorationWeightInput?.value || "",
    "Floors": floorsInput?.value || "",
    "Stops": stopsInput?.value || "",
    "Doors": smecDoorCountInput?.value || "",
    "Main Floor": smecMainFloorInput?.value || "",
    "Other Floors": smecOtherFloorsInput?.value || "",
    "Power Supply": smecPowerSupplyInput?.value || "",
    "Lighting Supply": smecLightingSupplyInput?.value || "",
    "AH": shaftWidthInput?.value || "",
    "BH": shaftDepthInput?.value || "",
    "Door type": shaftDoorTypeInput?.value || "",
    "TR": trInput?.value || "",
    "OH": ohInput?.value || "",
    "PD": pdInput?.value || "",
    "JJ": smecDoorWidthInput?.value || "",
    "Door mode": doorModeSelect?.value || "",
    "HH": doorHeightInput?.value || "",
    "AA": carWidthInput?.value || "",
    "BB": carDepthInput?.value || "",
    "HL": carHeightInput?.value || "",
    "Car Design": selectedDesign?.code || "",
    "Car Design Wall": selectedDesign?.wallDescription || "",
    "Car Design Door": selectedDesign?.doorDescription || "",
    "Ceiling": ceilingSelect?.value || "",
    "Floor Type": floorTypeSelect?.value || "",
    "Floor Pattern": floorPatternSelect?.value || "",
    "Wall": wallMaterialSelect?.value || "",
    "Car Door": carDoorMaterialSelect?.value || "",
    "Mirror": mirrorSelect?.value || "",
    "Handrail Position": mirrorPositionSelect?.value || "",
    "Handrail": handrailSelect?.value || "",
    "COP": copSelect?.value || "",
    "COP 2": cop2Select?.value || "",
    "COP Button": copButtonSelect?.value || "",
    "Wheelchair COP": wheelchairCopSelect?.value || "",
    "Wheelchair COP 2": wheelchairCop2Select?.value || "",
    "Wheelchair COP Button": wheelchairButtonSelect?.value || "",
    "Main Jamb": mainJambSelect?.value || "",
    "Main Landing Material": mainLandingMaterialSelect?.value || "",
    "Main Sill Bracket": mainSillBracketSelect?.value || "",
    "Main Landing Door": mainLandingDoorSelect?.value || "",
    "Other Jamb": otherJambSelect?.value || "",
    "Other Landing Material": otherLandingMaterialSelect?.value || "",
    "Other Sill Bracket": otherSillBracketSelect?.value || "",
    "Other Landing Door": otherLandingDoorSelect?.value || "",
    "Main LOP": mainLopSelect?.value || "",
    "Other LOP": otherLopSelect?.value || "",
    "LOP Button": lopButtonSelect?.value || "",
    "Other LOP Button": otherLopButtonSelect?.value || "",
    "Main Auxiliary LOP": mainAuxiliaryLopSelect?.value || "",
    "Other Auxiliary LOP": otherAuxiliaryLopSelect?.value || "",
    "Auxiliary LOP Button": auxiliaryLopButtonSelect?.value || "",
    "Other Auxiliary LOP Button": otherAuxiliaryLopButtonSelect?.value || "",
    "Hall Indicator": hallIndicatorSelect?.value || "",
    "Hall Lantern": hallLanternSelect?.value || "",
    "Other Requirements": smecOtherRequirementsInput?.value || ""
  };
}

function collectRequest() {
  const isXizi = supplierSelect.value === "XIZI";
  const selectedOptions = [...optionsList.querySelectorAll("input:checked")].map(input => input.value);

  return {
    supplier: supplierSelect.value,
    series: seriesSelect.value,
    capacityKg: Number(capacitySelect.value),
    speed: Number(speedSelect.value),
    stops: Math.max(1, Math.round(numberValue(stopsInput, 1))),
    doorWidthMm: Number((isXizi ? doorWidthSelect.value : smecDoorWidthInput.value) || 0),
    doorType: isXizi ? doorTypeSelect.value || null : null,
    doorManufacturer: isXizi ? doorManufacturerSelect.value || null : null,
    doorCount: Math.max(0, Math.round(numberValue(isXizi ? doorCountInput : smecDoorCountInput, 0))),
    extraHeightMm: 0,
    decorationCode: null,
    options: selectedOptions,
    efs: Boolean(efsToggle?.checked),
    e312: Boolean(e312Toggle?.checked),
    targetCurrency: targetCurrencySelect.value,
    projectId: pricingProjectSelect.value || null,
    projectConfigurationId: drawingConfigurationSelect.value || null,
    specificationFields: collectSpecificationFields(),
    name: pricingNameInput.value.trim() || null
  };
}

function markCalculationPending() {
  if (!state.currentUser || !state.catalog) return;
  pricingStatus.textContent = t("Считается...");
  pricingStatus.className = "pricing-status is-loading";
  savePricingButton.disabled = true;
  downloadTkpButton.disabled = true;
}

function scheduleLiveCalculation() {
  if (!state.currentUser || !state.catalog) return;
  window.clearTimeout(liveCalculationTimer);
  markCalculationPending();
  liveCalculationTimer = window.setTimeout(() => {
    calculate();
  }, LIVE_CALCULATION_DELAY_MS);
}

async function calculate(event) {
  event?.preventDefault();
  if (!state.currentUser || !state.catalog) return;
  window.clearTimeout(liveCalculationTimer);
  const requestId = ++calculationRequestId;
  const request = collectRequest();
  markCalculationPending();

  try {
    const response = await apiFetch("/api/pricing/calculate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });

    if (requestId !== calculationRequestId) return;

    if (!response.ok) {
      const messages = await readProblem(response, "Не удалось выполнить расчет");
      renderWarnings(messages, true);
      pricingStatus.textContent = t("Ошибка расчета");
      pricingStatus.className = "pricing-status is-blocked";
      state.lastCalculation = null;
      state.lastRequest = null;
      savePricingButton.disabled = true;
      downloadTkpButton.disabled = true;
      return;
    }

    const calculation = await sessionRequests.readJson(response);
    if (calculation === sessionRequests.stalePayload
        || requestId !== calculationRequestId) {
      return;
    }
    state.lastRequest = request;
    state.lastCalculation = calculation;
    renderCalculation();
  } catch {
    if (requestId !== calculationRequestId) return;
    renderWarnings(["Не удалось выполнить расчет. Проверьте соединение с API."], true);
    pricingStatus.textContent = t("Ошибка расчета");
    pricingStatus.className = "pricing-status is-blocked";
    state.lastCalculation = null;
    state.lastRequest = null;
    savePricingButton.disabled = true;
    downloadTkpButton.disabled = true;
  }
}

function renderCalculation() {
  const calculation = state.lastCalculation;
  if (!calculation) return;
  pricingStatus.textContent = calculation.status === "ready"
    ? t("Расчет готов")
    : calculation.status === "warning"
      ? t("Предварительный расчет")
      : t("Недоступно");
  pricingStatus.className = `pricing-status is-${calculation.status}`;
  totalCny.textContent = money(calculation.totalCny, "CNY");
  totalConverted.textContent = money(calculation.totalConverted, calculation.targetCurrency);
  rateInfo.textContent = `${t("Курс:")} 1 CNY = ${calculation.exchangeRate} ${calculation.targetCurrency} · ${calculation.exchangeRateSource}`;

  pricingLines.replaceChildren();
  for (const line of calculation.lines || []) {
    const row = document.createElement("div");
    row.className = `pricing-line is-${line.status}`;
    row.innerHTML = `
      <span>${escapeHtml(line.label)}</span>
      <small>${line.quantity} × ${line.unitPriceCny === null ? "проверка" : money(line.unitPriceCny, "CNY")}</small>
      <strong>${line.amountCny === null ? "—" : money(line.amountCny, "CNY")}</strong>
    `;
    pricingLines.append(row);
  }

  if (calculation.container) {
    const row = document.createElement("div");
    row.className = "pricing-line";
    row.innerHTML = `<span>Контейнер</span><small>справочно</small><strong>${escapeHtml(calculation.container.label)}</strong>`;
    pricingLines.append(row);
  }

  renderWarnings([...(calculation.blockers || []), ...(calculation.warnings || [])], calculation.status === "blocked");
  const saveDisabled = !canSavePricing()
    || calculation.status === "blocked"
    || !pricingProjectSelect.value;
  savePricingButton.disabled = saveDisabled;
  downloadTkpButton.disabled = saveDisabled;
}

function renderWarnings(messages, isError = false) {
  const fingerprint = JSON.stringify({ isError, messages });
  pricingWarnings.setAttribute("role", isError ? "alert" : "status");
  pricingWarnings.setAttribute("aria-live", isError ? "assertive" : "polite");
  pricingWarnings.className = isError ? "pricing-warnings is-error" : "pricing-warnings";
  if (messages.length === 0) {
    if (!pricingWarnings.hidden || pricingWarnings.childElementCount > 0) {
      pricingWarnings.replaceChildren();
    }
    pricingWarnings.hidden = true;
    pricingWarnings.dataset.warningFingerprint = "";
    return;
  }

  pricingWarnings.hidden = false;
  if (pricingWarnings.dataset.warningFingerprint === fingerprint) return;
  pricingWarnings.innerHTML = messages.map(message => `<div>${escapeHtml(message)}</div>`).join("");
  pricingWarnings.dataset.warningFingerprint = fingerprint;
}

async function savePricing() {
  if (!canSavePricing() || !state.lastRequest || !pricingProjectSelect.value) return;
  const response = await apiFetch(`/api/projects/${encodeURIComponent(pricingProjectSelect.value)}/pricing-specifications`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      name: pricingNameInput.value.trim() || state.lastRequest.name,
      request: state.lastRequest
    })
  });

  if (!response.ok) {
    const messages = await readProblem(response, "Не удалось сохранить расчет");
    renderWarnings(messages, true);
    return;
  }

  const savedSpecification = await sessionRequests.readJson(response);
  if (savedSpecification === sessionRequests.stalePayload) return;
  await loadProjects();
  if (!sessionRequests.isCurrent(response)) return;
  savePricingButton.disabled = true;
  downloadTkpButton.disabled = true;
  return savedSpecification;
}

async function saveAndDownloadTkp() {
  const savedSpecification = await savePricing();
  if (!savedSpecification?.id) {
    return;
  }

  window.location.assign(`/api/pricing-specifications/${encodeURIComponent(savedSpecification.id)}/tkp`);
}

async function register(event) {
  event.preventDefault();
  registerStatus.hidden = true;
  const response = await apiFetch("/api/auth/register", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      userName: registerUserName.value,
      displayName: registerDisplayName.value,
      password: registerPassword.value
    })
  });

  registerStatus.hidden = false;
  if (!response.ok) {
    registerStatus.className = "error";
    registerStatus.setAttribute("role", "alert");
    registerStatus.textContent = (await readProblem(response, t("Не удалось отправить заявку"))).join(" ");
    return;
  }

  registerForm.reset();
  registerStatus.className = "empty";
  registerStatus.setAttribute("role", "status");
  registerStatus.textContent = t("Заявка отправлена. Доступ появится после подтверждения администратором.");
}

async function login(event) {
  event.preventDefault();
  loginPassword.setCustomValidity("");
  const credentials = {
    userName: loginUserName.value,
    password: loginPassword.value
  };
  loginPassword.value = "";
  const response = await apiFetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(credentials)
  });

  if (!response.ok) {
    loginPassword.setCustomValidity(t("Неверный логин или пароль"));
    loginPassword.reportValidity();
    return;
  }

  const currentUser = await sessionRequests.readJson(response);
  if (currentUser === sessionRequests.stalePayload) return;
  clearPricingSessionState();
  loginPassword.setCustomValidity("");
  state.currentUser = currentUser;
  await Promise.allSettled([loadCatalog(), loadProjects()]);
  updateAuthView();
  if (isAuthenticated()) scheduleLiveCalculation();
}

async function logout() {
  clearPricingSessionState();
  state.currentUser = null;
  updateAuthView();
  await apiFetch("/api/auth/logout", { method: "POST" });
}

supplierSelect.addEventListener("change", renderCatalogControls);
smecEleSeriesInput?.addEventListener("change", () => updateSmecModels());
xiziModelSelect?.addEventListener("change", syncXiziPricingFields);
xiziDoorOpeningSelect?.addEventListener("change", syncXiziPricingFields);
xiziCarTypeSelect?.addEventListener("change", syncXiziPricingFields);
xiziShaftDepthInput?.addEventListener("input", syncXiziPricingFields);
xiziCarDepthInput?.addEventListener("input", syncXiziPricingFields);
decorationSelect.addEventListener("change", renderDecorationPreview);
[seriesSelect, capacitySelect, speedSelect].forEach(select => {
  select?.addEventListener("change", updateSmecPower);
});
document.querySelectorAll("[data-visual-select]").forEach(select => {
  select.addEventListener("change", () => {
    syncVisualSelect(select);
  });
});
floorTypeSelect?.addEventListener("change", () => updateSmecFloorPatterns());
document.addEventListener("click", event => {
  if (!event.target.closest(".visual-select")) {
    closeVisualSelects();
  }
});
document.addEventListener("focusin", event => {
  if (!event.target.closest(".visual-select")) {
    closeVisualSelects();
  }
});
pricingProjectSelect.addEventListener("change", () => {
  renderProjectConfigurations();
  renderSavedPricing();
  state.lastCalculation = null;
  savePricingButton.disabled = true;
  downloadTkpButton.disabled = true;
});
drawingConfigurationSelect.addEventListener("change", () => {
  const configuration = (state.configurationsByProjectId.get(pricingProjectSelect.value) || [])
    .find(item => item.id === drawingConfigurationSelect.value);
  if (configuration) applyConfiguration(configuration);
});
pricingForm.addEventListener("input", scheduleLiveCalculation);
pricingForm.addEventListener("change", scheduleLiveCalculation);
pricingForm.addEventListener("invalid", event => {
  openParentDisclosures(event.target);
}, true);
pricingForm.addEventListener("submit", calculate);
savePricingButton.addEventListener("click", savePricing);
downloadTkpButton.addEventListener("click", saveAndDownloadTkp);
registerForm?.addEventListener("submit", register);
loginForm?.addEventListener("submit", login);
logoutButton?.addEventListener("click", logout);
syncPricingAccessibleCopy();
setupPricingSearch();
syncMobilePricingSummary();
const pricingSummary = pricingStatus?.closest(".pricing-total");
if (pricingSummary) {
  new MutationObserver(syncMobilePricingSummary).observe(pricingSummary, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
    attributeFilter: ["class"]
  });
}
window.addEventListener("tflex:languagechange", () => {
  syncPricingAccessibleCopy();
  updateAuthView();
  closeVisualSelects();
  document.querySelectorAll("[data-visual-select] option[value='']").forEach(option => {
    option.textContent = t("Не выбрано");
  });
  syncAllVisualSelects();
  if (state.lastCalculation) renderCalculation();
  renderSavedPricing();
  syncMobilePricingSummary();
});

if (await loadCurrentUser()) {
  await Promise.allSettled([loadCatalog(), loadProjects()]);
  updateAuthView();
  if (isAuthenticated()) scheduleLiveCalculation();
}
