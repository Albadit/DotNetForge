// Forge theme bootstrap (.docs/architecture/design-system.md#themes). Loaded synchronously in <head> so the saved
// light/dark choice is applied before the first paint (no flash). Without a saved choice the system preference
// applies through CSS alone. ui.js owns the switch itself.
// It also marks <html class="js">, so controls that need JavaScript (the theme menu) can be shown from the first
// paint instead of appearing once the deferred ui.js has run.
(function () {
    "use strict";
    document.documentElement.classList.add("js");
    try {
        var theme = window.localStorage.getItem("dnf-theme");
        if (theme === "light" || theme === "dark") {
            document.documentElement.setAttribute("data-theme", theme);
        }
    } catch (e) {
        // Storage blocked (privacy mode): the system preference applies.
    }
})();
