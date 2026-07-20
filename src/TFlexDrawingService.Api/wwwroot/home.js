import { t } from "./i18n.js?v=20260720-ui-hardening-4";

const state = {
  currentUser: null
};

const guestMain = document.querySelector("#guestMain");
const authenticatedMain = document.querySelector("[data-authenticated-main]");
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
const logoutButton = document.querySelector("#logoutButton");
const activeTemplateCount = document.querySelector("#activeTemplateCount");
const globalSearchInput = document.querySelector(".global-search input");
const templateCardCloseTimers = new WeakMap();

function isAuthenticated() {
  return Boolean(state.currentUser?.isAuthenticated);
}

function getRoleLabel() {
  const roles = state.currentUser?.roles || [];
  if (roles.includes("Admin")) return "Admin";
  if (roles.includes("Operator")) return "Engineer";
  if (roles.includes("Viewer")) return "Viewer";
  return "User";
}

function updateAuthView() {
  const authenticated = isAuthenticated();

  if (guestMain) guestMain.hidden = authenticated;
  if (loginForm) loginForm.hidden = authenticated;
  if (userPanel) userPanel.hidden = !authenticated;
  if (authenticatedMain) authenticatedMain.hidden = !authenticated;

  if (authenticated) {
    if (currentUserName) {
      currentUserName.textContent = state.currentUser.displayName || state.currentUser.userName;
    }
    if (currentUserRole) {
      const role = getRoleLabel();
      currentUserRole.hidden = role !== "Admin";
      currentUserRole.textContent = role;
    }
  } else {
    if (currentUserName) currentUserName.textContent = "";
    if (currentUserRole) currentUserRole.hidden = true;
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

async function loadActiveTemplateCount() {
  if (!activeTemplateCount) return;

  const response = await apiFetch("/api/templates");
  if (!response.ok) return;

  const templates = await response.json();
  activeTemplateCount.textContent = String(Array.isArray(templates) ? templates.length : 0);
}

async function register(event) {
  event.preventDefault();
  if (!registerStatus) return;

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
    loginPassword.setCustomValidity(t("Неверный логин или пароль"));
    loginPassword.reportValidity();
    return;
  }

  state.currentUser = await response.json();
  loginPassword.value = "";
  updateAuthView();
  await loadActiveTemplateCount();
}

async function logout() {
  await apiFetch("/api/auth/logout", { method: "POST" });
  state.currentUser = null;
  updateAuthView();
}

function setCardState(card, isOpen) {
  const summary = card.querySelector(".home-action-card__summary");
  const body = card.querySelector(".home-action-card__body");

  card.classList.toggle("is-open", isOpen);
  summary?.setAttribute("aria-expanded", isOpen ? "true" : "false");
  body?.setAttribute("aria-hidden", isOpen ? "false" : "true");

  for (const element of card.querySelectorAll(".home-action-card__body a, .home-action-card__body button")) {
    if (isOpen) {
      element.removeAttribute("tabindex");
    } else {
      element.setAttribute("tabindex", "-1");
    }
  }
}

function setupHomeCards() {
  for (const card of document.querySelectorAll("[data-home-card]")) {
    const summary = card.querySelector(".home-action-card__summary");
    setCardState(card, false);
    summary?.addEventListener("click", () => {
      setCardState(card, !card.classList.contains("is-open"));
    });
  }
}

function setTemplateCardState(card, isOpen) {
  if (!card) return;
  card.classList.toggle("is-open", isOpen);
  card.setAttribute("aria-expanded", isOpen ? "true" : "false");
}

function openTemplateCard(card) {
  if (!card) return;

  for (const otherCard of document.querySelectorAll("[data-template-card]")) {
    if (otherCard !== card) {
      window.clearTimeout(templateCardCloseTimers.get(otherCard));
      setTemplateCardState(otherCard, false);
    }
  }

  window.clearTimeout(templateCardCloseTimers.get(card));
  setTemplateCardState(card, true);
  templateCardCloseTimers.set(card, window.setTimeout(() => {
    setTemplateCardState(card, false);
  }, 5000));
}

function setupTemplateCard() {
  for (const card of document.querySelectorAll("[data-template-card]")) {
    card.setAttribute("aria-expanded", "false");
    card.addEventListener("pointerenter", () => openTemplateCard(card));
    card.addEventListener("focusin", () => openTemplateCard(card));

    const button = card.querySelector(".home-template-card__button");
    button?.addEventListener("pointerdown", (event) => {
      if (event.button !== 0 || event.isPrimary === false) return;
      event.preventDefault();
      window.location.assign(card.href);
    });
  }
}

function setupHomeSearch() {
  if (!globalSearchInput) return;

  const cards = [...document.querySelectorAll("[data-template-card]")];
  const status = document.createElement("span");
  status.className = "global-search__status";
  status.setAttribute("role", "status");
  status.setAttribute("aria-live", "polite");
  globalSearchInput.closest(".global-search")?.append(status);

  const applySearch = () => {
    const query = globalSearchInput.value.trim().toLocaleLowerCase();
    let visibleCount = 0;
    for (const card of cards) {
      const matches = !query || card.textContent.toLocaleLowerCase().includes(query);
      card.hidden = !matches;
      if (matches) visibleCount += 1;
    }
    status.textContent = query && visibleCount === 0 ? t("Ничего не найдено") : "";
    return cards.filter(card => !card.hidden);
  };

  globalSearchInput.addEventListener("input", applySearch);
  globalSearchInput.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      globalSearchInput.value = "";
      applySearch();
      return;
    }

    if (event.key !== "Enter") return;
    const visibleCards = applySearch();
    if (visibleCards.length === 1) {
      event.preventDefault();
      window.location.assign(visibleCards[0].href);
    }
  });
  window.addEventListener("tflex:languagechange", applySearch);
}

registerForm?.addEventListener("submit", register);
loginForm?.addEventListener("submit", login);
logoutButton?.addEventListener("click", logout);
userPanel?.addEventListener("click", event => {
  if (logoutButton?.contains(event.target)) return;
  window.location.assign("/account");
});

setupHomeCards();
setupTemplateCard();
setupHomeSearch();
if (await loadCurrentUser()) {
  await loadActiveTemplateCount();
}
