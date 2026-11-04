// Settings → Environment Variables (.docs/pages/environment-variables.md): the sort select reloads the list in the
// chosen order (GET ?sort=; without JavaScript the form's own button does it), and the eye shows or hides a config
// value in place of its dots.
(function () {
    "use strict";

    document.addEventListener("change", function (event) {
        var select = event.target;
        var form = select instanceof HTMLSelectElement ? select.closest("form[data-env-vars-sort]") : null;
        if (form) { form.submit(); }
    });

    document.addEventListener("click", function (event) {
        var button = event.target instanceof Element ? event.target.closest("[data-env-reveal]") : null;
        var cell = button ? button.closest("[data-env-value]") : null;
        if (!cell) { return; }
        var show = button.getAttribute("aria-pressed") !== "true";
        cell.querySelector("[data-env-plain]").hidden = !show;
        cell.querySelector("[data-env-masked]").hidden = show;
        button.setAttribute("aria-pressed", show ? "true" : "false");
        button.setAttribute("aria-label", button.getAttribute(show ? "data-label-hide" : "data-label-show"));
        button.querySelectorAll("[data-reveal-icon]").forEach(function (icon) {
            icon.toggleAttribute("hidden", icon.getAttribute("data-reveal-icon") !== (show ? "hide" : "show"));
        });
    });
})();
