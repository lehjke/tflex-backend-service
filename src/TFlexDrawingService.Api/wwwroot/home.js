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
    if (currentUserRole) currentUserRole.textContent = getRoleLabel();
  } else {
    if (currentUserName) currentUserName.textContent = "";
    if (currentUserRole) currentUserRole.textContent = "User";
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

registerForm?.addEventListener("submit", register);
loginForm?.addEventListener("submit", login);
logoutButton?.addEventListener("click", logout);

setupHomeCards();
await loadCurrentUser();
