// The public site's language switcher (.docs/features/internationalization.md#public-site): choosing a language in
// <select data-language-switch> opens the page in that language (each option's value is its URL). The select is shown
// only with JavaScript; without it the <noscript> links next to it switch the language. No inline script (CSP).
(function () {
    "use strict";

    document.querySelectorAll("select[data-language-switch][hidden]").forEach(function (select) {
        select.hidden = false;
    });
    document.addEventListener("change", function (event) {
        var select = event.target;
        if (select instanceof HTMLSelectElement && select.matches("[data-language-switch]") && select.value) {
            window.location.assign(select.value);
        }
    });
})();
