try {
  if (localStorage.getItem("tflex-sidebar-collapsed") === "true") {
    document.documentElement.classList.add("sidebar-collapsed");
  }
} catch {
  // Storage can be unavailable in private or restricted browsing contexts.
}
