import { mountLanguageSwitch, t } from "./i18n.js?v=20260720-ui-hardening-4";

const menuButton = document.querySelector(".sidebar__menu-toggle");
const sidebarMenu = document.querySelector("#sidebarMenu");
const sidebarHelp = document.querySelector(".sidebar__help");
const menuBackdrop = document.querySelector(".mobile-menu-backdrop");
const mobileMenuQuery = window.matchMedia("(max-width: 900px)");
const mobileSearchQuery = window.matchMedia("(max-width: 620px)");
const sourceUserPanel = document.querySelector("#userPanel");
const sourceUserName = document.querySelector("#currentUserName");
const sourceLogoutButton = document.querySelector("#logoutButton");
const sourceSearchLabel = document.querySelector(".global-search");
let searchPlaceholder = null;
let mobileSearchSlot = null;

function setupSidebarControls() {
  if (!sidebarMenu || sidebarMenu.querySelector(".sidebar__controls")) return;

  const controls = document.createElement("div");
  controls.className = "sidebar__controls";

  if (sourceSearchLabel?.parentNode) {
    searchPlaceholder = document.createComment("global-search");
    sourceSearchLabel.parentNode.insertBefore(searchPlaceholder, sourceSearchLabel);
    mobileSearchSlot = document.createElement("div");
    mobileSearchSlot.className = "sidebar__mobile-search";
    controls.append(mobileSearchSlot);
    sourceSearchLabel.querySelector("input")?.addEventListener("keydown", event => {
      if (event.key === "Enter" && mobileSearchQuery.matches) {
        closeMobileMenu();
      }
    });
  }

  const languageRow = document.createElement("div");
  languageRow.className = "sidebar__language";
  const languageLabel = document.createElement("span");
  languageLabel.textContent = "Язык";
  languageRow.append(languageLabel);
  mountLanguageSwitch(languageRow);
  controls.append(languageRow);

  const session = document.createElement("div");
  session.className = "sidebar__session";
  session.hidden = true;
  session.innerHTML = `
    <a class="sidebar__account-link" href="/account">
      <span class="user-avatar" aria-hidden="true">ME</span>
      <span data-mobile-user-name></span>
    </a>
    <button class="sidebar__logout secondary secondary--compact" type="button">Выйти</button>
  `;
  session.querySelector(".sidebar__logout").addEventListener("click", event => {
    event.stopPropagation();
    sourceLogoutButton?.click();
    closeMobileMenu();
  });
  controls.append(session);
  sidebarMenu.append(controls);

  const syncSession = () => {
    const authenticated = Boolean(sourceUserPanel && !sourceUserPanel.hidden);
    session.hidden = false;
    session.querySelector("[data-mobile-user-name]").textContent = authenticated
      ? (sourceUserName?.textContent?.trim() || t("Личный кабинет"))
      : t("Войти");
    session.querySelector(".sidebar__logout").hidden = !authenticated;
  };
  syncSession();

  if (sourceUserPanel) {
    new MutationObserver(syncSession).observe(sourceUserPanel, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
      attributeFilter: ["hidden"]
    });
  }
}

function syncSearchPlacement() {
  if (!sourceSearchLabel || !searchPlaceholder || !mobileSearchSlot) return;
  if (mobileSearchQuery.matches) {
    mobileSearchSlot.append(sourceSearchLabel);
  } else {
    searchPlaceholder.parentNode?.insertBefore(sourceSearchLabel, searchPlaceholder.nextSibling);
  }
}

function setMobileMenuState(isOpen) {
  const wasOpen = document.body.classList.contains("mobile-menu-open");
  document.body.classList.toggle("mobile-menu-open", isOpen);
  menuButton?.setAttribute("aria-expanded", isOpen ? "true" : "false");
  menuButton?.setAttribute("aria-label", isOpen ? t("Закрыть меню") : t("Открыть меню"));
  if (sidebarMenu) {
    sidebarMenu.setAttribute("aria-hidden", mobileMenuQuery.matches && !isOpen ? "true" : "false");
    if ("inert" in sidebarMenu) {
      sidebarMenu.inert = mobileMenuQuery.matches && !isOpen;
    }
  }
  if (sidebarHelp) {
    const helpIsHidden = mobileMenuQuery.matches && !isOpen;
    sidebarHelp.setAttribute("aria-hidden", helpIsHidden ? "true" : "false");
    if ("inert" in sidebarHelp) {
      sidebarHelp.inert = helpIsHidden;
    }
    sidebarHelp.querySelectorAll("a[href], button:not(:disabled)").forEach(element => {
      if (helpIsHidden) {
        element.setAttribute("tabindex", "-1");
      } else {
        element.removeAttribute("tabindex");
      }
    });
  }

  if (menuBackdrop) {
    menuBackdrop.hidden = !isOpen;
  }

  if (mobileMenuQuery.matches && isOpen && !wasOpen) {
    requestAnimationFrame(() => {
      sidebarMenu?.querySelector("a[href]")?.focus({ preventScroll: true });
    });
  } else if (mobileMenuQuery.matches && !isOpen && wasOpen) {
    menuButton?.focus({ preventScroll: true });
  }
}

function closeMobileMenu() {
  setMobileMenuState(false);
}

function toggleMobileMenu() {
  setMobileMenuState(!document.body.classList.contains("mobile-menu-open"));
}

menuButton?.addEventListener("click", toggleMobileMenu);
menuBackdrop?.addEventListener("click", closeMobileMenu);

document.addEventListener("keydown", event => {
  if (event.key === "Escape") {
    closeMobileMenu();
    return;
  }

  if (event.key === "Tab" && document.body.classList.contains("mobile-menu-open")) {
    const focusable = [
      menuButton,
      ...sidebarMenu.querySelectorAll(
        "a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)"),
      ...(sidebarHelp?.querySelectorAll("a[href], button:not(:disabled)") || [])
    ]
      .filter(element => element && !element.closest("[hidden]") && element.getClientRects().length > 0);
    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
});

for (const link of sidebarMenu?.querySelectorAll("a") || []) {
  link.addEventListener("click", event => {
    if (link.getAttribute("aria-disabled") === "true") {
      event.preventDefault();
    }

    closeMobileMenu();
  });
}

mobileMenuQuery.addEventListener("change", event => {
  if (!event.matches) {
    closeMobileMenu();
  } else {
    setMobileMenuState(false);
  }
});
mobileSearchQuery.addEventListener("change", syncSearchPlacement);

window.addEventListener("tflex:languagechange", () => {
  setMobileMenuState(document.body.classList.contains("mobile-menu-open"));
});

setupSidebarControls();
syncSearchPlacement();
setMobileMenuState(false);
