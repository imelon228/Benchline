(() => {
  let listener;
  const getTheme = () => {
    try { return localStorage.getItem("benchline-theme") === "dark"; }
    catch { return false; }
  };
  const applyTheme = (dark) => {
    document.documentElement.dataset.theme = dark ? "dark" : "light";
    document.querySelector('meta[name="theme-color"]').content = dark ? "#0A0A0A" : "#FFFFFF";
  };
  applyTheme(getTheme());
  window.benchline = {
    dark: getTheme,
    theme(dark) {
      applyTheme(dark);
      try { localStorage.setItem("benchline-theme", dark ? "dark" : "light"); }
      catch { /* The current page still uses the selected theme. */ }
    },
    connect(component) {
      listener = () => component.invokeMethodAsync("Connectivity", navigator.onLine).catch(() => {});
      window.addEventListener("online", listener);
      window.addEventListener("offline", listener);
      if (!navigator.onLine) listener();
      return getTheme();
    },
    disconnect() {
      if (!listener) return;
      window.removeEventListener("online", listener);
      window.removeEventListener("offline", listener);
      listener = undefined;
    },
    openDialog(id) {
      const dialog = document.getElementById(id);
      if (dialog && !dialog.open) dialog.showModal();
    },
    closeDialog(id) {
      const dialog = document.getElementById(id);
      if (dialog?.open) dialog.close();
    }
  };
  document.addEventListener("keydown", event => {
    const current = event.target.closest("button[data-slot]");
    if (!current || !["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
    const row = Number(current.dataset.row);
    const column = Number(current.dataset.column);
    const horizontal = event.key === "ArrowLeft" || event.key === "ArrowRight";
    const direction = event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 1;
    const axis = horizontal ? "column" : "row";
    const origin = horizontal ? column : row;
    const candidates = [...current.closest("table").querySelectorAll("button[data-slot]:enabled")]
      .filter(button => horizontal ? Number(button.dataset.row) === row : Number(button.dataset.column) === column)
      .map(button => ({ button, distance: (Number(button.dataset[axis]) - origin) * direction }))
      .filter(candidate => candidate.distance > 0)
      .sort((left, right) => left.distance - right.distance);
    event.preventDefault();
    candidates[0]?.button.focus();
  });
  const motion = window.matchMedia("(prefers-reduced-motion: no-preference)");
  new MutationObserver(changes => {
    if (!motion.matches) return;
    for (const { target } of changes) {
      if (!target.hasAttribute("data-changed")) continue;
      target.querySelector(".station-heading")?.animate(
        [{ backgroundColor: "var(--hatch)" }, { backgroundColor: "var(--paper)" }],
        { duration: 650, easing: "ease-out" }
      );
    }
  }).observe(document.getElementById("app"), { subtree: true, attributes: true, attributeFilter: ["data-changed"] });
  let offlineScheduled = false;
  function prepareOffline() {
    if (offlineScheduled || !navigator.onLine || !("serviceWorker" in navigator)) return;
    offlineScheduled = true;
    const register = () => navigator.serviceWorker.register("service-worker.js").catch(error => {
      offlineScheduled = false;
      if (navigator.onLine) console.warn("Offline shell unavailable", error);
    });
    if (window.requestIdleCallback) window.requestIdleCallback(register, { timeout: 1200 });
    else window.setTimeout(register, 0);
  }
  window.benchline.prepareOffline = prepareOffline;
  window.addEventListener("online", prepareOffline);
})();
