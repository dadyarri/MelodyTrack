try {
  if (localStorage.getItem("melodytrack.theme") === "dark") {
    document.documentElement.dataset.theme = "dark";
  }
} catch {
  // The application can still boot when storage is unavailable.
}
