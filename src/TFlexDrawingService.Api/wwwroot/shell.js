const menuButton = document.querySelector(".sidebar__menu-toggle");
const sidebarMenu = document.querySelector("#sidebarMenu");
const menuBackdrop = document.querySelector(".mobile-menu-backdrop");
const mobileMenuQuery = window.matchMedia("(max-width: 900px)");

function setMobileMenuState(isOpen) {
  document.body.classList.toggle("mobile-menu-open", isOpen);
  menuButton?.setAttribute("aria-expanded", isOpen ? "true" : "false");
  menuButton?.setAttribute("aria-label", isOpen ? "Закрыть меню" : "Открыть меню");
  if (sidebarMenu) {
    sidebarMenu.setAttribute("aria-hidden", mobileMenuQuery.matches && !isOpen ? "true" : "false");
    if ("inert" in sidebarMenu) {
      sidebarMenu.inert = mobileMenuQuery.matches && !isOpen;
    }
  }

  if (menuBackdrop) {
    menuBackdrop.hidden = !isOpen;
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

setMobileMenuState(false);
